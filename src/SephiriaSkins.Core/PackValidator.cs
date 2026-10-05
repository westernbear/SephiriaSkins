using System.Text.RegularExpressions;
namespace SephiriaSkins.Core;
public static class PackValidator
{
    private static void Require(bool test, string error) { if (!test) throw new InvalidDataException(error); }
    public static void Validate(SkinManifest m, IReadOnlyDictionary<string, byte[]> files, AssetCatalog catalog)
    {
        Require(m.SchemaVersion == 1, "Unsupported schemaVersion.");
        Require(m.Id != null && Regex.IsMatch(m.Id, "^[a-z0-9][a-z0-9._-]{2,63}$"), "Invalid skin ID.");
        Require(!string.IsNullOrWhiteSpace(m.Name) && m.Name.Length <= 128 && !string.IsNullOrWhiteSpace(m.Author), "Name/author required.");
        Require(System.Version.TryParse(m.Version, out _), "Invalid version.");
        Require(m.CompatibleCatalogs != null && m.CompatibleCatalogs.Contains(catalog.Id), "Incompatible catalog: " + catalog.Id);
        Require(m.Resources != null && m.Body != null && m.Weapons != null && m.Effects != null && m.Visuals != null && m.Ui != null && m.Audio != null, "Null sections are forbidden; omit unused sections.");
        byte[] File(string path) { PackReader.SafePath(path); Require(files.ContainsKey(path), "Missing file: " + path); return files[path]; }
        void Ref(string? id, string kind)
        {
            Require(id != null && m.Resources!.TryGetValue(id, out _), "Unknown resource: " + id);
            Require(m.Resources![id!].Kind == kind, "Wrong resource kind: " + id);
        }
        foreach (var pair in m.Resources!)
        {
            var r = pair.Value;
            Require(r != null, "Null resource: " + pair.Key);
            var bytes = File(r!.File);
            Require(new[] { "sprite", "audio", "font", "material", "particle" }.Contains(r.Kind), "Invalid resource kind.");
            if (r.Bundle != null)
            {
                Require(r.Kind != "audio", "FMOD audio uses loose WAV/OGG; Unity exporter copies audio beside the bundle.");
                Require(r.Bundle.UnityVersion == catalog.UnityVersion && r.Bundle.Platform == "StandaloneWindows64", "Incompatible bundle version/platform.");
                Require(!string.IsNullOrWhiteSpace(r.Asset) && r.Bundle.Crc != 0, "Bundle requires asset and CRC.");
                Require(r.Bundle.Sha256 == Keys.Hash(bytes), "Bundle SHA256 mismatch.");
                Require(bytes.Length > 32 && System.Text.Encoding.ASCII.GetString(bytes, 0, 8) == "UnityFS\0", "Invalid UnityFS bundle.");
                var position = 12;
                string HeaderString()
                {
                    var begin = position;
                    while (position < bytes.Length && bytes[position] != 0 && position - begin < 128) position++;
                    Require(position < bytes.Length && bytes[position] == 0, "Invalid bundle header.");
                    return System.Text.Encoding.ASCII.GetString(bytes, begin, position++ - begin);
                }
                HeaderString();
                Require(HeaderString() == catalog.UnityVersion, "Bundle actual Unity revision mismatch.");
                Require(r.Rect == null, "Bundle sprite uses its imported rect.");
            }
            else if (r.Kind == "sprite")
            {
                Require(r.File.EndsWith(".png", StringComparison.OrdinalIgnoreCase), "Sprite must be PNG.");
                Require(bytes.Length >= 45 && bytes.Take(8).SequenceEqual(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 }), "Invalid PNG.");
                int Be(int i) => (bytes[i] << 24) | (bytes[i + 1] << 16) | (bytes[i + 2] << 8) | bytes[i + 3];
                var w = Be(16); var h = Be(20);
                Require(w > 0 && h > 0 && w <= 8192 && h <= 8192 && (long)w * h <= 16 * 1024 * 1024, "PNG dimensions exceed limits.");
                Require(bytes.Skip(bytes.Length - 8).Take(4).SequenceEqual(new byte[] { 73, 69, 78, 68 }), "Truncated PNG.");
                if (r.Rect != null) Require(r.Rect.Length == 4 && r.Rect[0] >= 0 && r.Rect[1] >= 0 && r.Rect[2] > 0 && r.Rect[3] > 0 && (long)r.Rect[0] + r.Rect[2] <= w && (long)r.Rect[1] + r.Rect[3] <= h, "Invalid sprite rect.");
                if (r.Border != null) Require(r.Border.Length == 4 && r.Border.All(x => x >= 0) && (long)r.Border[0] + r.Border[2] <= (r.Rect?[2] ?? w) && (long)r.Border[1] + r.Border[3] <= (r.Rect?[3] ?? h), "Invalid sprite border.");
            }
            else if (r.Kind == "audio")
            {
                var wav = r.File.EndsWith(".wav", StringComparison.OrdinalIgnoreCase);
                var ogg = r.File.EndsWith(".ogg", StringComparison.OrdinalIgnoreCase);
                Require(wav || ogg, "Audio must be WAV/OGG.");
                Require(bytes.Length > 44 && (wav ? System.Text.Encoding.ASCII.GetString(bytes, 0, 4) == "RIFF" && System.Text.Encoding.ASCII.GetString(bytes, 8, 4) == "WAVE" : System.Text.Encoding.ASCII.GetString(bytes, 0, 4) == "OggS"), "Invalid audio header.");
            }
            else throw new InvalidDataException("Font/material/particle require a Unity bundle.");
            if (r.Kind == "sprite") Require(r.Pivot != null && r.Pivot.Length == 2 && r.Pivot.All(x => Finite(x) && x >= 0 && x <= 1) && Finite(r.PixelsPerUnit) && r.PixelsPerUnit > 0 && r.PixelsPerUnit <= 4096, "Invalid sprite pivot/scale.");
        }
        if (m.Preview != null) Ref(m.Preview, "sprite");
        void Animations(Dictionary<string, AnimationBinding> bindings, string role)
        {
            foreach (var pair in bindings)
            {
                Require(catalog.Animations.TryGetValue(pair.Key, out var original) && original.Role == role, "Unknown " + role + " animation: " + pair.Key);
                Require(pair.Value != null && pair.Value.Frames != null && pair.Value.FrameIndices != null, "Null animation frames.");
                Require(pair.Value!.Frames!.Length == original!.FrameIndices.Length && pair.Value.FrameIndices!.SequenceEqual(original.FrameIndices), "Animation frame count/timing mismatch: " + pair.Key);
                foreach (var resource in pair.Value.Frames!) Ref(resource, "sprite");
                if (pair.Value.Material != null) Ref(pair.Value.Material, "material");
                if (pair.Value.Particle != null) Ref(pair.Value.Particle, "particle");
            }
        }
        Animations(m.Body!, "body"); Animations(m.Weapons!, "weapon"); Animations(m.Effects!, "effect");
        foreach (var pair in m.Visuals!)
        {
            Require(catalog.Visuals.ContainsKey(pair.Key), "Unknown visual target: " + pair.Key);
            Require(pair.Value != null, "Null visual binding.");
            if (pair.Value!.Sprite != null) Ref(pair.Value.Sprite, "sprite");
            if (pair.Value.Material != null) Ref(pair.Value.Material, "material");
        }
        foreach (var pair in m.Ui!)
        {
            Require(catalog.Ui.ContainsKey(pair.Key), "Unknown UI target: " + pair.Key);
            var u = pair.Value; Require(u != null, "Null UI binding.");
            var component = catalog.Ui[pair.Key].Component;
            Require(u!.Sprite == null || component == "Image" || component == "RawImage", "UI sprite requires Image/RawImage: " + pair.Key);
            Require((u.Font == null && u.ExistingFont == null && u.FontSize == null) || component == "TextMeshProUGUI" || component == "TextMeshPro", "UI font requires TMP text: " + pair.Key);
            Require(u.ImageType == null || component == "Image" && (u.ImageType == "simple" || u.ImageType == "sliced"), "Invalid UI imageType.");
            if (u!.Sprite != null) Ref(u.Sprite, "sprite");
            if (u.Material != null) Ref(u.Material, "material");
            if (u.Font != null) Ref(u.Font, "font");
            Require(u.Font == null || u.ExistingFont == null, "Choose font or existingFont.");
            Require(u.ExistingFont == null || !string.IsNullOrWhiteSpace(u.ExistingFont), "Existing font name is empty.");
            void Vector(float[]? vector, int count) { if (vector != null) Require(vector.Length == count && vector.All(Finite), "Invalid UI vector."); }
            Vector(u.Color, 4); Vector(u.AnchoredPosition, 2); Vector(u.SizeDelta, 2);
            if (u.Color != null) Require(u.Color.All(x => x >= 0 && x <= 1), "Invalid color.");
            if (u.FontSize != null) Require(Finite(u.FontSize.Value) && u.FontSize > 0 && u.FontSize <= 512, "Invalid fontSize.");
        }
        foreach (var pair in m.Audio!)
        {
            Require(catalog.Audio.ContainsKey(pair.Key), "Unknown FMOD event: " + pair.Key);
            var a = pair.Value; Require(a != null, "Null audio binding."); Ref(a!.Resource, "audio");
            Require(new[] { "local", "client" }.Contains(a.Scope) && new[] { "sfx", "music", "ambience" }.Contains(a.Channel) && Finite(a.Volume) && a.Volume >= 0 && a.Volume <= 1, "Invalid audio scope/channel/volume.");
            Require(a.Channel == "sfx" || a.Scope == "client", "Music/ambience must use client scope.");
        }
    }
    private static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
}
