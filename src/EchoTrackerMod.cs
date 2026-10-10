using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using EchoTracker;
using Il2Cpp;
using Il2CppInterop.Runtime;
using MelonLoader;
using MelonLoader.Utils;
using UnityEngine;
using UnityEngine.SceneManagement;

[assembly: MelonInfo(typeof(EchoTrackerMod), "Echo Tracker", "0.1.0", "sengokudaikon")]
[assembly: MelonGame("Eleventh Hour Games", "Last Epoch")]

namespace EchoTracker;

// Counts the player's kills by kind and times each zone, with the echo's timeline, corruption and time to the objective in
// the Monolith. A panel shows the zone under way, the last echo and the session; each echo left is appended to
// UserData/EchoTracker/history.jsonl. Kills come from the game's own death event (ActorManager.actorDiedEvent), which fires
// once for every actor that dies, whoever killed it.
public sealed class EchoTrackerMod : MelonMod {
    private static MelonPreferences_Entry<bool> _shown;
    private static MelonPreferences_Entry<bool> _recordHistory;
    private static MelonPreferences_Entry<float> _left;
    private static MelonPreferences_Entry<float> _top;
    private static MelonPreferences_Entry<float> _width;
    private static MelonPreferences_Entry<int> _fontSize;
    private static MelonPreferences_Entry<float> _opacity;

    private static ZoneRun _run;
    private static ZoneRun _lastEcho;
    private static int _sessionEchoes;
    private static double _sessionSeconds;
    private static int _sessionKills;

    private static IntPtr _subscribedManager;
    private static Il2CppSystem.Action<Actor, bool> _onActorDied;
    private static int _sceneHandle;
    private static float _nextPoll;
    private static bool _toggleWasDown;
    private static bool _faulted;

    private static GUIStyle _style;
    private static string _panelText = string.Empty;
    private static int _panelLines;
    private static float _nextRedraw;

    public override void OnInitializeMelon() {
        MelonPreferences_Category category = MelonPreferences.CreateCategory("EchoTracker");
        _shown = category.CreateEntry("Shown", true, description: "Show the panel. F9 toggles in game.");
        _recordHistory = category.CreateEntry("RecordHistory", true, description: "Append each echo to UserData/EchoTracker/history.jsonl.");
        _left = category.CreateEntry("Left", 16f, description: "Panel distance from the left edge, in pixels.");
        _top = category.CreateEntry("Top", 160f, description: "Panel distance from the top edge, in pixels.");
        _width = category.CreateEntry("Width", 330f, description: "Panel width, in pixels.");
        _fontSize = category.CreateEntry("FontSize", 14, description: "Panel text size, in pixels.");
        _opacity = category.CreateEntry("Opacity", 0.6f, description: "Panel background opacity, 0 to 1.");
        LoggerInstance.Msg($"Echo tracker panel {(_shown.Value ? "shown" : "hidden")}; F9 toggles");
    }

    public override void OnUpdate() {
        const int VirtualKeyF9 = 0x78;
        bool down = (GetAsyncKeyState(VirtualKeyF9) & 0x8000) != 0;
        if (down && !_toggleWasDown) {
            _shown.Value = !_shown.Value;
        }

        _toggleWasDown = down;
        if (_faulted) {
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
        } catch (Exception exception) {
            _faulted = true;
            MelonLogger.Error($"Echo tracker stopped: {exception}");
        }
    }

    public override void OnApplicationQuit() {
        Finish();
    }

    // A zone is one loaded instance of the active scene: the handle changes on every load, even of the same scene.
    private static void Poll() {
        Scene scene = SceneManager.GetActiveScene();
        if (scene.handle != _sceneHandle) {
            _sceneHandle = scene.handle;
            Finish();
            _run = new ZoneRun(scene.name);
            MelonLogger.Msg($"Zone {scene.name}");
        }

        Subscribe();
        Actor player = PlayerFinder.getPlayerActor();
        _run.HasPlayer |= player != null;
        if (_run.Character == null && player != null) {
            _run.Character = PlayerFinder.getPlayerData()?.CharacterName;
        }

        MonolithZoneManager echo = MonolithZoneManager.instance;
        if (echo == null) {
            return;
        }

        if (!_run.IsEcho && player != null) {
            _run.IsEcho = true;
            MonolithRunsManager runs = player.monolithRunsManager;
            if (runs != null && runs.TryGetCurrentOrMostRecentlyRequestedRun(out MonolithRun monolithRun) && monolithRun != null) {
                _run.Timeline = monolithRun.timeline?.displayName;
                _run.Corruption = monolithRun.web?.corruption;
                _run.Empowered = monolithRun.IsEmpowered;
            }

            MelonLogger.Msg($"Echo in {_run.Timeline ?? "an unknown timeline"}, corruption {_run.Corruption?.ToString(CultureInfo.InvariantCulture) ?? "unknown"}");
        }

        if (_run.ObjectiveSeconds == null && echo.objectiveComplete) {
            _run.ObjectiveSeconds = _run.Seconds;
        }
    }

