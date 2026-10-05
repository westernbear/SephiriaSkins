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
    private static readonly List<string> Projectiles = new();
    private static readonly List<string> Hits = new();
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
        plugin = instance; output = Path.Combine(root, "Export", "play-results.json");
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
            object? value = null; bool next;
            try { next = pending.Peek().MoveNext(); if (next) value = pending.Peek().Current; }
            catch (Exception e) { Exceptions.Add(phase + ": " + e); break; }
            if (!next) { (pending.Pop() as IDisposable)?.Dispose(); continue; }
            if (value is IEnumerator nested) pending.Push(nested);
            else yield return value;
        }
        observing = false;
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
            profile = SaveManager.Binded, profileFileAbsent = !SaveData.Exists(Profile) && !SaveData.Exists(Profile + "TMP") };
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
        var entry = PackDiscovery.Scan(Path.Combine(root, "Skins"), plugin.Catalog).Single(p => p.Pack?.Manifest.Id == "fan.hachiware" && p.Error == null);
        yield return apply(entry);
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
        if (Environment.GetCommandLineArgs().Contains("--skins-playtest-controls"))
        {
            yield return KeyboardControls();
            yield break;
        }
        var paths = new RuntimeCatalog.PathIndex();
        var graphics = Object.FindObjectsByType<UnityEngine.UI.Graphic>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        Add("path equivalence", new { checkedPaths = graphics.Length, mismatches = graphics.Count(g => paths.UiKey(g) != RuntimeCatalog.UiKey(g)) });
        var weapons = WeaponDatabase.GetDefaultWeapons().Where(w => w && w.enabled && w.activeState == WeaponEntity.EActiveState.Active && w.mainWeaponPrefab && w.mainWeaponPrefab.GetComponent<WeaponSimple>())
            .GroupBy(w => w.mainWeaponPrefab.GetComponent<WeaponSimple>().weaponType)
            .Select(g => g.OrderByDescending(w => w.isDefaultWeapon).ThenBy(w => w.id).First()).OrderBy(w => w.id).ToArray();
        Add("weapon coverage", new { defaults = weapons.Select(w => new { w.id, w.name, type = w.mainWeaponPrefab.GetComponent<WeaponSimple>().weaponType.ToString() }).ToArray(),
            otherTypes = WeaponDatabase.GetAll().Where(w => w.mainWeaponPrefab && w.mainWeaponPrefab.GetComponent<WeaponSimple>() && !weapons.Any(p => p.mainWeaponPrefab.GetComponent<WeaponSimple>().weaponType == w.mainWeaponPrefab.GetComponent<WeaponSimple>().weaponType)).Select(w => new { w.id, w.isDefaultWeapon, type = w.mainWeaponPrefab.GetComponent<WeaponSimple>().weaponType.ToString() }).ToArray() });
        foreach (var weapon in weapons)
        {
            if (Environment.GetCommandLineArgs().Contains("--skins-playtest-encounter") && weapon.id != 400) continue;
            foreach (var mode in new[] { "basic", "dash", "special" })
            {
                if (Environment.GetCommandLineArgs().Contains("--skins-playtest-encounter") && mode != "special") continue;
                object? original = null, themed = null;
                restore(); yield return Attack(weapon, mode, value => original = value);
                yield return apply(entry); yield return Attack(weapon, mode, value => themed = value);
                Add("combat " + weapon.id + " " + mode, new { weapon.id, type = weapon.mainWeaponPrefab.GetComponent<WeaponSimple>().weaponType.ToString(), mode, original, themed });
            }
        }
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
        RuntimeProbe.Capture(Path.Combine(root, "Export", "play-dungeon.png"), Debug.Log);
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
            if (Environment.GetCommandLineArgs().Contains("--skins-playtest-encounter")) yield return Encounter(root);
        }
        phase = "death and revive";
        player = Ownership.Local!;
        var gameOver = player.dieIsGameOver; player.dieIsGameOver = NestedBoolean.False;
        player.Die(0, null); yield return new WaitForSecondsRealtime(1);
        var died = player.IsDead;
        RuntimeProbe.Capture(Path.Combine(root, "Export", "play-death.png"), Debug.Log);
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
        RuntimeProbe.Capture(Path.Combine(root, "Export", "play-inventory.png"), Debug.Log);
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
            RuntimeProbe.Capture(Path.Combine(root, "Export", "play-selector-" + size.x + ".png"), Debug.Log);
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
        RuntimeProbe.Capture(Path.Combine(root, "Export", "play-final.png"), Debug.Log);
        phase = "session restart";
        var priorPlayer = Ownership.Local!.GetInstanceID();
        Mirror.NetworkManager.singleton.StopHost(); yield return new WaitForSecondsRealtime(4);
        session!.RestartRun();
        EOSLobbyManager.StartHostWhenReady();
        yield return Await(() => Ownership.Local && Ownership.Local!.GetInstanceID() != priorPlayer, 50, "session restart");
        yield return new WaitForSecondsRealtime(5);
        Add("session restart", new { newAvatar = Ownership.Local!.GetInstanceID() != priorPlayer, themed = IsThemed(Ownership.Local.TopdownActor.bodyRenderer.sprite), local = Ownership.Local.isLocalPlayer });
    }

    private static IEnumerator Attack(WeaponEntity weapon, string mode, Action<object> done)
    {
        phase = "attack " + weapon.id + " " + mode + " " + (plugin.Theme == null ? "original" : "theme"); Debug.Log("SKINS_PLAY_PHASE " + phase);
        var player = Ownership.Local!; var controller = player.GetComponent<WeaponControllerSimple>();
        player.CancelCurrentAction(); player.DespawnAllBullet(); controller.EquipWeapon(false, weapon.id);
        yield return new WaitForSecondsRealtime(.5f);
        player.Networkmp = player.MaxMp; player.CurrentDashModule.RestoreDashCount(99);
        var dummy = CombatManager.Instance.AllCreatures.Where(u => u && u.faction == "Dummy").OrderBy(u => Vector3.Distance(u.transform.position, player.transform.position)).FirstOrDefault();
        if (dummy) player.ReqSetPosition(dummy!.transform.position - Vector3.right * (mode == "dash" ? 2.5f : 1.2f), true);
        yield return new WaitForSecondsRealtime(.2f);
        var start = player.transform.position; var radius = player.TopdownRigidbody.MovementCollider.radius;
        var mpCosts = new List<int>();
        void Mp(int amount) => mpCosts.Add(amount);
        player.OnMpUsedServerside += Mp;
        UnityEngine.Random.InitState(105715);
        Events.Clear(); Projectiles.Clear(); Hits.Clear(); TrialProjectiles.Clear(); States.Clear(); FrameTimes.Clear(); FireTimes.Clear(); RoleFrames.Clear(); ThemedRoleFrames.Clear(); frames = themedFrames = 0;
        foreach (var gun in player.Inventory.charms.Values.OfType<Charm_Golem_Gun>()) gun.defaultAttackIntervalTimer.SetTimer(0);
        void Basic(int id) { Events.Add("basic:" + id); attackBegin = Time.time; }
        void Dash() { Events.Add("dash"); attackBegin = Time.time; }
        void Special(int id) { Events.Add("special:" + id); attackBegin = Time.time; }
        void SpecialSwing(int id) { if (controller.currentWeapon.weaponType == EWeaponType.Katana) Events.Add("special-swing:" + id); }
        void Hit(CombatBehaviour victim, DamageInstance damage, ProjectileBase projectile)
        { if (TrialProjectiles.Contains(projectile)) Hits.Add(Json.Write(new { victim = victim.name, damage.damage, damage.damageResult, from = damage.fromType.ToString(), damage.failed })); }
        controller.OnBeginAttackAnimation += Basic; controller.OnBeginDashAttackAnimation += Dash; controller.OnBeginSpecialAttackAnimation += Special;
        controller.OnSpecialAttackSwing += SpecialSwing;
        controller.OnBasicAttack += Hit; controller.OnDashAttack += Hit; controller.OnSpecialAttack += Hit;
        observing = true;
        attackBegin = Time.time;
        var aim = (Vector2)player.transform.position + Vector2.right * 3;
        player.ForceAimToPosition(aim);
        if (mode == "dash") player.CurrentDashModule.StartDash(aim);
        var type = controller.currentWeapon.weaponType;
        if (mode == "special") player.SubAttackButtonDown(Vector2.right); else player.AttackButtonDown(Vector2.right);
        if (mode == "special" || type == EWeaponType.Golem) yield return new WaitForSecondsRealtime(mode == "special" ? 1.5f : 1.2f);
        else yield return null;
        var guarded = player.isGuardEnabled;
        if (mode == "special" && (type == EWeaponType.SwordAndShield || type == EWeaponType.Katana))
        { player.AttackButtonDown(Vector2.right); yield return new WaitForSecondsRealtime(.12f); player.AttackButtonUp(); }
        if (mode == "special") player.SubAttackButtonUp(); else player.AttackButtonUp();
        yield return new WaitForSecondsRealtime(1.8f);
        observing = false;
        controller.OnBeginAttackAnimation -= Basic; controller.OnBeginDashAttackAnimation -= Dash; controller.OnBeginSpecialAttackAnimation -= Special;
        controller.OnSpecialAttackSwing -= SpecialSwing;
        controller.OnBasicAttack -= Hit; controller.OnDashAttack -= Hit; controller.OnSpecialAttack -= Hit;
        player.OnMpUsedServerside -= Mp;
        done(new { events = Events.ToArray(), projectiles = Projectiles.ToArray(), hits = Hits.ToArray(), states = States.OrderBy(s => s).ToArray(), frames, themedFrames,
            fireSeconds = FireTimes.ToArray(), frameMilliseconds = FrameTimes.Count == 0 ? 0 : FrameTimes.Average() * 1000,
            uiApplyMilliseconds = plugin.Theme == null ? 0 : plugin.Ui.LastApplyMilliseconds, guarded,
            roles = new Dictionary<string, int>(RoleFrames), themedRoles = new Dictionary<string, int>(ThemedRoleFrames),
            canMove = player.CanMove, weapon = controller.currentWeapon?.entityId, radius, radiusAfter = player.TopdownRigidbody.MovementCollider.radius, mp = player.mp, mpCosts });
        player.CancelCurrentAction(); player.CurrentDashModule.StopDash(); player.ReqSetPosition(start, true);
        yield return new WaitForSecondsRealtime(.3f);
    }

    private static void Projectile(ProjectileBase projectile)
    {
        if (!projectile) return;
        TrialProjectiles.Add(projectile);
        FireTimes.Add(Time.time - attackBegin);
        var melee = projectile as MeleeCollision;
        var size = melee?.GetSize(0);
        Projectiles.Add(Json.Write(new { prefab = RuntimeCatalog.Clean(projectile.name), projectile.defaultDamageRatio,
            kind = projectile.GetType().Name, damage = melee?.damage ?? (projectile as Bullet)?.Damage,
            meleeSize = size.HasValue ? new[] { size.Value.x, size.Value.y } : null,
            rangeBonus = melee?.rangeBonus, shape = melee?.fireShape.ToString(),
            colliders = projectile.GetComponentsInChildren<Collider2D>(true).Select(c => new { type = c.GetType().Name, c.isTrigger, offset = new[] { c.offset.x, c.offset.y },
                radius = (c as CircleCollider2D)?.radius, size = c is BoxCollider2D box ? new[] { box.size.x, box.size.y } : null }).ToArray() }));
    }
    private static void MeleeObserved(MeleeCollision __instance) { if (observing && Ownership.IsLocal(__instance.owner)) Projectile(__instance); }
    private static void BulletObserved(Bullet __instance) { if (observing && Ownership.IsLocal(__instance.Owner)) Projectile(__instance); }
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
    private static IEnumerator Encounter(string root)
    {
        phase = "live dungeon encounter";
        var player = Ownership.Local!;
        var floor = FloorGenerator.FindByGuid(player.currentFloorGuid);
        // Networked floor props are spawned as scene roots, not floor children.
        var spawners = Object.FindObjectsByType<RandomEnemyPhaseSpawner>(FindObjectsSortMode.None);
        Add("encounter discovery", new { floor = floor.name, spawners = spawners.Select(s => new { s.name, s.enableSpawn, phases = s.spawnPhases.phases.Count,
            center = new[] { ((s.detectArea_lb + s.detectArea_rt) / 2).x, ((s.detectArea_lb + s.detectArea_rt) / 2).y }, s.isSpawned, s.isCleared }).ToArray() });
        var spawner = spawners.Where(s => s.enableSpawn && s.spawnPhases.phases.Count > 0 && !s.isCleared)
            .OrderBy(s => Vector2.Distance((s.detectArea_lb + s.detectArea_rt) / 2, player.transform.position)).First();
        var center = (spawner.detectArea_lb + spawner.detectArea_rt) / 2;
        player.ReqSetPosition(center, true);
        if (!spawner.isSpawned) spawner.StartSpawn();
        yield return Await(() => CombatManager.Instance.AllCreatures.Any(u => u && u != player && !u.IsDead &&
            Vector3.Distance(u.transform.position, player.transform.position) < 30 && CombatManager.ContainsAttackableFaction(player.GetHostileFactionLayers(EDamageFromType.None), u.faction)), 20, "room enemies");
        var enemy = CombatManager.Instance.AllCreatures.First(u => u && u != player && !u.IsDead && Vector3.Distance(u.transform.position, player.transform.position) < 30 &&
            CombatManager.ContainsAttackableFaction(player.GetHostileFactionLayers(EDamageFromType.None), u.faction));
        var enemyName = enemy.name; var enemyFaction = enemy.faction;
        var enemyOriginal = enemy.TopdownActor?.bodyRenderer && !IsThemed(enemy.TopdownActor.bodyRenderer.sprite);
        var oldGameOver = player.dieIsGameOver; player.dieIsGameOver = NestedBoolean.False;
        var controller = player.GetComponent<WeaponControllerSimple>(); controller.EquipWeapon(false, 100);
        yield return new WaitForSecondsRealtime(.5f);
        var outgoing = 0; var incoming = 0; var hitStarted = player.hp; var priorHp = hitStarted;
        void Hit(CombatBehaviour _, DamageInstance damage, ProjectileBase __) { if (damage.damageResult > 0) outgoing++; }
        void Hp(float hp) { if (hp < priorHp) incoming++; priorHp = hp; }
        controller.OnBasicAttack += Hit; controller.OnDashAttack += Hit; controller.OnSpecialAttack += Hit; player.OnHpChangedServerside += Hp;
        player.ReqSetPosition(enemy.transform.position - Vector3.right * 1.4f, true);
        yield return new WaitForSecondsRealtime(1.2f);
        var direction = ((Vector2)enemy.transform.position - (Vector2)player.transform.position).normalized;
        InputSystem.QueueStateEvent(gamepad!, new GamepadState { rightStick = direction }.WithButton(GamepadButton.West));
        yield return new WaitForSecondsRealtime(5);
        InputSystem.QueueStateEvent(gamepad!, new GamepadState()); yield return new WaitForSecondsRealtime(.3f);
        controller.OnBasicAttack -= Hit; controller.OnDashAttack -= Hit; controller.OnSpecialAttack -= Hit; player.OnHpChangedServerside -= Hp;
        RuntimeProbe.Capture(Path.Combine(root, "Export", "play-encounter.png"), Debug.Log);
        Add("live encounter", new { spawner.isSpawned, inBattle = player.IsInBattle, outgoing, incoming, hpBefore = hitStarted, hpAfter = player.hp,
            localThemed = IsThemed(player.TopdownActor.bodyRenderer.sprite), enemyName, enemyFaction, enemyOriginal, enemySurvived = enemy && !enemy.IsDead, music = MusicState() });
        // The diagnostic suppresses automatic game-over only while exercising the room.
        // Native damage, AI, death and revive handlers still run.
        if (player.IsDead) player.Revive(player.MaxHp);
        player.dieIsGameOver = oldGameOver;
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
    private static bool IsThemed(Sprite? sprite) => sprite && sprite!.name.StartsWith("fan.hachiware:", StringComparison.Ordinal);
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
    private static void Write(string[] hooks, object? restored) => File.WriteAllText(output, Json.Write(new { results = Results, exceptions = Exceptions, hooks, restored }));
}
internal sealed class PlayProbeFrameSampler : MonoBehaviour
{
    private void Update() => PlayProbe.ObserveUpdate(Time.unscaledDeltaTime);
}
