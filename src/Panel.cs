using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using UnityEngine;

namespace MorditasTithe;

// The panel: drawn with IMGUI, sized from the screen height so it reads the same at any resolution. Unpinned, it drags
// from anywhere and resizes from the corner grip; pinned, it stays put and only its caption controls take clicks.
internal static class Panel {
    internal const float MinScale = 0.6f;
    internal const float MaxScale = 3f;
    private const int _clickSlot = 0;
    private const string _name = "MORDITAS' TITHE";

    private static Rect _rect;
    private static Rect _pin;
    private static Rect _debrief;
    private static Rect _grip;
    private static float _contentWidth;
    private static ZoneRun _widthFor;
    private static Styles _rowStyles;
    private static Row[] _rows;
    private static float _nextRows;
    private static bool _visible;

    private static int _sessionVersion = -1;
    private static SessionSums? _session;

    private enum Gesture { None, Move, Resize }
    private static Gesture _gesture;
    private static Vector2 _gestureMouse;
    private static Vector2 _gestureStart;
    private static float _gestureScale;
    private static float _liveScale = -1f;
    private static Vector2 _livePosition = new(-1f, -1f);

    private static float Unit => Screen.height / 1080f * (_liveScale > 0f ? _liveScale : Settings.Scale);

    private static Vector2 Position => _livePosition.x >= 0f ? _livePosition : Settings.Position;

    // Ends a gesture whose mouse-up IMGUI never saw (released over another window), and keeps the click area on what
    // takes clicks: the whole panel, or once pinned its caption controls.
    internal static void Update() {
        const int VirtualKeyLeftButton = 0x01;
        if (_gesture != Gesture.None && (Ui.GetAsyncKeyState(VirtualKeyLeftButton) & 0x8000) == 0) {
            EndGesture();
        }

        if (!_visible) {
            Ui.NoClickArea(_clickSlot);
            return;
        }

        Ui.ClickArea(_clickSlot, Settings.Pinned ? Rect.MinMaxRect(_debrief.xMin, _pin.yMin, _pin.xMax, _pin.yMax) : _rect);
    }

    internal static void Draw() {
        ZoneRun run = Zones.Run;
        _visible = Settings.Shown && MorditasTitheMod.InGame;
        if (!_visible) {
            return;
        }

        float unit = Unit;
        Styles styles = Ui.StylesFor(unit);
        // Rows are rebuilt a few times a second; IMGUI calls OnGUI several times a frame. The width only grows within a
        // zone and style (a new size or the game's fonts arriving make new styles), so the panel does not twitch as numbers
        // change.
        if (_widthFor != run || _rowStyles != styles) {
            _widthFor = run;
            _rowStyles = styles;
            _contentWidth = 0f;
            _rows = null;
        }

        if (_rows == null || Time.unscaledTime >= _nextRows) {
            _nextRows = Time.unscaledTime + 0.2f;
            _rows = Rows(run, styles);
            foreach (Row row in _rows) {
                if (row.Style != null) {
                    _contentWidth = Math.Max(_contentWidth, Ui.Width(row.Text, row.Style));
                }
            }

            // The name and the caption controls share the first line.
            float caption = Ui.Width(_name, styles.Caption) + Ui.Width("DEBRIEF", styles.Control) + Ui.Width("PINNED", styles.Control);
            _contentWidth = Math.Max(_contentWidth, caption + 40f * unit);
        }

        Row[] rows = _rows;
        float padding = 14f * unit;
        float gap = 4f * unit;
        float captionHeight = styles.Caption.fontSize * 1.4f;
        float height = padding * 2f + captionHeight + gap + rows.Sum(row => styles.Height(row.Style));
        float width = Math.Max(_contentWidth, 220f * unit) + padding * 2f;
        Vector2 position = Position;
        float x = Math.Clamp(position.x * Screen.width, 0f, Math.Max(0f, Screen.width - width));
        float y = Math.Clamp(position.y * Screen.height, 0f, Math.Max(0f, Screen.height - height));
        _rect = new Rect(x, y, width, height);
        float controlHeight = captionHeight + 4f * unit;
        float pinWidth = Ui.Width("PINNED", styles.Control) + 12f * unit;
        float debriefWidth = Ui.Width("DEBRIEF", styles.Control) + 12f * unit;
        _pin = new Rect(_rect.xMax - padding / 2f - pinWidth, _rect.y + padding - 2f * unit, pinWidth, controlHeight);
        _debrief = new Rect(_pin.x - 4f * unit - debriefWidth, _pin.y, debriefWidth, controlHeight);
        float gripSize = 16f * unit;
        _grip = new Rect(_rect.xMax - gripSize, _rect.yMax - gripSize, gripSize, gripSize);

        Input();

        Ui.Fill(_rect, new Color(Ui.Ground.r, Ui.Ground.g, Ui.Ground.b, Settings.Opacity));
        Ui.Fill(new Rect(_rect.x, _rect.y, _rect.width, Math.Max(2f, 2f * unit)), Ui.Blood);
        float top = _rect.y + padding;
        float left = _rect.x + padding;
        float inner = _rect.width - padding * 2f;
        Ui.Text(new Rect(left, top, inner, captionHeight), _name, styles.Caption);
        Ui.Control(_debrief, "DEBRIEF", Debrief.Open, styles.Control, unit);
        Ui.Control(_pin, Settings.Pinned ? "PINNED" : "PIN", Settings.Pinned, styles.Control, unit);
        top += captionHeight + gap;
        foreach (Row row in rows) {
            float rowHeight = styles.Height(row.Style);
            if (row.Style == null) {
                Ui.Fill(new Rect(left, top + 4f * unit, inner, Math.Max(1f, unit)), Ui.Rule);
            } else {
                Ui.Text(new Rect(left, top, inner, rowHeight), row.Text, row.Style);
            }

            top += rowHeight;
        }

        if (!Settings.Pinned) {
            Grip(unit);
        }
    }

