# usagi fan-art theme production record

Status: selected atlases inspected and the pack validated from folder/ZIP. Eight-pack switching/restoration and installer-only native output passed on 2026-10-06 with plugin SHA256 95f1c4c276efc1a88ba640acd1ae46dd83bffbb157678ad6a40e9d55313c761e. Final repeated full combat runs and the 60-minute soak were omitted at the user's request; earlier character production runs have separate DLL hashes and scopes in docs/evidence/series-020-20261006/README.md.

Appearance references: [official anime material](https://www.anime-chiikawa.jp/) and [official character merchandise](https://chiikawamarket.jp/products/4571609380290). Reviewed traits: Usagi: a pale warm-yellow rabbit with two LONG upright ears with pink inner ears, very round head and little body, tiny round white pompom tail, dark brown oval eyes with white highlights, high arched mischievous eyebrows, pink cheeks and little smiling W mouth. No clothing. Keep the complete long ears inside every cell. Official artwork is not included. This is newly generated unofficial fan art, unaffiliated with the rights holders.

Separate character weapon references: https://chiikawamarket.jp/products/4571609354970. A single complete straight YELLOW TORUBOU: a long slim yellow cylindrical baton with one rounded WHITE cap at EACH end. It is a straight rod, with no fork, blade, handle guard, ears, ribbon or ornamental branches. Upright and fully centered. Clean dark-cocoa outline, warm yellow fill and white caps.

The original PNGs were generated with the built-in OpenAI image_gen tool requesting transparent_background=true. Actual dates, exact prompts and original/selected SHA256 hashes are in generation-records.txt and generated-drafts.json. Body, effects, UI/portrait and any weapon are separately authored. No other character's images or audio are copied.

The user explicitly authorized scripted alpha-residue removal and cell-padding cleanup on 2026-10-06. Matching cleanup JSON records original/output hashes, reviewed source boundaries and retained-pixel checks. Artwork is copied unscaled without recoloring visible RGBA. Only removed exterior alpha and hidden RGB at alpha zero are cleared. Original images remain unmodified. Selected PNGs were inspected for complete connected body poses, empty gutters, positive-alpha content and consistent anchors. Their inspected files are:

- `body-final.png` SHA256 `6e304bbc689b1ce99b01394bc3f40f74892e09ec2eafea7e42d1316582521783`, report `body-inspection.json`
- `effects-final.png` SHA256 `ad4b7d8f25a78519bd3973563cbccac33fb5a2b78a5fc6d46a4f0cc74fcfda59`, report `effects-inspection.json`
- `ui-final.png` SHA256 `4bba8449bb49a942addd20aaad74471226dcb5cb9da96d33b5a1e5ec3281b6b4`, report `ui-inspection.json`
- `weapon-final.png` SHA256 `128fc3e19ebfccc2e6bc3798ee7bf44cce06f4428786c5acc91fd04821d6c811`, report `weapon-inspection.json`

Audio: twelve newly synthesized nonverbal vocalizations, effects, a separate sustained cue and original menu/exploration/battle music. audio/audio-provenance.json records character-specific melody, timbre, rhythm, parameters and hashes. audio-inspection.json independently checks decoded PCM, nonempty signal, peak headroom and loop endpoints. No original voice recording or game audio is sampled. Listening evaluation and actual native-event playback are separate; neither is claimed by file inspection. Listening evaluation was explicitly waived by the user on 2026-10-06; no audition or perceived sound-quality pass is claimed.

The catalog recipe connects all 1,100 body states and 6,405 ordered frames without modifying native FPS, transitions, loops, attack events, collision or network behavior. Only selected resources and production records enter the pack; source code and game DLLs are excluded. Actual game evidence is recorded separately under docs/evidence/series-020-20261006.
