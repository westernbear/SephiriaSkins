using SephiriaSkins.Core;
using System.Text.RegularExpressions;

namespace SephiriaSkins.Plugin;

// One immutable evidence directory per invocation. Never use a user supplied path.
internal sealed class DiagnosticRun
{
    public string PackId { get; }
    public bool ExplicitPack { get; }
    public string WeaponScope { get; }
    public int[]? WeaponIds { get; }
    public int SoakMinutes { get; }
    public string[] CyclePackIds { get; } = Array.Empty<string>();
    public Dictionary<string, string> SkippedTrials { get; } = new(StringComparer.Ordinal);
    public string RunId { get; } = DateTime.UtcNow.ToString("yyyyMMddTHHmmssfffZ") + "-" + Guid.NewGuid().ToString("N");
    public string DirectoryPath { get; }
    public string PluginSha256 { get; } = Keys.Hash(System.IO.File.ReadAllBytes(typeof(Plugin).Assembly.Location));
    public DiagnosticRun(string root, string[] arguments)
    {
        var options = arguments.Where(a => a.StartsWith("--skins-test-pack=", StringComparison.Ordinal)).ToArray();
        if (arguments.Contains("--skins-test-pack")) throw new ArgumentException("Use --skins-test-pack=<pack ID>.");
        if (options.Length > 1) throw new ArgumentException("Specify --skins-test-pack once.");
        ExplicitPack = options.Length == 1;
        PackId = ExplicitPack ? options[0].Substring("--skins-test-pack=".Length) : "fan.hachiware";
        if (!Regex.IsMatch(PackId, @"\A[a-z0-9][a-z0-9._-]{2,63}\z")) throw new ArgumentException("Invalid --skins-test-pack ID.");
        var cycleOptions = arguments.Where(a => a.StartsWith("--skins-cycle-packs=", StringComparison.Ordinal)).ToArray();
        if (cycleOptions.Length > 1 || arguments.Contains("--skins-cycle-packs")) throw new ArgumentException("Use --skins-cycle-packs=<comma separated IDs> once.");
        if (cycleOptions.Length == 1)
        {
            CyclePackIds = cycleOptions[0].Substring("--skins-cycle-packs=".Length).Split(',');
            if (CyclePackIds.Length < 2 || CyclePackIds.Distinct().Count() != CyclePackIds.Length ||
                CyclePackIds.Any(id => !Regex.IsMatch(id, @"\A[a-z0-9][a-z0-9._-]{2,63}\z")))
                throw new ArgumentException("Cycle requires two or more unique pack IDs.");
        }
        var weaponOptions = arguments.Where(a => a.StartsWith("--skins-test-weapons=", StringComparison.Ordinal)).ToArray();
        if (weaponOptions.Length > 1 || arguments.Contains("--skins-test-weapons")) throw new ArgumentException("Use --skins-test-weapons=defaults or a comma separated ID list once.");
        WeaponScope = weaponOptions.Length == 0 ? "all" : weaponOptions[0].Substring("--skins-test-weapons=".Length);
        if (WeaponScope != "all" && WeaponScope != "defaults")
        {
            if (!Regex.IsMatch(WeaponScope, @"\A[0-9]+(?:,[0-9]+)*\z")) throw new ArgumentException("Invalid diagnostic weapon selection.");
            WeaponIds = WeaponScope.Split(',').Select(int.Parse).Distinct().ToArray();
        }
        var soakOptions = arguments.Where(a => a.StartsWith("--skins-soak-minutes=", StringComparison.Ordinal)).ToArray();
        if (soakOptions.Length > 1 || arguments.Contains("--skins-soak-minutes")) throw new ArgumentException("Use --skins-soak-minutes=<1..180> once.");
        if (soakOptions.Length == 1)
        {
            if (!int.TryParse(soakOptions[0].Substring("--skins-soak-minutes=".Length), out var minutes) || minutes < 1 || minutes > 180)
                throw new ArgumentException("Diagnostic soak must be 1..180 minutes.");
            SoakMinutes = minutes;
        }
        var skipOptions = arguments.Where(a => a.StartsWith("--skins-skip-trials=", StringComparison.Ordinal)).ToArray();
        var reasons = arguments.Where(a => a.StartsWith("--skins-skip-reason=", StringComparison.Ordinal)).ToArray();
        if (skipOptions.Length > 1 || reasons.Length > 1 || arguments.Contains("--skins-skip-trials") || arguments.Contains("--skins-skip-reason"))
            throw new ArgumentException("Specify skipped native trials and their reason once.");
        if (skipOptions.Length != reasons.Length) throw new ArgumentException("Skipped native trials require --skins-skip-reason=<reason>.");
        if (skipOptions.Length == 1)
        {
            var skipped = skipOptions[0].Substring("--skins-skip-trials=".Length);
            var reason = reasons[0].Substring("--skins-skip-reason=".Length).Trim();
            if (!Regex.IsMatch(skipped, @"\A[0-9]+:(?:basic|dash|special)(?:,[0-9]+:(?:basic|dash|special))*\z") ||
                reason.Length < 3 || reason.Length > 256 || reason.Any(char.IsControl)) throw new ArgumentException("Invalid skipped native trial or reason.");
            foreach (var trial in skipped.Split(',')) SkippedTrials.Add(trial, reason);
        }
        DirectoryPath = Path.Combine(root, "Export", "diagnostics", PackId, RunId);
        Directory.CreateDirectory(DirectoryPath);
        System.IO.File.WriteAllText(File("run.json"), Json.Write(new { packId = PackId, runId = RunId, pluginSha256 = PluginSha256, weaponScope = WeaponScope, soakMinutes = SoakMinutes, cyclePackIds = CyclePackIds, skippedTrials = SkippedTrials, arguments, startedUtc = DateTime.UtcNow }));
    }
    public string File(string name) => Path.Combine(DirectoryPath, name);
    public PackEntry Select(IEnumerable<PackEntry> entries)
    {
        var entry = entries.SingleOrDefault(p => p.Pack?.Manifest.Id == PackId && p.Error == null)
            ?? throw new InvalidOperationException("Diagnostic pack unavailable or rejected: " + PackId);
        RecordManifest(entry);
        return entry;
    }
    public void RecordManifest(PackEntry entry) => System.IO.File.WriteAllText(File("manifest-" + entry.Pack!.Manifest.Id + ".json"), Json.Write(entry.Pack.Manifest));
}
