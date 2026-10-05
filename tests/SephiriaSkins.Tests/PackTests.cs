using System.IO.Compression;
using SephiriaSkins.Core;
using Xunit;
namespace SephiriaSkins.Tests;
public sealed class PackTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "SephiriaSkinsTests-" + Guid.NewGuid().ToString("N"));
    private readonly AssetCatalog catalog = new();
    private static readonly byte[] Png = Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAusB9Wl6K0IAAAAASUVORK5CYII=");
    public PackTests() { Directory.CreateDirectory(root); }
    private SkinManifest Manifest(string id = "test.skin") => new() { Id = id, Name = "Test", Version = "1.0.0", Author = "Test author", CompatibleCatalogs = new[] { catalog.Id } };
    private string Write(SkinManifest m, string folder = "pack")
    {
        var dir = Path.Combine(root, folder); Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, "skin.json"), Json.Write(m)); File.WriteAllBytes(Path.Combine(dir, "sprite.png"), Png); return dir;
    }
    [Fact]
    public void FolderAndZipHaveEqualSnapshotAndOptionalSections()
    {
        var path = Write(Manifest()); var zip = Path.Combine(root, "skin.zip"); ZipFile.CreateFromDirectory(path, zip);
        var a = PackReader.Read(path, catalog); var b = PackReader.Read(zip, catalog);
        Assert.Equal(a.Manifest.Id, b.Manifest.Id); Assert.Empty(a.Manifest.Body); Assert.Empty(a.Manifest.Audio);
        Assert.Equal(a.Files.Keys.Order(), b.Files.Keys.Order());
        foreach (var key in a.Files.Keys) Assert.Equal(a.Files[key], b.Files[key]);
    }
    [Theory]
    [InlineData("../evil.png")]
    [InlineData("/evil.png")]
    [InlineData("C:/evil.png")]
    [InlineData("a\\b.png")]
    [InlineData("a/../b.png")]
    [InlineData("a//b.png")]
    [InlineData("a./b")]
    [InlineData("CON.png")]
    [InlineData("a /b")]
    [InlineData("./skin.json")]
    public void UnsafePathsAreRejected(string path) => Assert.Throws<InvalidDataException>(() => PackReader.SafePath(path));
    [Fact]
    public void ZipTraversalIsRejectedWithoutExtraction()
    {
        var zip = Path.Combine(root, "bad.zip");
        using (var z = ZipFile.Open(zip, ZipArchiveMode.Create)) { using var w = new StreamWriter(z.CreateEntry("../skin.json").Open()); w.Write(Json.Write(Manifest())); }
        Assert.Throws<InvalidDataException>(() => PackReader.Read(zip, catalog));
        Assert.False(File.Exists(Path.Combine(root, "skin.json")));
    }
    [Fact]
    public void DuplicateIdsExcludeBothPacks()
    {
        Write(Manifest(), "one"); Write(Manifest(), "two");
        var entries = PackDiscovery.Scan(root, catalog); Assert.Equal(2, entries.Count);
        Assert.All(entries, e => Assert.Contains("Duplicate", e.Error));
    }
    [Fact]
    public void MissingOrTruncatedDeclaredSpriteIsRejected()
    {
        var m = Manifest(); m.Resources["body"] = new ResourceRef { File = "missing.png" };
        var dir = Write(m); Assert.Throws<InvalidDataException>(() => PackReader.Read(dir, catalog));
        m.Resources["body"].File = "sprite.png"; Write(m);
        File.WriteAllBytes(Path.Combine(dir, "sprite.png"), Png.Take(30).ToArray());
        Assert.Throws<InvalidDataException>(() => PackReader.Read(dir, catalog));
    }
    [Fact]
    public void SparseUnsortedFramesMustMatchCatalogExactly()
    {
        var m = Manifest(); var indices = new[] { 0, 3, 1 };
        catalog.Animations["body/set/idle"] = new CatalogAnimation { Role = "body", FrameIndices = indices };
        m.Resources["sprite"] = new ResourceRef { File = "sprite.png" };
        m.Body["body/set/idle"] = new AnimationBinding { FrameIndices = indices, Frames = new[] { "sprite", "sprite", "sprite" } };
        var dir = Write(m); Assert.Single(PackReader.Read(dir, catalog).Manifest.Body);
        m.Body["body/set/idle"].FrameIndices = new[] { 0, 1, 3 }; Write(m);
        Assert.Throws<InvalidDataException>(() => PackReader.Read(dir, catalog));
    }
    [Fact]
    public void BundleVersionAndPlatformAndHashAreChecked()
    {
        var m = Manifest(); m.Resources["material"] = new ResourceRef { File = "sprite.png", Kind = "material", Asset = "test", Bundle = new BundleInfo { UnityVersion = "6000.6.4f1", Crc = 1 } };
        var dir = Write(m); Assert.Throws<InvalidDataException>(() => PackReader.Read(dir, catalog));
        m.Resources["material"].Bundle!.UnityVersion = catalog.UnityVersion; m.Resources["material"].Bundle!.Platform = "Android"; Write(m);
        Assert.Throws<InvalidDataException>(() => PackReader.Read(dir, catalog));
        m.Resources["material"].Bundle!.Platform = "StandaloneWindows64"; Write(m);
        Assert.Throws<InvalidDataException>(() => PackReader.Read(dir, catalog));
    }
    [Fact]
    public void FailedPreparationKeepsCurrentThemeAlive()
    {
        var transaction = new ThemeTransaction<TestTheme>(); var good = new TestTheme();
        Assert.True(transaction.TryPrepare(() => good, (_, _) => { }, out _));
        Assert.False(transaction.TryPrepare(() => throw new InvalidDataException("broken"), (_, _) => throw new Exception("must not commit"), out var error));
        Assert.Equal("broken", error); Assert.Same(good, transaction.Current); Assert.False(good.Disposed);
        transaction.Restore((_, _) => { }); Assert.Null(transaction.Current); Assert.True(good.Disposed);
    }
    [Fact]
    public void DuplicateCaseFoldedZipPathsAndCodeAreRejected()
    {
        var zip = Path.Combine(root, "bad.zip");
        using (var z = ZipFile.Open(zip, ZipArchiveMode.Create))
        { z.CreateEntry("skin.json"); z.CreateEntry("Skin.json"); }
        Assert.Throws<InvalidDataException>(() => PackReader.Read(zip, catalog));
        var dir = Write(Manifest()); File.WriteAllText(Path.Combine(dir, "payload.cs"), "code");
        Assert.Throws<InvalidDataException>(() => PackReader.Read(dir, catalog));
    }
    [Fact]
    public void ResourceRectAndNanAreRejected()
    {
        var m = Manifest(); m.Resources["sprite"] = new ResourceRef { File = "sprite.png", Rect = new[] { 0, 0, 2, 2 } };
        var dir = Write(m); Assert.Throws<InvalidDataException>(() => PackReader.Read(dir, catalog));
        m.Resources["sprite"].Rect = null; m.Resources["sprite"].PixelsPerUnit = float.NaN; Write(m);
        Assert.Throws<InvalidDataException>(() => PackReader.Read(dir, catalog));
    }
    [Fact]
    public void EmptyDeepFoldersAreRejectedBeforeRecursiveEnumeration()
    {
        var dir = Write(Manifest());
        Directory.CreateDirectory(Path.Combine(dir, Path.Combine(Enumerable.Repeat("a", 33).ToArray())));
        Assert.Throws<InvalidDataException>(() => PackReader.Read(dir, catalog));
    }
    [Fact]
    public void SpriteBordersAndImageTypeAreValidated()
    {
        var m = Manifest(); m.Resources["sprite"] = new ResourceRef { File = "sprite.png", Border = new[] { 1,0,1,0 } };
        var dir = Write(m); Assert.Throws<InvalidDataException>(() => PackReader.Read(dir,catalog));
        m.Resources["sprite"].Border = new[] { 0,0,0,0 };
        catalog.Ui["ui/image"] = new CatalogUi { Component = "Image" };
        m.Ui["ui/image"] = new UiBinding { Sprite = "sprite", ImageType = "sliced" }; Write(m);
        Assert.Single(PackReader.Read(dir,catalog).Manifest.Ui);
        m.Ui["ui/image"].ImageType = "unknown"; Write(m);
        Assert.Throws<InvalidDataException>(() => PackReader.Read(dir,catalog));
    }
    [Fact]
    public void UiResourceMustMatchTargetComponent()
    {
        catalog.Ui["ui/text"] = new CatalogUi { Component = "TextMeshProUGUI" };
        var m = Manifest(); m.Resources["sprite"] = new ResourceRef { File = "sprite.png" };
        m.Ui["ui/text"] = new UiBinding { Sprite = "sprite" };
        var dir = Write(m);
        Assert.Throws<InvalidDataException>(() => PackReader.Read(dir, catalog));
        catalog.Ui["ui/text"].Component = "Image";
        Assert.Single(PackReader.Read(dir, catalog).Manifest.Ui);
    }
    private sealed class TestTheme : IDisposable { public bool Disposed; public void Dispose() => Disposed = true; }
    [Fact]
    public void FailedCommitDisposesCandidateAndKeepsPrevious()
    {
        var transaction = new ThemeTransaction<TestTheme>(); var previous = new TestTheme(); var candidate = new TestTheme();
        Assert.True(transaction.TryPrepare(() => previous, (_, _) => { }, out _));
        Assert.False(transaction.TryPrepare(() => candidate, (_, _) => throw new InvalidOperationException("commit failed"), out var error));
        Assert.Equal("commit failed", error); Assert.Same(previous, transaction.Current);
        Assert.False(previous.Disposed); Assert.True(candidate.Disposed);
        transaction.Restore((_, _) => { });
    }
    [Fact]
    public void ZipSymlinkIsRejected()
    {
        var zip = Path.Combine(root, "linked.zip");
        using (var archive = ZipFile.Open(zip, ZipArchiveMode.Create))
        {
            var entry = archive.CreateEntry("skin.json"); entry.ExternalAttributes = unchecked((int)0xA1FF0000);
            using var writer = new StreamWriter(entry.Open()); writer.Write(Json.Write(Manifest()));
        }
        Assert.Throws<InvalidDataException>(() => PackReader.Read(zip, catalog));
    }
    public void Dispose() { Directory.Delete(root, true); }
}
