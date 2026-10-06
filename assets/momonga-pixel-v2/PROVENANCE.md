# Momonga pixel-art theme 1.0.1

Status: new body, effects and portrait/UI atlases visually reviewed and inspected. Folder/ZIP loading and focused native game checks are recorded separately in docs/evidence/momonga-pixel-021-20261007.

The new native visual check did not complete: Steam initialization failed or the Steam client shut down before visual results were recorded. It is unverified; the prior 0.2.0 gameplay pass is not counted for the new artwork. Existing saves and the main game's config were unchanged.

The user requested a pixel-style regeneration of the existing Momonga theme on 2026-10-06. The pack keeps its fan.momonga ID. The prior source artwork is retained in assets/momonga and the previously published 1.0.0 pack remains available in release 0.2.0. This replacement is explicitly authorized; no other character pack is changed.

Appearance references: [official anime material](https://www.anime-chiikawa.jp/) and [official Momonga merchandise](https://chiikawamarket.jp/products/4571609380306). White head/body, round sky-blue inner ears, blue oval nose, white paws and a single attached fluffy blue tail are retained. Official artwork is not included. This is unofficial fan art, unaffiliated with the rights holders.

Three new original PNGs were produced with the built-in OpenAI image_gen tool on 2026-10-06, requesting transparent backgrounds. Existing Momonga atlases were identity, pose-order and UI-role references. Exact executed prompts, original dimensions and hashes are recorded in generation-records.txt and generated-drafts.json. The result uses stepped pixel contours and compact flat clusters; the requested canvas dimensions are distinguished from the tool's actual dimensions in the records. Original PNGs remain unmodified.

The user already authorized scripted alpha-residue removal and cell-padding cleanup. tools/prepare_sprite_atlas.py isolates exterior alpha and places visible artwork in padded cells without scaling or recoloring. Retained visible RGBA is copied verbatim; hidden RGB at alpha zero is cleared. Matching cleanup records include original/selected hashes and reviewed source boundaries. Strict inspection and visual review covered 32 connected body poses, 24 separate effect phases and eight UI elements including a portrait. Back poses are faceless, ears and tails fit, and body, effects and weapons remain separate.

Audio is unchanged from the same character's 1.0.0 pack: twelve procedural vocal/effect/music cues generated with profile momonga, seed 101. audio/audio-provenance.json and audio-inspection.json retain original generation parameters and decoded checks. No other character's audio or original game/voice recording is copied. Listening was waived by the user and is not claimed for this update.

The catalog compiler preserves all 1,100 body states and 6,405 ordered frames, native FPS, events, loops, transitions, collision and networking. Native weapons are retained; weapon bindings, weapon masks and decoration hiding are omitted. Pixel/UI toggles continue to use the existing runtime settings. The selector retains native UI. Only reviewed resources and production records enter the skin pack; no executable author code or game DLL is included.
