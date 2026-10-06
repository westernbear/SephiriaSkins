"""Verify folder/ZIP, decoder, cosmetic frame and restoration evidence independently."""
import argparse
import json
from pathlib import Path


def read(path):
    return json.loads(Path(path).read_text(encoding="utf-8-sig"))


def require(value, label):
    if not value:
        raise ValueError(label)


def verify(probe, cleanup):
    if probe.get("lobby"):
        lobby = probe["lobby"]
        require(lobby["created"] and lobby["members"] == 1 and lobby["privacy"] == "k_ELobbyTypePrivate" and not lobby["error"], "private solo lobby")
    require(not probe["errors"] and probe["packs"] and
            {p["skin"] for p in probe["packs"]} == {p["skin"] for p in probe["zip"]}, "same packs folder/ZIP")
    require(all(p["applied"] for p in probe["packs"] + probe["zip"]), "pack apply")
    if "runId" in probe:
        require(probe["runId"] == cleanup["runId"] and probe["packId"] == cleanup["packId"], "matching output run")
    for zipped in probe["zip"]:
        if "duplicateIdsExcludeBoth" in zipped:
            require(zipped["duplicateIdsExcludeBoth"], zipped["skin"] + " duplicate IDs")
    for pack in probe["packs"]:
        label = pack["skin"] + " "
        for key in ("collisionRadiusUnchanged", "failedReloadPreserved", "repeatedReload", "immediateReapply", "fallbacksExcludeSkinFonts"):
            require(pack[key], label + key)
        frames = pack["frames"]
        require(frames["checkedFrames"] > 0 and frames["failures"] == 0 and frames["timelineUnchanged"], label + "frame fixture")
        if "missingStates" in frames:
            require(not frames["missingStates"] and frames["states"] == frames["expectedStates"], label + "complete body states")
        for negative in pack.get("negativeReloads", []):
            require(negative["preserved"] and negative["acceptedSnapshot"] == negative["decodeFixture"], label + "negative reload " + negative["kind"])
        for field, keys in (
            ("uiRoundTrip", ("textPreserved", "restored")),
            ("pooledEffect", ("localReplacement", "ownerCleared", "recycledOriginal")),
            ("audioSettings", ("masterMute", "pause", "pitchTracking", "originalSuppressed")),
            ("musicStop", ("started", "originalMutedDuringStop", "replacementStopped")),
        ):
            value = pack[field]
            if value["available"]:
                require(all(value[key] for key in keys) and not value.get("failures"), label + field)
        require(all(pack["selector"].values()), label + "native selector/details")
        if pack.get("audioEvents", {}).get("available"):
            events = pack["audioEvents"]["cases"]
            for event in events:
                require(all(event[k] for k in ("started", "loopMatches", "stopped", "restarted", "released")), label + "audio lifetime " + event["path"])
                if "releaseMarked" in event:
                    require(event["releaseMarked"] and event["releasePlaybackTracked"] and event["nativeReleased"], label + "native deferred release and stop cleanup")
                require(event["sustained"] if event["loop"] else event["naturalEnd"], label + "audio natural/continuous lifetime")
                settings = event["settings"]
                require(settings["available"] and all(settings[k] for k in ("masterMute", "channelMute", "volumeTracking", "instancePause", "volumeRestored", "pause", "pitchTracking", "originalSuppressed")), label + "audio event settings")
        require(all(font["koreanFallback"] for font in pack["fonts"]), label + "font fallback")
    unity = next((pack for pack in probe["packs"] if pack["skin"] == "example.unity"), None)
    if unity:
        require(unity["frames"]["materialsChecked"] > 0 and unity["frames"]["particlesChecked"] > 0, "Unity material/particle fixture")
    require(not cleanup["exceptions"] and not cleanup["errors"] and cleanup["profileAbsent"] and
            cleanup["themeNull"] and cleanup["audioReplacements"] == 0, "output cleanup")
    if "configBytesRestored" in cleanup:
        require(cleanup["configBytesRestored"], "original output config bytes restored")
    if any(pack["fonts"] for pack in probe["packs"]):
        require(cleanup["fontLifetime"]["removedFonts"] > 0 and cleanup["fontLifetime"]["faceResets"] > 0, "font retirement exercised")
    return {"packs": [{"id": pack["skin"], "states": pack["frames"]["states"], "frames": pack["frames"]["checkedFrames"],
                       "audioSettingsAvailable": pack["audioSettings"]["available"]} for pack in probe["packs"]], "cleanupPassed": True}


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("output")
    parser.add_argument("cleanup")
    args = parser.parse_args()
    print(json.dumps(verify(read(args.output), read(args.cleanup)), ensure_ascii=False, indent=2))
