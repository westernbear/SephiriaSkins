using System.Collections;
using HarmonyLib;
using SephiriaSkins.Core;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.EventSystems;
using Object = UnityEngine.Object;

namespace SephiriaSkins.Plugin;

// Explicit opt-in only. Drives the game's live host and native input/combat APIs.
// Save data is ephemeral; no existing profile or Steam Cloud save is bound.
internal static class PlayProbe
{
    private static readonly string Profile = "SKINS_PLAYTEST_" + Guid.NewGuid().ToString("N");
    private static readonly List<object> Results = new();
    private static readonly List<string> Exceptions = new();
    private static readonly List<string> Events = new();
    internal static event Action<ProjectileBase>? NativeFireObserved;
    private static readonly List<string> Projectiles = new();
    private static readonly List<string> Hits = new();
    private static readonly List<string> UnitHits = new();
    private static readonly HashSet<ProjectileBase> TrialProjectiles = new();
    private static readonly HashSet<string> States = new();
    private static readonly List<float> FrameTimes = new();
    private static readonly List<float> FireTimes = new();
    private static readonly Dictionary<string, int> RoleFrames = new(), ThemedRoleFrames = new();
    private static float attackBegin;
    private static GameObject? sampler;
    private static int frames, themedFrames;
    private static bool observing;
    private static string phase = "startup";
    private static string output = "";
    private static Plugin plugin = null!;
    private static Gamepad? gamepad;
    private static string? priorScheme;
    private static InputDevice[] priorDevices = Array.Empty<InputDevice>();
    private static DiagnosticSession? session;
    private static string? priorLanguage;
    private static int priorWidth, priorHeight;
    private static FullScreenMode priorFullscreen;
    private static Harmony? diagnostic;
    private static InputSettings.BackgroundBehavior priorBackground;

    public static IEnumerator Run(Plugin instance, Func<PackEntry, IEnumerator> apply, Action restore,
        Action<bool> selector, Func<string[]> hookErrors, string root, Action restorePreference)
    {
        plugin = instance; output = plugin.Diagnostics.File("play-results.json");
        Directory.CreateDirectory(Path.GetDirectoryName(output));
        // Batch players have no hardware devices. Supply a supported Input System
        // device before the game's PlayerInput OnEnable selects its control scheme.
        priorBackground = InputSystem.settings.backgroundBehavior;
        InputSystem.settings.backgroundBehavior = InputSettings.BackgroundBehavior.IgnoreFocus;
        gamepad = InputSystem.AddDevice<Gamepad>("Skins diagnostic gamepad");
        sampler = new GameObject("SephiriaSkinsPlaySampler", typeof(PlayProbeFrameSampler));
        Object.DontDestroyOnLoad(sampler);
        Application.logMessageReceived += Log;
        var pending = new Stack<IEnumerator>(); pending.Push(Steps(apply, restore, selector, root));
        // Flatten child coroutines so failures are recorded and restoration still runs.
        while (pending.Count > 0)
        {
            if (File.Exists(plugin.Diagnostics.File("cancel.request"))) { Exceptions.Add("diagnostic cancelled by request"); break; }
            object? value = null; bool next;
            try { next = pending.Peek().MoveNext(); if (next) value = pending.Peek().Current; }
            catch (Exception e) { Exceptions.Add(phase + ": " + e); break; }
            if (!next) { (pending.Pop() as IDisposable)?.Dispose(); continue; }
            if (value is IEnumerator nested) pending.Push(nested);
            else yield return value;
        }
        while (pending.Count > 0) (pending.Pop() as IDisposable)?.Dispose();
        observing = false;
        NativeFireObserved = null;
        try
        {
            selector(false); restore();
            restorePreference();
            if (gamepad != null)
            {
                if (PlayerInputController.Instance && priorScheme != null && priorDevices.Length > 0)
                    PlayerInputController.Instance.playerInput.SwitchCurrentControlScheme(priorScheme, priorDevices);
                InputSystem.RemoveDevice(gamepad); gamepad = null;
            }
            InputSystem.settings.backgroundBehavior = priorBackground;
            if (priorLanguage != null) LocalizationManager.Instance.LoadLanguage(priorLanguage);
            if (priorWidth > 0) Screen.SetResolution(priorWidth, priorHeight, priorFullscreen);
            RuntimeProbe.LeaveLobby();
            if (Mirror.NetworkServer.active) Mirror.NetworkManager.singleton.StopHost();
            if (sampler) Object.Destroy(sampler);
        }
        catch (Exception e) { Exceptions.Add("cleanup: " + e); }
        yield return new WaitForSecondsRealtime(3);
        session?.Dispose(); session = null;
        var restored = new { themeNull = plugin.Theme == null, audioChannels = plugin.Audio.ActiveReplacements, virtualDeviceRemoved = gamepad == null,
            configBytesRestored = plugin.DiagnosticConfigRestored, profile = SaveManager.Binded, profileFileAbsent = !SaveData.Exists(Profile) && !SaveData.Exists(Profile + "TMP") };
        Write(hookErrors(), restored);
        Application.logMessageReceived -= Log;
        diagnostic?.UnpatchSelf(); diagnostic = null;
        if (Environment.GetCommandLineArgs().Contains("--skins-probe-exit")) Application.Quit();
    }