    // In an echo, a boss fight or a combat zone: the zone under way, then the last echo and the session in brief. In a hub
    // nothing is under way, so the last echo is shown in full instead.
    private static Row[] Rows(ZoneRun run, Styles styles) {
        var rows = new List<Row>();
        ZoneRun last = Zones.LastEcho;
        SessionSums session = Session();
        switch (run.Kind) {
            case ZoneKind.Hub when last == null:
                rows.Add(new Row("No echo yet this session", styles.Note));
                break;
            case ZoneKind.Hub:
                rows.Add(new Row("Last echo", styles.Note));
                RunRows(rows, last, styles);
                break;
            case ZoneKind.Boss:
                BossRows(rows, run, styles);
                break;
            default:
                RunRows(rows, run, styles);
                if (last != null) {
                    rows.Add(new Row(null, null));
                    string lastObjective = last.ObjectiveSeconds is { } finished ? $"objective {Ui.Clock(finished)}" : "objective not done";
                    rows.Add(new Row($"Last echo  {Ui.Clock(last.Seconds)} · {lastObjective} · {last.Kills.Total} kills", styles.Note));
                }

                break;
        }

        bool divided = run.Kind is not (ZoneKind.Echo or ZoneKind.Combat) || last == null;
        if (session.Echoes > 0) {
            if (divided) {
                rows.Add(new Row(null, null));
                divided = false;
            }

            rows.Add(new Row($"Session  {session.Echoes} {(session.Echoes == 1 ? "echo" : "echoes")} · {Ui.Clock(session.Seconds / session.Echoes)} and {session.Kills / session.Echoes} kills each", styles.Note));
        }

        if (session.Bosses.Count > 0) {
            if (divided) {
                rows.Add(new Row(null, null));
            }

            const int Shown = 3;
            string named = string.Join(" · ", session.Bosses.Take(Shown).Select(boss => boss.Kills > 1 ? $"{boss.Name} ×{boss.Kills}" : boss.Name));
            string more = session.Bosses.Count > Shown ? $" · {session.Bosses.Count - Shown} more" : string.Empty;
            rows.Add(new Row($"Boss fights  {named}{more}", styles.Note));
        }

        return rows.ToArray();
    }

    // An echo is headed by its timeline with the corruption under it, then the zone; any other zone by its name.
    private static void RunRows(List<Row> rows, ZoneRun run, Styles styles) {
        if (run.IsEcho && run.Timeline != null) {
            rows.Add(new Row(run.Timeline, styles.Heading));
            CorruptionRow(rows, run, styles);
            if (!string.IsNullOrEmpty(run.ZoneName)) {
                rows.Add(new Row(run.ZoneName, styles.Note));
            }
        } else {
            rows.Add(new Row(string.IsNullOrEmpty(run.ZoneName) ? run.Scene : run.ZoneName, styles.Heading));
        }

        string objective = !run.InMonolith ? string.Empty
            : Ui.Tint(run.ObjectiveSeconds is { } done ? $"   objective {Ui.Clock(done)}" : "   objective open", Ui.Muted);
        rows.Add(new Row($"{Ui.Clock(run.Seconds)}{objective}", styles.Figure));
        double minutes = run.Seconds / 60d;
        string rate = minutes >= 0.5 ? Ui.Tint($"   {run.Kills.Total / minutes:0} a minute", Ui.Muted) : string.Empty;
        rows.Add(new Row($"<b>{run.Kills.Total}</b> kills{rate}", styles.Body));
        rows.Add(new Row($"{Ui.Tint($"Magic {run.Kills[KillKind.Magic]}", Ui.Magic)}   {Ui.Tint($"Rare {run.Kills[KillKind.Rare]}", Ui.Rare)}   "
            + Ui.Tint($"Champion {run.Kills[KillKind.Champion]}   Boss {run.Kills[KillKind.Boss]}", Ui.Unique), styles.Body));
        int minibosses = run.Kills[KillKind.Miniboss];
        int lizards = run.Kills[KillKind.Lizard];
        string extra = string.Join("   ", new[] {
            minibosses > 0 ? $"Minibosses {minibosses}" : null,
            lizards > 0 ? $"Lizards {lizards}" : null,
            run.Deaths > 0 ? Ui.Tint($"Deaths {run.Deaths}", Ui.Blood) : null,
        }.Where(part => part != null));
        if (extra.Length > 0) {
            rows.Add(new Row(extra, styles.Body));
        }
    }

