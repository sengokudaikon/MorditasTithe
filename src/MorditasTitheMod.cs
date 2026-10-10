using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using HarmonyLib;
using Il2Cpp;
using Il2CppInterop.Runtime;
using MelonLoader;
using MorditasTithe;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

[assembly: MelonInfo(typeof(MorditasTitheMod), "Morditas' Tithe", "0.4.0", "sengokudaikon")]
[assembly: MelonGame("Eleventh Hour Games", "Last Epoch")]

namespace MorditasTithe;

// Counts the player's kills by kind and times each zone, with the echo's timeline, corruption and time to the objective in
// the Monolith, and names every boss slain. Endgame boss fights (a timeline's boss, the Shade, a pinnacle, a boss woven
// echo) are their own kind of zone, kept apart from the echoes. A panel shows the zone under way, the last echo and the
// session; the debrief (F10) adds up the session or everything recorded. Each zone left is appended to
// UserData/MorditasTithe/history.jsonl.
public sealed class MorditasTitheMod : MelonMod {
    // The woven echoes whose point is a boss: the endgame bosses, Uber Aberroth, the Shade's pinnacle, the Harbingers, the
    // boss rush and Morditas himself.
    private static readonly HashSet<WovenEchoType> _bossEchoes = [
        WovenEchoType.EndgameMajasa, WovenEchoType.BossRush, WovenEchoType.EndgameOrian, WovenEchoType.EndgameDraalQueen,
        WovenEchoType.ShadeGauntlet, WovenEchoType.UberAterroth, WovenEchoType.HarbingerEcho, WovenEchoType.HarbingerBossRush,
        WovenEchoType.SuperShadePinnacle, WovenEchoType.Morditas, WovenEchoType.UberMorditas, WovenEchoType.EndgameYrun,
    ];

    private static MelonPreferences_Category _category;
    private static MelonPreferences_Entry<bool> _shown;
    private static MelonPreferences_Entry<bool> _pinned;
    private static MelonPreferences_Entry<bool> _recordHistory;
    private static MelonPreferences_Entry<float> _left;
    private static MelonPreferences_Entry<float> _top;
    private static MelonPreferences_Entry<float> _scale;
    private static MelonPreferences_Entry<float> _opacity;

    private static ZoneRun _run;
    private static ZoneRun _lastEcho;

    private static int _sceneHandle;
    private static float _nextPoll;
    private static bool _panelKeyWasDown;
    private static bool _debriefKeyWasDown;
    private static bool _faulted;

    public override void OnInitializeMelon() {
        _category = MelonPreferences.CreateCategory("MorditasTithe");
        _shown = _category.CreateEntry("Shown", true, description: "Show the panel. F9 toggles in game.");
        _pinned = _category.CreateEntry("Pinned", false, description: "Pinned: the panel stays put and clicks pass through it.");
        _recordHistory = _category.CreateEntry("RecordHistory", true, description: "Append each zone to UserData/MorditasTithe/history.jsonl; the debrief adds it up.");
        _opacity = _category.CreateEntry("Opacity", 0.8f, description: "Panel background opacity, 0 to 1.");
        _left = _category.CreateEntry("Left", -1f, description: "Panel left edge as a share of the screen width; dragging sets it. -1 places it by default.");
        _top = _category.CreateEntry("Top", -1f, description: "Panel top edge as a share of the screen height; dragging sets it. -1 places it by default.");
        _scale = _category.CreateEntry("Scale", 1f, description: "Panel size; the corner grip sets it. 1 is the default at any resolution.");
        if (_recordHistory.Value) {
            History.Load();
        }

        LoggerInstance.Msg($"Morditas' Tithe panel {(_shown.Value ? "shown" : "hidden")}; F9 toggles it, F10 the debrief");
    }

    internal static bool Shown => _shown.Value;

    internal static bool Pinned {
        get => _pinned.Value;
        set {
            _pinned.Value = value;
            _category.SaveToFile(false);
        }
    }

    internal static float Opacity => Math.Clamp(_opacity.Value, 0f, 1f);

    internal static float Scale => Math.Clamp(_scale.Value, Panel.MinScale, Panel.MaxScale);

    // The saved position, or the default: on the left, a little above the middle of the screen.
    internal static Vector2 Position => new(_left.Value < 0f ? 0.015f : _left.Value, _top.Value < 0f ? 0.22f : _top.Value);

    internal static void SavePlacement(Vector2 position, float scale) {
        _left.Value = position.x;
        _top.Value = position.y;
        _scale.Value = scale;
        _category.SaveToFile(false);
    }

