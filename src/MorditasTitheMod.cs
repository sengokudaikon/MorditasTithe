using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Text.RegularExpressions;
using Il2Cpp;
using Il2CppInterop.Runtime;
using MelonLoader;
using MelonLoader.Utils;
using MorditasTithe;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using Object = UnityEngine.Object;

[assembly: MelonInfo(typeof(MorditasTitheMod), "Morditas' Tithe", "0.2.0", "sengokudaikon")]
[assembly: MelonGame("Eleventh Hour Games", "Last Epoch")]

namespace MorditasTithe;

// Counts the player's kills by kind and times each zone, with the echo's timeline, corruption and time to the objective in
// the Monolith. A panel shows the zone under way, the last echo and the session; each echo left is appended to
// UserData/MorditasTithe/history.jsonl. Kills come from the game's own death event (ActorManager.actorDiedEvent), which
// fires once for every actor that dies, whoever killed it.
public sealed class MorditasTitheMod : MelonMod {
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
    private static int _sessionEchoes;
    private static double _sessionSeconds;
    private static int _sessionKills;

    private static IntPtr _subscribedManager;
    private static Il2CppSystem.Action<Actor, bool> _onActorDied;
    private static int _sceneHandle;
    private static float _nextPoll;
    private static bool _toggleWasDown;
    private static bool _faulted;

