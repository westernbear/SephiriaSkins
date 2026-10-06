"""Original character-specific procedural cues and music, without sampled voices.

Use a new output directory. A manifest maps these cue names to actual catalog
GUIDs; the generator never edits event lifetime or game audio settings.
"""
import argparse
import hashlib
import json
import math
import random
import struct
import wave
from pathlib import Path

RATE = 22050
PROFILES = {
    "momonga": dict(seed=101, key=65, scale=[0, 2, 4, 7, 9], melody=[0, 4, 2, 7, 4, 9, 7, 2], bpm=118, instrument="bell", rhythm=[0, .5, 1.5, 2, 3], voice=630),
    "chiikawa": dict(seed=202, key=60, scale=[0, 2, 4, 5, 7, 9, 11], melody=[0, 2, 4, 2, 7, 4, 2, 0], bpm=94, instrument="flute", rhythm=[0, 1, 2.5, 3], voice=510),
    "usagi": dict(seed=303, key=62, scale=[0, 2, 4, 7, 9], melody=[7, 0, 9, 4, 7, 2, 9, 0], bpm=152, instrument="pluck", rhythm=[0, .5, 1, 1.5, 2.5, 3.5], voice=760),
    "kurimanju": dict(seed=404, key=57, scale=[0, 2, 3, 5, 7, 9, 10], melody=[0, 3, 7, 10, 9, 7, 5, 3], bpm=82, instrument="reed", rhythm=[0, .67, 2, 2.67, 3.67], voice=260),
    "rakko": dict(seed=505, key=55, scale=[0, 2, 3, 5, 7, 8, 10], melody=[0, 7, 5, 3, 2, 7, 10, 5], bpm=126, instrument="string", rhythm=[0, .75, 1.5, 2, 3], voice=340),
    "shisa": dict(seed=606, key=64, scale=[0, 4, 5, 7, 11], melody=[0, 5, 7, 11, 7, 5, 4, 0], bpm=106, instrument="mallet", rhythm=[0, 1, 1.5, 2.5, 3], voice=580),
    "furuhonya": dict(seed=707, key=63, scale=[0, 2, 4, 5, 7, 9, 11], melody=[4, 2, 0, 7, 5, 4, 9, 7], bpm=102, instrument="celesta", rhythm=[0, .5, 2, 2.5], voice=550),
}


def tone(frequency, t, instrument):
    phase = 2 * math.pi * frequency * t
    harmonics = {
        "bell": [(1, 1), (2.01, .25), (3.96, .09)],
        "flute": [(1, 1), (2, .1), (3, .04)],
        "pluck": [(1, 1), (2, .35), (3, .18)],
        "reed": [(1, 1), (3, .25), (5, .1)],
        "string": [(1, 1), (2, .28), (3, .18), (4, .09)],
        "mallet": [(1, 1), (3, .15), (6, .04)],
        "celesta": [(1, 1), (2, .18), (4, .07)],
    }[instrument]
    return sum(math.sin(phase * multiplier) * gain for multiplier, gain in harmonics) / 1.6


def write(path, samples):
    peak = max(map(abs, samples), default=0)
    gain = min(1, .78 / peak) if peak else 1
    with wave.open(str(path), "wb") as wav:
        wav.setparams((1, 2, RATE, 0, "NONE", "not compressed"))
        wav.writeframes(b"".join(struct.pack("<h", round(x * gain * 32767)) for x in samples))
    return {"file": path.name, "seconds": round(len(samples) / RATE, 3), "peak": round(peak * gain, 5),
            "sha256": hashlib.sha256(path.read_bytes()).hexdigest()}


def vocal(profile, contour):
    samples = []
    for ratio, duration in contour:
        for i in range(round(duration * RATE)):
            t = i / RATE
            frequency = profile["voice"] * ratio
            phase = 2 * math.pi * frequency * (t + .0018 * math.sin(t * 32))
            envelope = min(1, t / .012) * min(1, (duration-t) / .05)
            # Newly synthesized vowel-like chirps; no speech or actor recording.
            value = sum(math.sin(phase * k) * math.exp(-((frequency*k-1450)/900)**2) / k for k in range(1, 9))
            samples.append(value * envelope * .32)
        samples.extend([0] * round(.018 * RATE))
    return samples