    private static IEnumerator Steps(Func<PackEntry, IEnumerator> apply, Action restore, Action<bool> selector, string root)
    {
        yield return new WaitForSecondsRealtime(20);
        phase = "isolated host";
        if (!SteamManager.Initialized) throw new InvalidOperationException("Start and sign in to Steam, then restart the playtest.");
        if (Mirror.NetworkServer.active || Mirror.NetworkClient.active) throw new InvalidOperationException("Playtest requires a fresh title session.");
        session = new DiagnosticSession(Profile);
        priorLanguage = LocalizationManager.Instance.CurrentLanguage;
        priorWidth = Screen.width; priorHeight = Screen.height; priorFullscreen = Screen.fullScreenMode;
        diagnostic = new Harmony("dev.sephiria.skins.playtest");
        diagnostic.Patch(AccessTools.Method(typeof(Animator2D_Basic), "SetFrame"), postfix: new HarmonyMethod(typeof(PlayProbe), nameof(FrameObserved)));
        diagnostic.Patch(AccessTools.Method(typeof(MeleeCollision), nameof(MeleeCollision.Initialize)), postfix: new HarmonyMethod(typeof(PlayProbe), nameof(MeleeObserved)));
        diagnostic.Patch(AccessTools.Method(typeof(Bullet), nameof(Bullet.Initialize)), postfix: new HarmonyMethod(typeof(PlayProbe), nameof(BulletObserved)));
        foreach (var method in typeof(ProjectileBase).Assembly.GetTypes().Where(t => !t.IsAbstract && typeof(ProjectileBase).IsAssignableFrom(t))
                     .Select(t => t.GetMethod(nameof(ProjectileBase.Initialize))).Where(m => m != null && m.DeclaringType != typeof(Bullet) && m.DeclaringType != typeof(MeleeCollision)).Distinct())
            diagnostic.Patch(method!, postfix: new HarmonyMethod(typeof(PlayProbe), nameof(SpecialProjectileObserved)));
        EOSLobbyManager.StartHostWhenReady();
        yield return Await(() => (bool)Ownership.Local, 50, "local player spawn");
        yield return new WaitForSecondsRealtime(8);
        yield return RuntimeProbe.CreatePrivateLobby(value => Add("solo lobby", value));
        Add("environment", new
        {
            memoryOnlySave = !SaveManager.Current.enableSave && !SaveManager.Current.enableCloudSave,
            profile = SaveManager.Binded, server = Mirror.NetworkServer.active, client = Mirror.NetworkClient.active,
            stages = Resources.FindObjectsOfTypeAll<StageEntity>().Where(s => s).Select(s => new { s.id, s.requireQuestProgress }).ToArray(),
            floors = DungeonManager.Instance.generatedFloors.Values.Select(f => new { f.guid, f.name, f.stageName }).ToArray(),
            creatures = CombatManager.Instance.AllCreatures.Where(u => u).Select(u => new { u.name, u.faction, u.hp }).ToArray(),
            bindings = PlayerInputController.Instance.playerInput.actions.Select(a => new { a.name, paths = a.bindings.Select(b => b.effectivePath).ToArray() }).ToArray()
        });
        if (Environment.GetCommandLineArgs().Contains("--skins-playtest-discover")) yield break;
        var entry = plugin.Diagnostics.Select(PackDiscovery.Scan(Path.Combine(root, "Skins"), plugin.Catalog));
        var persistedStartup = Environment.GetCommandLineArgs().Contains("--skins-playtest-persisted");
        if (persistedStartup)
        {
            var startupTheme = plugin.Theme ?? throw new InvalidOperationException("Saved theme did not load through normal startup.");
            var initialUi = VisualProbe.UiSnapshot(startupTheme.Pack.Manifest.Ui.Keys);
            plugin.SuspendDiagnosticTheme();
            var originalUi = VisualProbe.UiSnapshot(startupTheme.Pack.Manifest.Ui.Keys);
            plugin.ResumeDiagnosticTheme(startupTheme);
            var body = Ownership.Local!.TopdownActor.bodyRenderer.sprite;
            Add("persisted startup", new { selected = plugin.SelectedId, theme = plugin.Theme?.Pack.Manifest.Id,
                pixel = plugin.PixelArt, gameUi = plugin.GameUi, bodyThemed = IsThemed(body),
                filter = body ? body.texture.filterMode.ToString() : "", uiOriginal = initialUi == originalUi,
                uiRoundTrip = plugin.GameUi ? RuntimeProbe.CheckUiRoundTrip(plugin) : new { available = false },
                config = File.ReadAllText(plugin.Config.ConfigFilePath) });
        }
        else
        {
            plugin.SetPixelArt(true); plugin.SetGameUi(true);
            yield return apply(entry);
        }
        var input = PlayerInputController.Instance.playerInput;
        priorScheme = input.currentControlScheme; priorDevices = input.devices.Where(d => d != gamepad).ToArray();
        if (!input.user.valid)
        {
            input.enabled = false; input.defaultControlScheme = "Gamepad";
            input.gameObject.SetActive(true); input.enabled = true;
        }
        input.SwitchCurrentControlScheme("Gamepad", gamepad);
        InputSystem.EnableDevice(gamepad!);
        yield return null;
        var player = Ownership.Local!;
        var start = player.transform.position;
        InputSystem.QueueDeltaStateEvent(gamepad!.leftStick, new Vector2(.65f, 0));
        yield return new WaitForSecondsRealtime(.5f);
        InputSystem.QueueDeltaStateEvent(gamepad.leftStick, Vector2.zero);
        InputSystem.QueueDeltaStateEvent(gamepad.rightStick, Vector2.right);
        yield return new WaitForSecondsRealtime(.3f);
        Add("gamepad movement", new { distance = Vector3.Distance(start, player.transform.position), scheme = input.currentControlScheme,
            nativeInput = new[] { player.localDataStorage.currentInput.x, player.localDataStorage.currentInput.y } });
        var nativeAttacks = 0;
        void PadAttack(int _) => nativeAttacks++;
        player.GetComponent<WeaponControllerSimple>().OnBeginAttackAnimation += PadAttack;
        InputSystem.QueueStateEvent(gamepad, new GamepadState { rightStick = Vector2.right }.WithButton(GamepadButton.West)); yield return new WaitForSecondsRealtime(.15f);
        InputSystem.QueueStateEvent(gamepad, new GamepadState { rightStick = Vector2.right }); yield return new WaitForSecondsRealtime(1.5f);
        player.GetComponent<WeaponControllerSimple>().OnBeginAttackAnimation -= PadAttack;
        Add("gamepad attack", new { nativeAttacks });
        if (persistedStartup) yield break;
        if (plugin.Diagnostics.CyclePackIds.Length > 0)
        {
            yield return CycleProbe.Run(plugin, apply, restore, root, Add);
            yield break;
        }
        if (Environment.GetCommandLineArgs().Contains("--skins-playtest-controls"))
        {
            yield return KeyboardControls();
            yield break;
        }
        if (Environment.GetCommandLineArgs().Contains("--skins-playtest-visual"))
        {
            yield return VisualProbe.Inspect(plugin, apply, restore, selector, root);
            yield break;
        }
        if (Environment.GetCommandLineArgs().Contains("--skins-playtest-with-visual"))
            yield return VisualProbe.Inspect(plugin, apply, restore, selector, root);
        var paths = new RuntimeCatalog.PathIndex();
        var graphics = Object.FindObjectsByType<UnityEngine.UI.Graphic>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        Add("path equivalence", new { checkedPaths = graphics.Length, mismatches = graphics.Count(g => paths.UiKey(g) != RuntimeCatalog.UiKey(g)) });
        var weapons = DiagnosticWeapons.Playable();
        Add("weapon coverage", DiagnosticWeapons.Coverage());
        foreach (var weapon in weapons)
        {
            if (Environment.GetCommandLineArgs().Contains("--skins-playtest-encounter") && weapon.id != 400) continue;
            foreach (var mode in new[] { "basic", "dash", "special" })
            {
                if (!DiagnosticWeapons.ShouldRun(weapon, mode)) continue;
                if (Environment.GetCommandLineArgs().Contains("--skins-playtest-encounter") && mode != "special") continue;
                object? original = null, themed = null;
                var combatTheme = plugin.SuspendDiagnosticTheme();
                yield return Attack(weapon, mode, value => original = value);
                plugin.ResumeDiagnosticTheme(combatTheme);
                yield return Attack(weapon, mode, value => themed = value);
                Add("combat " + weapon.id + " " + mode, new { weapon.id, type = weapon.mainWeaponPrefab.GetComponent<WeaponSimple>().weaponType.ToString(), mode, original, themed });
            }
        }
        if (Environment.GetCommandLineArgs().Contains("--skins-playtest-combat-only")) yield break;
        phase = "costume";
        foreach (var costume in Resources.FindObjectsOfTypeAll<CostumeEntity>().Where(c => c).Take(4))
        {
            player.EquipCostume(costume.id, CostumeDatabase.FindDefaultCostumeSkin(costume.id).skinID); yield return new WaitForSecondsRealtime(.8f);
            Add("costume", new { requested = costume.id, actual = player.currentCostume, themed = IsThemed(player.TopdownActor.bodyRenderer.sprite) });
        }
        phase = "floor travel";
        var dungeon = DungeonManager.Instance;
        var home = dungeon.FindHomeFloor();
        var stage = dungeon.Race.stages.First(s => s && s.firstFloor);
        dungeon.LoadStageAndMove(stage.name);
        yield return Await(() => Ownership.Local && Ownership.Local!.currentFloorGuid != home.guid && Ownership.Local.loadingScreenType == -1, 40, "dungeon entry");
        yield return new WaitForSecondsRealtime(3);
        player = Ownership.Local!;
        Add("dungeon entry", new { stage = stage.name, player.currentFloorGuid, player.isInDungeon, themed = IsThemed(player.TopdownActor.bodyRenderer.sprite), music = MusicState() });
        RuntimeProbe.Capture(plugin.Diagnostics.File("play-dungeon.png"), Debug.Log);
        var nextFloor = dungeon.generatedFloors.Values.FirstOrDefault(f => f.stageName == stage.name && f.guid != player.currentFloorGuid);
        if (nextFloor != null)
        {
            dungeon.FloorAlloc(nextFloor.guid);
            yield return Await(() => FloorGenerator.FindByGuid(nextFloor.guid)?.GenerateSuccess == true, 30, "next floor generation");
            var spawn = FloorGenerator.FindByGuid(nextFloor.guid).spawnPoints.First(s => s);
            dungeon.MoveFloor(player, nextFloor.guid, spawn.SpawnPointId, 1, allowSave: false);
            yield return Await(() => Ownership.Local && Ownership.Local!.currentFloorGuid == nextFloor.guid && Ownership.Local.loadingScreenType == -1, 40, "next floor");
            yield return new WaitForSecondsRealtime(2);
            Add("floor transition", new { nextFloor.name, current = Ownership.Local!.currentFloorGuid, themed = IsThemed(Ownership.Local.TopdownActor.bodyRenderer.sprite) });
            if (Environment.GetCommandLineArgs().Contains("--skins-playtest-encounter") || Environment.GetCommandLineArgs().Contains("--skins-playtest-with-encounter")) yield return Encounter(root);
        }
        phase = "death and revive";
        player = Ownership.Local!;
        var gameOver = player.dieIsGameOver; player.dieIsGameOver = NestedBoolean.False;
        player.Die(0, null); yield return new WaitForSecondsRealtime(1);
        var died = player.IsDead;
        RuntimeProbe.Capture(plugin.Diagnostics.File("play-death.png"), Debug.Log);
        player.Revive(player.MaxHp); yield return new WaitForSecondsRealtime(2);
        player.dieIsGameOver = gameOver;
        Add("death revive", new { died, revived = !player.IsDead, hp = player.hp, themed = IsThemed(player.TopdownActor.bodyRenderer.sprite) });
        dungeon.FloorAlloc(home.guid);
        yield return Await(() => FloorGenerator.FindByGuid(home.guid)?.GenerateSuccess == true, 30, "town generation");
        var homeSpawn = FloorGenerator.FindByGuid(home.guid).spawnPoints.First(s => s);
        dungeon.MoveFloor(player, home.guid, homeSpawn.SpawnPointId, 1, allowSave: false);
        yield return Await(() => Ownership.Local && Ownership.Local!.currentFloorGuid == home.guid && Ownership.Local.loadingScreenType == -1, 40, "town return");
        yield return new WaitForSecondsRealtime(2);
        Add("town return", new { floor = Ownership.Local!.currentFloorGuid, themed = IsThemed(Ownership.Local.TopdownActor.bodyRenderer.sprite), music = MusicState() });
        if (Environment.GetCommandLineArgs().Contains("--skins-playtest-encounter")) yield break;
        phase = "language and resolution";
        var statusPanel = UIManager.Instance.GetElement<UI_CharacterStatusPanel>();
        statusPanel.Open(); yield return new WaitForSecondsRealtime(.5f);
        RuntimeProbe.Capture(plugin.Diagnostics.File("play-inventory.png"), Debug.Log);
        Add("inventory HUD", new { opened = statusPanel.IsOpened, roundTrip = RuntimeProbe.CheckUiRoundTrip(plugin) });
        statusPanel.Close(); yield return new WaitForSecondsRealtime(.3f);
        var options = UIManager.Instance.GetElement<UI_OptionsPanel>(); options.Open();
        foreach (var language in LocalizationManager.Instance.Languages)
        {
            LocalizationManager.Instance.LoadLanguage(language); yield return new WaitForSecondsRealtime(.4f);
            var texts = Resources.FindObjectsOfTypeAll<TMPro.TMP_Text>().Where(t => t && t.gameObject.activeInHierarchy && t.font).ToArray();
            plugin.Ui.Restore();
            var originalText = texts.ToDictionary(t => t.GetInstanceID(), t => t.text);
            var nativeMissing = Missing(texts);
            plugin.Ui.Apply(plugin.Theme);
            var skinMissing = Missing(texts);
            Add("language", new { language, activeTexts = texts.Length, nativeMissing, skinMissing,
                textPreserved = texts.All(t => t.text == originalText[t.GetInstanceID()]), newMissing = new string(skinMissing.Except(nativeMissing).ToArray()) });
        }
        options.Close(); yield return new WaitForSecondsRealtime(.3f);
        LocalizationManager.Instance.LoadLanguage(priorLanguage);
        foreach (var size in new[] { new Vector2Int(1280, 720), new Vector2Int(1920, 1080) })
        {
            Screen.SetResolution(size.x, size.y, FullScreenMode.Windowed); yield return new WaitForSecondsRealtime(.6f);
            selector(true); yield return null;
            var first = EventSystem.current.currentSelectedGameObject;
            InputSystem.QueueStateEvent(gamepad, new GamepadState().WithButton(GamepadButton.DpadDown)); yield return new WaitForSecondsRealtime(.15f);
            InputSystem.QueueStateEvent(gamepad, new GamepadState()); yield return new WaitForSecondsRealtime(.15f);
            var movedFocus = EventSystem.current.currentSelectedGameObject != first;
            RuntimeProbe.Capture(plugin.Diagnostics.File("play-selector-" + size.x + ".png"), Debug.Log);
            InputSystem.QueueStateEvent(gamepad, new GamepadState().WithButton(GamepadButton.East)); yield return new WaitForSecondsRealtime(.15f);
            InputSystem.QueueStateEvent(gamepad, new GamepadState()); yield return new WaitForSecondsRealtime(.4f);
            Add("resolution selector", new { width = Screen.width, height = Screen.height, movedFocus, closedByGamepad = !plugin.SelectorOpen });
            selector(false);
        }
        phase = "reload endurance";
        restore(); yield return Reclaim();
        var before = ResourcesSnapshot();
        for (var i = 0; i < 25; i++)
        {
            yield return apply(entry); yield return new WaitForSecondsRealtime(.05f);
            restore(); yield return new WaitForSecondsRealtime(.05f);
        }
        yield return Reclaim();
        Add("reload endurance", new { cycles = 25, before, after = ResourcesSnapshot(), channels = plugin.Audio.ActiveReplacements });
        yield return apply(entry);
        RuntimeProbe.Capture(plugin.Diagnostics.File("play-final.png"), Debug.Log);
        phase = "session restart";
        var priorPlayer = Ownership.Local!.GetInstanceID();
        Mirror.NetworkManager.singleton.StopHost(); yield return new WaitForSecondsRealtime(4);
        session!.RestartRun();
        EOSLobbyManager.StartHostWhenReady();
        yield return Await(() => Ownership.Local && Ownership.Local!.GetInstanceID() != priorPlayer, 50, "session restart");
        yield return new WaitForSecondsRealtime(5);
        Add("session restart", new { newAvatar = Ownership.Local!.GetInstanceID() != priorPlayer, themed = IsThemed(Ownership.Local.TopdownActor.bodyRenderer.sprite), local = Ownership.Local.isLocalPlayer });
        if (Environment.GetCommandLineArgs().Contains("--skins-playtest-with-controls")) yield return KeyboardControls();
        if (plugin.Diagnostics.SoakMinutes > 0) yield return Soak(entry, apply);
    }

