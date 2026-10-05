"""Verify recorded live-game diagnostics; requires the installed game to produce JSON."""
import argparse
import json
from pathlib import Path


def read(path):
    return json.loads(Path(path).read_text(encoding="utf-8-sig"))


def require(value, label):
    if not value:
        raise ValueError(label)


def entries(report, name):
    return [item["value"] for item in report["results"] if item["name"] == name]


def clean_play(report):
    require(not report["exceptions"] and not report["hooks"], "game/hook exceptions")
    restored = report["restored"]
    require(restored and restored["themeNull"] and restored["virtualDeviceRemoved"]
            and restored["profileFileAbsent"] and restored["audioChannels"] == 0, "play cleanup")
    lobby = entries(report, "solo lobby")[0]
    require(lobby["created"] and lobby["members"] == 1 and not lobby["error"], "solo lobby")
    require(entries(report, "environment")[0]["memoryOnlySave"], "memory-only profile")
    require(entries(report, "gamepad movement")[0]["distance"] > 1, "native gamepad movement")
    require(entries(report, "gamepad attack")[0]["nativeAttacks"] > 0, "native gamepad attack")


def hit_signature(hit):
    # Final crit damage is stochastic; compare native raw damage and hit routing.
    parsed = json.loads(hit)
    return tuple(parsed[key] for key in ("victim", "damage", "from", "failed"))


def verify_combat(report):
    cases = [item["value"] for item in report["results"] if item["name"].startswith("combat ")]
    expected = {(weapon, mode) for weapon in (0, 10, 20, 100, 400, 500)
                for mode in ("basic", "dash", "special")}
    require({(case["id"], case["mode"]) for case in cases} == expected and len(cases) == 18, "18 default weapon trials")
    max_timing = 0
    for case in cases:
        original, themed = case["original"], case["themed"]
        label = f'{case["id"]} {case["mode"]}'
        require(original["events"] == themed["events"] and original["events"], label + " native events")
        if case["mode"] == "special":
            require(any(event.startswith("special") for event in original["events"]), label + " actual special attack")
        require([json.loads(p) for p in original["projectiles"]] == [json.loads(p) for p in themed["projectiles"]]
                and original["projectiles"], label + " damage/shape/colliders")
        require(sorted(map(hit_signature, original["hits"])) == sorted(map(hit_signature, themed["hits"])), label + " native hits")
        if "mpCosts" in original:
            require(original["mpCosts"] == themed["mpCosts"], label + " MP costs")
        require(original["radius"] == original["radiusAfter"] == themed["radius"] == themed["radiusAfter"], label + " player collider")
        require(original["canMove"] and themed["canMove"] and themed["themedFrames"] > 0, label + " completed/themed")
        require(len(original["fireSeconds"]) == len(themed["fireSeconds"]), label + " fire count")
        delta = max(abs(a - b) for a, b in zip(original["fireSeconds"], themed["fireSeconds"]))
        # Allow two measured frames plus 5 ms for coroutine/sample quantization.
        tolerance = 2 * max(original["frameMilliseconds"], themed["frameMilliseconds"]) / 1000 + .005
        require(delta <= tolerance, label + " fire timing")
        max_timing = max(max_timing, delta)
    return {"combatTrials": len(cases), "maxFireTimingDifferenceMs": round(max_timing * 1000, 3)}