def music(profile, variant):
    tempo = profile["bpm"] * {"menu": .9, "explore": 1, "battle": 1.22}[variant]
    beat = 60 / tempo
    result = [0.] * round(32 * beat * RATE)
    decay = 3 if profile["instrument"] in ("bell", "pluck", "mallet", "celesta") else 1.2

    def note(start, length, midi, gain, instrument):
        frequency = 440 * 2 ** ((midi - 69) / 12)
        for i in range(round(length * RATE)):
            at = round(start * RATE) + i
            if at >= len(result):
                break
            t = i / RATE
            envelope = min(1, t / .015) * max(0, min(1, (length-t) / .06)) * math.exp(-t * decay)
            result[at] += tone(frequency, t, instrument) * envelope * gain

    offset = {"menu": 0, "explore": 2, "battle": 4}[variant]
    for bar in range(8):
        root = profile["key"] + [0, 5, 7, 0][bar % 4]
        for pulse in (0, 2):
            note((bar*4+pulse)*beat, beat*1.7, root-12, .14, "pluck")
        scale = profile["scale"]
        for pulse, shift in enumerate((scale[0], scale[2], scale[4], scale[2])):
            note((bar*4+pulse)*beat, beat*.7, root+shift, .075, profile["instrument"])
        for step, position in enumerate(profile["rhythm"]):
            index = (bar*len(profile["rhythm"])+step+offset) % len(profile["melody"])
            pitch = profile["key"] + 12 + profile["melody"][index]
            note((bar*4+position)*beat, beat*.6, pitch, .22, profile["instrument"])
        if variant == "battle":
            for position in (0, 1, 2, 3):
                note((bar*4+position)*beat, .09, 36 if position % 2 == 0 else 43, .1, "mallet")
    # Silence at both loop endpoints avoids a discontinuity; event controls stop.
    fade = round(.035 * RATE)
    for i in range(fade):
        result[i] *= i/fade
        result[-i-1] *= i/fade
    return result


def generate(character, output):
    profile = PROFILES[character]
    output = Path(output)
    if output.exists():
        raise ValueError("Audio output must be a new directory")
    output.mkdir(parents=True)
    records = []
    for name, contour in {
        "attack": [(1, .12), (1.2, .1)], "hurt": [(1.3, .11), (.8, .16)],
        "death": [(1, .12), (.75, .18), (.55, .22)],
        "revive": [(.8, .1), (1, .12), (1.4, .23)]}.items():
        records.append(write(output / (name+".wav"), vocal(profile, contour)))
    rng = random.Random(profile["seed"])
    for name, duration, ratio in (("slash", .18, 1.7), ("dash", .22, 2.2), ("impact", .16, .6), ("menu", .12, 2)):
        samples = []
        for i in range(round(duration*RATE)):
            t = i/RATE
            envelope = math.exp(-t*20) * min(1, t/.008) * min(1, (duration-t)/.01)
            value = tone(profile["voice"]*ratio*(1-t/duration*.5), t, profile["instrument"])
            noise = (rng.random()*2-1) * (.08 if name != "menu" else .005)
            samples.append((value*.4+noise)*envelope)
        records.append(write(output / (name+".wav"), samples))
    for variant in ("menu", "explore", "battle"):
        records.append(write(output / (variant+"_music.wav"), music(profile, variant)))
    # A separate quiet sustained cue follows native charging/minigun/spell
    # event stop and release. It is never baked into an attack voice or music.
    duration = .8
    samples = []
    for i in range(round(duration*RATE)):
        t = i/RATE
        envelope = min(1, t/.035) * min(1, (duration-t)/.035)
        samples.append(tone(profile["voice"]*.5, t, profile["instrument"]) * envelope * .14)
    records.append(write(output / "charge_loop.wav", samples))
    provenance = {"character": character, "profile": profile, "rate": RATE,
                  "method": "Original additive synthesis; no sampled music, speech or game recordings", "files": records}
    (output / "audio-provenance.json").write_text(json.dumps(provenance, indent=2), encoding="utf-8")
    return provenance


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("character", choices=PROFILES)
    parser.add_argument("output", nargs="?")
    parser.add_argument("--describe", action="store_true", help="Show synthesis profile without creating assets")
    args = parser.parse_args()
    if args.describe:
        print(json.dumps(PROFILES[args.character], indent=2))
    elif args.output:
        print(json.dumps(generate(args.character, args.output), indent=2))
    else:
        parser.error("output directory is required")
