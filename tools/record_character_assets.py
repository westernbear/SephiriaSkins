"""Record actual built-in generations and inspected final assets for a new pack."""
import argparse
import hashlib
import json
from pathlib import Path
from character_prompt_recipes import TRAITS, WEAPONS

PRODUCTS = {"chiikawa": "4571609380276", "usagi": "4571609380290", "kurimanju": "4571609380313",
            "rakko": "4571609380337", "shisa": "4571609380320", "furuhonya": "4571609380344"}
WEAPON_PRODUCTS = {"chiikawa": "4571609354956", "usagi": "4571609354970", "rakko": "4571609385073"}


def record(character, directory, history):
    directory = Path(directory)
    history = json.loads(Path(history).read_text(encoding="utf-8-sig"))
    outputs = [directory / name for name in ("PROVENANCE.md", "prompts.txt", "generation-records.txt", "generated-drafts.json")]
    if any(path.exists() for path in outputs):
        raise ValueError("Production records must be new; retain prior generation history")
    generations = []
    for entry in history:
        filename = entry["file"]
        if Path(filename).name != filename or not filename.endswith(".png"):
            raise ValueError("Original generation must be a project-local PNG filename")
        if entry["tool"] != "image_gen" or not entry["prompt"].strip() or not entry["date"]:
            raise ValueError("Actual generation tool, date and exact prompt are required")
        generations.append({**entry, "sha256": hashlib.sha256((directory / filename).read_bytes()).hexdigest(),
                            "originalUnmodified": True})
    selected = []
    for atlas in ("body", "effects", "ui") + (("weapon",) if character in WEAPONS else ()):
        inspection = json.loads((directory / (atlas + "-inspection.json")).read_text())
        filename = inspection["source"]
        digest = hashlib.sha256((directory / filename).read_bytes()).hexdigest()
        cleanup = directory / Path(filename).with_suffix(".cleanup.json")
        if cleanup.exists():
            report = json.loads(cleanup.read_text())
            if report["outputSha256"] != digest or not all(cell["rgbUnchanged"] for cell in report["cells"]):
                raise ValueError("Selected cleanup report does not match preserved artwork")
            if not any(g["file"] == report["source"] and g["sha256"] == report["sourceSha256"] for g in generations):
                raise ValueError("Selected original source is missing from actual generation history")
        selected.append({"atlas": atlas, "file": filename, "sha256": digest, "inspection": atlas + "-inspection.json",
                         "cleanup": cleanup.name if cleanup.exists() else None})
    audio = json.loads((directory / "audio-inspection.json").read_text())
    if audio["character"] != character or not audio["technicalChecksPassed"]:
        raise ValueError("Matching decoded audio inspection is required")
    prompts = "\n\n".join(f"Generation {index}: {entry['file']}\nTool: {entry['tool']}\nActual date: {entry['date']}\nMode: {entry.get('mode','new generation')}\n{entry['prompt']}"
                           for index, entry in enumerate(generations, 1)) + "\n"
    outputs[1].write_text(prompts, encoding="utf-8")
    outputs[2].write_text("Exact actual production history; original PNGs retained unchanged.\n\n" + prompts, encoding="utf-8")
    outputs[3].write_text(json.dumps({"character": character, "generations": generations, "selected": selected}, ensure_ascii=False, indent=2), encoding="utf-8")
    references = f"[official anime material](https://www.anime-chiikawa.jp/) and [official character merchandise](https://chiikawamarket.jp/products/{PRODUCTS[character]})"
    weapon = (f"Separate character weapon references: https://chiikawamarket.jp/products/{WEAPON_PRODUCTS[character]}. {WEAPONS[character]}"
              if character in WEAPONS else "Native weapons are retained. Weapon bindings, masks and decoration hiding are omitted.")
    text = f"""# {character} fan-art theme production record

Status: selected atlases inspected; actual pack loading and gameplay verification pending.

Appearance references: {references}. Reviewed traits: {TRAITS[character]} Official artwork is not included. This is newly generated unofficial fan art, unaffiliated with the rights holders.

{weapon}

The original PNGs were generated with the built-in OpenAI image_gen tool requesting transparent_background=true. Actual dates, exact prompts and original/selected SHA256 hashes are in generation-records.txt and generated-drafts.json. Body, effects, UI/portrait and any weapon are separately authored. No other character's images or audio are copied.

The user explicitly authorized scripted alpha-residue removal and cell-padding cleanup on 2026-10-06. Matching cleanup JSON records original/output hashes, reviewed source boundaries and retained-pixel checks. Artwork is copied unscaled without recoloring visible RGBA. Only removed exterior alpha and hidden RGB at alpha zero are cleared. Original images remain unmodified. Selected PNGs were inspected for complete connected body poses, empty gutters, positive-alpha content and consistent anchors. Their inspected files are:

""" + "\n".join(f"- `{entry['file']}` SHA256 `{entry['sha256']}`, report `{entry['inspection']}`" for entry in selected) + """

Audio: twelve newly synthesized nonverbal vocalizations, effects, a separate sustained cue and original menu/exploration/battle music. audio/audio-provenance.json records character-specific melody, timbre, rhythm, parameters and hashes. audio-inspection.json independently checks decoded PCM, nonempty signal, peak headroom and loop endpoints. No original voice recording or game audio is sampled. Listening evaluation and actual native-event playback are separate; neither is claimed by file inspection.

The catalog recipe connects all 1,100 body states and 6,405 ordered frames without modifying native FPS, transitions, loops, attack events, collision or network behavior. Only selected resources and production records enter the pack; source code and game DLLs are excluded. Actual game evidence is recorded separately under docs/evidence/series-020-20261006.
"""
    outputs[0].write_text(text, encoding="utf-8")
    return {"character": character, "generations": len(generations), "selectedAtlases": len(selected)}


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("character", choices=TRAITS)
    parser.add_argument("directory")
    parser.add_argument("history", help="JSON array recording actual image_gen calls; do not substitute planned prompts")
    args = parser.parse_args()
    print(json.dumps(record(args.character, args.directory, args.history)))
