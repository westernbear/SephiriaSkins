"""Verify direct theme switching and original restoration in a live host."""
import argparse
import json
from pathlib import Path

from diagnostic_expectations import expected_trials, verify_weapon_renderers
from verify_play_results import clean_play, entries, require


def verify(report):
    clean_play(report)
    inventory = entries(report, "cycle inventory")[0]
    ids = inventory["packIds"]
    manifests = {m["id"]: m for m in inventory["manifests"]}
    require(len(ids) >= 2 and len(set(ids)) == len(ids) and set(ids) == set(manifests), "cycle manifest identities")
    weapons = {weapon for weapon, _ in expected_trials(inventory["coverage"])}
    originals = {int(key): value for key, value in inventory["originals"].items()}
    require(set(originals) == weapons, "cycle native weapon inventory")
    switches = entries(report, "cycle switch")
    require(inventory["rounds"] == 2 and [(s["round"], s["skin"]) for s in switches] ==
            [(r, skin) for r in range(2) for skin in ids], "two ordered pack cycles")
    checked = 0
    for switch in switches:
        skin = switch["skin"]
        manifest = manifests[skin]
        require(all(switch[k] for k in ("applied", "uiOff", "uiOffAfterReload", "sameBounds", "sampling")), skin + " switches/reload")
        ui = switch["uiRoundTrip"]
        if manifest.get("ui"):
            require(ui["available"] and ui["changed"] > 0 and ui["textPreserved"] and ui["restored"] and not ui["failures"], skin + " UI restoration")
        require({f["id"] for f in switch["fresh"]} == weapons and len(switch["fresh"]) == len(weapons), skin + " fresh equips")
        samples = [(switch["reusedWeapon"], switch["reused"]), (switch["reloadWeapon"], switch["reloadRenderers"])]
        samples.extend((f["id"], f["renderers"]) for f in switch["fresh"])
        for weapon, renderers in samples:
            for renderer in renderers:
                sprite = renderer.get("sprite") or ""
                require(not any(sprite.startswith(other + ":") for other in ids if other != skin), skin + " prior theme sprite")
            checked += verify_weapon_renderers(manifest, originals[weapon], renderers)
    restored = entries(report, "cycle restoration")[0]
    require(restored["themeNull"] and restored["audioChannels"] == 0 and restored["uiOriginal"], "cycle original restoration")
    for renderer in restored["renderers"]:
        require(not any((renderer.get("sprite") or "").startswith(skin + ":") for skin in ids), "cycle original sprite")
    verify_weapon_renderers({"id": "original", "weapons": {}, "visuals": {}}, originals[restored["weapon"]], restored["renderers"])
    require(restored["before"] == restored["after"], "cycle resource counts")
    return {"packIds": ids, "rounds": inventory["rounds"], "weapons": len(weapons), "switches": len(switches), "checkedWeaponRenderers": checked}


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("report")
    args = parser.parse_args()
    report = json.loads(Path(args.report).read_text(encoding="utf-8-sig"))
    print(json.dumps(verify(report), indent=2))