    // Opt-in automated endurance, using native actions in the isolated live host.
    // It does not stand in for manual exploration or remote co-op verification.
    private static IEnumerator Soak(PackEntry entry, Func<PackEntry, IEnumerator> apply)
    {
        if (!RuntimeProbe.IsPrivateSoloLobby(out var startingLobby))
        {
            RuntimeProbe.LeaveLobby();
            yield return RuntimeProbe.CreatePrivateLobby(value => Add("endurance lobby recreate", value));
            if (!RuntimeProbe.IsPrivateSoloLobby(out startingLobby)) throw new InvalidOperationException("Endurance requires an active private solo lobby.");
        }
        Add("endurance lobby start", startingLobby);
        var began = Time.realtimeSinceStartup;
        var startedUtc = DateTime.UtcNow;
        var duration = plugin.Diagnostics.SoakMinutes * 60;
        var nativeWeapons = DiagnosticWeapons.All().Where(w => w.isDefaultWeapon && DiagnosticWeapons.SkipReason(w) == null).ToArray();
        var cycles = 0; var actions = 0; var skippedActions = 0;
        var bad = new List<string>();
        while (Time.realtimeSinceStartup - began < duration)
        {
            var weapon = nativeWeapons[cycles % nativeWeapons.Length];
            foreach (var mode in new[] { "basic", "dash", "special" })
            {
                if (!DiagnosticWeapons.ShouldRun(weapon, mode)) { skippedActions++; continue; }
                object? trial = null;
                yield return Attack(weapon, mode, value => trial = value);
                actions++;
                // Keep raw native evidence for every action, with periodic summaries.
                File.AppendAllText(plugin.Diagnostics.File("soak-actions.jsonl"), Json.Write(new { actions, weapon.id, mode, trial }).Replace("\r", "").Replace("\n", "") + "\n");
            }
            phase = "automated endurance";
            plugin.SetPixelArt(cycles % 2 == 0); plugin.SetGameUi(cycles % 4 < 2);
            if (cycles % 5 == 0) yield return apply(entry);
            var player = Ownership.Local!;
            var before = player.transform.position;
            InputSystem.QueueDeltaStateEvent(gamepad!.leftStick, cycles % 2 == 0 ? Vector2.up : Vector2.down);
            yield return new WaitForSecondsRealtime(.4f);
            InputSystem.QueueDeltaStateEvent(gamepad.leftStick, Vector2.zero);
            yield return new WaitForSecondsRealtime(.2f);
            if (!IsThemed(player.TopdownActor.bodyRenderer.sprite)) bad.Add("native body not themed at cycle " + cycles);
            if (Vector3.Distance(before, player.transform.position) < .01f) bad.Add("native movement did not advance at cycle " + cycles);
            if (!RuntimeProbe.IsPrivateSoloLobby(out var cycleLobby)) bad.Add("private solo lobby changed at cycle " + cycles);
            cycles++;
            var progress = new { startedUtc, requestedMinutes = plugin.Diagnostics.SoakMinutes, elapsedSeconds = Time.realtimeSinceStartup - began,
                cycles, actions, skippedActions, theme = plugin.Theme?.Pack.Manifest.Id, audioChannels = plugin.Audio.ActiveReplacements,
                lobby = cycleLobby,
                failures = bad.ToArray(), exceptions = Exceptions.Count, completed = Time.realtimeSinceStartup - began >= duration,
                scope = "automated native combat/movement, rendering, four switch combinations and reload in solo host; no remote participant" };
            File.WriteAllText(plugin.Diagnostics.File("soak-progress.json"), Json.Write(progress));
            if (cycles % 3 == 0) Debug.Log("SKINS_SOAK_PROGRESS " + Json.Write(progress));
        }
        plugin.SetPixelArt(true); plugin.SetGameUi(true);
        if (!RuntimeProbe.IsPrivateSoloLobby(out var endingLobby)) bad.Add("private solo lobby unavailable at endurance end");
        Add("endurance lobby end", endingLobby);
        Add("automated endurance", new { startedUtc, requestedMinutes = plugin.Diagnostics.SoakMinutes,
            elapsedSeconds = Time.realtimeSinceStartup - began, cycles, actions, skippedActions, failures = bad.ToArray(), completed = true });
    }

