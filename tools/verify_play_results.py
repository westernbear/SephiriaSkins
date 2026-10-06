"""Verify recorded live-game diagnostics; requires the installed game to produce JSON."""
import argparse
import json
from pathlib import Path
from diagnostic_expectations import expected_trials
from verify_output_results import verify as verify_output


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
    if "configBytesRestored" in restored:
        require(restored["configBytesRestored"], "original config bytes restored")
    lobby = entries(report, "solo lobby")[0]
    require(lobby["created"] and lobby["members"] == 1 and lobby["privacy"] == "k_ELobbyTypePrivate" and not lobby["error"], "private solo lobby")
    require(entries(report, "environment")[0]["memoryOnlySave"], "memory-only profile")
    require(entries(report, "gamepad movement")[0]["distance"] > 1, "native gamepad movement")
    require(entries(report, "gamepad attack")[0]["nativeAttacks"] > 0, "native gamepad attack")


def hit_signature(hit):
    # Final crit damage is stochastic; compare native raw damage and hit routing.
    parsed = json.loads(hit)
    return tuple(parsed[key] for key in ("victim", "damage", "from", "failed"))


def native_quick_draw(trial):
    state = trial.get("secondary") or {}
    return (state.get("nativeClass") == "WeaponSimple_Katana"
            and state.get("sheathAction") == "Sheath" and state.get("useQuickDraw")
            and state.get("bladeSheathed") and state.get("sheathEnabled")
            and state.get("animatorGuard") and "basic:2" in trial["events"])


def native_transformed_attack(trial):
    state = trial.get("secondary") or {}
    return (state.get("nativeClass") == "WeaponSimple_GreatSword"
            and state.get("transforms") and state.get("transformed")
            and any(event.startswith("basic:") for event in trial["events"]))


def native_movement_dash(trial):
    return (trial.get("nativeClass") == "WeaponSimple_Dagger" and "nativeDashData" in trial
            and trial["nativeDashData"] is None and trial["events"] == ["dash"]
            and not trial["projectiles"] and not trial.get("unitHits") and not trial["hits"])


def native_crossbow_preparation(trial):
    before, after = trial.get("secondaryBefore") or {}, trial.get("secondary") or {}
    if after.get("nativeClass") != "WeaponSimple_Crossbow" or before.get("nativeClass") != "WeaponSimple_Crossbow":
        return False
    if not any(e.startswith("basic:") for e in trial["events"]):
        return False
    action = after.get("crossbowSpecial")
    if action == "FastReload":
        return (before.get("ammo", -1) < before.get("magazineCapacity", -1)
                and after.get("ammo", -1) == after.get("magazineCapacity", -2) and trial.get("mpCosts"))
    if action == "IceBuff":
        return before.get("iceBuff") == 0 and after.get("iceBuff", 0) > 0
    if action == "AmmoCompression":
        return before.get("compressedAmmo") is False and after.get("compressedAmmo") is True
    return False


def native_charged_bow(trial):
    state = trial.get("bowRelease") or {}
    shots = [json.loads(shot) for shot in trial["projectiles"]]
    return (trial.get("nativeClass") == "WeaponSimple_Bow" and state.get("ratio", 0) >= 1
            and state.get("phase") == 1 and shots and
            all(shot.get("damageId") == "Weapon_BasicAttack" and shot.get("kind") == "Bullet" for shot in shots))


