using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using SephiriaSkins.Core;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.TextCore.LowLevel;
using Object = UnityEngine.Object;

public static class SkinExporter
{
    [MenuItem("SephiriaSkins/Export selected recipe")]
    public static void ExportSelected()
    {
        var recipe = Selection.activeObject as SkinExportRecipe;
        if (!recipe) throw new InvalidOperationException("Select a SkinExportRecipe asset.");
        Export(recipe);
    }
    public static void Export(SkinExportRecipe recipe)
    {
        if (Application.unityVersion != "6000.3.21f1") throw new InvalidOperationException("Use Unity 6000.3.21f1, found " + Application.unityVersion);
        var catalog = Json.Read<AssetCatalog>(File.ReadAllText(Path.GetFullPath(recipe.catalogFile)));
        var manifest = Json.Read<SkinManifest>(recipe.manifest.text);
        if (recipe.resources.Select(r => r.id).Distinct().Count() != recipe.resources.Count) throw new InvalidOperationException("Duplicate export resource IDs.");
        var output = Path.GetFullPath(recipe.outputDirectory);
        Directory.CreateDirectory(output);
        var assets = recipe.resources.Where(r => !(r.asset is AudioClip)).Select(r => AssetDatabase.GetAssetPath(r.asset)).Distinct().ToArray();
        foreach (var resource in recipe.resources)
            if (string.IsNullOrEmpty(AssetDatabase.GetAssetPath(resource.asset))) throw new InvalidOperationException("Every resource must be a saved Unity asset: " + resource.id);
        foreach (var dependency in assets.SelectMany(p => AssetDatabase.GetDependencies(p, true)).Distinct())
        {
            if (dependency.EndsWith(".dll")) throw new InvalidOperationException("Skin bundles cannot contain executable DLLs.");
            if (dependency.EndsWith(".cs"))
            {
                var script = AssetDatabase.LoadAssetAtPath<MonoScript>(dependency);
                if (!dependency.StartsWith("Packages/com.unity.ugui/", StringComparison.Ordinal) || !script || script.GetClass() != typeof(TMP_FontAsset))
                    throw new InvalidOperationException("Custom scripts are forbidden: " + dependency);
            }
        }
        BundleInfo metadata = null;
        if (assets.Length > 0)
        {
            var build = new AssetBundleBuild { assetBundleName = "theme.bundle", assetNames = assets };
            var result = BuildPipeline.BuildAssetBundles(output, new[] { build }, BuildAssetBundleOptions.ChunkBasedCompression | BuildAssetBundleOptions.StrictMode, BuildTarget.StandaloneWindows64);
            if (!result) throw new InvalidOperationException("Bundle build failed.");
            var file = Path.Combine(output, "theme.bundle");
            if (!BuildPipeline.GetCRCForAssetBundle(file, out var crc) || crc == 0) throw new InvalidOperationException("Could not compute CRC.");
            metadata = new BundleInfo { UnityVersion = Application.unityVersion, Platform = "StandaloneWindows64", Crc = crc, Sha256 = Keys.Hash(File.ReadAllBytes(file)) };
        }
        var generatedLoose = new System.Collections.Generic.HashSet<string>();
        foreach (var r in recipe.resources)
        {
            if (r.asset is AudioClip)
            {
                var sourceAudio = AssetDatabase.GetAssetPath(r.asset);
                var extension = Path.GetExtension(sourceAudio).ToLowerInvariant();
                if (extension != ".wav" && extension != ".ogg") throw new InvalidOperationException("Audio must be WAV/OGG: " + sourceAudio);
                var bytes = File.ReadAllBytes(sourceAudio);
                var relative = "audio/" + Keys.Hash(bytes).Substring(0, 20) + extension;
                Directory.CreateDirectory(Path.Combine(output, "audio"));
                File.Copy(sourceAudio, Path.Combine(output, relative), true);
                generatedLoose.Add(relative);
                manifest.Resources[r.id] = new ResourceRef { File = relative, Kind = "audio" };
                continue;
            }
            var kind = r.asset is Sprite ? "sprite" : r.asset is AudioClip ? "audio" : r.asset is TMP_FontAsset ? "font" : r.asset is Material ? "material" : r.asset is GameObject ? "particle" : null;
            if (kind == null) throw new InvalidOperationException("Unsupported export asset: " + r.id);
            if (r.asset is GameObject prefab)
            {
                if (prefab.GetComponentsInChildren<Component>(true).Any(c => !(c is Transform) && !(c is ParticleSystem) && !(c is ParticleSystemRenderer))) throw new InvalidOperationException("Only particle components are allowed: " + r.id);
                if (prefab.GetComponentsInChildren<ParticleSystem>(true).Any(p => p.collision.enabled || p.trigger.enabled)) throw new InvalidOperationException("Particle collision/trigger modules must be disabled: " + r.id);
            }
            var path = AssetDatabase.GetAssetPath(r.asset).ToLowerInvariant();
            manifest.Resources[r.id] = new ResourceRef { File = "theme.bundle", Asset = path + (r.asset is Sprite ? "#" + r.asset.name : ""), Kind = kind, Bundle = metadata };
        }
        File.WriteAllText(Path.Combine(output, "skin.json"), Json.Write(manifest));
        // If a recipe keeps loose resources, copy them from beside its manifest.
        var source = Path.GetDirectoryName(AssetDatabase.GetAssetPath(recipe.manifest));
        foreach (var r in manifest.Resources.Values.Where(r => r.Bundle == null))
        {
            PackReader.SafePath(r.File);
            var target = Path.Combine(output, r.File); Directory.CreateDirectory(Path.GetDirectoryName(target));
            if (!generatedLoose.Contains(r.File)) File.Copy(Path.Combine(source, r.File), target, true);
        }
        PackReader.Read(output, catalog);
        Debug.Log("SEPHIRIA_SKIN_EXPORT_OK " + output);
    }
    // -batchmode -executeMethod SkinExporter.BuildExample -quit
    [MenuItem("SephiriaSkins/Build example pack")]
    public static void BuildExample()
    {
        if (!Resources.Load<TMP_Settings>("TMP Settings"))
        {
            var package = UnityEditor.PackageManager.PackageInfo.FindForAssembly(typeof(TMP_Text).Assembly);
            AssetDatabase.ImportPackage(Path.Combine(package.resolvedPath, "Package Resources/TMP Essential Resources.unitypackage"), false);
            AssetDatabase.Refresh();
            if (!Resources.Load<TMP_Settings>("TMP Settings")) { Debug.Log("SEPHIRIA_TMP_IMPORTED; run BuildExample again after import."); return; }
        }
        Directory.CreateDirectory("Assets/Example");
        File.Copy("../assets/hachiware/audio/menu.ogg", "Assets/Example/ping.ogg", true);
        var pixels = new Texture2D(32, 32, TextureFormat.RGBA32, false);
        for (var y = 0; y < 32; y++) for (var x = 0; x < 32; x++)
            pixels.SetPixel(x, y, Math.Abs(x - 16) + Math.Abs(y - 16) <= 11 ? new Color(0.5f, 0.85f, 1) : Color.clear);
        pixels.Apply(); File.WriteAllBytes("Assets/Example/diamond.png", pixels.EncodeToPNG()); Object.DestroyImmediate(pixels);
        AssetDatabase.Refresh();
        var importer = (TextureImporter)AssetImporter.GetAtPath("Assets/Example/diamond.png");
        importer.textureType = TextureImporterType.Sprite; importer.spriteImportMode = SpriteImportMode.Single; importer.spritePixelsPerUnit = 32; importer.filterMode = FilterMode.Point; importer.SaveAndReimport();
        AssetDatabase.ImportAsset("Assets/Example/diamond.png", ImportAssetOptions.ForceSynchronousImport);
        var sprite = AssetDatabase.LoadAllAssetsAtPath("Assets/Example/diamond.png").OfType<Sprite>().FirstOrDefault();
        var material = new Material(Shader.Find("Sprites/Default")); AssetDatabase.CreateAsset(material, "Assets/Example/Blue.mat");
        var fontPath = "Assets/TextMesh Pro/Fonts/LiberationSans.ttf";
        AssetDatabase.Refresh();
        var font = TMP_FontAsset.CreateFontAsset(AssetDatabase.LoadAssetAtPath<Font>(fontPath), 90, 9, GlyphRenderMode.SDFAA, 512, 512, AtlasPopulationMode.Dynamic, true);
        AssetDatabase.CreateAsset(font, "Assets/Example/AdventureFont.asset");
        AssetDatabase.AddObjectToAsset(font.material, font);
        foreach (var atlas in font.atlasTextures) if (atlas) AssetDatabase.AddObjectToAsset(atlas, font);
        var particle = new GameObject("BlueSparkle"); var ps = particle.AddComponent<ParticleSystem>();
        var main = ps.main; main.startColor = new Color(0.5f,0.85f,1); main.startSize = 0.08f; main.startLifetime = 0.3f; main.loop = true;
        ps.GetComponent<ParticleSystemRenderer>().sharedMaterial = material;
        var prefab = PrefabUtility.SaveAsPrefabAsset(particle, "Assets/Example/BlueSparkle.prefab"); Object.DestroyImmediate(particle);
        var catalog = Json.Read<AssetCatalog>(File.ReadAllText("../catalog/catalog-1.0.33.json"));
        var manifest = new SkinManifest { Id = "example.unity", Version = "1.0.0", Name = "Unity 예제", Author = "SephiriaSkins", CompatibleCatalogs = new[] { catalog.Id }, Preview = "preview" };
        manifest.Resources["preview"] = new ResourceRef { File = "diamond.png", PixelsPerUnit = 32 };
        var uiSound = catalog.Audio.FirstOrDefault(p => p.Value.Path.StartsWith("event:/UI/", StringComparison.OrdinalIgnoreCase));
        if (uiSound.Key != null) manifest.Audio[uiSound.Key] = new AudioBinding { Resource = "ping", Scope = "client", Channel = "sfx", Volume = .7f };
        foreach (var body in catalog.Animations.Where(p => p.Value.Role == "body"))
            manifest.Body[body.Key] = new AnimationBinding { FrameIndices = body.Value.FrameIndices, Frames = body.Value.FrameIndices.Select(_ => "diamond").ToArray(), Material = "material", Particle = "particle" };
        foreach (var ui in catalog.Ui.Where(p => p.Value.Component == "TextMeshProUGUI" && p.Value.ReferencePath.IndexOf("Title", StringComparison.OrdinalIgnoreCase) >= 0))
            manifest.Ui[ui.Key] = new UiBinding { Font = "font" };
        File.WriteAllText("Assets/Example/skin.json", Json.Write(manifest)); AssetDatabase.Refresh();
        var recipe = ScriptableObject.CreateInstance<SkinExportRecipe>(); recipe.manifest = AssetDatabase.LoadAssetAtPath<TextAsset>("Assets/Example/skin.json");
        recipe.resources.Add(new SkinExportRecipe.Resource { id="diamond",asset=sprite });
        recipe.resources.Add(new SkinExportRecipe.Resource { id="material",asset=material });
        recipe.resources.Add(new SkinExportRecipe.Resource { id="font",asset=font });
        recipe.resources.Add(new SkinExportRecipe.Resource { id="particle",asset=prefab });
        recipe.resources.Add(new SkinExportRecipe.Resource { id="ping",asset=AssetDatabase.LoadAssetAtPath<AudioClip>("Assets/Example/ping.ogg") });
        AssetDatabase.CreateAsset(recipe, "Assets/Example/Recipe.asset"); AssetDatabase.SaveAssets(); Export(recipe);
    }
}
