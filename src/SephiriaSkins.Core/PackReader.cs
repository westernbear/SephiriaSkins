using System.IO.Compression;
using System.Text;
namespace SephiriaSkins.Core;

public sealed class PackSnapshot
{
    public SkinManifest Manifest { get; }
    public IReadOnlyDictionary<string, byte[]> Files { get; }
    public string Source { get; }
    public PackSnapshot(SkinManifest manifest, Dictionary<string, byte[]> files, string source)
        => (Manifest, Files, Source) = (manifest, files, source);
}

// A pack is read into a bounded snapshot. No ZIP extraction and no Unity operations here.
public static class PackReader
{
    public const int MaxFiles = 8192;
    public const long MaxFileBytes = 64L * 1024 * 1024;
    public const long MaxPackBytes = 256L * 1024 * 1024;
    public static string SafePath(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || path.Length > 240 || path.Contains('\\') ||
            path.StartsWith("/", StringComparison.Ordinal) || path.Any(c => c < 32 || ":*?\"<>|".Contains(c)))
            throw new InvalidDataException("Unsafe pack path: " + path);
        foreach (var part in path.Split('/'))
        {
            if (part.Length == 0 || part == "." || part == ".." || part.EndsWith(".") || part.EndsWith(" "))
                throw new InvalidDataException("Unsafe pack path: " + path);
            var stem = part.Split('.')[0].ToUpperInvariant();
            if (new[] { "CON", "PRN", "AUX", "NUL", "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9", "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9" }.Contains(stem))
                throw new InvalidDataException("Reserved Windows pack path: " + path);
        }
        return path;
    }
    private static void NoLinks(string path)
    {
        for (var item = new DirectoryInfo(Path.GetFullPath(path)); item != null; item = item.Parent)
            if (item.Exists && (item.Attributes & FileAttributes.ReparsePoint) != 0)
                throw new InvalidDataException("Links/junctions are not supported: " + item.FullName);
        if (File.Exists(path) && (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0)
            throw new InvalidDataException("Linked pack files are not supported.");
    }
    public static PackSnapshot Read(string source, AssetCatalog catalog)
    {
        NoLinks(source);
        var files = new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase);
        long total = 0;
        void Add(string name, long length, Stream stream)
        {
            SafePath(name);
            if (files.Count >= MaxFiles || length < 0 || length > MaxFileBytes || (total += length) > MaxPackBytes)
                throw new InvalidDataException("Pack exceeds size/file limits.");
            if (files.ContainsKey(name)) throw new InvalidDataException("Duplicate pack path: " + name);
            if (new[] { ".dll", ".exe", ".cs", ".ps1", ".bat", ".cmd" }.Contains(Path.GetExtension(name).ToLowerInvariant()))
                throw new InvalidDataException("Executable skin content is forbidden: " + name);
            using var memory = new MemoryStream();
            var buffer = new byte[8192];
            int read;
            while ((read = stream.Read(buffer, 0, buffer.Length)) > 0)
            {
                if (memory.Length + read > length) throw new InvalidDataException("Unexpected file size: " + name);
                memory.Write(buffer, 0, read);
            }
            if (memory.Length != length) throw new InvalidDataException("Truncated file: " + name);
            files.Add(name, memory.ToArray());
        }
        if (Directory.Exists(source))
        {
            var root = Path.GetFullPath(source).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
            var directories = 0;
            void Visit(string directory, int depth)
            {
                if (++directories > MaxFiles || depth > 32) throw new InvalidDataException("Pack exceeds directory/depth limits.");
                NoLinks(directory);
                foreach (var file in Directory.EnumerateFiles(directory))
                {
                    NoLinks(file);
                    using var stream = File.OpenRead(file);
                    Add(Path.GetFullPath(file).Substring(root.Length).Replace('\\', '/'), stream.Length, stream);
                }
                foreach (var child in Directory.EnumerateDirectories(directory))
                {
                    SafePath(Path.GetFullPath(child).Substring(root.Length).Replace('\\', '/'));
                    Visit(child, depth + 1);
                }
            }
            Visit(source, 0);
        }
        else
        {
            using var zip = ZipFile.OpenRead(source);
            if (zip.Entries.Count > MaxFiles) throw new InvalidDataException("Too many ZIP entries.");
            foreach (var entry in zip.Entries)
            {
                if (entry.FullName.EndsWith("/")) { SafePath(entry.FullName.TrimEnd('/')); continue; }
                if (((entry.ExternalAttributes >> 16) & 0xF000) == 0xA000)
                    throw new InvalidDataException("ZIP symlink is forbidden.");
                using var stream = entry.Open();
                Add(entry.FullName, entry.Length, stream);
            }
        }
        if (!files.TryGetValue("skin.json", out var json) || json.Length > 2 * 1024 * 1024)
            throw new InvalidDataException("skin.json must be at the pack root (max 2 MiB).");
        var manifest = Json.Read<SkinManifest>(new UTF8Encoding(false, true).GetString(json).TrimStart('\uFEFF'));
        PackValidator.Validate(manifest, files, catalog);
        return new PackSnapshot(manifest, files, source);
    }
}

public sealed class PackEntry
{
    public string Source { get; set; } = "";
    public PackSnapshot? Pack { get; set; }
    public string? Error { get; set; }
    public string Name => Pack?.Manifest.Name ?? Path.GetFileName(Source);
}
public static class PackDiscovery
{
    public static List<PackEntry> Scan(string root, AssetCatalog catalog)
    {
        Directory.CreateDirectory(root);
        var entries = new List<PackEntry>();
        foreach (var source in Directory.EnumerateDirectories(root).Concat(Directory.EnumerateFiles(root, "*.zip")).OrderBy(x => x, StringComparer.Ordinal))
        {
            var entry = new PackEntry { Source = source };
            try { entry.Pack = PackReader.Read(source, catalog); }
            catch (Exception e) { entry.Error = e.Message; }
            entries.Add(entry);
        }
        foreach (var group in entries.Where(x => x.Pack != null).GroupBy(x => x.Pack!.Manifest.Id, StringComparer.OrdinalIgnoreCase).Where(g => g.Count() > 1))
            foreach (var entry in group) entry.Error = "Duplicate skin ID: " + group.Key;
        return entries;
    }
}