    internal static ZoneRun Run => _run;

    internal static ZoneRun LastEcho => _lastEcho;

    // In a game, with a character: the windows show only then.
    internal static bool InGame => !_faulted && _run != null && _run.HasPlayer;

    public override void OnUpdate() {
        const int VirtualKeyF9 = 0x78;
        const int VirtualKeyF10 = 0x79;
        if (Pressed(VirtualKeyF9, ref _panelKeyWasDown)) {
            _shown.Value = !_shown.Value;
        }

        if (Pressed(VirtualKeyF10, ref _debriefKeyWasDown)) {
            Debrief.Toggle();
        }

        if (_faulted) {
            Ui.NoClickAreas();
            return;
        }

        try {
            // Time runs while the game does: a paused game (time scale 0) does not count.
            if (_run != null && Time.timeScale > 0f) {
                _run.Seconds += Time.unscaledDeltaTime;
            }

            if (Time.unscaledTime >= _nextPoll) {
                _nextPoll = Time.unscaledTime + 0.25f;
                Poll();
            }

            Panel.Update();
            Debrief.Update();
        } catch (Exception exception) {
            Fault("stopped", exception);
        }
    }

    public override void OnGUI() {
        if (_faulted) {
            return;
        }

        try {
            Panel.Draw();
            Debrief.Draw();
        } catch (Exception exception) {
            Fault("stopped drawing", exception);
        }
    }

    public override void OnApplicationQuit() {
        Finish();
    }

    internal static void Fault(string what, Exception exception) {
        _faulted = true;
        Ui.NoClickAreas();
        MelonLogger.Error($"Morditas' Tithe {what}: {exception}");
    }

    private static bool Pressed(int virtualKey, ref bool wasDown) {
        bool down = (Ui.GetAsyncKeyState(virtualKey) & 0x8000) != 0;
        bool pressed = down && !wasDown;
        wasDown = down;
        return pressed;
    }

    // A zone is one loaded instance of the active scene: the handle changes on every load, even of the same scene. What
    // kind of zone it is comes from the game's own scene list, which the game loads after its first scenes (splash, login);
    // until then a zone counts as a hub and is classified once the list is there.
    private static void Poll() {
        Scene scene = SceneManager.GetActiveScene();
        if (scene.handle != _sceneHandle) {
            _sceneHandle = scene.handle;
            Finish();
            _run = new ZoneRun(scene.name);
        }

        if (!_run.Classified) {
            ClassifyZone(scene);
        }

        Actor player = PlayerFinder.getPlayerActor();
        if (player == null) {
            return;
        }

        if (!_run.HasPlayer) {
            _run.HasPlayer = true;
            _run.Character = PlayerFinder.getPlayerData()?.CharacterName;
        }

        if (!_run.InMonolith) {
            return;
        }

        if (!_run.LookedUpRun) {
            _run.LookedUpRun = true;
            MonolithRunsManager runs = player.monolithRunsManager;
            // A woven echo entered from outside a run (Morditas's) finds a run with no timeline: it has no corruption either.
            if (runs != null && runs.TryGetCurrentOrMostRecentlyRequestedRun(out MonolithRun monolithRun) && monolithRun != null
                && !string.IsNullOrEmpty(monolithRun.timeline?.displayName)) {
                _run.Timeline = monolithRun.timeline.displayName;
                _run.Corruption = monolithRun.web?.corruption;
                _run.Empowered = monolithRun.IsEmpowered;
            }
        }

        // The zone manager's instance outlives its scene for a moment: only this scene's own counts.
        MonolithZoneManager echo = MonolithZoneManager.instance;
        if (echo == null || echo.gameObject.scene.handle != _sceneHandle) {
            return;
        }

        // An echo already done when first seen is one gone back into (through its portal): not an echo run of its own.
        if (!_run.SawEcho) {
            _run.SawEcho = true;
            _run.Revisit = echo.objectiveComplete;
        }

        LookAtEcho(echo);
        if (_run.ObjectiveSeconds == null && !_run.Revisit && echo.objectiveComplete) {
            _run.ObjectiveSeconds = _run.Seconds;
        }
    }