def verify_combat_case(case, require_themed=True):
    original, themed = case["original"], case["themed"]
    label = f'{case["id"]} {case["mode"]}'
    require(original["events"] == themed["events"], label + " native events")
    guard_only = case["mode"] == "special" and original.get("guarded") and themed.get("guarded")
    require(original["events"] or guard_only or native_charged_bow(original) and native_charged_bow(themed), label + " actual attack or guard")
    if case["mode"] == "special":
        require(any(event.startswith("special") for event in original["events"]) or guard_only
                or native_quick_draw(original) and native_quick_draw(themed)
                or native_transformed_attack(original) and native_transformed_attack(themed)
                or native_crossbow_preparation(original) and native_crossbow_preparation(themed), label + " actual special attack, guard or native secondary variant")
        if "secondary" in original:
            require(original["secondary"] == themed["secondary"], label + " native secondary preparation")
        if "secondaryBefore" in original:
            require(original["secondaryBefore"] == themed["secondaryBefore"], label + " equal secondary preconditions")
    movement_only = case["mode"] == "dash" and native_movement_dash(original) and native_movement_dash(themed)
    require([json.loads(p) for p in original["projectiles"]] == [json.loads(p) for p in themed["projectiles"]]
            and (original["projectiles"] or guard_only or movement_only), label + " damage/shape/colliders")
    require(original["guarded"] == themed["guarded"], label + " native guard state")
    require(sorted(map(hit_signature, original["hits"])) == sorted(map(hit_signature, themed["hits"])), label + " native hits")
    if "unitHits" in original:
        require(sorted(map(hit_signature, original["unitHits"])) == sorted(map(hit_signature, themed["unitHits"])), label + " direct unit damage")
    if "mpCosts" in original:
        require(original["mpCosts"] == themed["mpCosts"], label + " MP costs")
    if "moneySpent" in original:
        require(original["preparedMoney"] == themed["preparedMoney"] and original["moneySpent"] == themed["moneySpent"], label + " native money costs")
    require(original["radius"] == original["radiusAfter"] == themed["radius"] == themed["radiusAfter"], label + " player collider")
    if "preparedBuffs" in original:
        require(original["preparedBuffs"] == themed["preparedBuffs"], label + " equal prepared native buffs")
    if "preparedTargetBuffs" in original:
        require(original["preparedTargetBuffs"] == themed["preparedTargetBuffs"], label + " equal prepared target buffs")
    if "preparedTargetDebuffs" in original:
        require(original["preparedTargetDebuffs"] == themed["preparedTargetDebuffs"] == [], label + " equal cleared target debuffs")
    if "targetStatusComplete" in original:
        require(original["targetStatusComplete"] and themed["targetStatusComplete"], label + " native target status lifetime completed")
    if original.get("mineLifecycle") or themed.get("mineLifecycle"):
        first, second = original.get("mineLifecycle") or {}, themed.get("mineLifecycle") or {}
        require(first.get("targetEligible") is second.get("targetEligible") and first.get("detected") == second.get("detected"), label + " native mine fixture preconditions")
        for trial in (original, themed):
            mine = trial.get("mineLifecycle") or {}
            require(mine.get("detected", 0) > 0 and "targetEligible" in mine and mine.get("reason"), label + " recorded mine scope")
            if mine["targetEligible"]:
                require(all(mine.get(key) for key in ("landed", "targetMoved", "detonated"))
                        and mine.get("nativeHitsAfterMove", 0) > 0, label + " native grounded mine detonation and damage")
            else:
                require(not mine["targetMoved"], label + " ineligible mine target is not a detonation pass")
    require(original["canMove"] and themed["canMove"], label + " completed")
    require(original["themedFrames"] == 0 and not any(original.get("themedRoles", {}).values()), label + " original visual restoration")
    if require_themed:
        require(themed["themedFrames"] > 0, label + " themed body frames")
    else:
        require(original["themedFrames"] == themed["themedFrames"] == 0, label + " native baseline body frames")
    require(len(original["fireSeconds"]) == len(themed["fireSeconds"]), label + " fire count")
    delta = max((abs(a - b) for a, b in zip(original["fireSeconds"], themed["fireSeconds"])), default=0)
    # Allow two measured frames plus 5 ms for coroutine/sample quantization.
    tolerance = 2 * max(original["frameMilliseconds"], themed["frameMilliseconds"]) / 1000 + .005
    require(delta <= tolerance, label + " fire timing")
    return delta


def verify_combat(report, require_themed=True):
    cases = [item["value"] for item in report["results"] if item["name"].startswith("combat ")]
    coverage = entries(report, "weapon coverage")[0]
    expected = expected_trials(coverage) if "weapons" in coverage else {
        (weapon["id"], mode) for weapon in coverage["defaults"] for mode in ("basic", "dash", "special")}
    require({(case["id"], case["mode"]) for case in cases} == expected and len(cases) == len(expected), "enumerated weapon trials")
    max_timing = 0
    for case in cases:
        max_timing = max(max_timing, verify_combat_case(case, require_themed))
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
    endurance = entries(report, "automated endurance")
    if endurance:
        soak = endurance[0]
        require(soak["completed"] and soak["elapsedSeconds"] >= soak["requestedMinutes"] * 60
                and soak["cycles"] > 0 and soak["actions"] > 0 and soak["actions"] + soak.get("skippedActions", 0) == soak["cycles"] * 3 and not soak["failures"], "automated endurance")
        summary["automatedEnduranceMinutes"] = round(soak["elapsedSeconds"] / 60, 2)
        lobby_states = entries(report, "endurance lobby start") + entries(report, "endurance lobby end")
        if lobby_states:
            require(len(lobby_states) == 2 and all(l["created"] and l["members"] == 1 and
                    l["privacy"] == "k_ELobbyTypePrivate" and not l["error"] for l in lobby_states), "endurance private solo lobby")
        summary["enduranceLobbyEndpointsRecorded"] = len(lobby_states) == 2
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
    if "packId" in read(args.play):
        identities = [read(args.play), controls, encounter]
        require(len({r.get("packId") for r in identities}) == 1, "same selected pack in all play reports")
        if any("pluginSha256" in r for r in identities):
            require(len({r.get("pluginSha256") for r in identities}) == 1, "same plugin build in all play reports")
    fight = entries(encounter, "live encounter")[0]
    require(fight["isSpawned"] and fight["inBattle"] and fight["outgoing"] > 0 and fight["incoming"] > 0
            and fight["localThemed"] and fight["enemyOriginal"], "live enemy combat")
    if "livingBodyFrames" in fight:
        require(fight["livingBodyFrames"] > 0 and fight["livingBodyFrames"] == fight["themedLivingBodyFrames"], "all visible living combat frames themed")
    for name in ("dungeon entry", "town return"):
        music = entries(encounter, name)[0]["music"]
        require(music["bound"] and music["originalVolume"] == 0 and music["channels"] > 0, name + " music")
    summary.update(liveOutgoingHits=fight["outgoing"], liveIncomingHits=fight["incoming"])
    mines = entries(encounter, "live mine encounter")
    if mines:
        mine = mines[0]
        if mine["available"]:
            require(mine["fires"] > 0 and all(mine[k] for k in ("landed", "targetMoved", "detonated", "localThemed"))
                    and mine["hits"] > 0, "actual enemy proximity mine detonation")
        else:
            require(mine.get("reason"), "unexecuted enemy mine reason")
        summary["liveMineEncounter"] = mine
    if args.output_probe:
        require(args.cleanup, "output-probe cleanup report required")
        summary["output"] = verify_output(read(args.output_probe), read(args.cleanup))
    print(json.dumps(summary, ensure_ascii=False, indent=2))


if __name__ == "__main__":
    main()