    // A boss fight is headed by the boss once it falls (the zone until then), and timed to its fall.
    private static void BossRows(List<Row> rows, ZoneRun run, Styles styles) {
        string zone = string.IsNullOrEmpty(run.ZoneName) ? run.Scene : run.ZoneName;
        BossKill? first = run.Bosses.Count > 0 ? run.Bosses[0] : null;
        rows.Add(new Row(first?.Name ?? zone, styles.Heading));
        CorruptionRow(rows, run, styles);
        rows.Add(new Row(first == null ? zone : $"{zone}{(run.Timeline != null ? $" · {run.Timeline}" : string.Empty)}", styles.Note));
        rows.Add(new Row(first == null ? Ui.Clock(run.Seconds) : $"{Ui.Clock(first.Value.Seconds)}{Ui.Tint("   slain", Ui.Muted)}", styles.Figure));
        foreach (BossKill boss in run.Bosses.Skip(1)) {
            rows.Add(new Row($"{Ui.Tint(boss.Name, Ui.Unique)}   {Ui.Tint($"slain {Ui.Clock(boss.Seconds)}", Ui.Muted)}", styles.Body));
        }

        string extra = string.Join("   ", new[] {
            run.Kills.Total > run.Bosses.Count ? $"{run.Kills.Total} kills" : null,
            run.Deaths > 0 ? Ui.Tint($"Deaths {run.Deaths}", Ui.Blood) : null,
        }.Where(part => part != null));
        if (extra.Length > 0) {
            rows.Add(new Row(extra, styles.Body));
        }
    }

    private static void CorruptionRow(List<Row> rows, ZoneRun run, Styles styles) {
        if (run.Corruption is not { } corruption) {
            return;
        }

        string empowered = run.Empowered ? Ui.Tint("   Empowered", Ui.Muted) : string.Empty;
        rows.Add(new Row($"{Ui.Tint($"Corruption <b>{corruption.ToString(CultureInfo.InvariantCulture)}</b>", Ui.Blood)}{empowered}", styles.Body));
    }

    // The session so far: its echoes, and the bosses felled in its boss fights, most often first.
    private static SessionSums Session() {
        if (_sessionVersion == History.Version && _session is { } known) {
            return known;
        }

        _sessionVersion = History.Version;
        var echoes = History.Records.Where(record => record.Session && record.Kind == ZoneKind.Echo).ToList();
        var bosses = new Totals(History.Records.Where(record => record.Session)).Bosses.Values
            .Where(boss => boss.Endgame)
            .OrderByDescending(boss => boss.Kills)
            .ThenBy(boss => boss.Name, StringComparer.CurrentCulture)
            .ToList();
        var sums = new SessionSums(echoes.Count, echoes.Sum(record => record.Seconds), echoes.Sum(record => record.Kills.Total), bosses);
        _session = sums;
        return sums;
    }

    // Unpinned: a press on the grip resizes, anywhere else on the panel moves it; the caption controls work in both states.
    // The debrief lies over the panel, so a press on it is the debrief's.
    private static void Input() {
        Event current = Event.current;
        if (current == null) {
            return;
        }

        Vector2 mouse = current.mousePosition;
        if (current.type == EventType.MouseDown && Debrief.Contains(mouse)) {
            return;
        }

        switch (current.type) {
            case EventType.MouseDown when current.button == 0 && _pin.Contains(mouse):
                Settings.Pinned = !Settings.Pinned;
                current.Use();
                break;
            case EventType.MouseDown when current.button == 0 && _debrief.Contains(mouse):
                Debrief.Toggle();
                current.Use();
                break;
            case EventType.MouseDown when current.button == 0 && !Settings.Pinned && _rect.Contains(mouse):
                _gesture = _grip.Contains(mouse) ? Gesture.Resize : Gesture.Move;
                _gestureMouse = mouse;
                _gestureStart = new Vector2(_rect.x, _rect.y);
                _gestureScale = Settings.Scale;
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
        Settings.SavePlacement(position, _liveScale > 0f ? _liveScale : Settings.Scale);
        _liveScale = -1f;
        _livePosition = new Vector2(-1f, -1f);
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
                Ui.Fill(new Rect(x, y, dot, dot), color);
            }
        }
    }

    private readonly record struct SessionSums(int Echoes, double Seconds, int Kills, List<BossTally> Bosses);
}