    // An echo whose point is a boss is a boss fight: its zone manager says so for a timeline's boss and the Shade, and a
    // woven echo's own manager names its type. That manager comes up as the echo starts, so it is looked for over the
    // zone's first seconds; the log names what was found, for each echo.
    private static void LookAtEcho(MonolithZoneManager echo) {
        if (_run.LookedAtEcho) {
            return;
        }

        WovenEchoType woven = WovenEchoType.None;
        foreach (Object found in Object.FindObjectsByType(Il2CppType.Of<WovenEchoZoneManager>(), FindObjectsSortMode.None)) {
            WovenEchoZoneManager manager = found.TryCast<WovenEchoZoneManager>();
            if (manager != null && manager.gameObject.scene.handle == _sceneHandle) {
                woven = manager.wovenEchoType;
                break;
            }
        }

        bool timelineBoss = echo.isTimelineBossEncounter || echo.defaultObjectiveType == MonolithObjectiveType.Shade;
        if (woven == WovenEchoType.None && !timelineBoss && _run.Seconds < 8d) {
            return;
        }

        _run.LookedAtEcho = true;
        if (timelineBoss || _bossEchoes.Contains(woven)) {
            _run.Kind = ZoneKind.Boss;
        }

        string wovenName = woven == WovenEchoType.None ? string.Empty : $", woven echo {woven}";
        MelonLogger.Msg($"{_run.Kind} {_run.ZoneName} in {_run.Timeline ?? "no timeline"}, corruption {_run.Corruption?.ToString(CultureInfo.InvariantCulture) ?? "none"}, "
            + $"objective {echo.defaultObjectiveType}{(echo.isTimelineBossEncounter ? ", timeline boss" : string.Empty)}{wovenName}");
    }

    // SceneList.instance is the field itself: reading it does not trip the game's guard that SceneList's methods throw
    // from before it is loaded. A scene the list cannot describe stays a hub rather than stopping the mod.
    private static void ClassifyZone(Scene scene) {
        if (SceneList.instance == null) {
            return;
        }

        _run.Classified = true;
        try {
            _run.Kind = KindOf(scene, out string zoneName, out bool inMonolith);
            _run.ZoneName = zoneName;
            _run.InMonolith = inMonolith;
        } catch (Exception exception) {
            MelonLogger.Warning($"Zone {scene.name} could not be classified, so it counts as a hub: {exception.Message}");
            return;
        }

        MelonLogger.Msg($"Zone {scene.name} ({_run.Kind}{(_run.ZoneName != null ? $", {_run.ZoneName}" : string.Empty)})");
    }

    // Towns, the End of Time, Traveler's Rest and the like are hubs: nothing is timed or counted there. A pinnacle is a
    // boss fight from the start; an echo may turn out to be one once its zone manager is up.
    private static ZoneKind KindOf(Scene scene, out string zoneName, out bool inMonolith) {
        SceneDetails details = SceneList.GetSceneDetails(scene);
        zoneName = details == null ? null : string.IsNullOrEmpty(details.LocalizedName) ? details.DisplayName : details.LocalizedName;
        inMonolith = false;
        if (details == null) {
            return ZoneKind.Hub;
        }

        string name = scene.name;
        if (details.sceneType is SceneType.Town or SceneType.NonGameplay || SceneList.IsNonCombatZone(name) || SceneList.IsMonolithRestZone(name)) {
            return ZoneKind.Hub;
        }

        bool echo = SceneList.IsMonolithEcho(scene);
        inMonolith = echo || SceneList.isMonolithZone(name);
        if (details.sceneType == SceneType.Pinnacle) {
            return ZoneKind.Boss;
        }

        return echo ? ZoneKind.Echo : ZoneKind.Combat;
    }

    internal static void OnDying(Dying dying) {
        if (_faulted || _run == null || _run.Kind == ZoneKind.Hub || dying == null || dying.unsummoned) {
            return;
        }

        try {
            Actor dead = dying.getActor();
            if (dead == null) {
                return;
            }

            Actor player = PlayerFinder.getPlayerActor();
            if (player != null && dead.Pointer == player.Pointer) {
                _run.Deaths++;
                return;
            }

            if (!IsKill(dead)) {
                return;
            }

            KillKind kind = Classify(dead);
            _run.Kills[(int)kind]++;
            // A boss is named once a zone, however many times it goes down (some rise again for another phase).
            if (kind == KillKind.Boss && _run.BossActors.Add(dead.Pointer)) {
                var boss = new BossKill(BossName(dead), _run.Seconds);
                _run.Bosses.Add(boss);
                MelonLogger.Msg($"{boss.Name} slain at {Ui.Clock(boss.Seconds)} in {_run.ZoneName ?? _run.Scene}");
            }
        } catch (Exception exception) {
            Fault("stopped counting", exception);
        }
    }