    public override void OnInitializeMelon() {
        _category = MelonPreferences.CreateCategory("MorditasTithe");
        _shown = _category.CreateEntry("Shown", true, description: "Show the panel. F9 toggles in game.");
        _pinned = _category.CreateEntry("Pinned", false, description: "Pinned: the panel stays put and clicks pass through it.");
        _recordHistory = _category.CreateEntry("RecordHistory", true, description: "Append each echo to UserData/MorditasTithe/history.jsonl.");
        _opacity = _category.CreateEntry("Opacity", 0.8f, description: "Panel background opacity, 0 to 1.");
        _left = _category.CreateEntry("Left", -1f, description: "Panel left edge as a share of the screen width; dragging sets it. -1 places it by default.");
        _top = _category.CreateEntry("Top", -1f, description: "Panel top edge as a share of the screen height; dragging sets it. -1 places it by default.");
        _scale = _category.CreateEntry("Scale", 1f, description: "Panel size; the corner grip sets it. 1 is the default at any resolution.");
        LoggerInstance.Msg($"Morditas' Tithe panel {(_shown.Value ? "shown" : "hidden")}; F9 toggles");
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

    internal static (int Echoes, double Seconds, int Kills) Session => (_sessionEchoes, _sessionSeconds, _sessionKills);

    public override void OnUpdate() {
        const int VirtualKeyF9 = 0x78;
        bool down = (GetAsyncKeyState(VirtualKeyF9) & 0x8000) != 0;
        if (down && !_toggleWasDown) {
            _shown.Value = !_shown.Value;
        }

        _toggleWasDown = down;
        if (_faulted) {
            Panel.Hide();
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
        } catch (Exception exception) {
            Fault("stopped drawing", exception);
        }
    }

    public override void OnApplicationQuit() {
        Finish();
    }

    internal static void Fault(string what, Exception exception) {
        _faulted = true;
        Panel.Hide();
        MelonLogger.Error($"Morditas' Tithe {what}: {exception}");
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
        if (player == null) {
            return;
        }

        if (!_run.HasPlayer) {
            _run.HasPlayer = true;
            _run.Character = PlayerFinder.getPlayerData()?.CharacterName;
            _run.ZoneName = SceneList.GetCurrentSceneDisplayName();
        }

        MonolithZoneManager echo = MonolithZoneManager.instance;
        if (echo == null) {
            return;
        }

        if (!_run.IsEcho) {
            _run.IsEcho = true;
            MonolithRunsManager runs = player.monolithRunsManager;
            if (runs != null && runs.TryGetCurrentOrMostRecentlyRequestedRun(out MonolithRun monolithRun) && monolithRun != null) {
                _run.Timeline = monolithRun.timeline?.displayName;
                _run.Corruption = monolithRun.web?.corruption;
                _run.Empowered = monolithRun.IsEmpowered;
            }

            MelonLogger.Msg($"Echo {_run.ZoneName} in {_run.Timeline ?? "an unknown timeline"}, corruption {_run.Corruption?.ToString(CultureInfo.InvariantCulture) ?? "unknown"}");
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
            Fault("stopped counting", exception);
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
        MelonLogger.Msg($"Echo left after {Panel.Clock(run.Seconds)}: {run.TotalKills} kills{(run.ObjectiveSeconds is { } objective ? $", objective at {Panel.Clock(objective)}" : ", objective not done")}");
        if (_recordHistory.Value) {
            Record(run);
        }
    }

    private static void Record(ZoneRun run) {
        try {
            string folder = Path.Combine(MelonEnvironment.UserDataDirectory, "MorditasTithe");
            Directory.CreateDirectory(folder);
            var record = new {
                left = DateTimeOffset.Now.ToString("o", CultureInfo.InvariantCulture),
                character = run.Character,
                scene = run.Scene,
                zone = run.ZoneName,
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
            MelonLogger.Warning($"Morditas' Tithe could not record the echo: {exception.Message}");
        }
    }

    [DllImport("user32.dll")]
    private static extern short GetAsyncKeyState(int virtualKey);
}

// The panel: drawn with IMGUI, sized from the screen height so it reads the same at any resolution. Unpinned, it drags
// from anywhere and resizes from the corner grip; pinned, it stays put and only the pin control takes clicks. The area
// that takes clicks is mirrored by an invisible uGUI rectangle carrying the game's own UIMouseListener, so the game treats
// it as UI and does not walk the character to a click on it.
internal static class Panel {
    internal const float MinScale = 0.6f;
    internal const float MaxScale = 3f;
    private const string _name = "MORDITAS' TITHE";

    // Game-owned rarity colours (EpochColor MAGIC, RARE; UNIQUE for champions and bosses). The blood red is ours.
    private static readonly Color _blood = new(0.80f, 0.16f, 0.20f, 1f);
    private static readonly Color _magic = Hex(0x36A3E2);
    private static readonly Color _rare = Hex(0xE3D157);
    private static readonly Color _unique = Hex(0xEB730A);
    private static readonly Color _muted = new(0.74f, 0.72f, 0.70f, 1f);
    private static readonly Regex _tags = new("<[^>]+>", RegexOptions.Compiled);

    private static GUIStyle _caption;
    private static GUIStyle _heading;
    private static GUIStyle _body;
    private static GUIStyle _figure;
    private static GUIStyle _note;
    private static GUIStyle _pinStyle;
    private static int _styledSize;
    private static Font _bodyFont;
    private static Font _displayFont;
    private static int _fontSearches;
    private static float _nextFontSearch;

    private static Rect _rect;
    private static Rect _pin;
    private static Rect _grip;
    private static float _contentWidth;
    private static ZoneRun _widthFor;
    private static Row[] _rows;
    private static float _nextRows;
    private static bool _visible;

    private enum Gesture { None, Move, Resize }
    private static Gesture _gesture;
    private static Vector2 _gestureMouse;
    private static Vector2 _gestureStart;
    private static float _gestureScale;
    private static float _liveScale = -1f;
    private static Vector2 _livePosition = new(-1f, -1f);

    private static GameObject _blockerRoot;
    private static RectTransform _blocker;

    private static float Unit => Screen.height / 1080f * (_liveScale > 0f ? _liveScale : MorditasTitheMod.Scale);

    private static Vector2 Position => _livePosition.x >= 0f ? _livePosition : MorditasTitheMod.Position;

    internal static void Hide() {
        _visible = false;
        if (_blockerRoot != null) {
            _blockerRoot.SetActive(false);
        }
    }

    // Ends a gesture whose mouse-up IMGUI never saw (released over another window), and keeps the click blocker on the
    // area that takes clicks.
    internal static void Update() {
        const int VirtualKeyLeftButton = 0x01;
        if (_gesture != Gesture.None && (GetAsyncKeyState(VirtualKeyLeftButton) & 0x8000) == 0) {
            EndGesture();
        }

        if (!_visible) {
            Hide();
            return;
        }

        EnsureBlocker();
        _blockerRoot.SetActive(true);
        Rect area = MorditasTitheMod.Pinned ? _pin : _rect;
        _blocker.anchoredPosition = new Vector2(area.x, Screen.height - area.yMax);
        _blocker.sizeDelta = new Vector2(area.width, area.height);
    }

    internal static void Draw() {
        ZoneRun run = MorditasTitheMod.Run;
        _visible = MorditasTitheMod.Shown && run != null && run.HasPlayer;
        if (!_visible) {
            return;
        }

        float unit = Unit;
        Styles(unit);
        // Rows are rebuilt a few times a second; IMGUI calls OnGUI several times a frame. The width only grows within a
        // zone, so the panel does not twitch as numbers change.
        if (_widthFor != run) {
            _widthFor = run;
            _contentWidth = 0f;
            _rows = null;
        }

        if (_rows == null || Time.unscaledTime >= _nextRows) {
            _nextRows = Time.unscaledTime + 0.2f;
            _rows = Rows(run);
            foreach (Row row in _rows) {
                if (row.Style != null) {
                    _contentWidth = Math.Max(_contentWidth, row.Style.CalcSize(new GUIContent(row.Text)).x);
                }
            }

            // The name and the pin control share the first line.
            float name = _caption.CalcSize(new GUIContent(_name)).x + _pinStyle.CalcSize(new GUIContent("PINNED")).x;
            _contentWidth = Math.Max(_contentWidth, name + 24f * unit);
        }

        Row[] rows = _rows;

        float padding = 14f * unit;
        float gap = 4f * unit;
        float captionHeight = _caption.fontSize * 1.4f;
        float height = padding * 2f + captionHeight + gap;
        foreach (Row row in rows) {
            height += row.Style == null ? 10f * unit : row.Style.fontSize * 1.35f;
        }

        float width = Math.Max(_contentWidth, 220f * unit) + padding * 2f;
        Vector2 position = Position;
        float x = Math.Clamp(position.x * Screen.width, 0f, Math.Max(0f, Screen.width - width));
        float y = Math.Clamp(position.y * Screen.height, 0f, Math.Max(0f, Screen.height - height));
        _rect = new Rect(x, y, width, height);
        float pinSize = captionHeight;
        _pin = new Rect(_rect.xMax - padding - pinSize * 2.6f, _rect.y + padding - 2f * unit, pinSize * 2.6f + padding / 2f, pinSize + 4f * unit);
        float gripSize = 16f * unit;
        _grip = new Rect(_rect.xMax - gripSize, _rect.yMax - gripSize, gripSize, gripSize);

        Input();

        Fill(_rect, new Color(0.04f, 0.035f, 0.035f, MorditasTitheMod.Opacity));
        Fill(new Rect(_rect.x, _rect.y, _rect.width, Math.Max(2f, 2f * unit)), _blood);
        float top = _rect.y + padding;
        float left = _rect.x + padding;
        float inner = _rect.width - padding * 2f;
        Text(new Rect(left, top, inner, captionHeight), _name, _caption);
        PinControl(unit);
        top += captionHeight + gap;
        foreach (Row row in rows) {
            if (row.Style == null) {
                Fill(new Rect(left, top + 4f * unit, inner, Math.Max(1f, unit)), new Color(1f, 1f, 1f, 0.12f));
                top += 10f * unit;
                continue;
            }

            float rowHeight = row.Style.fontSize * 1.35f;
            Text(new Rect(left, top, inner, rowHeight), row.Text, row.Style);
            top += rowHeight;
        }

        if (!MorditasTitheMod.Pinned) {
            Grip(unit);
        }
    }

    private static Row[] Rows(ZoneRun run) {
        var rows = new System.Collections.Generic.List<Row>();
        if (run.IsEcho) {
            rows.Add(new Row(run.Timeline ?? "Echo", _heading));
            string corruption = run.Corruption is { } value ? $"Corruption {value.ToString(CultureInfo.InvariantCulture)}" : null;
            string where = string.Join(" · ", new[] { run.ZoneName, corruption }.Where(part => !string.IsNullOrEmpty(part)));
            if (where.Length > 0) {
                rows.Add(new Row(where, _note));
            }
        } else {
            rows.Add(new Row(string.IsNullOrEmpty(run.ZoneName) ? run.Scene : run.ZoneName, _heading));
        }

        string objective = run.IsEcho
            ? run.ObjectiveSeconds is { } done ? $"   <color={Html(_muted)}>objective {Clock(done)}</color>" : $"   <color={Html(_muted)}>objective open</color>"
            : string.Empty;
        rows.Add(new Row($"{Clock(run.Seconds)}{objective}", _figure));
        double minutes = run.Seconds / 60d;
        string rate = minutes >= 0.5 ? $"   <color={Html(_muted)}>{run.TotalKills / minutes:0} a minute</color>" : string.Empty;
        rows.Add(new Row($"<b>{run.TotalKills}</b> kills{rate}", _body));
        rows.Add(new Row($"<color={Html(_magic)}>Magic {run.Kills[(int)KillKind.Magic]}</color>   <color={Html(_rare)}>Rare {run.Kills[(int)KillKind.Rare]}</color>   "
            + $"<color={Html(_unique)}>Champion {run.Kills[(int)KillKind.Champion]}   Boss {run.Kills[(int)KillKind.Boss]}</color>", _body));
        int lizards = run.Kills[(int)KillKind.Lizard];
        if (lizards > 0 || run.Deaths > 0) {
            string extra = string.Join("   ", new[] { lizards > 0 ? $"Lizards {lizards}" : null, run.Deaths > 0 ? $"<color={Html(_blood)}>Deaths {run.Deaths}</color>" : null }
                .Where(part => part != null));
            rows.Add(new Row(extra, _body));
        }

        ZoneRun last = MorditasTitheMod.LastEcho;
        (int echoes, double seconds, int kills) = MorditasTitheMod.Session;
        if (last != null || echoes > 0) {
            rows.Add(new Row(null, null));
        }

        if (last != null) {
            string lastObjective = last.ObjectiveSeconds is { } finished ? $"objective {Clock(finished)}" : "objective not done";
            rows.Add(new Row($"Last echo  {Clock(last.Seconds)} · {lastObjective} · {last.TotalKills} kills", _note));
        }

        if (echoes > 0) {
            rows.Add(new Row($"Session  {echoes} {(echoes == 1 ? "echo" : "echoes")} · {Clock(seconds / echoes)} and {kills / echoes} kills each", _note));
        }

        return rows.ToArray();
    }

    // Unpinned: a press on the grip resizes, anywhere else on the panel moves it; the pin control toggles in both states.
    private static void Input() {
        Event current = Event.current;
        if (current == null) {
            return;
        }

        Vector2 mouse = current.mousePosition;
        switch (current.type) {
            case EventType.MouseDown when current.button == 0 && _pin.Contains(mouse):
                MorditasTitheMod.Pinned = !MorditasTitheMod.Pinned;
                current.Use();
                break;
            case EventType.MouseDown when current.button == 0 && !MorditasTitheMod.Pinned && _rect.Contains(mouse):
                _gesture = _grip.Contains(mouse) ? Gesture.Resize : Gesture.Move;
                _gestureMouse = mouse;
                _gestureStart = new Vector2(_rect.x, _rect.y);
                _gestureScale = MorditasTitheMod.Scale;
                _liveScale = _gestureScale;
                _livePosition = new Vector2(_rect.x / Screen.width, _rect.y / Screen.height);
                current.Use();
                break;
            case EventType.MouseDrag when _gesture == Gesture.Move:
                Vector2 moved = _gestureStart + (mouse - _gestureMouse);
                _livePosition = new Vector2(moved.x / Screen.width, moved.y / Screen.height);
                current.Use();
                break;
            case EventType.MouseDrag when _gesture == Gesture.Resize:
                // The panel grows with the distance from its left edge to the pointer, as a window's corner does.
                float startReach = Math.Max(1f, _gestureMouse.x - _rect.x);
                _liveScale = Math.Clamp(_gestureScale * (mouse.x - _rect.x) / startReach, MinScale, MaxScale);
                current.Use();
                break;
            case EventType.MouseUp when _gesture != Gesture.None:
                EndGesture();
                current.Use();
                break;
        }
    }

    private static void EndGesture() {
        if (_gesture == Gesture.None) {
            return;
        }

        _gesture = Gesture.None;
        Vector2 position = _rect.width > 0f ? new Vector2(_rect.x / Screen.width, _rect.y / Screen.height) : _livePosition;
        MorditasTitheMod.SavePlacement(position, _liveScale > 0f ? _liveScale : MorditasTitheMod.Scale);
        _liveScale = -1f;
        _livePosition = new Vector2(-1f, -1f);
    }

    // "Pin" while it can move, "Pinned" once it is pinned; brighter under the pointer.
    private static void PinControl(float unit) {
        bool pinned = MorditasTitheMod.Pinned;
        bool hover = _pin.Contains(Event.current.mousePosition);
        if (hover) {
            Fill(_pin, new Color(1f, 1f, 1f, 0.08f));
        }

        Color color = pinned ? _blood : hover ? Color.white : _muted;
        var label = new Rect(_pin.x, _pin.y + 2f * unit, _pin.width - 4f * unit, _pin.height);
        _pinStyle.normal.textColor = color;
        Text(label, pinned ? "PINNED" : "PIN", _pinStyle);
    }

    // Three short diagonal strokes in the corner, as a window's size grip.
    private static void Grip(float unit) {
        float dot = Math.Max(2f, 2f * unit);
        float step = 4f * unit;
        var color = new Color(1f, 1f, 1f, _grip.Contains(Event.current.mousePosition) || _gesture == Gesture.Resize ? 0.8f : 0.35f);
        for (int row = 0; row < 3; row++) {
            for (int column = 0; column <= row; column++) {
                float x = _grip.xMax - 3f * unit - (row - column) * step - dot;
                float y = _grip.yMax - 3f * unit - column * step - dot;
                Fill(new Rect(x, y, dot, dot), color);
            }
        }
    }

    // A one-pixel shadow under every line keeps the text legible over bright ground.
    private static void Text(Rect rect, string text, GUIStyle style) {
        Color color = style.normal.textColor;
        style.normal.textColor = new Color(0f, 0f, 0f, 0.85f);
        float offset = Math.Max(1f, Screen.height / 1080f);
        GUI.Label(new Rect(rect.x + offset, rect.y + offset, rect.width, rect.height), _tags.Replace(text, string.Empty), style);
        style.normal.textColor = color;
        GUI.Label(rect, text, style);
    }

    private static void Fill(Rect rect, Color color) {
        Color previous = GUI.color;
        GUI.color = color;
        GUI.DrawTexture(rect, Texture2D.whiteTexture);
        GUI.color = previous;
    }

    private static void Styles(float unit) {
        FindFonts();
        int size = (int)MathF.Round(19f * unit);
        if (_body != null && _styledSize == size) {
            return;
        }

        _styledSize = size;
        _contentWidth = 0f;
        _caption = Style(_displayFont ?? _bodyFont, (int)MathF.Round(13f * unit), _blood, FontStyle.Bold);
        _heading = Style(_displayFont ?? _bodyFont, (int)MathF.Round(23f * unit), Color.white, FontStyle.Normal);
        _figure = Style(_bodyFont, (int)MathF.Round(26f * unit), Color.white, FontStyle.Bold);
        _body = Style(_bodyFont, size, new Color(0.94f, 0.92f, 0.90f, 1f), FontStyle.Normal);
        _note = Style(_bodyFont, (int)MathF.Round(15f * unit), _muted, FontStyle.Normal);
        _pinStyle = Style(_displayFont ?? _bodyFont, (int)MathF.Round(13f * unit), _muted, FontStyle.Bold);
        _pinStyle.alignment = TextAnchor.UpperRight;
        _rows = null;
    }

    private static GUIStyle Style(Font font, int size, Color color, FontStyle fontStyle) {
        var style = new GUIStyle { fontSize = Math.Max(8, size), richText = true, wordWrap = false, fontStyle = fontStyle, clipping = TextClipping.Overflow };
        if (font != null) {
            style.font = font;
        }

        style.normal.textColor = color;
        return style;
    }

    // The game's own faces, once it has loaded them: Noto Sans for text, its LastEpoch face for the name and headings.
    // The default IMGUI font stands in until then.
    private static void FindFonts() {
        if ((_bodyFont != null && _displayFont != null) || _fontSearches >= 20 || Time.unscaledTime < _nextFontSearch) {
            return;
        }

        _nextFontSearch = Time.unscaledTime + 2f;

        _fontSearches++;
        var fonts = Resources.FindObjectsOfTypeAll(Il2CppType.Of<Font>())
            .Select(found => found.TryCast<Font>())
            .Where(font => font != null && font.dynamic)
            .ToList();
        Font body = fonts.FirstOrDefault(font => font.name.Contains("Noto", StringComparison.OrdinalIgnoreCase) && font.name.Contains("Regular", StringComparison.OrdinalIgnoreCase))
            ?? fonts.FirstOrDefault(font => font.name.Contains("Noto", StringComparison.OrdinalIgnoreCase));
        Font display = fonts.FirstOrDefault(font => font.name.StartsWith("LastEpoch", StringComparison.OrdinalIgnoreCase) && !font.name.Contains("Alt", StringComparison.OrdinalIgnoreCase))
            ?? fonts.FirstOrDefault(font => font.name.StartsWith("LastEpoch", StringComparison.OrdinalIgnoreCase));
        if (body == null && display == null) {
            return;
        }

        _bodyFont ??= body;
        _displayFont ??= display;
        _body = null;
        MelonLogger.Msg($"Fonts: text {_bodyFont?.name ?? "default"}, display {_displayFont?.name ?? "default"} (loaded: {string.Join(", ", fonts.Select(font => font.name).Distinct())})");
    }

    private static void EnsureBlocker() {
        if (_blockerRoot != null) {
            return;
        }

        _blockerRoot = new GameObject("MorditasTithe.ClickArea");
        Object.DontDestroyOnLoad(_blockerRoot);
        Canvas canvas = _blockerRoot.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 30000;
        _blockerRoot.AddComponent<GraphicRaycaster>();
        var area = new GameObject("Area");
        area.transform.SetParent(_blockerRoot.transform, false);
        Image image = area.AddComponent<Image>();
        image.color = new Color(0f, 0f, 0f, 0f);
        image.raycastTarget = true;
        area.AddComponent<UIMouseListener>();
        _blocker = area.GetComponent<RectTransform>();
        _blocker.anchorMin = Vector2.zero;
        _blocker.anchorMax = Vector2.zero;
        _blocker.pivot = Vector2.zero;
    }

    internal static string Clock(double seconds) {
        var time = TimeSpan.FromSeconds(Math.Max(0d, seconds));
        return time.TotalHours >= 1d
            ? time.ToString(@"h\:mm\:ss", CultureInfo.InvariantCulture)
            : time.ToString(@"mm\:ss", CultureInfo.InvariantCulture);
    }

    private static Color Hex(int rgb) {
        return new Color(((rgb >> 16) & 0xFF) / 255f, ((rgb >> 8) & 0xFF) / 255f, (rgb & 0xFF) / 255f, 1f);
    }

    private static string Html(Color color) {
        return "#" + ColorUtility.ToHtmlStringRGB(color);
    }

    [DllImport("user32.dll")]
    private static extern short GetAsyncKeyState(int virtualKey);

    // A null style is a divider.
    private readonly record struct Row(string Text, GUIStyle Style);
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
    public string ZoneName { get; set; }
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
