# Third-party notices

Newtonsoft.Json 13.0.3 — MIT, Copyright James Newton-King. Unity game DLLs are not distributed.

The Windows x64 installer includes the unmodified official [BepInEx 5.4.23.5 distribution](https://github.com/BepInEx/BepInEx/releases/tag/v5.4.23.5), verified against its published SHA256. It contains BepInEx (MIT), HarmonyX 2.9.0 and Harmony interoperability assemblies (MIT), Mono.Cecil 0.10.4 (MIT), MonoMod.RuntimeDetour/Utils 22.1.29.1 (MIT), and Unity Doorstop 4.5.0 (LGPL 2.1). The runtime payload and individual file hashes are pinned in `tools/bepinex-runtime.lock.json`; installation copies no existing user's plugins, settings or cache.

Complete runtime license texts are included in `licenses/BepInEx-MIT.txt`, `licenses/BepInEx.Harmony-MIT.txt`, `licenses/HarmonyX-MIT.txt`, `licenses/Harmony-MIT.txt`, `licenses/Mono.Cecil-MIT.txt`, `licenses/MonoMod-MIT.txt` and `licenses/UnityDoorstop-LGPL-2.1.txt`. Doorstop's corresponding source, including its build scripts and license, is supplied unchanged as `third-party/UnityDoorstop-v4.5.0-source.zip` inside the installer. Its upstream source is [NeighTools/UnityDoorstop v4.5.0](https://github.com/NeighTools/UnityDoorstop/tree/v4.5.0). The shared `winhttp.dll` remains replaceable independently of this mod.

Complete license texts: `licenses/Newtonsoft.Json-MIT.txt`, `licenses/LiberationSans-OFL.txt`, `licenses/Unity-UGUI-TMP.txt`.

The Unity example uses Liberation Sans from Unity TMP Essential Resources under SIL Open Font License 1.1; its license is included beside the example. TextMesh Pro resource shaders follow the notices imported by Unity's TMP package. Generated bundles reference the game's existing TMP asset type and contain no author executable scripts.

Hachiware, Momonga, Chiikawa, Usagi, Kurimanju, Rakko, Shisa and Furuhonya (Kani) are characters from Chiikawa by Nagano. Artwork here is newly generated fan art, not official game or anime artwork. Character rights remain with their respective owners. Each character's `assets/<character>/PROVENANCE.md` records references, prompts and generated assets. Procedural audio is original project content.
