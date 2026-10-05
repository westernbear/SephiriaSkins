using HarmonyLib;
using TMPro;
using UnityEngine;
using UnityEngine.TextCore.LowLevel;

namespace SephiriaSkins.Plugin;

internal static class FontLifetime
{
    private static readonly System.Reflection.FieldInfo FontQueue = AccessTools.Field(typeof(TMP_FontAsset), "k_FontAssets_FontFeaturesUpdateQueue");
    private static readonly System.Reflection.FieldInfo FontLookup = AccessTools.Field(typeof(TMP_FontAsset), "k_FontAssets_FontFeaturesUpdateQueueLookup");
    private static readonly System.Reflection.FieldInfo AtlasQueue = AccessTools.Field(typeof(TMP_FontAsset), "k_FontAssets_AtlasTexturesUpdateQueue");
    private static readonly System.Reflection.FieldInfo AtlasLookup = AccessTools.Field(typeof(TMP_FontAsset), "k_FontAssets_AtlasTexturesUpdateQueueLookup");
    private static readonly System.Reflection.MethodInfo LoadFace = AccessTools.Method(typeof(TMP_FontAsset), "LoadFontFace");
    private static readonly System.Reflection.MethodInfo UpdateFeatures = AccessTools.Method(typeof(TMP_FontAsset), "UpdateGPOSFontFeaturesForNewlyAddedGlyphs");
    internal static int RemovedFonts, RemovedAtlases, FaceResets;

    public static void Retire(IEnumerable<TMP_FontAsset> fonts)
    {
        // TMP's OnDestroy does not remove deferred feature/atlas updates. Cancel
        // only updates belonging to assets about to be destroyed.
        var ids = new HashSet<int>(); var atlases = new HashSet<int>();
        foreach (var font in fonts)
        {
            if (!font) continue;
            ids.Add(font.GetInstanceID());
            foreach (var atlas in font.atlasTextures) if (atlas) atlases.Add(atlas.GetInstanceID());
        }
        var queue = (List<TMP_FontAsset>)FontQueue.GetValue(null);
        RemovedFonts += queue.RemoveAll(f => !f || ids.Contains(f.GetInstanceID()));
        var fontLookup = (HashSet<int>)FontLookup.GetValue(null);
        fontLookup.Clear(); fontLookup.UnionWith(queue.Select(f => f.instanceID));
        var atlasQueue = (List<Texture2D>)AtlasQueue.GetValue(null);
        RemovedAtlases += atlasQueue.RemoveAll(t => !t || atlases.Contains(t.GetInstanceID()));
        var atlasLookup = (HashSet<int>)AtlasLookup.GetValue(null);
        atlasLookup.Clear(); atlasLookup.UnionWith(atlasQueue.Select(t => t.GetInstanceID()));
    }
    public static void BeforeSourceUnload()
    {
        // Drain surviving fonts while their sources are alive, loading each face
        // explicitly: TMP's deferred GPOS callback assumes a current native face.
        var queue = (List<TMP_FontAsset>)FontQueue.GetValue(null);
        foreach (var font in queue.ToArray())
            if (font && (FontEngineError)LoadFace.Invoke(font, null) == FontEngineError.Success)
                UpdateFeatures.Invoke(font, null);
        queue.Clear(); ((HashSet<int>)FontLookup.GetValue(null)).Clear();
        // Native cached faces must not retain data from a Font unloaded with a bundle.
        FontEngine.UnloadAllFontFaces(); FaceResets++;
    }
}