    private static IEnumerator Attack(WeaponEntity weapon, string mode, Action<object> done)
    {
        phase = "attack " + weapon.id + " " + mode + " " + (plugin.Theme == null ? "original" : "theme"); Debug.Log("SKINS_PLAY_PHASE " + phase);
        var player = Ownership.Local!; var controller = player.GetComponent<WeaponControllerSimple>();
        player.CancelCurrentAction(); player.DespawnAllBullet(); player.ClearBuffs(); controller.EquipWeapon(false, weapon.id);
        yield return new WaitForSecondsRealtime(.5f);
        if (!controller.currentWeapon || controller.currentWeapon.entityId != weapon.id)
            throw new InvalidOperationException("Native equip failed for weapon " + weapon.id);
        // A fresh avatar has only 50 MP; upgraded skills may cost more. Raise
        // only this memory-only fixture's capacity, equally for both A/B trials.
        var priorMaxMp = player.maxMp;
        var priorMoney = player.Money;
        if (controller.currentWeapon is WeaponSimple_GreatSword { moneyWhirlwind: true }) player.NetworkcurrentMoney = Math.Max(priorMoney, 10000);
        player.NetworkmaxMp = Math.Max(priorMaxMp, 1000);
        player.Networkmp = player.MaxMp; player.CurrentDashModule.RestoreDashCount(99);
        var dummy = CombatManager.Instance.AllCreatures.Where(u => u && u.faction == "Dummy").OrderBy(u => Vector3.Distance(u.transform.position, player.transform.position)).FirstOrDefault();
        // Poison/bleed from a previous sample otherwise keeps ticking into the
        // next comparison. Reset native buffs on this isolated target too.
        if (dummy)
        {
            dummy!.ClearBuffs();
            // Native negative statuses are separate from positive Buffs.
            foreach (var debuff in dummy.Debuffs.ToArray()) if (debuff && !debuff.IsEndBuff) debuff.Destroy();
        }
        var nativeFireData = mode == "special" ? ReadFireData(controller.currentWeapon, nameof(WeaponSimple.GetSpecialAttack)) : mode == "dash" ? ReadFireData(controller.currentWeapon, nameof(WeaponSimple.GetDashAttack)) : controller.currentWeapon.GetBasicAttack(0, "");
        var mineInput = nativeFireData is NewWeaponFireData_Bullet mineData && mineData.bulletPrefab && UsesMine(mineData.bulletPrefab);
        if (dummy) player.ReqSetPosition(dummy!.transform.position - Vector3.right * (mineInput ? 8 : mode == "dash" ? 2.5f : 1.2f), true);
        yield return new WaitForSecondsRealtime(.2f);
        var start = player.transform.position; var radius = player.TopdownRigidbody.MovementCollider.radius;
        var mpCosts = new List<int>();
        void Mp(int amount) => mpCosts.Add(amount);
        player.OnMpUsedServerside += Mp;
        UnityEngine.Random.InitState(105715);
        Events.Clear(); Projectiles.Clear(); Hits.Clear(); UnitHits.Clear(); TrialProjectiles.Clear(); States.Clear(); FrameTimes.Clear(); FireTimes.Clear(); RoleFrames.Clear(); ThemedRoleFrames.Clear(); frames = themedFrames = 0;
        foreach (var gun in player.Inventory.charms.Values.OfType<Charm_Golem_Gun>()) gun.defaultAttackIntervalTimer.SetTimer(0);
        void Basic(int id) { Events.Add("basic:" + id); attackBegin = Time.time; }
        void Dash() { Events.Add("dash"); attackBegin = Time.time; }
        void Special(int id) { Events.Add("special:" + id); attackBegin = Time.time; }
        void SpecialSwing(int id) { if (controller.currentWeapon.weaponType == EWeaponType.Katana) Events.Add("special-swing:" + id); }
        void Hit(CombatBehaviour victim, DamageInstance damage, ProjectileBase projectile)
        { if (TrialProjectiles.Contains(projectile)) Hits.Add(Json.Write(new { victim = victim.name, damage.damage, damage.damageResult, from = damage.fromType.ToString(), damage.failed })); }
        void UnitHit(UnitAvatar victim, DamageInstance damage)
        { UnitHits.Add(Json.Write(new { victim = victim.name, damage.damage, damage.damageResult, from = damage.fromType.ToString(), damage.failed })); }
        player.OnAttackUnit += UnitHit;
        controller.OnBeginAttackAnimation += Basic; controller.OnBeginDashAttackAnimation += Dash; controller.OnBeginSpecialAttackAnimation += Special;
        controller.OnSpecialAttackSwing += SpecialSwing;
        controller.OnBasicAttack += Hit; controller.OnDashAttack += Hit; controller.OnSpecialAttack += Hit;
        observing = true;
        attackBegin = Time.time;
        var nativeClass = controller.currentWeapon.GetType().Name;
        var nativeDashData = ReadFireData(controller.currentWeapon, nameof(WeaponSimple.GetDashAttack))?.name;
        var preparedBuffs = player.Buffs.Select(b => b.ID).OrderBy(id => id).ToArray();
        var preparedTargetBuffs = dummy ? dummy!.Buffs.Select(b => b.ID).OrderBy(id => id).ToArray() : Array.Empty<string>();
        var preparedTargetDebuffs = dummy ? dummy!.Debuffs.Where(b => b && !b.IsEndBuff).Select(b => b.ID).OrderBy(id => id).ToArray() : Array.Empty<string>();
        var aim = (Vector2)player.transform.position + Vector2.right * 3;
        player.ForceAimToPosition(aim);
        if (mode == "dash") player.CurrentDashModule.StartDash(aim);
        var type = controller.currentWeapon.weaponType;
        // Modern katana requests its native smash only while an ordinary swing
        // is active. An idle secondary press cannot exercise that variant.
        var modernKatana = controller.currentWeapon is WeaponSimple_Katana_New;
        if (mode == "special" && modernKatana)
        {
            player.AttackButtonDown(Vector2.right); yield return new WaitForSecondsRealtime(.12f); player.AttackButtonUp();
        }
        // Fast reload requires a partly spent magazine. Prepare it through a
        // real native shot rather than changing the weapon's ammo fields.
        if (mode == "special" && controller.currentWeapon is WeaponSimple_Crossbow { specialAttackType: WeaponSimple_Crossbow.ESpecialAttackType.FastReload })
        {
            player.AttackButtonDown(Vector2.right);
            var deadline = Time.realtimeSinceStartup + 2;
            while (Projectiles.Count == 0 && Time.realtimeSinceStartup < deadline) yield return null;
            player.AttackButtonUp(); yield return new WaitForSecondsRealtime(.1f);
        }
        var secondaryBefore = SecondaryState(controller);
        if (mode == "special") player.SubAttackButtonDown(Vector2.right); else player.AttackButtonDown(Vector2.right);
        if (mode == "special" && controller.currentWeapon is WeaponSimple_GreatSword chargingSword)
        {
            var deadline = Time.realtimeSinceStartup + 10;
            while (!(bool)AccessTools.Field(typeof(WeaponSimple_GreatSword), "sweepRequest").GetValue(chargingSword) &&
                   !chargingSword.isTransformed && Time.realtimeSinceStartup < deadline) yield return null;
        }
        else if (mode == "special" && controller.currentWeapon is WeaponSimple_Crossbow { specialAttackType: WeaponSimple_Crossbow.ESpecialAttackType.Minigun })
        {
            var deadline = Time.realtimeSinceStartup + 5;
            while (Projectiles.Count == 0 && Time.realtimeSinceStartup < deadline) yield return null;
        }
        else if (controller.currentWeapon is WeaponSimple_Bow bow)
        {
            var deadline = Time.realtimeSinceStartup + 3;
            while (bow.pullTriggerRatio < 1 && Time.realtimeSinceStartup < deadline) yield return null;
        }
        else if (mode == "special" || type == EWeaponType.Golem) yield return new WaitForSecondsRealtime(mode == "special" ? 1.5f : 1.2f);
        else if (type == EWeaponType.Katana || type == EWeaponType.Crossbow)
        {
            // Legacy sheath-only movesets are polled in LateUpdate. Release
            // after a native attack starts instead of racing a one-frame press.
            var deadline = Time.realtimeSinceStartup + 1.5f;
            while ((type == EWeaponType.Crossbow ? Projectiles.Count == 0 : Events.Count == 0) && Time.realtimeSinceStartup < deadline) yield return null;
        }
        else yield return null;
        var guarded = player.isGuardEnabled;
        var bowRelease = controller.currentWeapon is WeaponSimple_Bow chargedBow
            ? new { ratio = chargedBow.pullTriggerRatio, phase = chargedBow.isPullingTriggerPhase } : null;
        var secondary = SecondaryState(controller);
        if (mode == "special" && controller.currentWeapon is WeaponSimple_Crossbow buffBow &&
            (buffBow.specialAttackType == WeaponSimple_Crossbow.ESpecialAttackType.IceBuff || buffBow.specialAttackType == WeaponSimple_Crossbow.ESpecialAttackType.AmmoCompression))
        {
            var count = Projectiles.Count; player.AttackButtonDown(Vector2.right);
            var deadline = Time.realtimeSinceStartup + 2;
            while (Projectiles.Count == count && Time.realtimeSinceStartup < deadline) yield return null;
            player.AttackButtonUp();
        }
        if (mode == "special" && (type == EWeaponType.SwordAndShield || type == EWeaponType.Katana && !modernKatana))
        {
            var count = Events.Count;
            player.AttackButtonDown(Vector2.right);
            var deadline = Time.realtimeSinceStartup + 1.5f;
            while (Events.Count == count && Time.realtimeSinceStartup < deadline) yield return null;
            player.AttackButtonUp();
        }
        if (mode == "special") player.SubAttackButtonUp(); else player.AttackButtonUp();
        if (mode == "special" && controller.currentWeapon is WeaponSimple_GreatSword { specialAttackToTransform: true, isTransformed: true })
        {
            yield return new WaitForSecondsRealtime(.2f);
            secondary = SecondaryState(controller);
            var count = Events.Count; player.AttackButtonDown(Vector2.right);
            var deadline = Time.realtimeSinceStartup + 1.5f;
            while (Events.Count == count && Time.realtimeSinceStartup < deadline) yield return null;
            player.AttackButtonUp();
        }
        yield return new WaitForSecondsRealtime(1.8f);
        var statusDeadline = Time.realtimeSinceStartup + 20;
        while (dummy && dummy!.Debuffs.Any(b => b && !b.IsEndBuff && b.Attacker == player) && Time.realtimeSinceStartup < statusDeadline) yield return null;
        var targetStatusComplete = !dummy || !dummy!.Debuffs.Any(b => b && !b.IsEndBuff && b.Attacker == player);
        object? mineLifecycle = null;
        var mines = TrialProjectiles.OfType<Bullet>().Where(b => b && UsesMine(b.gameObject)).ToArray();
        if (mines.Length > 0)
        {
            var activeMine = mines.FirstOrDefault(b => b.gameObject.activeInHierarchy);
            var mineRigid = activeMine ? activeMine!.GetComponent<TopdownRigidbody>() : null;
            var deadline = Time.realtimeSinceStartup + 4;
            while (activeMine && activeMine!.gameObject.activeInHierarchy && mineRigid && !mineRigid!.IsGrounded && Time.realtimeSinceStartup < deadline) yield return null;
            var landed = activeMine && mineRigid && mineRigid!.IsGrounded;
            var targetEligible = dummy && !dummy!.IsDead && !dummy.canBeTarget.IsFalse() && dummy.TopdownActor.YPos <= 5 &&
                CombatManager.ContainsAttackableFaction(player.GetHostileFactionLayers(EDamageFromType.None), dummy.faction);
            var targetMoved = false; var beforeHits = UnitHits.Count; var originalTarget = dummy ? dummy!.transform.position : Vector3.zero;
            if (landed && targetEligible)
            {
                // This is an isolated native dummy, moved by its network API.
                // The original mine's own proximity test triggers detonation.
                try
                {
                    dummy!.ReqSetPosition(activeMine!.transform.position, true); targetMoved = true;
                    yield return new WaitForSecondsRealtime(.8f);
                }
                finally { if (dummy) dummy!.ReqSetPosition(originalTarget, true); }
            }
            mineLifecycle = new { detected = mines.Length, mineInput, landed = (bool)landed, targetMoved, targetEligible = (bool)targetEligible,
                detonated = targetMoved && (!activeMine || !activeMine!.gameObject.activeInHierarchy),
                nativeHitsAfterMove = UnitHits.Count - beforeHits,
                reason = !targetEligible ? "native practice dummy is excluded by mine target criteria; actual enemy detonation remains a separate encounter check" : targetMoved ? "native target proximity after grounded deployment" : "mine already ended or did not reach grounded deployment within the bounded window" };
        }
        observing = false;
        controller.OnBeginAttackAnimation -= Basic; controller.OnBeginDashAttackAnimation -= Dash; controller.OnBeginSpecialAttackAnimation -= Special;
        controller.OnSpecialAttackSwing -= SpecialSwing;
        controller.OnBasicAttack -= Hit; controller.OnDashAttack -= Hit; controller.OnSpecialAttack -= Hit;
        player.OnMpUsedServerside -= Mp;
        player.OnAttackUnit -= UnitHit;
        done(new { events = Events.ToArray(), projectiles = Projectiles.ToArray(), hits = Hits.ToArray(), unitHits = UnitHits.ToArray(), states = States.OrderBy(s => s).ToArray(), frames, themedFrames,
            fireSeconds = FireTimes.ToArray(), frameMilliseconds = FrameTimes.Count == 0 ? 0 : FrameTimes.Average() * 1000,
            uiApplyMilliseconds = plugin.Theme == null ? 0 : plugin.Ui.LastApplyMilliseconds, guarded, bowRelease, targetStatusComplete, secondary, secondaryBefore, nativeClass, nativeDashData, preparedBuffs, preparedTargetBuffs, preparedTargetDebuffs, mineLifecycle,
            inputSequence = modernKatana && mode == "special" ? "basic .12s then secondary during native swing" : type == EWeaponType.Katana ? "primary held until native attack begins; secondary preparation recorded before primary" :
                controller.currentWeapon is WeaponSimple_Bow ? "native bow primary held until full charge, then released" :
                type == EWeaponType.GreatSword && mode == "special" ? "native secondary held until sweep ready or transformation; native primary follows transformation" :
                type == EWeaponType.Crossbow ? "native primary held until first volley; fast reload primes one shot, buff/compression secondary followed by native shot; minigun stopped after first volley" : "native primary/secondary",
            roles = new Dictionary<string, int>(RoleFrames), themedRoles = new Dictionary<string, int>(ThemedRoleFrames),
            canMove = player.CanMove, weapon = controller.currentWeapon?.entityId, radius, radiusAfter = player.TopdownRigidbody.MovementCollider.radius, mp = player.mp, preparedMaxMp = player.MaxMp, mpCosts,
            preparedMoney = controller.currentWeapon is WeaponSimple_GreatSword { moneyWhirlwind: true } ? Math.Max(priorMoney, 10000) : priorMoney,
            moneySpent = (controller.currentWeapon is WeaponSimple_GreatSword { moneyWhirlwind: true } ? Math.Max(priorMoney, 10000) : priorMoney) - player.Money });
        player.CancelCurrentAction(); player.CurrentDashModule.StopDash(); player.ReqSetPosition(start, true);
        player.NetworkmaxMp = priorMaxMp; player.Networkmp = Math.Min(player.mp, player.MaxMp);
        player.NetworkcurrentMoney = priorMoney;
        yield return new WaitForSecondsRealtime(.3f);
    }
    private static bool UsesMine(GameObject prefab) => prefab.GetComponentInChildren<BulletMoveModule_CrossbowMine>(true) || prefab.GetComponentInChildren<BulletMoveModule_CrossbowMineHoming>(true);