def verify_full(report):
    clean_play(report)
    summary = verify_combat(report)
    require(entries(report, "path equivalence")[0]["mismatches"] == 0, "UI path equivalence")
    costumes = entries(report, "costume")
    require(len(costumes) == 4 and all(c["requested"] == c["actual"] and c["themed"] for c in costumes), "costumes")
    for name in ("dungeon entry", "floor transition", "town return", "session restart"):
        require(entries(report, name)[0]["themed"], name)
    life = entries(report, "death revive")[0]
    require(life["died"] and life["revived"] and life["themed"], "death/revive")
    inventory = entries(report, "inventory HUD")[0]
    ui = inventory["roundTrip"]
    require(inventory["opened"] and ui["available"] and ui["checkedGraphics"] > 0
            and ui["changed"] > 0 and ui["textPreserved"] and ui["restored"] and not ui["failures"], "inventory UI round trip")
    languages = entries(report, "language")
    require(len(languages) == 15 and all(l["textPreserved"] and not l["newMissing"] for l in languages), "15 visible UI languages")
    resolutions = entries(report, "resolution selector")
    require({(r["width"], r["height"]) for r in resolutions} == {(1280, 720), (1920, 1080)}
            and all(r["movedFocus"] and r["closedByGamepad"] for r in resolutions), "native gamepad selector at two resolutions")
    reload = entries(report, "reload endurance")[0]
    require(reload["cycles"] == 25 and reload["channels"] == 0, "reload endurance")
    for key in ("textures", "sprites", "fonts", "materials", "bundles"):
        require(reload["before"][key] == reload["after"][key], "reload resource count " + key)
    summary.update(languages=len(languages), reloadCycles=reload["cycles"], resolutions=len(resolutions))
    return summary


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("play")
    parser.add_argument("--controls", required=True)
    parser.add_argument("--encounter", required=True)
    parser.add_argument("--output-probe")
    parser.add_argument("--cleanup")
    args = parser.parse_args()
    summary = verify_full(read(args.play))
    controls = read(args.controls); clean_play(controls)
    keyboard = entries(controls, "keyboard selector")[0]
    require(all(keyboard[k] for k in ("opened", "blocked", "closedByEscape")), "native keyboard selector")
    require(all(entries(controls, "long skin name")[0].values()), "long name ellipsis")
    encounter = read(args.encounter); clean_play(encounter)
    fight = entries(encounter, "live encounter")[0]
    require(fight["isSpawned"] and fight["inBattle"] and fight["outgoing"] > 0 and fight["incoming"] > 0
            and fight["localThemed"] and fight["enemyOriginal"], "live enemy combat")
    for name in ("dungeon entry", "town return"):
        music = entries(encounter, name)[0]["music"]
        require(music["bound"] and music["originalVolume"] == 0 and music["channels"] > 0, name + " music")
    summary.update(liveOutgoingHits=fight["outgoing"], liveIncomingHits=fight["incoming"])
    if args.output_probe:
        probe = read(args.output_probe)
        require(not probe["errors"] and len(probe["packs"]) == 3 and len(probe["zip"]) == 3, "three packs folder/ZIP")
        require(all(p["applied"] for p in probe["packs"] + probe["zip"]), "pack apply")
        for pack in probe["packs"]:
            for key in ("collisionRadiusUnchanged", "failedReloadPreserved", "repeatedReload", "immediateReapply", "fallbacksExcludeSkinFonts"):
                require(pack[key], pack["skin"] + " " + key)
            frames = pack["frames"]
            require(frames["checkedFrames"] > 0 and frames["failures"] == 0 and frames["timelineUnchanged"], pack["skin"] + " frame fixture")
            for field in ("uiRoundTrip", "pooledEffect"):
                value = pack[field]
                if value["available"]:
                    required = ("textPreserved", "restored") if field == "uiRoundTrip" else ("localReplacement", "ownerCleared", "recycledOriginal")
                    require(all(value[key] for key in required), pack["skin"] + " " + field)
            require(all(font["koreanFallback"] for font in pack["fonts"]), pack["skin"] + " font fallback")
        unity = next(pack for pack in probe["packs"] if pack["skin"] == "example.unity")
        require(unity["frames"]["materialsChecked"] > 0 and unity["frames"]["particlesChecked"] > 0, "Unity material/particle fixture")
        require(args.cleanup, "output-probe cleanup report required")
        cleanup = read(args.cleanup)
        require(not cleanup["exceptions"] and not cleanup["errors"] and cleanup["profileAbsent"]
                and cleanup["themeNull"] and cleanup["audioReplacements"] == 0, "output-probe cleanup")
        lifetime = cleanup["fontLifetime"]
        require(lifetime["removedFonts"] > 0 and lifetime["faceResets"] > 0, "font retirement path exercised")
    print(json.dumps(summary, ensure_ascii=False, indent=2))


if __name__ == "__main__":
    main()