    // The death event belongs to the ActorManager instance; a new one (after a reload) needs the handler again.
    private static void Subscribe() {
        ActorManager manager = ActorManager.instance;
        if (manager == null || manager.Pointer == _subscribedManager) {
            return;
        }

        _onActorDied ??= DelegateSupport.ConvertDelegate<Il2CppSystem.Action<Actor, bool>>(new Action<Actor, bool>(OnActorDied));
        manager.add_actorDiedEvent(_onActorDied);
        _subscribedManager = manager.Pointer;
    }

    // Unsummoned actors (a minion or totem that expired) are not kills. Anything else hostile to the player counts,
    // including what enemies summon, as the game's own echo progress does.
    private static void OnActorDied(Actor dead, bool unsummoned) {
        if (_faulted || _run == null || dead == null || unsummoned) {
            return;
        }

        try {
            Actor player = PlayerFinder.getPlayerActor();
            if (player == null) {
                return;
            }

            if (dead.Pointer == player.Pointer) {
                _run.Deaths++;
                return;
            }

            Alignment alignment = player.GetAlignment();
            Alignment deadAlignment = dead.GetAlignment();
            if (alignment == null || deadAlignment == null || !alignment.IsFoe(deadAlignment) || dead.IsSummonedTotem()) {
                return;
            }

            _run.Kills[(int)Classify(dead)]++;
        } catch (Exception exception) {
            _faulted = true;
            MelonLogger.Error($"Echo tracker stopped counting: {exception}");
        }
    }

