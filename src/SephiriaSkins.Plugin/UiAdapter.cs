using SephiriaSkins.Core;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
namespace SephiriaSkins.Plugin;
internal sealed class UiAdapter
{
    private sealed class State
    {
        public Graphic Graphic = null!;
        public UiBinding Binding = null!;
        public Sprite? Sprite, AppliedSprite;
        public Texture? Texture, AppliedTexture;
        public Material? Material, AppliedMaterial;
        public TMP_FontAsset? Font, AppliedFont;
        public Material? FontMaterial, AppliedFontMaterial;
        public Color Color, AppliedColor;
        public Vector2 Position, AppliedPosition, Size, AppliedSize;
        public float FontSize, AppliedFontSize;
        public Image.Type ImageType, AppliedImageType;
    }
    private readonly Dictionary<int, State> states = new();
    internal double LastApplyMilliseconds { get; private set; }
    public void Apply(RuntimeTheme? theme, bool includeInactive = true)
    {
        if (theme == null) return;
        var started = System.Diagnostics.Stopwatch.GetTimestamp();
        var paths = new RuntimeCatalog.PathIndex();
        Dictionary<string, TMP_FontAsset>? existingFonts = null;
        foreach (var g in UnityEngine.Object.FindObjectsByType<Graphic>(includeInactive ? FindObjectsInactive.Include : FindObjectsInactive.Exclude, FindObjectsSortMode.None))
        {
            if (!g || !g.gameObject.scene.IsValid() || g.GetComponentInParent<SkinSelectorMarker>()) continue;
            if (!theme.Pack.Manifest.Ui.TryGetValue(paths.UiKey(g), out var u)) continue;
            var id = g.GetInstanceID();
            if (!states.TryGetValue(id, out var s)) states[id] = s = new State { Graphic = g, Binding = u, Color = g.color, Position = g.rectTransform.anchoredPosition, Size = g.rectTransform.sizeDelta };
            if (u.Color != null)
            {
                if (g.color != s.AppliedColor) s.Color = g.color;
                s.AppliedColor = new Color(u.Color[0], u.Color[1], u.Color[2], u.Color[3]); g.color = s.AppliedColor;
            }
            if (u.Sprite != null && g is Image image)
            {
                if (image.sprite != s.AppliedSprite) s.Sprite = image.sprite;
                s.AppliedSprite = theme.Get<Sprite>(u.Sprite); image.sprite = s.AppliedSprite;
            }
            if (u.ImageType != null && g is Image typeImage)
            {
                if (typeImage.type != s.AppliedImageType) s.ImageType = typeImage.type;
                s.AppliedImageType = u.ImageType == "sliced" ? Image.Type.Sliced : Image.Type.Simple;
                typeImage.type = s.AppliedImageType;
            }
            if (u.Sprite != null && g is RawImage raw)
            {
                if (raw.texture != s.AppliedTexture) s.Texture = raw.texture;
                var sprite = theme.Get<Sprite>(u.Sprite);
                // RawImage atlas UV is handled by Image bindings instead of stretching the whole atlas.
                if (sprite.rect.width != sprite.texture.width || sprite.rect.height != sprite.texture.height) throw new InvalidDataException("RawImage requires a whole texture: " + RuntimeCatalog.UiKey(g));
                s.AppliedTexture = sprite.texture; raw.texture = s.AppliedTexture;
            }
            if (u.Material != null && !(g is TMP_Text))
            {
                if (g.material != s.AppliedMaterial) s.Material = g.material;
                s.AppliedMaterial = theme.Get<Material>(u.Material); g.material = s.AppliedMaterial;
            }
            if (u.AnchoredPosition != null)
            {
                if (g.rectTransform.anchoredPosition != s.AppliedPosition) s.Position = g.rectTransform.anchoredPosition;
                s.AppliedPosition = new Vector2(u.AnchoredPosition[0], u.AnchoredPosition[1]); g.rectTransform.anchoredPosition = s.AppliedPosition;
            }
            if (u.SizeDelta != null)
            {
                if (g.rectTransform.sizeDelta != s.AppliedSize) s.Size = g.rectTransform.sizeDelta;
                s.AppliedSize = new Vector2(u.SizeDelta[0], u.SizeDelta[1]); g.rectTransform.sizeDelta = s.AppliedSize;
            }
            if (g is TMP_Text text)
            {
                if (u.Font != null || u.ExistingFont != null)
                {
                    if (text.font != s.AppliedFont) { s.Font = text.font; s.FontMaterial = text.fontSharedMaterial; }
                    else if (s.AppliedFontMaterial && text.fontSharedMaterial != s.AppliedFontMaterial) s.FontMaterial = text.fontSharedMaterial;
                    if (u.Font == null && existingFonts == null)
                        existingFonts = Resources.FindObjectsOfTypeAll<TMP_FontAsset>().Where(f => f).GroupBy(f => f.name).ToDictionary(f => f.Key, f => f.First(), StringComparer.Ordinal);
                    s.AppliedFont = u.Font != null ? theme.Get<TMP_FontAsset>(u.Font) : existingFonts!.TryGetValue(u.ExistingFont!, out var existing) ? existing : null;
                    if (!s.AppliedFont) throw new InvalidDataException("Existing font unavailable: " + u.ExistingFont);
                    if (u.Font != null && s.Font && s.Font != s.AppliedFont && !s.AppliedFont!.fallbackFontAssetTable.Contains(s.Font))
                        s.AppliedFont.fallbackFontAssetTable.Add(s.Font);
                    text.font = s.AppliedFont;
                    s.AppliedFontMaterial = text.fontSharedMaterial;
                }
                if (u.Material != null)
                {
                    if (text.fontSharedMaterial != s.AppliedMaterial) s.Material = s.FontMaterial ?? text.fontSharedMaterial;
                    s.AppliedMaterial = theme.Get<Material>(u.Material); text.fontSharedMaterial = s.AppliedMaterial;
                    s.AppliedFontMaterial = s.AppliedMaterial;
                }
                if (u.FontSize != null)
                {
                    if (text.fontSize != s.AppliedFontSize) s.FontSize = text.fontSize;
                    s.AppliedFontSize = u.FontSize.Value; text.fontSize = s.AppliedFontSize;
                }
            }
        }
        foreach (var p in states.Where(p => !p.Value.Graphic).ToArray()) states.Remove(p.Key);
        LastApplyMilliseconds = (System.Diagnostics.Stopwatch.GetTimestamp() - started) * 1000.0 / System.Diagnostics.Stopwatch.Frequency;
    }
    public void Restore()
    {
        foreach (var s in states.Values)
        {
            var g = s.Graphic; if (!g) continue;
            var u = s.Binding;
            if (u.Color != null && g.color == s.AppliedColor) g.color = s.Color;
            if (g is Image i && u.Sprite != null && i.sprite == s.AppliedSprite) i.sprite = s.Sprite;
            if (g is Image typeImage && u.ImageType != null && typeImage.type == s.AppliedImageType) typeImage.type = s.ImageType;
            if (g is RawImage r && u.Sprite != null && r.texture == s.AppliedTexture) r.texture = s.Texture;
            if (u.Material != null && !(g is TMP_Text) && g.material == s.AppliedMaterial) g.material = s.Material;
            if (u.AnchoredPosition != null && g.rectTransform.anchoredPosition == s.AppliedPosition) g.rectTransform.anchoredPosition = s.Position;
            if (u.SizeDelta != null && g.rectTransform.sizeDelta == s.AppliedSize) g.rectTransform.sizeDelta = s.Size;
            if (g is TMP_Text t)
            {
                if ((u.Font != null || u.ExistingFont != null) && t.font == s.AppliedFont)
                {
                    var restoreMaterial = t.fontSharedMaterial == s.AppliedFontMaterial;
                    t.font = s.Font;
                    if (restoreMaterial && s.FontMaterial) t.fontSharedMaterial = s.FontMaterial;
                }
                else if (u.Material != null && t.fontSharedMaterial == s.AppliedMaterial) t.fontSharedMaterial = s.Material;
                if (u.FontSize != null && t.fontSize == s.AppliedFontSize) t.fontSize = s.FontSize;
            }
        }
        states.Clear();
    }
}
