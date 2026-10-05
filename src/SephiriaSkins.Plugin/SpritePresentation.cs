using UnityEngine;
using Object = UnityEngine.Object;
namespace SephiriaSkins.Plugin;

// Isolate each atlas region. Native reflection shaders invert full-texture UVs;
// feeding an atlas directly exposes neighbouring cells. Never alter game textures.
internal sealed class SpritePresentation : IDisposable
{
    private readonly Dictionary<(string, float, float, Vector2, bool), Sprite> sprites = new();
    private readonly Dictionary<Texture2D, Color32[]> pixels = new();
    private readonly List<Object> owned = new();
    public Sprite Get(string id, Sprite source, bool pixel, Sprite? original)
    {
        var rect = source.rect;
        if (source.packed) rect = source.textureRect;
        var width = original ? original!.rect.width / original.pixelsPerUnit : source.rect.width / source.pixelsPerUnit;
        var height = original ? original!.rect.height / original.pixelsPerUnit : source.rect.height / source.pixelsPerUnit;
        var pivot = original ? original!.pivot / original.rect.size : source.pivot / source.rect.size;
        var key = (id, width, height, pivot, pixel);
        if (sprites.TryGetValue(key, out var existing)) return existing;
        var ppu = Math.Min(rect.width / width, rect.height / height);
        if (pixel) ppu = Math.Min(32f, ppu);
        var textureLimit = Math.Max(1, SystemInfo.maxTextureSize);
        ppu = Math.Min(ppu, Math.Min(textureLimit / width, textureLimit / height));
        // Floating sprite rects keep world bounds and pivot identical when rounded
        // texture dimensions differ by a pixel. No transform/physics changes.
        var targetRect = new Rect(0, 0, width * ppu, height * ppu);
        var w = Mathf.CeilToInt(targetRect.width); var h = Mathf.CeilToInt(targetRect.height);
        var texture = new Texture2D(w, h, TextureFormat.RGBA32, false) { filterMode = pixel ? FilterMode.Point : FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp };
        owned.Add(texture);
        var input = Read(source.texture);
        var output = new Color32[w * h];
        for (var y = 0; y < h; y++) for (var x = 0; x < w; x++)
        {
            var sx = Mathf.Clamp(Mathf.FloorToInt(rect.x + (x + .5f) / targetRect.width * rect.width), (int)rect.x, (int)rect.xMax - 1);
            var sy = Mathf.Clamp(Mathf.FloorToInt(rect.y + (y + .5f) / targetRect.height * rect.height), (int)rect.y, (int)rect.yMax - 1);
            var color = input[sy * source.texture.width + sx];
            if (pixel)
            {
                // Coverage samples preserve thin shafts/outlines which fall
                // between pixel centres. Colour is averaged only over ink.
                var alpha = 0; var red = 0; var green = 0; var blue = 0; var count = 0;
                for (var yy = 0; yy < 4; yy++) for (var xx = 0; xx < 4; xx++)
                {
                    var px = Mathf.Clamp(Mathf.FloorToInt(rect.x + (x + (xx + .5f) / 4) / targetRect.width * rect.width), (int)rect.x, (int)rect.xMax - 1);
                    var py = Mathf.Clamp(Mathf.FloorToInt(rect.y + (y + (yy + .5f) / 4) / targetRect.height * rect.height), (int)rect.y, (int)rect.yMax - 1);
                    var sample = input[py * source.texture.width + px];
                    if (sample.a < 128) continue;
                    alpha = 255; red += sample.r; green += sample.g; blue += sample.b; count++;
                }
                color = count == 0 ? new Color32(0,0,0,0) : new Color32((byte)(red/count),(byte)(green/count),(byte)(blue/count),(byte)alpha);
            }
            output[y * w + x] = color;
        }
        texture.SetPixels32(output); texture.Apply(false, true);
        var sprite = Sprite.Create(texture, targetRect, pivot, ppu, 0, SpriteMeshType.FullRect);
        sprite.name = source.name; owned.Add(sprite); sprites.Add(key, sprite);
        return sprite;
    }
    private Color32[] Read(Texture2D texture)
    {
        if (pixels.TryGetValue(texture, out var data)) return data;
        if (texture.isReadable) return pixels[texture] = texture.GetPixels32();
        var previous = RenderTexture.active;
        var target = RenderTexture.GetTemporary(texture.width, texture.height, 0, RenderTextureFormat.ARGB32);
        Texture2D? readable = null;
        try
        {
            Graphics.Blit(texture, target); RenderTexture.active = target;
            readable = new Texture2D(texture.width, texture.height, TextureFormat.RGBA32, false);
            readable.ReadPixels(new Rect(0, 0, texture.width, texture.height), 0, 0); readable.Apply();
            return pixels[texture] = readable.GetPixels32();
        }
        finally { RenderTexture.active = previous; RenderTexture.ReleaseTemporary(target); if (readable) Object.Destroy(readable); }
    }
    public void Dispose()
    {
        foreach (var asset in owned) if (asset) Object.Destroy(asset);
        owned.Clear(); sprites.Clear(); pixels.Clear();
    }
}
