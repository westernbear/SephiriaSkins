"""Verify native renderer composition and option round trips recorded in-game."""
import argparse
import json
import re
from pathlib import Path
from diagnostic_expectations import expected_trials, manifest_sprites, verify_body_renderers, verify_weapon_renderers
from verify_play_results import native_quick_draw, native_transformed_attack


def require(value, label):
    if not value:
        raise ValueError(label)


def read(path):
    return json.loads(Path(path).read_text(encoding="utf-8-sig"))


def dimensions(renderer):
    return tuple(float(v) / renderer["ppu"] for v in re.findall(r"(?:width|height):([0-9.]+)", renderer["rect"]))


def verify_presentation(comparison, pack_id):
    identity = pack_id + ":" + comparison["resource"]
    require(comparison["pixelFrame"] == comparison["smoothFrame"] == identity, "same authored presentation frame")
    for field in ("Bounds", "Pivot"):
        pixel, smooth = comparison["pixel" + field], comparison["smooth" + field]
        require(len(pixel) == len(smooth) == 2 and all(abs(a-b) < .00001 for a,b in zip(pixel,smooth)), "same frame " + field)


def verify(report, cleanup, manifest=None):
    require(not cleanup["exceptions"] and not cleanup["hooks"], "game/hook errors")
    require(cleanup["restored"]["themeNull"] and cleanup["restored"]["profileFileAbsent"]
            and cleanup["restored"]["virtualDeviceRemoved"], "session cleanup")
    snapshots = [r for r in report if "themed" in r]
    metadata = next((r for r in report if "manifest" in r), None)
    if metadata:
        manifest = metadata["manifest"]
        require(metadata["packId"] == manifest["id"] == cleanup["packId"]
                and metadata["runId"] == cleanup["runId"], "matching pack/run identity")
        expected = expected_trials(metadata["coverage"])
    else:
        # Historical reports need their actual manifest supplied explicitly.
        require(manifest, "manifest required for historical visual report")
        expected = {(r["id"], mode) for r in snapshots for mode in ("basic", "dash", "special")}
    weapon_ids = {weapon for weapon, _ in expected}
    require({r["id"] for r in snapshots} == weapon_ids and len(snapshots) == len(weapon_ids), "enumerated weapon renderers")
    body_sprites = manifest_sprites(manifest, "body")
    back_sprites = {manifest["id"] + ":" + frame for key, binding in manifest.get("body", {}).items()
                    if "_BACK/" in key for frame in binding["frames"]}
    originals = {r["id"]: {x["path"]: x for x in r["original"]} for r in report if "original" in r}
    fitted = 0
    reflections = 0
    for trial in snapshots:
        if metadata:
            fitted += verify_weapon_renderers(manifest, list(originals[trial["id"]].values()), trial["themed"])
            reflections += verify_body_renderers(manifest, list(originals[trial["id"]].values()), trial["themed"])["nativeReflections"]
        for renderer in trial["themed"]:
            if renderer["sprite"] in body_sprites:
                require(renderer["fullTexture"], "body/reflection atlas isolation")
            if metadata:
                binding = manifest.get("visuals", {}).get(renderer.get("visualKey"), {})
                source = originals[trial["id"]].get(renderer["path"])
                if renderer["active"] and binding.get("fitOriginal") and binding.get("sprite") and source and source["sprite"]:
                    original_size, themed_size = dimensions(source), dimensions(renderer)
                    require(len(original_size) == len(themed_size) == 2 and
                            all(abs(a - b) < .001 for a, b in zip(original_size, themed_size)), "weapon frame dimensions")
    captures = [r for r in report if "mode" in r and not r.get("visualAction")]
    require({(r["id"], r["mode"]) for r in captures} == expected, "enumerated attack captures")
    if any("pixel" in r for r in captures):
        require({(r["id"], r["mode"], r["frame"], r["pixel"]) for r in captures} ==
                {(weapon, mode, frame, pixel) for weapon, mode in expected for frame in range(3) for pixel in (True, False)}
                and len(captures) == len(expected) * 6, "pixel/smooth attack coverage")
    else:
        require(len(captures) == len(expected) * 3, "three attack samples")
    actions = [r for r in report if r.get("visualAction")]
    if actions:
        require({(r["id"], r["mode"], r["pixel"]) for r in actions} ==
                {(weapon, mode, pixel) for weapon, mode in expected for pixel in (True, False)} and
                len(actions) == len(expected) * 2, "native pixel/smooth action coverage")
        require(all(r["events"] or r["guarded"] for r in actions), "visual input triggered native attack or guard")
        for action in actions:
            if "nativeFires" in action:
                require(action["nativeFires"] > 0 or action["guarded"], "visual actual projectile or guard")
            if action.get("modernKatana") and action["mode"] == "special":
                require(action["specialFires"] > 0, "visual modern katana actual special projectile")
            if action["mode"] == "special":
                require(any(e.startswith("special") for e in action["events"]) or action["guarded"] or native_quick_draw(action) or native_transformed_attack(action), "visual native secondary attack, guard or variant")
    facings = [r for r in report if "facing" in r and "id" in r]
    require({(r["id"], r["facing"]) for r in facings} == {(weapon, facing) for weapon in weapon_ids for facing in ("front", "back")}
            and len(facings) == len(weapon_ids) * 2, "front/back requests")
    back = sum(any(x["sprite"] in back_sprites for x in r["renderers"]) for r in facings if r["facing"] == "back")
    require(back >= len(weapon_ids) - 1, "native back states")
    require({r["reviveFrame"] for r in report if "reviveFrame" in r} == {0, 1, 2}, "revive composition captures")
    reload = next(r for r in report if "uiOffSurvivesReload" in r)
    require(reload["uiOffSurvivesReload"] and reload["themeRemains"], "UI OFF reload")
    toggles = next(r["toggles"] for r in report if "toggles" in r)
    require(all(value for key, value in toggles.items() if key != "labels"), "option buttons / rendering / UI restore")
    comparison = next((r["presentationComparison"] for r in report if "presentationComparison" in r), None)
    if comparison:
        verify_presentation(comparison, manifest["id"])
    water_survey = next((r for r in report if r.get("waterSurvey")), None)
    water_captures = [r for r in report if r.get("waterCapture")]
    if water_survey and water_survey["available"]:
        require({(r["pixel"], r["facing"]) for r in water_captures} ==
                {(pixel, facing) for pixel in (True, False) for facing in ("front", "back")} and len(water_captures) == 4, "native water shore captures")
        for capture in water_captures:
            require(capture["alive"] and capture["canMove"], "verified solid shore")
            require(verify_body_renderers(manifest, capture["nativeRenderers"], capture["themedRenderers"])["nativeReflections"] > 0, "native water reflection composition")
    return {"packId": manifest["id"], "weapons": len(weapon_ids), "fittedWeaponRenderers": fitted, "nativeReflectionSnapshots": reflections, "attackCaptures": len(captures),
            "nativeBackStates": back, "togglesPassed": True, "uiOffReloadPassed": True, "nativeWaterShoreCaptures": len(water_captures),
            "nativeWaterSurvey": water_survey}


if __name__ == "__main__":
    parser = argparse.ArgumentParser()
    parser.add_argument("renderers")
    parser.add_argument("cleanup")
    parser.add_argument("--manifest", help="Manifest for a historical report without embedded expectations")
    args = parser.parse_args()
    print(json.dumps(verify(read(args.renderers), read(args.cleanup), read(args.manifest) if args.manifest else None), indent=2))