    // One kind per kill, the most notable first: a champion is also rare, a boss may be rare too.
    private static KillKind Classify(Actor actor) {
        if (actor.IsLootLizard) {
            return KillKind.Lizard;
        }

        if (actor.isBossOrMiniboss()) {
            return KillKind.Boss;
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

    private static void Finish() {
        ZoneRun run = _run;
        _run = null;
        if (run == null || !run.IsEcho || !run.HasPlayer) {
            return;
        }

        _lastEcho = run;
        _sessionEchoes++;
        _sessionSeconds += run.Seconds;
        _sessionKills += run.TotalKills;
        MelonLogger.Msg($"Echo left after {Clock(run.Seconds)}: {run.TotalKills} kills{(run.ObjectiveSeconds is { } objective ? $", objective at {Clock(objective)}" : ", objective not done")}");
        if (_recordHistory.Value) {
            Record(run);
        }
    }

    private static void Record(ZoneRun run) {
        try {
            string folder = Path.Combine(MelonEnvironment.UserDataDirectory, "EchoTracker");
            Directory.CreateDirectory(folder);
            var record = new {
                left = DateTimeOffset.Now.ToString("o", CultureInfo.InvariantCulture),
                character = run.Character,
                scene = run.Scene,
                timeline = run.Timeline,
                corruption = run.Corruption,
                empowered = run.Empowered,
                seconds = Math.Round(run.Seconds, 1),
                objectiveSeconds = run.ObjectiveSeconds is { } objective ? Math.Round(objective, 1) : (double?)null,
                kills = run.TotalKills,
                normal = run.Kills[(int)KillKind.Normal],
                magic = run.Kills[(int)KillKind.Magic],
                rare = run.Kills[(int)KillKind.Rare],
                champion = run.Kills[(int)KillKind.Champion],
                boss = run.Kills[(int)KillKind.Boss],
                lizard = run.Kills[(int)KillKind.Lizard],
                deaths = run.Deaths,
            };
            File.AppendAllText(Path.Combine(folder, "history.jsonl"), JsonSerializer.Serialize(record) + "\n");
        } catch (Exception exception) {
            MelonLogger.Warning($"Echo tracker could not record the echo: {exception.Message}");
        }
    }

    public override void OnGUI() {
        if (!_shown.Value || _faulted || _run == null || !_run.HasPlayer) {
            return;
        }

        try {
            // The text is rebuilt a few times a second; IMGUI calls OnGUI several times a frame.
            if (Time.unscaledTime >= _nextRedraw) {
                _nextRedraw = Time.unscaledTime + 0.2f;
                (_panelText, _panelLines) = PanelText();
            }

            int fontSize = Math.Clamp(_fontSize.Value, 8, 48);
            if (_style == null || _style.fontSize != fontSize) {
                _style = new GUIStyle { fontSize = fontSize, richText = true, wordWrap = false };
                _style.normal.textColor = Color.white;
            }

            const float Padding = 8f;
            float height = _panelLines * fontSize * 1.3f + Padding * 2f;
            var panel = new Rect(_left.Value, _top.Value, _width.Value, height);
            Color previous = GUI.color;
            GUI.color = new Color(0f, 0f, 0f, Math.Clamp(_opacity.Value, 0f, 1f));
            GUI.DrawTexture(panel, Texture2D.whiteTexture);
            GUI.color = previous;
            GUI.Label(new Rect(panel.x + Padding, panel.y + Padding, panel.width - Padding * 2f, panel.height - Padding * 2f), _panelText, _style);
        } catch (Exception exception) {
            _faulted = true;
            MelonLogger.Error($"Echo tracker stopped drawing: {exception}");
        }
    }

    // Colours are the game's rarity colours (EpochColor MAGIC, RARE; UNIQUE for champions and bosses).
    private static (string Text, int Lines) PanelText() {
        ZoneRun run = _run;
        var text = new StringBuilder();
        int lines = 0;
        void Line(string line) {
            if (lines > 0) {
                text.Append('\n');
            }

            text.Append(line);
            lines++;
        }

        if (run.IsEcho) {
            string corruption = run.Corruption is { } value ? $" · corruption {value.ToString(CultureInfo.InvariantCulture)}" : string.Empty;
            Line($"<b>{run.Timeline ?? "Echo"}</b>{corruption}");
        } else {
            Line($"<b>{run.Scene}</b>");
        }

        string objective = run.IsEcho
            ? run.ObjectiveSeconds is { } done ? $" · objective {Clock(done)}" : " · objective open"
            : string.Empty;
        Line($"{Clock(run.Seconds)}{objective}");
        double minutes = run.Seconds / 60d;
        string rate = minutes >= 0.5 ? $" · {run.TotalKills / minutes:0}/min" : string.Empty;
        Line($"Kills <b>{run.TotalKills}</b>{rate}");
        Line($"<color=#36A3E2>Magic {run.Kills[(int)KillKind.Magic]}</color>  <color=#E3D157>Rare {run.Kills[(int)KillKind.Rare]}</color>  "
            + $"<color=#EB730A>Champion {run.Kills[(int)KillKind.Champion]}  Boss {run.Kills[(int)KillKind.Boss]}</color>");
        string lizards = run.Kills[(int)KillKind.Lizard] > 0 ? $"Lizards {run.Kills[(int)KillKind.Lizard]}  " : string.Empty;
        if (lizards.Length > 0 || run.Deaths > 0) {
            Line($"{lizards}{(run.Deaths > 0 ? $"Deaths {run.Deaths}" : string.Empty)}".TrimEnd());
        }

        if (_lastEcho != null) {
            string lastObjective = _lastEcho.ObjectiveSeconds is { } last ? Clock(last) : "not done";
            Line($"<color=#BBBBBB>Last echo {Clock(_lastEcho.Seconds)} · objective {lastObjective} · {_lastEcho.TotalKills} kills</color>");
        }

        if (_sessionEchoes > 0) {
            Line($"<color=#BBBBBB>Session {_sessionEchoes} echoes · {Clock(_sessionSeconds / _sessionEchoes)} and {_sessionKills / _sessionEchoes} kills each</color>");
        }

        return (text.ToString(), lines);
    }

    private static string Clock(double seconds) {
        var time = TimeSpan.FromSeconds(Math.Max(0d, seconds));
        return time.TotalHours >= 1d
            ? time.ToString(@"h\:mm\:ss", CultureInfo.InvariantCulture)
            : time.ToString(@"mm\:ss", CultureInfo.InvariantCulture);
    }

    [DllImport("user32.dll")]
    private static extern short GetAsyncKeyState(int virtualKey);
}

internal enum KillKind {
    Normal,
    Magic,
    Rare,
    Champion,
    Boss,
    Lizard,
}

internal sealed class ZoneRun {
    public ZoneRun(string scene) {
        Scene = scene;
    }

    public string Scene { get; }
    public double Seconds { get; set; }
    public bool HasPlayer { get; set; }
    public string Character { get; set; }
    public bool IsEcho { get; set; }
    public string Timeline { get; set; }
    public int? Corruption { get; set; }
    public bool Empowered { get; set; }
    public double? ObjectiveSeconds { get; set; }
    public int[] Kills { get; } = new int[Enum.GetValues<KillKind>().Length];
    public int Deaths { get; set; }
    public int TotalKills => Kills.Sum();
}
