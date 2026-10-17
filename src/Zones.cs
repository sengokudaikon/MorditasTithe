using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Il2Cpp;
using Il2CppInterop.Runtime;
using MelonLoader;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace MorditasTithe;

// The zone under way and the last echo. A zone is one loaded instance of the active scene: the handle changes on every
// load, even of the same scene. What kind of zone it is comes from the game's own scene list, which the game loads after
// its first scenes (splash, login); until then a zone counts as a hub and is classified once the list is there.
internal static class Zones {
    // The woven echoes whose point is a boss: the endgame bosses, Uber Aberroth, the Shade's pinnacle, the Harbingers, the
    // boss rush and Morditas himself.
    private static readonly HashSet<WovenEchoType> _bossEchoes = [
        WovenEchoType.EndgameMajasa, WovenEchoType.BossRush, WovenEchoType.EndgameOrian, WovenEchoType.EndgameDraalQueen,
        WovenEchoType.ShadeGauntlet, WovenEchoType.UberAterroth, WovenEchoType.HarbingerEcho, WovenEchoType.HarbingerBossRush,
        WovenEchoType.SuperShadePinnacle, WovenEchoType.Morditas, WovenEchoType.UberMorditas, WovenEchoType.EndgameYrun,
    ];

    private static int _sceneHandle;
    private static float _nextPoll;

    internal static ZoneRun Run { get; private set; }

    internal static ZoneRun LastEcho { get; private set; }

    internal static void Update() {
        // Time runs while the game does: a paused game (time scale 0) does not count.
        if (Run != null && Time.timeScale > 0f) {
            Run.Seconds += Time.unscaledDeltaTime;
        }

        if (Time.unscaledTime >= _nextPoll) {
            _nextPoll = Time.unscaledTime + 0.25f;
            Poll();
        }
    }

    private static void Poll() {
        Scene scene = SceneManager.GetActiveScene();
        if (scene.handle != _sceneHandle) {
            _sceneHandle = scene.handle;
            Finish();
            Run = new ZoneRun(scene.name);
        }

        ZoneRun run = Run;
        if (!run.Classified) {
            Classify(run, scene);
        }

        Actor player = PlayerFinder.getPlayerActor();
        if (player == null) {
            return;
        }

        if (!run.HasPlayer) {
            run.HasPlayer = true;
            run.Character = PlayerFinder.getPlayerData()?.CharacterName;
        }

        if (!run.InMonolith) {
            return;
        }

        if (!run.LookedUpRun) {
            run.LookedUpRun = true;
            MonolithRunsManager runs = player.monolithRunsManager;
            // A woven echo entered from outside a run (Morditas's) finds a run with no timeline: it has no corruption either.
            if (runs != null && runs.TryGetCurrentOrMostRecentlyRequestedRun(out MonolithRun monolithRun) && monolithRun != null
                && !string.IsNullOrEmpty(monolithRun.timeline?.displayName)) {
                run.Timeline = monolithRun.timeline.displayName;
                run.Corruption = monolithRun.web?.corruption;
                run.Empowered = monolithRun.IsEmpowered;
            }
        }

        // The zone manager's instance outlives its scene for a moment: only this scene's own counts.
        MonolithZoneManager echo = MonolithZoneManager.instance;
        if (echo == null || echo.gameObject.scene.handle != _sceneHandle) {
            return;
        }

        // An echo already done when first seen is one gone back into (through its portal): not an echo run of its own.
        if (!run.SawEcho) {
            run.SawEcho = true;
            run.Revisit = echo.objectiveComplete;
        }

        LookAtEcho(run, echo);
        if (run.ObjectiveSeconds == null && !run.Revisit && echo.objectiveComplete) {
            run.ObjectiveSeconds = run.Seconds;
        }
    }

    // An echo whose point is a boss is a boss fight: its zone manager says so for a timeline's boss and the Shade, and a
    // woven echo's own manager names its type. That manager comes up as the echo starts, so it is looked for over the
    // zone's first seconds; the log names what was found, for each echo.
    private static void LookAtEcho(ZoneRun run, MonolithZoneManager echo) {
        if (run.LookedAtEcho) {
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
        if (woven == WovenEchoType.None && !timelineBoss && run.Seconds < 8d) {
            return;
        }

        run.LookedAtEcho = true;
        if (timelineBoss || _bossEchoes.Contains(woven)) {
            run.Kind = ZoneKind.Boss;
        }

        string wovenName = woven == WovenEchoType.None ? string.Empty : $", woven echo {woven}";
        MelonLogger.Msg($"{run.Kind} {run.ZoneName} in {run.Timeline ?? "no timeline"}, corruption {run.Corruption?.ToString(CultureInfo.InvariantCulture) ?? "none"}, "
            + $"objective {echo.defaultObjectiveType}{(echo.isTimelineBossEncounter ? ", timeline boss" : string.Empty)}{wovenName}");
    }

    // SceneList.instance is the field itself: reading it does not trip the game's guard that SceneList's methods throw
    // from before it is loaded. A scene the list cannot describe stays a hub rather than stopping the mod.
    private static void Classify(ZoneRun run, Scene scene) {
        if (SceneList.instance == null) {
            return;
        }

        run.Classified = true;
        try {
            run.Kind = KindOf(scene, out string zoneName, out bool inMonolith);
            run.ZoneName = zoneName;
            run.InMonolith = inMonolith;
        } catch (Exception exception) {
            MelonLogger.Warning($"Zone {scene.name} could not be classified, so it counts as a hub: {exception.Message}");
            return;
        }

        MelonLogger.Msg($"Zone {scene.name} ({run.Kind}{(run.ZoneName != null ? $", {run.ZoneName}" : string.Empty)})");
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

    // Ends the zone under way: logs it, keeps it as the last echo if it was one, and records it.
    internal static void Finish() {
        ZoneRun run = Run;
        Run = null;
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
        MelonLogger.Msg($"{run.Kind} {run.Scene} left after {Ui.Clock(run.Seconds)}: {run.Kills.Total} kills{bosses}{objective}");
        if (run.IsEcho) {
            LastEcho = run;
        }

        History.Add(run, Settings.RecordHistory);
    }
}

internal enum ZoneKind {
    Hub,
    Combat,
    Echo,
    Boss,
}

internal sealed class ZoneRun(string scene) {
    public string Scene { get; } = scene;
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
    public KillCounts Kills { get; } = new();
    public int Deaths { get; set; }
    public List<BossKill> Bosses { get; } = [];
    public HashSet<IntPtr> BossActors { get; } = [];
}
