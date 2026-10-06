"""Build an authored character recipe against exact catalog identities.

No pose or event identity is inferred from display names. Existing output is
never overwritten. Validate the result with PackTool before installing it.
"""
import argparse
import json
import re
import shutil
import zipfile
from pathlib import Path, PurePosixPath

from PIL import Image


def read(path):
    return json.loads(Path(path).read_text(encoding="utf-8-sig"))


def safe_file(root, name):
    path = PurePosixPath(name)
    if not name or "\\" in name or ":" in name or path.is_absolute() or any(
            part in ("", ".", "..") for part in name.split("/")):
        raise ValueError(f"Unsafe asset path: {name}")
    result = (root / name).resolve()
    if not result.is_relative_to(root.resolve()) or not result.is_file():
        raise ValueError(f"Missing asset or asset escapes source directory: {name}")
    return result


def sequence(poses, animation, resources):
    phase = "cycle"
    if isinstance(poses, dict):
        phase = poses.get("phase", "cycle")
        poses = poses.get("poses")
    if phase not in ("cycle", "once"):
        raise ValueError("Authored sequence phase must be cycle or once")
    if not poses or not isinstance(poses, list) or any(p not in resources or resources[p]["kind"] != "sprite" for p in poses):
        raise ValueError(f"Missing authored sprite sequence: {animation['setName']} / {animation['state']}")
    indices = animation["frameIndices"]
    ordered = sorted(set(indices))
    ranks = {index: rank for rank, index in enumerate(ordered)}
    # Arrays may be unsorted. Select art by native keyframe time while retaining
    # the exact source array order; otherwise a late keyframe can restart a pose.
    span = ordered[-1] - ordered[0] if ordered else 0
    frames = [poses[ranks[index] % len(poses)] if phase == "cycle" else
              poses[round((index-ordered[0]) * (len(poses)-1) / span) if span else len(poses)//2]
              for index in indices]
    return {"frames": frames,
            "frameIndices": list(animation["frameIndices"])}


def check_images(source, resources, body_ids):
    for resource_id, resource in resources.items():
        path = safe_file(source, resource["file"])
        if resource["kind"] != "sprite":
            continue
        with Image.open(path) as image:
            if image.format != "PNG" or "A" not in image.getbands():
                raise ValueError(f"Sprite needs PNG with alpha: {resource_id}")
            width, height = image.size
            x, y, w, h = resource.get("rect", [0, 0, width, height])
            if min(x, y) < 0 or min(w, h) <= 0 or x + w > width or y + h > height:
                raise ValueError(f"Sprite rect exceeds PNG: {resource_id}")
            alpha = image.getchannel("A").crop((x, height-y-h, x+w, height-y))
            bounds = alpha.getbbox()
            if not bounds:
                raise ValueError(f"Empty authored sprite: {resource_id}")
            if resource_id in body_ids and (bounds[0] == 0 or bounds[1] == 0 or bounds[2] == w or bounds[3] == h):
                raise ValueError(f"Body touches cell edge; provide transparent padding: {resource_id}")


def compose(recipe, catalog):
    identity = recipe["manifest"]
    if not re.fullmatch(r"[a-z0-9][a-z0-9._-]{2,63}", identity["id"]):
        raise ValueError("Invalid character ID")
    resources = recipe["resources"]
    states = {animation["state"] for animation in catalog["animations"].values() if animation["role"] == "body"}
    unknown_states = set(recipe["bodyStates"]) - states
    if unknown_states:
        raise ValueError(f"Unknown body states: {sorted(unknown_states)}")
    manifest = dict(identity, schemaVersion=1, compatibleCatalogs=[catalog["id"]],
                    resources=resources, body={}, weapons={}, effects={},
                    visuals=recipe.get("visuals", {}), ui=recipe.get("ui", {}), audio=recipe.get("audio", {}))
    body_ids = set()
    for key, animation in catalog["animations"].items():
        if animation["role"] == "body":
            poses = recipe["bodyStates"].get(animation["state"])
            binding = sequence(poses, animation, resources)
            body_ids.update(binding["frames"])
            manifest["body"][key] = binding
        elif animation["role"] == "weapon" and recipe.get("weaponAnimations"):
            rule = recipe["weaponAnimations"].get(key)
            if not rule:
                raise ValueError(f"Missing weapon animation mapping: {key}")
            manifest["weapons"][key] = dict(sequence(rule, animation, resources), fitOriginal=True)
        elif animation["role"] == "effect" and key in recipe.get("effectAnimations", {}):
            manifest["effects"][key] = dict(sequence(recipe["effectAnimations"][key], animation, resources), fitOriginal=True)
    for section, catalog_section in (("effectAnimations", "animations"), ("weaponAnimations", "animations"),
                                     ("visuals", "visuals"), ("ui", "ui"), ("audio", "audio")):
        unknown = set(recipe.get(section, {})) - set(catalog[catalog_section])
        if unknown:
            raise ValueError(f"Unknown catalog identities in {section}: {sorted(unknown)}")
    for section, role in (("effectAnimations", "effect"), ("weaponAnimations", "weapon")):
        if any(catalog["animations"][key]["role"] != role for key in recipe.get(section, {})):
            raise ValueError(f"Wrong animation role in {section}")
    if not recipe.get("weaponAnimations") and any(catalog["visuals"][key]["role"] == "weapon" for key in manifest["visuals"]):
        raise ValueError("Weaponless characters must omit weapon visuals, including hide bindings")
    return manifest, body_ids


def build(recipe_path, catalog_path, output):
    recipe_path = Path(recipe_path).resolve()
    source = recipe_path.parent
    output = Path(output).resolve()
    if output.exists():
        raise ValueError("Output must be a new directory; existing packs are never overwritten")
    recipe = read(recipe_path)
    manifest, body_ids = compose(recipe, read(catalog_path))
    if output.parent.exists():
        for sibling in output.parent.iterdir():
            existing = None
            if sibling.is_dir() and (sibling / "skin.json").is_file():
                existing = read(sibling / "skin.json")
            elif sibling.is_file() and sibling.suffix.lower() == ".zip":
                with zipfile.ZipFile(sibling) as archive:
                    if "skin.json" in archive.namelist() and archive.getinfo("skin.json").file_size <= 2 * 1024 * 1024:
                        existing = json.loads(archive.read("skin.json").decode("utf-8-sig"))
            if existing and existing.get("id") == manifest["id"]:
                raise ValueError(f"Character ID already exists: {manifest['id']}")
    check_images(source, manifest["resources"], body_ids)
    files = {r["file"] for r in manifest["resources"].values()}
    records = recipe.get("records", ["PROVENANCE.md", "prompts.txt"])
    if "PROVENANCE.md" not in records or "prompts.txt" not in records:
        raise ValueError("Provenance and prompts must accompany generated assets")
    files.update(records)
    validated = [(name, safe_file(source, name)) for name in sorted(files)]
    if any(path.suffix.lower() in (".cs", ".dll", ".exe", ".ps1", ".py", ".js", ".bat", ".cmd") for _, path in validated):
        raise ValueError("Executable author payloads are forbidden")
    output.mkdir(parents=True)
    for name, source_file in validated:
        destination = output / name
        destination.parent.mkdir(parents=True, exist_ok=True)
        shutil.copyfile(source_file, destination)
    (output / "skin.json").write_text(json.dumps(manifest, ensure_ascii=False, indent=2), encoding="utf-8")
    return {"id": manifest["id"], "output": str(output), **{s: len(manifest[s]) for s in ("body", "weapons", "effects", "ui", "audio")}}


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("recipe")
    parser.add_argument("output")
    parser.add_argument("--catalog", default=str(Path(__file__).resolve().parents[1] / "catalog/catalog-1.0.33.json"))
    args = parser.parse_args()
    print(json.dumps(build(args.recipe, args.catalog, args.output), ensure_ascii=False, indent=2))
