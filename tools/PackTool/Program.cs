using System.IO.Compression;
using SephiriaSkins.Core;

try
{
    if (args.Length < 3) throw new ArgumentException("Usage: PackTool validate|zip|template <catalog.json> <pack-or-output> [output.zip]");
    var catalog = Json.Read<AssetCatalog>(File.ReadAllText(args[1]));
    if (args[0] == "template")
    {
        if (Directory.Exists(args[2]) && Directory.EnumerateFileSystemEntries(args[2]).Any())
            throw new ArgumentException("Template output must be a new or empty directory.");
        Directory.CreateDirectory(args[2]);
        var manifest = new SkinManifest { Id = "my.skin", Version = "1.0.0", Name = "My skin", Author = "Author", CompatibleCatalogs = new[] { catalog.Id } };
        File.WriteAllText(Path.Combine(args[2], "skin.json"), Json.Write(manifest));
        File.WriteAllText(Path.Combine(args[2], "catalog.json"), Json.Write(catalog));
        File.WriteAllText(Path.Combine(args[2], "animations.json"), Json.Write(catalog.Animations));
        Console.WriteLine("Empty pack/template: " + args[2]);
    }
    else
    {
        var pack = PackReader.Read(args[2], catalog);
        Console.WriteLine("VALID " + pack.Manifest.Id + ": " + pack.Manifest.Resources.Count + " resources; " + pack.Manifest.Body.Count + " body states");
        if (args[0] == "zip")
        {
            if (args.Length != 4 || File.Exists(args[3])) throw new ArgumentException("Specify a new output ZIP path.");
            using var archive = ZipFile.Open(args[3], ZipArchiveMode.Create);
            foreach (var file in pack.Files.OrderBy(p => p.Key, StringComparer.Ordinal))
            {
                var entry = archive.CreateEntry(file.Key, CompressionLevel.Optimal);
                entry.LastWriteTime = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
                using var stream = entry.Open(); stream.Write(file.Value);
            }
            Console.WriteLine("Wrote " + args[3]);
        }
        else if (args[0] != "validate") throw new ArgumentException("Unknown command.");
    }
}
catch (Exception e) { Console.Error.WriteLine(e.Message); Environment.ExitCode = 1; }
