"""Inspect generated PCM assets without changing them or claiming audition."""
import argparse
import hashlib
import json
import math
import struct
import wave
from pathlib import Path

EXPECTED = {"attack", "hurt", "death", "revive", "slash", "dash", "impact", "menu", "charge_loop",
            "menu_music", "explore_music", "battle_music"}


def inspect(directory):
    directory = Path(directory)
    provenance = json.loads((directory / "audio-provenance.json").read_text(encoding="utf-8-sig"))
    declared = {entry["file"]: entry for entry in provenance["files"]}
    expected = {name + ".wav" for name in EXPECTED}
    if set(declared) != expected or len(provenance["files"]) != len(expected):
        raise ValueError("Expected twelve distinct character cues and music tracks")
    cases = []
    for name in sorted(expected):
        path = directory / name
        digest = hashlib.sha256(path.read_bytes()).hexdigest()
        if declared[name].get("sha256") != digest:
            raise ValueError("Recorded audio hash differs: " + name)
        with wave.open(str(path), "rb") as source:
            if (source.getnchannels(), source.getsampwidth(), source.getframerate(), source.getcomptype()) != (1, 2, 22050, "NONE"):
                raise ValueError("Expected mono 16-bit 22050 Hz PCM: " + name)
            payload = source.readframes(source.getnframes())
            samples = struct.unpack("<" + "h" * (len(payload)//2), payload)
        if not samples or len(set(samples)) < 16:
            raise ValueError("Empty or constant audio: " + name)
        peak = max(abs(value) for value in samples)/32767
        if peak > .79:
            raise ValueError("Audio exceeds authored headroom: " + name)
        loop = name == "charge_loop.wav" or name.endswith("_music.wav")
        boundary = abs(samples[-1]-samples[0])/32767
        if loop and (boundary > .001 or max(abs(samples[0]), abs(samples[-1]))/32767 > .001):
            raise ValueError("Discontinuous authored loop endpoints: " + name)
        cases.append({"file": name, "sha256": digest, "samples": len(samples), "seconds": len(samples)/22050,
                      "peak": peak, "rms": math.sqrt(sum(value*value for value in samples)/len(samples))/32767,
                      "dcMean": sum(samples)/len(samples)/32767, "loop": loop, "loopBoundaryDelta": boundary})
    return {"character": provenance["character"], "format": "mono 16-bit PCM at 22050 Hz", "files": cases,
            "technicalChecksPassed": True, "listeningConfirmed": False,
            "scope": "File integrity, decoded PCM, nonempty signal, peak headroom and loop endpoints; does not establish perceived sound quality or native event playback"}


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("directory")
    parser.add_argument("output", help="New inspection JSON; audio is never changed")
    args = parser.parse_args()
    output = Path(args.output)
    if output.exists():
        parser.error("Inspection output must be new")
    result = inspect(args.directory)
    output.write_text(json.dumps(result, indent=2), encoding="utf-8")
    print(json.dumps({"character": result["character"], "files": len(result["files"]), "technicalChecksPassed": True, "listeningConfirmed": False}))
