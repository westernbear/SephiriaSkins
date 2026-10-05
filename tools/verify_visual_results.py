"""Verify native renderer composition and option round trips recorded in-game."""
import argparse
import json
import re
from pathlib import Path


def require(value, label):
    if not value:
        raise ValueError(label)


def read(path):
    return json.loads(Path(path).read_text(encoding="utf-8-sig"))


def dimensions(renderer):
    return tuple(float(v) / renderer["ppu"] for v in re.findall(r"(?:width|height):([0-9.]+)", renderer["rect"]))


def verify(report, cleanup):
    require(not cleanup["exceptions"] and not cleanup["hooks"], "game/hook errors")
    require(cleanup["restored"]["themeNull"] and cleanup["restored"]["profileFileAbsent"]
            and cleanup["restored"]["virtualDeviceRemoved"], "session cleanup")
    snapshots = [r for r in report if "themed" in r]
    require({r["id"] for r in snapshots} == {0, 10, 20, 100, 400, 500}, "six weapon families")
    originals = {r["id"]: {x["path"]: x for x in r["original"]} for r in report if "original" in r}
    fitted = 0
    for trial in snapshots:
        primary = [r for r in trial["themed"] if r["active"] and r["path"].endswith("/BodySprite")]
        require(primary and all(r["sprite"] == "fan.hachiware:weapon.sasumata" for r in primary), "unified sasumata")
        for renderer in primary:
            source = originals[trial["id"]][renderer["path"]]
            require(all(abs(a - b) < .001 for a, b in zip(dimensions(source), dimensions(renderer))), "weapon frame dimensions")
            require(source["shader"] == renderer["shader"], "native material retained")
            fitted += 1
        for renderer in trial["themed"]:
            if renderer["sprite"] and renderer["sprite"].startswith("fan.hachiware:body."):
                require(renderer["fullTexture"], "body/reflection atlas isolation")
            if renderer["active"] and renderer["path"].endswith("WeaponStencil"):
                require(renderer["sprite"] == "fan.hachiware:weapon.sasumata", "weapon/shield mask alignment")
            if renderer["active"] and renderer["path"].endswith(("AddOnSprite", "BladeSpriteAddOn")):
                require(renderer["sprite"] is None, "duplicate weapon decoration hidden")
    captures = [r for r in report if "mode" in r]
    expected = {(weapon, mode) for weapon in (0, 10, 20, 100, 400, 500) for mode in ("basic", "dash", "special")}
    require({(r["id"], r["mode"]) for r in captures} == expected, "18 attacks / three samples each")
    if any("pixel" in r for r in captures):
        require(len(captures) == 108 and all(len([r for r in captures if r["pixel"] == pixel]) == 54 for pixel in (True, False)), "pixel/smooth attack coverage")
    else:
        require(len(captures) == 54, "three attack samples")
    facings = [r for r in report if "facing" in r]
    require(len(facings) == 12, "front/back requests")
    back = sum(any((x["sprite"] or "").startswith("fan.hachiware:body.1.") for x in r["renderers"]) for r in facings if r["facing"] == "back")
    require(back >= 5, "native back states")
    require({r["reviveFrame"] for r in report if "reviveFrame" in r} == {0, 1, 2}, "revive composition captures")
    reload = next(r for r in report if "uiOffSurvivesReload" in r)
    require(reload["uiOffSurvivesReload"] and reload["themeRemains"], "UI OFF reload")
    toggles = next(r["toggles"] for r in report if "toggles" in r)
    require(all(value for key, value in toggles.items() if key != "labels"), "option buttons / rendering / UI restore")
    return {"weaponFamilies": 6, "fittedWeaponRenderers": fitted, "attackCaptures": len(captures),
            "nativeBackStates": back, "togglesPassed": True, "uiOffReloadPassed": True}


if __name__ == "__main__":
    parser = argparse.ArgumentParser()
    parser.add_argument("renderers")
    parser.add_argument("cleanup")
    args = parser.parse_args()
    print(json.dumps(verify(read(args.renderers), read(args.cleanup)), indent=2))