    private static NewWeaponFireData? ReadFireData(WeaponSimple weapon, string method)
    {
        var declared = method == nameof(WeaponSimple.GetDashAttack) ? weapon.dashAttacks : weapon.specialAttacks;
        if (declared.Length == 0 && weapon.GetType().GetMethod(method)?.DeclaringType == typeof(WeaponSimple)) return null;
        return method == nameof(WeaponSimple.GetDashAttack) ? weapon.GetDashAttack(0) : weapon.GetSpecialAttack(0);
    }

    internal static object SecondaryState(WeaponControllerSimple controller)
    {
        var katana = controller.currentWeapon as WeaponSimple_Katana;
        var sword = controller.currentWeapon as WeaponSimple_GreatSword;
        var crossbow = controller.currentWeapon as WeaponSimple_Crossbow;
        return new { nativeClass = controller.currentWeapon.GetType().Name,
            sheathAction = katana?.sheathActionType.ToString(), useQuickDraw = katana?.useQuickDraw ?? false,
            bladeSheathed = katana?.isBladeSheathed ?? false, sheathEnabled = katana?.sheathStateEnabled ?? false,
            animatorGuard = controller.animator.GetBool(AnimHashContainer.Instance.GuardHash),
            longCharge = sword?.longCharge ?? false, sweepReady = sword != null && (bool)AccessTools.Field(typeof(WeaponSimple_GreatSword), "sweepRequest").GetValue(sword),
            transforms = sword?.specialAttackToTransform ?? false, transformed = sword?.isTransformed ?? false,
            moneyWhirlwind = sword?.moneyWhirlwind ?? false, lightningChance = crossbow ? controller.unitAvatar.GetCustomStatUnsafe("LIGHTNINGCROSSBOW") : 0,
            crossbowSpecial = crossbow?.specialAttackType.ToString(), compressedAmmo = crossbow?.hasCompressedAmmo ?? false,
            iceBuff = crossbow ? controller.unitAvatar.GetCustomStatUnsafe("ICECROSSBOWBUFF") : 0,
            ammo = crossbow?.ammoInCurrentMagazine ?? -1, magazineCapacity = crossbow?.currentMagazineCapacity ?? -1 };
    }

