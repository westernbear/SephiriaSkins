namespace SephiriaSkins.Core;

// Keys are catalog identities, never just Unity object names.
public sealed class SkinManifest
{
    public int SchemaVersion { get; set; } = 1;
    public string Id { get; set; } = "";
    public string Version { get; set; } = "";
    public string Name { get; set; } = "";
    public string Author { get; set; } = "";
    public string Description { get; set; } = "";
    public string? Preview { get; set; }
    public string[] CompatibleCatalogs { get; set; } = Array.Empty<string>();
    public Dictionary<string, ResourceRef> Resources { get; set; } = new();
    public Dictionary<string, AnimationBinding> Body { get; set; } = new();
    public Dictionary<string, AnimationBinding> Weapons { get; set; } = new();
    public Dictionary<string, AnimationBinding> Effects { get; set; } = new();
    public Dictionary<string, VisualBinding> Visuals { get; set; } = new();
    public Dictionary<string, UiBinding> Ui { get; set; } = new();
    public Dictionary<string, AudioBinding> Audio { get; set; } = new();
}

public sealed class ResourceRef
{
    public string File { get; set; } = "";
    public string Kind { get; set; } = "sprite"; // sprite, audio, font, material, particle
    public string? Asset { get; set; }
    public BundleInfo? Bundle { get; set; }
    public int[]? Rect { get; set; } // x,y,width,height, Unity bottom-left coordinates
    public int[]? Border { get; set; } // left,bottom,right,top PNG pixels for UI 9-slicing
    public float[] Pivot { get; set; } = new[] { 0.5f, 0.5f };
    public float PixelsPerUnit { get; set; } = 32;
}
public sealed class BundleInfo
{
    public string UnityVersion { get; set; } = "6000.3.21f1";
    public string Platform { get; set; } = "StandaloneWindows64";
    public uint Crc { get; set; }
    public string Sha256 { get; set; } = "";
}
public sealed class AnimationBinding
{
    public string[] Frames { get; set; } = Array.Empty<string>();
    public int[] FrameIndices { get; set; } = Array.Empty<int>();
    public string? Material { get; set; }
    public string? Particle { get; set; }
}
public sealed class UiBinding
{
    public string? Sprite { get; set; }
    public string? Material { get; set; }
    public string? Font { get; set; }
    public string? ExistingFont { get; set; }
    public float[]? Color { get; set; }
    public float[]? AnchoredPosition { get; set; }
    public float[]? SizeDelta { get; set; }
    public float? FontSize { get; set; }
    public string? ImageType { get; set; } // simple, sliced; Image only
}
public sealed class VisualBinding
{
    public string? Sprite { get; set; }
    public string? Material { get; set; }
}
public sealed class AudioBinding
{
    public string Resource { get; set; } = "";
    public string Scope { get; set; } = "local"; // local, client
    public string Channel { get; set; } = "sfx"; // sfx, music, ambience
    public bool Loop { get; set; }
    public float Volume { get; set; } = 1;
}
public sealed class AssetCatalog
{
    public int SchemaVersion { get; set; } = 1;
    public string Id { get; set; } = "sephiria-1.0.33";
    public string GameVersion { get; set; } = "1.0.33";
    public string UnityVersion { get; set; } = "6000.3.21f1";
    public string AssemblySha256 { get; set; } = "";
    public bool Complete { get; set; }
    public Dictionary<string, CatalogAnimation> Animations { get; set; } = new();
    public Dictionary<string, CatalogUi> Ui { get; set; } = new();
    public Dictionary<string, CatalogVisual> Visuals { get; set; } = new();
    public Dictionary<string, CatalogAudio> Audio { get; set; } = new();
    public Dictionary<string, string> Costumes { get; set; } = new();
    public Dictionary<string, string> WeaponTypes { get; set; } = new();
}
public sealed class CatalogAnimation
{
    public string Role { get; set; } = "";
    public string SetName { get; set; } = "";
    public string State { get; set; } = "";
    public string ReferencePath { get; set; } = "";
    public int Fps { get; set; }
    public bool Repeat { get; set; }
    public int[] FrameIndices { get; set; } = Array.Empty<int>();
    public string[] SpriteNames { get; set; } = Array.Empty<string>();
    public string[] Events { get; set; } = Array.Empty<string>();
}
public sealed class CatalogUi
{
    public string Role { get; set; } = "";
    public string ReferencePath { get; set; } = "";
    public string Component { get; set; } = "";
}
public sealed class CatalogVisual
{
    public string Role { get; set; } = "";
    public string ReferencePath { get; set; } = "";
    public string SpriteName { get; set; } = "";
}
public sealed class CatalogAudio
{
    public string Path { get; set; } = "";
    public string Channel { get; set; } = "sfx";
    public string ReferencePath { get; set; } = "";
}
