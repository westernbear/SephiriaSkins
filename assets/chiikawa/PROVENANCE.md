# chiikawa fan-art theme production record

Status: selected atlases inspected and the pack validated from folder/ZIP. Eight-pack switching/restoration and installer-only native output passed on 2026-10-06 with plugin SHA256 95f1c4c276efc1a88ba640acd1ae46dd83bffbb157678ad6a40e9d55313c761e. Final repeated full combat runs and the 60-minute soak were omitted at the user's request; earlier character production runs have separate DLL hashes and scopes in docs/evidence/series-020-20261006/README.md.

Appearance references: [official anime material](https://www.anime-chiikawa.jp/) and [official character merchandise](https://chiikawamarket.jp/products/4571609380276). Reviewed traits: Chiikawa: a very small pure-white round bear-like creature, two tiny round ears, no hair cap, tiny white round tail, dark brown oval eyes with white highlights, slightly worried gentle eyebrows, three short blush lines on each pink cheek, little W mouth and tiny white paws. No clothing. Official artwork is not included. This is newly generated unofficial fan art, unaffiliated with the rights holders.

Separate character weapon references: https://chiikawamarket.jp/products/4571609354956. A single complete PINK SASUMATA: one long straight slim pink shaft with a symmetric open U-shaped two-prong fork at its top, rounded pale-white bead-like caps on the two prong tips. No extra prongs. Upright, shaft bottom down, fork up, whole object centered. Clean dark-cocoa outline, flat pink fill and pale-white tip caps.

The original PNGs were generated with the built-in OpenAI image_gen tool requesting transparent_background=true. Actual dates, exact prompts and original/selected SHA256 hashes are in generation-records.txt and generated-drafts.json. Body, effects, UI/portrait and any weapon are separately authored. No other character's images or audio are copied.

The user explicitly authorized scripted alpha-residue removal and cell-padding cleanup on 2026-10-06. Matching cleanup JSON records original/output hashes, reviewed source boundaries and retained-pixel checks. Artwork is copied unscaled without recoloring visible RGBA. Only removed exterior alpha and hidden RGB at alpha zero are cleared. Original images remain unmodified. Selected PNGs were inspected for complete connected body poses, empty gutters, positive-alpha content and consistent anchors. Their inspected files are:

- `body-final.png` SHA256 `cc5ae8545c441c1c37ee0725912a0fa5c7aee876734c8bd7664a8a09494b4891`, report `body-inspection.json`
- `effects-final.png` SHA256 `22718bba9d5c2d93e227e51f9a911a7857b21c91a770fa34b7931519af156a88`, report `effects-inspection.json`
- `ui-final.png` SHA256 `63768f0a389a9a698c15c6bc759bb66ad65606541aa5e37f2fabdaf7c1b7ac7e`, report `ui-inspection.json`
- `weapon-final.png` SHA256 `3f8f7d6bd349efaed9087ac4716b801cb74c023800dbac4db289ab7a4a49dbe7`, report `weapon-inspection.json`

Audio: twelve newly synthesized nonverbal vocalizations, effects, a separate sustained cue and original menu/exploration/battle music. audio/audio-provenance.json records character-specific melody, timbre, rhythm, parameters and hashes. audio-inspection.json independently checks decoded PCM, nonempty signal, peak headroom and loop endpoints. No original voice recording or game audio is sampled. Listening evaluation and actual native-event playback are separate; neither is claimed by file inspection. Listening evaluation was explicitly waived by the user on 2026-10-06; no audition or perceived sound-quality pass is claimed.

The catalog recipe connects all 1,100 body states and 6,405 ordered frames without modifying native FPS, transitions, loops, attack events, collision or network behavior. Only selected resources and production records enter the pack; source code and game DLLs are excluded. Actual game evidence is recorded separately under docs/evidence/series-020-20261006.
