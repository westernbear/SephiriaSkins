# shisa fan-art theme production record

Status: selected atlases inspected and the pack validated from folder/ZIP. Eight-pack switching/restoration and installer-only native output passed on 2026-10-06 with plugin SHA256 95f1c4c276efc1a88ba640acd1ae46dd83bffbb157678ad6a40e9d55313c761e. Final repeated full combat runs and the 60-minute soak were omitted at the user's request; earlier character production runs have separate DLL hashes and scopes in docs/evidence/series-020-20261006/README.md.

Appearance references: [official anime material](https://www.anime-chiikawa.jp/) and [official character merchandise](https://chiikawamarket.jp/products/4571609380320). Reviewed traits: Shisa: a small cream lion-dog with little pointed cream ears, a distinctive orange-gold CURLY MANE surrounding the sides and lower edge of the head, two curled orange-gold eyebrows, dark brown oval eyes, pink cheeks and tiny cream paws. Back view has a cream-white rear head framed by the orange-gold curls and a tiny attached tail with an orange tip. No blue, green, clothes, food or accessories. Official artwork is not included. This is newly generated unofficial fan art, unaffiliated with the rights holders.

Native weapons are retained. Weapon bindings, masks and decoration hiding are omitted.

The original PNGs were generated with the built-in OpenAI image_gen tool requesting transparent_background=true. Actual dates, exact prompts and original/selected SHA256 hashes are in generation-records.txt and generated-drafts.json. Body, effects, UI/portrait and any weapon are separately authored. No other character's images or audio are copied.

The user explicitly authorized scripted alpha-residue removal and cell-padding cleanup on 2026-10-06. Matching cleanup JSON records original/output hashes, reviewed source boundaries and retained-pixel checks. Artwork is copied unscaled without recoloring visible RGBA. Only removed exterior alpha and hidden RGB at alpha zero are cleared. Original images remain unmodified. Selected PNGs were inspected for complete connected body poses, empty gutters, positive-alpha content and consistent anchors. Their inspected files are:

- `body-final.png` SHA256 `196c14f049f235654c99e8b82b1921a2dede6dae3b1ac8a140877ec66e7558b6`, report `body-inspection.json`
- `effects-final.png` SHA256 `efc2a8d67d1f18276f0bd25abf967f4d1878ae1d6a418b480b23ae3eb93e7a77`, report `effects-inspection.json`
- `ui-final.png` SHA256 `99ee39bd0d54cbff56ab415947104077b67583968d79ff6222291a8ed21f8e5c`, report `ui-inspection.json`

Audio: twelve newly synthesized nonverbal vocalizations, effects, a separate sustained cue and original menu/exploration/battle music. audio/audio-provenance.json records character-specific melody, timbre, rhythm, parameters and hashes. audio-inspection.json independently checks decoded PCM, nonempty signal, peak headroom and loop endpoints. No original voice recording or game audio is sampled. Listening evaluation and actual native-event playback are separate; neither is claimed by file inspection. Listening evaluation was explicitly waived by the user on 2026-10-06; no audition or perceived sound-quality pass is claimed.

The catalog recipe connects all 1,100 body states and 6,405 ordered frames without modifying native FPS, transitions, loops, attack events, collision or network behavior. Only selected resources and production records enter the pack; source code and game DLLs are excluded. Actual game evidence is recorded separately under docs/evidence/series-020-20261006.