    private static void Projectile(ProjectileBase projectile, string? initializedDamageId = null, float? initializedDamage = null, float? initializedRange = null)
    {
        if (!projectile) return;
        // Golem guns fire through native charms rather than the controller's
        // attack-animation callbacks. Record the actual projectile event.
        if (Ownership.Local!.GetComponent<WeaponControllerSimple>().currentWeapon?.weaponType == EWeaponType.Golem)
            Events.Add("golem-fire:" + RuntimeCatalog.Clean(projectile.name));
        TrialProjectiles.Add(projectile);
        FireTimes.Add(Time.time - attackBegin);
        var melee = projectile as MeleeCollision;
        var size = melee?.GetSize(0);
        Projectiles.Add(Json.Write(new { prefab = RuntimeCatalog.Clean(projectile.name), projectile.defaultDamageRatio,
            kind = projectile.GetType().Name, damageId = melee?.damageId ?? (projectile as Bullet)?.damageId ?? initializedDamageId, damage = melee?.damage ?? (projectile as Bullet)?.Damage ?? initializedDamage,
            meleeSize = size.HasValue ? new[] { size.Value.x, size.Value.y } : null,
            rangeBonus = melee?.rangeBonus ?? initializedRange, shape = melee?.fireShape.ToString(),
            colliders = projectile.GetComponentsInChildren<Collider2D>(true).Select(c => new { type = c.GetType().Name, c.isTrigger, offset = new[] { c.offset.x, c.offset.y },
                radius = (c as CircleCollider2D)?.radius, size = c is BoxCollider2D box ? new[] { box.size.x, box.size.y } : c is CapsuleCollider2D capsule ? new[] { capsule.size.x, capsule.size.y } : null,
                direction = c is CapsuleCollider2D cap ? cap.direction.ToString() : null,
                polygonPaths = c is PolygonCollider2D polygon ? Enumerable.Range(0, polygon.pathCount).Select(i => polygon.GetPath(i).Select(p => new[] { p.x, p.y }).ToArray()).ToArray() : null,
                edgePoints = c is EdgeCollider2D edge ? edge.points.Select(p => new[] { p.x, p.y }).ToArray() : null }).ToArray() }));
    }
    private static void MeleeObserved(MeleeCollision __instance)
    {
        if (!Ownership.IsLocal(__instance.owner)) return;
        if (observing) Projectile(__instance);
        NativeFireObserved?.Invoke(__instance);
    }
    private static void BulletObserved(Bullet __instance)
    {
        if (!Ownership.IsLocal(__instance.Owner)) return;
        if (observing) Projectile(__instance);
        NativeFireObserved?.Invoke(__instance);
    }
    private static void SpecialProjectileObserved(ProjectileBase __instance, object[] __args)
    {
        if (__instance is Bullet || __instance is MeleeCollision || __args.Length < 14 || __args[6] is not UnitAvatar owner || !Ownership.IsLocal(owner)) return;
        if (observing) Projectile(__instance, (string)__args[2], (float)__args[3], (float)__args[13]);
        NativeFireObserved?.Invoke(__instance);
    }
    internal static void ObserveUpdate(float seconds) { if (observing) FrameTimes.Add(seconds); }
    private static void FrameObserved(Animator2D_Basic __instance)
    {
        if (!observing || Ownership.Role(__instance) is not string role) return;
        RoleFrames.TryGetValue(role, out var count); RoleFrames[role] = count + 1;
        var renderer = __instance is Animator2D_SpriteRenderer single ? single.spriteRenderer : (__instance as Animator2D_MultipleSpriteRenderer)?.spriteRenderers.FirstOrDefault();
        if (renderer && IsThemed(renderer.sprite)) { ThemedRoleFrames.TryGetValue(role, out var themed); ThemedRoleFrames[role] = themed + 1; }
        if (role != "body") return;
        frames++; States.Add(__instance.CurrentStateName);
        if (renderer && IsThemed(renderer.sprite)) themedFrames++;
    }
    private static object MusicState()
    {
        var manager = SoundManager.Instance;
        var instance = manager.CurrentPlayingBGM;
        instance.getVolume(out var volume); instance.getPlaybackState(out var state);
        var key = "guid:" + manager.currentPlayingBGMEvent.Guid;
        var bound = plugin.Theme?.Pack.Manifest.Audio.ContainsKey(key) == true;
        return new { path = AudioAdapter.EventKey(manager.currentPlayingBGMEvent.Guid), key, bound, originalVolume = volume, state = state.ToString(), channels = plugin.Audio.ActiveReplacements };
    }
    private static Vector3? EncounterGround(RandomEnemyPhaseSpawner spawner, Vector3? nearEnemy = null)
    {
        var maps = Object.FindObjectsByType<UnityEngine.Tilemaps.Tilemap>(FindObjectsSortMode.None)
            .Where(m => m && m.gameObject.activeInHierarchy).ToArray();
        var center = (spawner.detectArea_lb + spawner.detectArea_rt) / 2;
        var safe = new List<Vector3>();
        // A procedural room's geometric center can be a pit. Use the installed
        // ground tiles and physical pit/obstacle colliders, without changing them.
        for (var x = Mathf.Ceil((spawner.detectArea_lb.x + 1.5f) * 2) / 2; x <= spawner.detectArea_rt.x - 1.5f; x += .5f)
        for (var y = Mathf.Ceil((spawner.detectArea_lb.y + 1.5f) * 2) / 2; y <= spawner.detectArea_rt.y - 1.5f; y += .5f)
        {
            var point = new Vector3(x, y, 0);
            if (nearEnemy.HasValue && (Vector2.Distance(point, nearEnemy.Value) < 1.25f || Vector2.Distance(point, nearEnemy.Value) > 3)) continue;
            if (Physics2D.OverlapCircle(point, .6f, CombatManager.PathfindingObstacleLayerMask | CombatManager.PitLayerMask)) continue;
            if (maps.Any(map =>
            {
                var tile = map.GetTile(map.WorldToCell(point));
                return tile && TileDatabase.FindGroundTile(tile)?.type == GroundTileEntity.Type.Ground;
            })) safe.Add(point);
        }
        return safe.OrderBy(p => Vector2.Distance(p, nearEnemy ?? (Vector3)center)).Select(p => (Vector3?)p).FirstOrDefault();
    }
    private static IEnumerator Encounter(string root)
    {
        phase = "live dungeon encounter";
        var player = Ownership.Local!;
        var floor = FloorGenerator.FindByGuid(player.currentFloorGuid);
        // Networked floor props are spawned as scene roots, not floor children.
        var spawners = Object.FindObjectsByType<RandomEnemyPhaseSpawner>(FindObjectsSortMode.None);
        Add("encounter discovery", new { floor = floor.name, spawners = spawners.Select(s => new { s.name, s.enableSpawn, phases = s.spawnPhases.phases.Count,
            center = new[] { ((s.detectArea_lb + s.detectArea_rt) / 2).x, ((s.detectArea_lb + s.detectArea_rt) / 2).y }, s.isSpawned, s.isCleared }).ToArray() });
        var candidates = spawners.Where(s => s.enableSpawn && s.spawnPhases.phases.Count > 0 && !s.isCleared)
            .OrderBy(s => Vector2.Distance((s.detectArea_lb + s.detectArea_rt) / 2, player.transform.position)).ToArray();
        RandomEnemyPhaseSpawner? spawner = null; UnitAvatar? selectedEnemy = null;
        foreach (var candidate in candidates)
        {
            var ground = EncounterGround(candidate);
            if (!ground.HasValue)
            {
                Add("encounter room attempt", new { candidate.name, available = false, reason = "No verified native solid standing point inside this room." });
                continue;
            }
            player.ReqSetPosition(ground.Value, true);
            yield return new WaitForSecondsRealtime(.5f);
            var stable = !player.IsDead && player.TopdownRigidbody.IsGrounded && !player.TopdownRigidbody.IsPitFalling &&
                Vector2.Distance(player.transform.position, ground.Value) < .75f;
            if (stable && !candidate.isSpawned) candidate.StartSpawn();
            var deadline = Time.realtimeSinceStartup + (stable ? 15 : 0);
            while (stable && !selectedEnemy && Time.realtimeSinceStartup < deadline)
            {
                selectedEnemy = CombatManager.Instance.AllCreatures.Where(u => u && u != player && !u.IsDead && !u.canBeTarget.IsFalse() &&
                    u.transform.position.x >= candidate.detectArea_lb.x && u.transform.position.x <= candidate.detectArea_rt.x &&
                    u.transform.position.y >= candidate.detectArea_lb.y && u.transform.position.y <= candidate.detectArea_rt.y &&
                    Vector3.Distance(u.transform.position, player.transform.position) < 30 &&
                    CombatManager.ContainsAttackableFaction(player.GetHostileFactionLayers(EDamageFromType.None), u.faction))
                    .OrderBy(u => Vector3.Distance(u.transform.position, player.transform.position)).FirstOrDefault();
                if (!selectedEnemy) yield return null;
            }
            Add("encounter room attempt", new { candidate.name, available = (bool)selectedEnemy, stable,
                requestedPosition = ground.Value.ToString(), actualPosition = player.transform.position.ToString(),
                candidate.isSpawned, reason = selectedEnemy ? "Actual native room enemy observed." :
                    !stable ? "Native standing point did not remain grounded; room not used." : "No eligible native enemy observed in the bounded spawn window." });
            if (selectedEnemy) { spawner = candidate; break; }
        }
        if (!spawner || !selectedEnemy) throw new InvalidOperationException("No room supplied a verified native ground position and actual enemy; see encounter room attempts.");
        var enemy = selectedEnemy!;
        var enemyName = enemy.name; var enemyFaction = enemy.faction;
        var enemyOriginal = enemy.TopdownActor?.bodyRenderer && !IsThemed(enemy.TopdownActor.bodyRenderer.sprite);
        var oldGameOver = player.dieIsGameOver; player.dieIsGameOver = NestedBoolean.False;
        var controller = player.GetComponent<WeaponControllerSimple>(); controller.EquipWeapon(false, 100);
        yield return new WaitForSecondsRealtime(.5f);
        var outgoing = 0; var incoming = 0; var hitStarted = player.hp; var priorHp = hitStarted;
        void Hit(CombatBehaviour _, DamageInstance damage, ProjectileBase __) { if (damage.damageResult > 0) outgoing++; }
        void Hp(float hp) { if (hp < priorHp) incoming++; priorHp = hp; }
        controller.OnBasicAttack += Hit; controller.OnDashAttack += Hit; controller.OnSpecialAttack += Hit; player.OnHpChangedServerside += Hp;
        var combatGround = EncounterGround(spawner!, enemy.transform.position)
            ?? throw new InvalidOperationException("No verified native standing point near the selected room enemy.");
        player.ReqSetPosition(combatGround, true);
        Add("encounter combat ground", new { position = combatGround.ToString(), enemyPosition = enemy.transform.position.ToString(),
            scope = "standing-position preparation only; native AI, damage, attacks and colliders unchanged" });
        yield return new WaitForSecondsRealtime(1.2f);
        // Observe native enemy damage before attacking so a quick kill does not
        // make an otherwise valid fixture miss its required incoming-hit case.
        var passiveStarted = Time.realtimeSinceStartup;
        while (incoming == 0 && !player.IsDead && enemy && !enemy.IsDead && Time.realtimeSinceStartup - passiveStarted < 12)
            yield return null;
        Add("native incoming observation", new { seconds = Time.realtimeSinceStartup - passiveStarted, incoming, nativeAi = true });
        if (player.IsDead) { player.Revive(player.MaxHp); yield return new WaitForSecondsRealtime(.3f); }
        var direction = enemy ? ((Vector2)enemy!.transform.position - (Vector2)player.transform.position).normalized : Vector2.right;
        InputSystem.QueueStateEvent(gamepad!, new GamepadState { rightStick = direction }.WithButton(GamepadButton.West));
        // A native enemy may kill the avatar during this observation. Death
        // and hit blinking can intentionally hide the sprite at the endpoint;
        // judge visible living frames, rather than treating a blank death frame
        // as a missing cosmetic. Every observed nonblank frame must be themed.
        var combatStarted = Time.realtimeSinceStartup;
        var livingBodyFrames = 0; var themedLivingBodyFrames = 0;
        while (Time.realtimeSinceStartup - combatStarted < 5)
        {
            var body = player.TopdownActor.bodyRenderer.sprite;
            if (!player.IsDead && body)
            {
                livingBodyFrames++;
                if (IsThemed(body)) themedLivingBodyFrames++;
            }
            yield return null;
        }
        InputSystem.QueueStateEvent(gamepad!, new GamepadState()); yield return new WaitForSecondsRealtime(.3f);
        controller.OnBasicAttack -= Hit; controller.OnDashAttack -= Hit; controller.OnSpecialAttack -= Hit; player.OnHpChangedServerside -= Hp;
        RuntimeProbe.Capture(plugin.Diagnostics.File("play-encounter.png"), Debug.Log);
        Add("live encounter", new { spawner!.isSpawned, inBattle = player.IsInBattle, outgoing, incoming, hpBefore = hitStarted, hpAfter = player.hp,
            localThemed = livingBodyFrames > 0 && livingBodyFrames == themedLivingBodyFrames, livingBodyFrames, themedLivingBodyFrames,
            deadAtCapture = player.IsDead, bodySpriteAtCapture = player.TopdownActor.bodyRenderer.sprite?.name,
            enemyName, enemyFaction, enemyOriginal, enemySurvived = enemy && !enemy.IsDead, music = MusicState() });
        // The diagnostic suppresses automatic game-over only while exercising the room.
        // Native damage, AI, death and revive handlers still run.
        if (player.IsDead) player.Revive(player.MaxHp);
        player.dieIsGameOver = oldGameOver;
        yield return MineEncounterProbe.Inspect(plugin, Add);
    }
    private static IEnumerator KeyboardControls()
    {
        phase = "keyboard selector";
        var keyboard = InputSystem.AddDevice<Keyboard>("Skins diagnostic keyboard");
        InputSystem.EnableDevice(keyboard);
        try
        {
            var key = ((BepInEx.Configuration.ConfigEntry<Key>)AccessTools.Field(typeof(Plugin), "shortcut").GetValue(plugin)).Value;
            InputSystem.QueueStateEvent(keyboard, new KeyboardState(key)); yield return new WaitForSecondsRealtime(.15f);
            InputSystem.QueueStateEvent(keyboard, new KeyboardState()); yield return new WaitForSecondsRealtime(.15f);
            var opened = plugin.SelectorOpen; var blocked = PlayerInputController.Instance.BlockAvatarInput;
            var marker = Object.FindObjectsByType<SkinSelectorMarker>(FindObjectsSortMode.None).First(m => m.GetComponent<UI_MessageBox_YesNo>()?.IsOpened == true);
            var label = marker.GetComponentsInChildren<UnityEngine.UI.Button>().First(b => b.GetComponent<UnityEngine.UI.LayoutElement>()).GetComponentInChildren<TMPro.TextMeshProUGUI>();
            var originalName = label.text;
            var longName = "아주 긴 스킨 이름 日本語 中文 " + new string('W', 100);
            label.text = longName; label.ForceMeshUpdate();
            Add("long skin name", new { textPreserved = label.text == longName, ellipsis = label.overflowMode == TMPro.TextOverflowModes.Ellipsis, clipped = label.isTextTruncated });
            label.text = originalName;
            InputSystem.QueueStateEvent(keyboard, new KeyboardState(Key.Escape)); yield return new WaitForSecondsRealtime(.15f);
            InputSystem.QueueStateEvent(keyboard, new KeyboardState()); yield return new WaitForSecondsRealtime(.3f);
            Add("keyboard selector", new { key = key.ToString(), opened, blocked, closedByEscape = !plugin.SelectorOpen });
        }
        finally
        {
            PlayerInputController.Instance.playerInput.SwitchCurrentControlScheme("Gamepad", gamepad!);
            InputSystem.RemoveDevice(keyboard);
        }
    }
    private static string Missing(IEnumerable<TMPro.TMP_Text> texts) => new string(texts.SelectMany(t =>
        System.Text.RegularExpressions.Regex.Replace(t.text, "<[^>]*>", "").Where(c => !char.IsWhiteSpace(c) && !char.IsControl(c) && !t.font.HasCharacter(c, true))).Distinct().ToArray());
    private static bool IsThemed(Sprite? sprite) => sprite && sprite!.name.StartsWith(plugin.Diagnostics.PackId + ":", StringComparison.Ordinal);
    private static IEnumerator Await(Func<bool> condition, float seconds, string label)
    {
        var until = Time.realtimeSinceStartup + seconds;
        while (!condition() && Time.realtimeSinceStartup < until) yield return null;
        if (!condition()) throw new TimeoutException(label);
    }
    private static IEnumerator Reclaim() { yield return null; yield return null; yield return Resources.UnloadUnusedAssets(); GC.Collect(); }
    private static object ResourcesSnapshot() => new
    {
        textures = Resources.FindObjectsOfTypeAll<Texture2D>().Count(t => t), sprites = Resources.FindObjectsOfTypeAll<Sprite>().Count(t => t),
        fonts = Resources.FindObjectsOfTypeAll<TMPro.TMP_FontAsset>().Count(t => t), materials = Resources.FindObjectsOfTypeAll<Material>().Count(t => t),
        managedBytes = GC.GetTotalMemory(false), bundles = AssetBundle.GetAllLoadedAssetBundles().Count()
    };
    private static void Log(string message, string stack, LogType type)
    {
        if (type == LogType.Exception) Exceptions.Add(phase + ": " + message + "\n" + stack);
    }
    private static void Add(string name, object value) { Results.Add(new { name, value }); Debug.Log("SKINS_PLAY_RESULT " + name + " " + Json.Write(value)); Write(Array.Empty<string>(), null); }
    private static void Write(string[] hooks, object? restored) => File.WriteAllText(output, Json.Write(new { packId = plugin.Diagnostics.PackId, runId = plugin.Diagnostics.RunId, pluginSha256 = plugin.Diagnostics.PluginSha256, results = Results, exceptions = Exceptions, hooks, restored }));
}
internal sealed class PlayProbeFrameSampler : MonoBehaviour
{
    private void Update() => PlayProbe.ObserveUpdate(Time.unscaledDeltaTime);
}
