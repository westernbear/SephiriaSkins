using System.Collections;
using SephiriaSkins.Core;
using TMPro;
using UnityEngine;
using Object = UnityEngine.Object;
namespace SephiriaSkins.Plugin;

internal sealed class RuntimeTheme : IDisposable
{
    public PackSnapshot Pack { get; }
    public Dictionary<string, Object> Assets { get; } = new();
    public Dictionary<string, FMOD.Sound> Sounds { get; } = new();
    private readonly List<FMOD.Sound> ownedSounds = new();
    public int AssetCount => Assets.Count + Sounds.Count;
    private readonly List<Object> owned = new();
    private sealed class SharedBundle { public AssetBundle Bundle = null!; public int References; }
    private static readonly Dictionary<string, SharedBundle> sharedBundles = new();
    private static readonly Dictionary<int, int> skinFonts = new();
    private readonly List<string> bundleLeases = new();
    private readonly HashSet<int> fontIds = new();
    private void TrackFont(TMP_FontAsset font)
    {
        var id = font.GetInstanceID();
        if (fontIds.Add(id)) skinFonts[id] = skinFonts.TryGetValue(id, out var count) ? count + 1 : 1;
    }
    private RuntimeTheme(PackSnapshot pack) => Pack = pack;
    public T Get<T>(string id) where T : Object => (T)Assets[id];
    public FMOD.Sound GetSound(string id) => Sounds[id];
    public static IEnumerator Prepare(PackSnapshot pack, string cache, Action<RuntimeTheme?, string?> done)
    {
        var theme = new RuntimeTheme(pack);
        var textures = new Dictionary<string, Texture2D>();
        var loadedBundles = new Dictionary<string, AssetBundle>();
        var loadedSounds = new Dictionary<string, FMOD.Sound>();
        var allFonts = Resources.FindObjectsOfTypeAll<TMP_FontAsset>();
        var liveFontIds = new HashSet<int>(allFonts.Where(f => f).Select(f => f.GetInstanceID()));
        foreach (var retired in skinFonts.Where(p => p.Value == 0 && !liveFontIds.Contains(p.Key)).Select(p => p.Key).ToArray()) skinFonts.Remove(retired);
        var gameFonts = allFonts.Where(f => f && !skinFonts.ContainsKey(f.GetInstanceID())).ToArray();
        var handedOff = false;
        try
        {
            foreach (var pair in pack.Manifest.Resources)
            {
                Object? asset = null;
                string? error = null;
                var r = pair.Value;
                var bytes = pack.Files[r.File];
                if (r.Bundle != null)
                {
                    try
                    {
                        if (!loadedBundles.TryGetValue(r.File, out var bundle))
                        {
                            var hash = r.Bundle.Sha256;
                            if (!sharedBundles.TryGetValue(hash, out var shared))
                            {
                                bundle = AssetBundle.LoadFromMemory(bytes, r.Bundle.Crc);
                                if (!bundle) throw new InvalidDataException("Bundle CRC/platform/load failure: " + r.File);
                                sharedBundles[hash] = shared = new SharedBundle { Bundle = bundle };
                            }
                            bundle = shared.Bundle; shared.References++;
                            loadedBundles.Add(r.File, bundle); theme.bundleLeases.Add(hash);
                        }
                        var type = r.Kind switch { "sprite" => typeof(Sprite), "audio" => typeof(AudioClip), "font" => typeof(TMP_FontAsset), "material" => typeof(Material), "particle" => typeof(GameObject), _ => typeof(Object) };
                        var subAsset = r.Asset!.Split('#');
                        asset = subAsset.Length == 2 ? bundle.LoadAssetWithSubAssets(subAsset[0], type).FirstOrDefault(x => x.name == subAsset[1]) : bundle.LoadAsset(r.Asset!, type);
                        if (!asset) throw new InvalidDataException("Missing/wrong bundle asset type: " + r.Asset);
                        if (asset is GameObject prefab)
                        {
                            // Cosmetic prefabs cannot bring behaviours, collision, physics or audio.
                            foreach (var component in prefab.GetComponentsInChildren<Component>(true))
                                if (!(component is Transform) && !(component is ParticleSystem) && !(component is ParticleSystemRenderer))
                                    throw new InvalidDataException("Particle prefab contains forbidden component: " + component.GetType().Name);
                            foreach (var particle in prefab.GetComponentsInChildren<ParticleSystem>(true))
                                if (particle.collision.enabled || particle.trigger.enabled)
                                    throw new InvalidDataException("Cosmetic particle collision/trigger modules must be disabled.");
                        }
                        if (asset is TMP_FontAsset font)
                        {
                            if (!font.material || font.atlasTextures == null || font.atlasTextures.Length == 0 || !font.atlasTextures[0])
                                throw new InvalidDataException("Font requires an atlas and material: " + r.Asset);
                            theme.TrackFont(font);
                            var clone = Object.Instantiate(font); asset = clone;
                            // Detach destructive ownership before any fallible allocations.
                            // TMP_FontAsset.OnDestroy destroys its atlas textures and material.
                            clone.atlasTextures = new Texture2D[font.atlasTextures.Length];
                            clone.material = null!;
                            theme.owned.Add(clone); theme.TrackFont(clone);
                            HarmonyLib.AccessTools.Field(typeof(TMP_FontAsset), "m_AtlasTexture").SetValue(clone, null);
                            for (var i = 0; i < font.atlasTextures.Length; i++)
                                if (font.atlasTextures[i]) clone.atlasTextures[i] = Object.Instantiate(font.atlasTextures[i]);
                            clone.material = Object.Instantiate(font.material);
                            clone.material.mainTexture = clone.atlasTextures[0];
                            clone.fallbackFontAssetTable = new List<TMP_FontAsset>();
                            foreach (var fallback in gameFonts)
                                if (!clone.fallbackFontAssetTable.Contains(fallback)) clone.fallbackFontAssetTable.Add(fallback);
                        }
                    }
                    catch (Exception e) { error = e.Message; }
                }
                else if (r.Kind == "sprite")
                {
                    try
                    {
                        if (!textures.TryGetValue(r.File, out var texture))
                        {
                            texture = new Texture2D(2, 2, TextureFormat.RGBA32, false) { filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp };
                            theme.owned.Add(texture);
                            if (!ImageConversion.LoadImage(texture, bytes, true)) throw new InvalidDataException("PNG decode failed: " + r.File);
                            textures.Add(r.File, texture);
                        }
                        var rect = r.Rect == null ? new Rect(0, 0, texture.width, texture.height) : new Rect(r.Rect[0], r.Rect[1], r.Rect[2], r.Rect[3]);
                        var border = r.Border == null ? Vector4.zero : new Vector4(r.Border[0], r.Border[1], r.Border[2], r.Border[3]);
                        asset = Sprite.Create(texture, rect, new Vector2(r.Pivot[0], r.Pivot[1]), r.PixelsPerUnit, 0, SpriteMeshType.FullRect, border);
                        asset.name = pack.Manifest.Id + ":" + pair.Key; theme.owned.Add(asset);
                    }
                    catch (Exception e) { error = e.Message; }
                }
                else if (r.Kind == "audio")
                {
                    try
                    {
                        if (!loadedSounds.TryGetValue(r.File, out var sound))
                        {
                            var info = new FMOD.CREATESOUNDEXINFO { cbsize = System.Runtime.InteropServices.Marshal.SizeOf<FMOD.CREATESOUNDEXINFO>(), length = (uint)bytes.Length };
                            var header = FMODUnity.RuntimeManager.CoreSystem.createSound(bytes, FMOD.MODE.OPENMEMORY | FMOD.MODE.OPENONLY, ref info, out var metadata);
                            if (header != FMOD.RESULT.OK) throw new InvalidDataException("FMOD audio metadata failed: " + r.File + " (" + header + ")");
                            try
                            {
                                var lengthResult = metadata.getLength(out var frames, FMOD.TIMEUNIT.PCM);
                                var formatResult = metadata.getFormat(out _, out _, out var channels, out var bits);
                                if (lengthResult != FMOD.RESULT.OK || formatResult != FMOD.RESULT.OK || frames == 0 || channels <= 0 || channels > 8 || bits <= 0 ||
                                    (long)frames * channels * Math.Max(4, (bits + 7) / 8) > 128L * 1024 * 1024)
                                    throw new InvalidDataException("Empty/oversized decoded audio: " + r.File);
                            }
                            finally { metadata.release(); }
                            var result = FMODUnity.RuntimeManager.CoreSystem.createSound(bytes, FMOD.MODE.OPENMEMORY | FMOD.MODE.CREATESAMPLE | FMOD.MODE.LOOP_NORMAL, ref info, out sound);
                            if (result != FMOD.RESULT.OK) throw new InvalidDataException("FMOD audio decode failed: " + r.File + " (" + result + ")");
                            theme.ownedSounds.Add(sound);
                            sound.getLength(out var length, FMOD.TIMEUNIT.PCM);
                            if (length == 0 || length > 100_000_000) throw new InvalidDataException("Empty/oversized audio: " + r.File);
                            loadedSounds.Add(r.File, sound);
                        }
                        theme.Sounds.Add(pair.Key, sound);
                    }
                    catch (Exception e) { error = e.Message; }
                    if (error == null) { yield return null; continue; }
                }
                if (error != null || !asset)
                {
                    done(null, error ?? "Could not load " + pair.Key); yield break;
                }
                theme.Assets.Add(pair.Key, asset!);
            }
            string? validationError = null;
            try
            {
                foreach (var binding in pack.Manifest.Ui.Values)
                    if (binding.ExistingFont != null && !Resources.FindObjectsOfTypeAll<TMP_FontAsset>().Any(f => f && f.name == binding.ExistingFont))
                        throw new InvalidDataException("Existing font unavailable: " + binding.ExistingFont);
                foreach (var pair in pack.Manifest.Ui)
                    if (pair.Value.Sprite != null && Plugin.Instance!.Catalog.Ui[pair.Key].Component == "RawImage")
                    {
                        var sprite = theme.Get<Sprite>(pair.Value.Sprite);
                        if (sprite.rect != new Rect(0, 0, sprite.texture.width, sprite.texture.height))
                            throw new InvalidDataException("RawImage requires a whole texture: " + pair.Key);
                    }
            }
            catch (Exception e) { validationError = e.Message; }
            if (validationError != null) { done(null, validationError); yield break; }
            done(theme, null); handedOff = true;
        }
        finally { if (!handedOff) theme.Dispose(); }
    }
    public void Dispose()
    {
        foreach (var sound in ownedSounds) sound.release();
        ownedSounds.Clear(); Sounds.Clear();
        foreach (var item in owned) if (item) Object.Destroy(item);
        foreach (var id in fontIds)
        {
            // Destroy is deferred to frame end. Keep retired IDs excluded from fallback
            // discovery until the objects have actually disappeared.
            skinFonts[id]--;
        }
        foreach (var hash in bundleLeases)
        {
            var shared = sharedBundles[hash];
            if (--shared.References == 0) { if (shared.Bundle) shared.Bundle.Unload(true); sharedBundles.Remove(hash); }
        }
        owned.Clear(); bundleLeases.Clear(); fontIds.Clear(); Assets.Clear();
    }
}