    // Anything hostile to the player counts, including what enemies summon, as the game's own echo progress does; enemy
    // totems do not, nor (checked by the caller) anything unsummoned: a minion or totem that expired.
    private static bool IsKill(Actor dead) {
        Actor player = PlayerFinder.getPlayerActor();
        if (player == null || dead.Pointer == player.Pointer) {
            return false;
        }

        Alignment alignment = player.GetAlignment();
        Alignment deadAlignment = dead.GetAlignment();
        return alignment != null && deadAlignment != null && alignment.IsFoe(deadAlignment) && !dead.IsSummonedTotem();
    }

    // One kind per kill, the most notable first: a champion is also rare, a boss may be rare too. Boss and miniboss come
    // from the actor's data (its boss type), so a boss is one the game itself makes a boss.
    private static KillKind Classify(Actor actor) {
        if (actor.IsLootLizard) {
            return KillKind.Lizard;
        }

        if (actor.isBoss()) {
            return KillKind.Boss;
        }

        if (actor.isMiniboss()) {
            return KillKind.Miniboss;
        }

        if (actor.IsChampion) {
            return KillKind.Champion;
        }

        return actor.rarity switch {
            Actor.Rarity.Rare => KillKind.Rare,
            Actor.Rarity.Magic => KillKind.Magic,
            _ => KillKind.Normal,
        };
    }

    // The name the game shows for it, in the game's language.
    private static string BossName(Actor actor) {
        ActorData data = actor.data;
        string name = data?.GetLocalizedActorName();
        if (string.IsNullOrWhiteSpace(name)) {
            name = data?.actorName;
        }

        if (string.IsNullOrWhiteSpace(name)) {
            name = actor.name.Replace("(Clone)", string.Empty, StringComparison.Ordinal);
        }

        return name.Trim();
    }

    private static void Finish() {
        ZoneRun run = _run;
        _run = null;
        if (run == null || !run.HasPlayer || run.Kind == ZoneKind.Hub) {
            return;
        }

        if (run.Revisit) {
            MelonLogger.Msg($"{run.Kind} {run.Scene} gone back into for {Ui.Clock(run.Seconds)}, not recorded: its objective was already done");
            return;
        }

        string objective = !run.InMonolith ? string.Empty
            : run.ObjectiveSeconds is { } done ? $", objective at {Ui.Clock(done)}" : ", objective not done";
        string bosses = run.Bosses.Count > 0 ? $", slew {string.Join(", ", run.Bosses.Select(boss => boss.Name))}" : string.Empty;
        MelonLogger.Msg($"{run.Kind} {run.Scene} left after {Ui.Clock(run.Seconds)}: {run.TotalKills} kills{bosses}{objective}");
        if (run.IsEcho) {
            _lastEcho = run;
        }

        History.Add(run, _recordHistory.Value);
    }
}

// Dying.onEnter runs as any actor starts to die. It is virtual, so the game reaches it through the vtable and the detour
// sees every call.
[HarmonyPatch(typeof(Dying), nameof(Dying.onEnter))]
internal static class DyingOnEnterPatch {
    private static void Postfix(Dying __instance) {
        MorditasTitheMod.OnDying(__instance);
    }
}

internal enum ZoneKind {
    Hub,
    Combat,
    Echo,
    Boss,
}

internal enum KillKind {
    Normal,
    Magic,
    Rare,
    Champion,
    Miniboss,
    Boss,
    Lizard,
}

internal sealed class ZoneRun {
    public ZoneRun(string scene) {
        Scene = scene;
    }

    public string Scene { get; }
    public ZoneKind Kind { get; set; } = ZoneKind.Hub;
    public bool Classified { get; set; }
    public bool InMonolith { get; set; }
    public bool IsEcho => Kind == ZoneKind.Echo;
    public bool LookedUpRun { get; set; }
    public bool LookedAtEcho { get; set; }
    public bool SawEcho { get; set; }
    public bool Revisit { get; set; }
    public string ZoneName { get; set; }
    public double Seconds { get; set; }
    public bool HasPlayer { get; set; }
    public string Character { get; set; }
    public string Timeline { get; set; }
    public int? Corruption { get; set; }
    public bool Empowered { get; set; }
    public double? ObjectiveSeconds { get; set; }
    public int[] Kills { get; } = new int[Enum.GetValues<KillKind>().Length];
    public int Deaths { get; set; }
    public List<BossKill> Bosses { get; } = [];
    public HashSet<IntPtr> BossActors { get; } = [];
    public int TotalKills => Kills.Sum();
}
