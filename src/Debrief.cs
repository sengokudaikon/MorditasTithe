using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using UnityEngine;

namespace MorditasTithe;

// The debrief (F10, or DEBRIEF on the panel): what the session's zones, or every zone recorded, add up to. Echoes on the
// left; bosses on the right, the endgame fights' bosses first with their fastest kill, then those felled elsewhere. It
// sits in the middle of the screen over everything and takes every click on it; it counts only zones already left.
internal static class Debrief {
    private const int _clickSlot = 1;
    private const string _name = "MORDITAS' TITHE · DEBRIEF";

    private static bool _open;
    private static bool _allTime;
    private static Rect _rect;
    private static Rect _session;
    private static Rect _everything;
    private static Rect _close;

    private static int _builtVersion = -1;
    private static bool _builtAllTime;
    private static Styles _builtStyles;
    private static string _scope;
    private static Row[] _echoes;
    private static Row[] _bosses;
    private static float _echoesWidth;
    private static float _bossesWidth;

    internal static bool Open => _open && MorditasTitheMod.InGame;

    // Opens on the session once it has a zone to show, on everything recorded until then.
    internal static void Toggle() {
        _open = !_open;
        if (_open) {
            _allTime = !History.Records.Any(record => record.Session);
        }
    }

    internal static bool Contains(Vector2 mouse) {
        return Open && _rect.Contains(mouse);
    }

    internal static void Update() {
        if (Open && _rect.width > 0f) {
            Ui.ClickArea(_clickSlot, _rect);
        } else {
            Ui.NoClickArea(_clickSlot);
        }
    }

    internal static void Draw() {
        if (!Open) {
            return;
        }

        float unit = Screen.height / 1080f * Math.Clamp(MorditasTitheMod.Scale, 1f, 1.5f);
        Styles styles = Ui.StylesFor(unit);
        if (_builtVersion != History.Version || _builtAllTime != _allTime || _builtStyles != styles) {
            Build(styles);
        }

        float padding = 24f * unit;
        float gap = 6f * unit;
        float gutter = 40f * unit;
        float captionHeight = styles.Caption.fontSize * 1.4f;
        float scopeHeight = styles.Height(styles.Note);
        float echoesWidth = Math.Max(_echoesWidth, 340f * unit);
        float bossesWidth = Math.Max(_bossesWidth, 300f * unit);
        float columns = Math.Max(_echoes.Sum(row => styles.Height(row.Style)), _bosses.Sum(row => styles.Height(row.Style)));
        float width = padding * 2f + echoesWidth + gutter + bossesWidth;
        float height = padding * 2f + captionHeight + gap + scopeHeight + gap * 3f + columns;
        _rect = new Rect(Math.Max(0f, (Screen.width - width) / 2f), Math.Max(0f, (Screen.height - height) / 2f), width, height);

        float controlHeight = captionHeight + 4f * unit;
        float closeWidth = Ui.Width("CLOSE", styles.Control) + 14f * unit;
        float everythingWidth = Ui.Width("ALL TIME", styles.Control) + 14f * unit;
        float sessionWidth = Ui.Width("SESSION", styles.Control) + 14f * unit;
        float controlsTop = _rect.y + padding - 2f * unit;
        _close = new Rect(_rect.xMax - padding / 2f - closeWidth, controlsTop, closeWidth, controlHeight);
        _everything = new Rect(_close.x - 18f * unit - everythingWidth, controlsTop, everythingWidth, controlHeight);
        _session = new Rect(_everything.x - 4f * unit - sessionWidth, controlsTop, sessionWidth, controlHeight);

        Input();

        Ui.Fill(_rect, new Color(Ui.Ground.r, Ui.Ground.g, Ui.Ground.b, 0.96f));
        Ui.Fill(new Rect(_rect.x, _rect.y, _rect.width, Math.Max(2f, 3f * unit)), Ui.Blood);
        float top = _rect.y + padding;
        float left = _rect.x + padding;
        Ui.Text(new Rect(left, top, width, captionHeight), _name, styles.Caption);
        Ui.Control(_session, "SESSION", !_allTime, styles.Control, unit);
        Ui.Control(_everything, "ALL TIME", _allTime, styles.Control, unit);
        Ui.Control(_close, "CLOSE", false, styles.Control, unit);
        top += captionHeight + gap;
        Ui.Text(new Rect(left, top, width, scopeHeight), _scope, styles.Note);
        top += scopeHeight + gap;
        Ui.Fill(new Rect(left, top + gap, width - padding * 2f, Math.Max(1f, unit)), Ui.Rule);
        top += gap * 2f;
        Column(_echoes, left, top, echoesWidth, styles, unit);
        float rule = left + echoesWidth + gutter / 2f;
        Ui.Fill(new Rect(rule, top + gap, Math.Max(1f, unit), columns - gap), Ui.Rule);
        Column(_bosses, left + echoesWidth + gutter, top, bossesWidth, styles, unit);
    }

    private static void Column(Row[] rows, float left, float top, float width, Styles styles, float unit) {
        foreach (Row row in rows) {
            float rowHeight = styles.Height(row.Style);
            if (row.Style == null) {
                Ui.Fill(new Rect(left, top + 4f * unit, width, Math.Max(1f, unit)), Ui.Rule);
            } else {
                Ui.Text(new Rect(left, top, width, rowHeight), row.Text, row.Style);
            }

            top += rowHeight;
        }
    }

    // Every press on the debrief is its own: the controls act, the rest only keeps the click from the game and the panel.
    private static void Input() {
        Event current = Event.current;
        if (current == null || current.type != EventType.MouseDown || !_rect.Contains(current.mousePosition)) {
            return;
        }

        Vector2 mouse = current.mousePosition;
        if (current.button == 0) {
            if (_close.Contains(mouse)) {
                _open = false;
            } else if (_session.Contains(mouse)) {
                _allTime = false;
            } else if (_everything.Contains(mouse)) {
                _allTime = true;
            }
        }

        current.Use();
    }

    private static void Build(Styles styles) {
        _builtVersion = History.Version;
        _builtAllTime = _allTime;
        _builtStyles = styles;
        var totals = new Totals(History.Records.Where(record => _allTime || record.Session));
        string characters = totals.Characters.Count switch {
            0 => string.Empty,
            1 => $" · {totals.Characters.First()}",
            _ => $" · {totals.Characters.Count} characters",
        };
        string zones = $"{Ui.Count(totals.Zones)} {(totals.Zones == 1 ? "zone" : "zones")}";
        _scope = _allTime ? $"Everything recorded · {zones}{characters}" : $"Zones left this session · {zones}{characters}";
        _echoes = EchoRows(totals, styles);
        _bosses = BossRows(totals, styles);
        _echoesWidth = Widest(_echoes);
        _bossesWidth = Widest(_bosses);
    }

    private static float Widest(Row[] rows) {
        return rows.Where(row => row.Style != null).Select(row => Ui.Width(row.Text, row.Style)).DefaultIfEmpty(0f).Max();
    }

    private static Row[] EchoRows(Totals totals, Styles styles) {
        var rows = new List<Row> { new("Echoes", styles.Heading) };
        int echoes = totals.Echoes;
        if (echoes == 0) {
            rows.Add(new Row("No echoes yet", styles.Note));
            OtherZones(rows, totals, styles);
            return rows.ToArray();
        }

        rows.Add(new Row($"{Ui.Count(echoes)} {(echoes == 1 ? "echo" : "echoes")}", styles.Figure));
        rows.Add(new Row($"{Ui.Clock(totals.EchoSeconds)} in all · {Ui.Clock(totals.EchoSeconds / echoes)} each", styles.Note));
        if (totals.Objectives > 0) {
            rows.Add(new Row($"Objective in {Ui.Clock(totals.ObjectiveSeconds / totals.Objectives)} on average", styles.Body));
            ZoneRecord fastest = totals.FastestObjective;
            rows.Add(new Row($"Fastest {Ui.Clock(fastest.ObjectiveSeconds ?? 0d)} · {Where(fastest)}", styles.Note));
        }

        rows.Add(new Row(null, null));
        double minutes = totals.EchoSeconds / 60d;
        string rate = minutes > 0d ? $" · {totals.EchoKills / minutes:0} a minute" : string.Empty;
        rows.Add(new Row($"<b>{Ui.Count(totals.EchoKills)}</b> kills · {Ui.Count((double)totals.EchoKills / echoes)} each{rate}", styles.Body));
        int[] kills = totals.Kills;
        rows.Add(new Row($"{Ui.Tint($"Magic {Ui.Count(kills[(int)KillKind.Magic])}", Ui.Magic)}   {Ui.Tint($"Rare {Ui.Count(kills[(int)KillKind.Rare])}", Ui.Rare)}   "
            + Ui.Tint($"Champion {Ui.Count(kills[(int)KillKind.Champion])}", Ui.Unique), styles.Body));
        // Records from before 0.4.0 counted bosses and minibosses together, so the two are shown together.
        string lizards = kills[(int)KillKind.Lizard] > 0 ? $"   Lizards {Ui.Count(kills[(int)KillKind.Lizard])}" : string.Empty;
        rows.Add(new Row($"{Ui.Tint($"Bosses and minibosses {Ui.Count(kills[(int)KillKind.Boss] + kills[(int)KillKind.Miniboss])}", Ui.Unique)}{lizards}", styles.Body));
        rows.Add(new Row($"Most in one echo {Ui.Count(totals.MostKills.TotalKills)} · {Where(totals.MostKills)}", styles.Note));
        rows.Add(new Row(null, null));
        if (totals.HighestCorruption > 0) {
            rows.Add(new Row($"Highest corruption {Ui.Tint($"<b>{totals.HighestCorruption.ToString(CultureInfo.InvariantCulture)}</b>", Ui.Blood)}", styles.Body));
        }

        rows.Add(new Row(totals.EchoDeaths > 0 ? Ui.Tint($"Deaths {Ui.Count(totals.EchoDeaths)}", Ui.Blood) : "No deaths", styles.Body));
        if (totals.Timelines.Count > 0) {
            const int Shown = 5;
            var timelines = totals.Timelines.OrderByDescending(pair => pair.Value).ThenBy(pair => pair.Key, StringComparer.CurrentCulture).ToList();
            string named = string.Join(" · ", timelines.Take(Shown).Select(pair => $"{pair.Key} {Ui.Count(pair.Value)}"));
            string more = timelines.Count > Shown ? $" · {timelines.Count - Shown} more" : string.Empty;
            rows.Add(new Row($"{named}{more}", styles.Note));
        }

        OtherZones(rows, totals, styles);
        return rows.ToArray();
    }

    private static void OtherZones(List<Row> rows, Totals totals, Styles styles) {
        if (totals.OtherZones == 0) {
            return;
        }

        rows.Add(new Row(null, null));
        rows.Add(new Row($"Other zones {Ui.Count(totals.OtherZones)} · {Ui.Count(totals.OtherKills)} kills", styles.Note));
    }

    private static Row[] BossRows(Totals totals, Styles styles) {
        var rows = new List<Row> { new("Bosses", styles.Heading) };
        if (totals.BossFights > 0) {
            rows.Add(new Row($"{Ui.Count(totals.BossFights)} boss {(totals.BossFights == 1 ? "fight" : "fights")}", styles.Figure));
            string deaths = totals.BossFightDeaths switch { 0 => "no deaths", 1 => "1 death", _ => $"{Ui.Count(totals.BossFightDeaths)} deaths" };
            rows.Add(new Row($"{Ui.Clock(totals.BossFightSeconds)} in all · {deaths}", styles.Note));
        }

        var slain = totals.Bosses.Values.OrderByDescending(boss => boss.Kills).ThenBy(boss => boss.Name, StringComparer.CurrentCulture).ToList();
        foreach (BossTally boss in slain.Where(boss => boss.Endgame)) {
            string fastest = boss.Fastest is { } seconds ? Ui.Tint($"   best {Ui.Clock(seconds)}", Ui.Muted) : string.Empty;
            rows.Add(new Row($"{Ui.Tint(boss.Name, Ui.Unique)}  ×{Ui.Count(boss.Kills)}{fastest}", styles.Body));
        }

        var elsewhere = slain.Where(boss => !boss.Endgame).ToList();
        if (elsewhere.Count > 0) {
            const int Shown = 8;
            rows.Add(new Row(null, null));
            rows.Add(new Row("Elsewhere", styles.Note));
            foreach (BossTally boss in elsewhere.Take(Shown)) {
                rows.Add(new Row($"{boss.Name}  ×{Ui.Count(boss.Kills)}", styles.Body));
            }

            if (elsewhere.Count > Shown) {
                rows.Add(new Row($"and {elsewhere.Count - Shown} more", styles.Note));
            }
        }

        if (slain.Count == 0) {
            rows.Add(new Row("No bosses slain yet", styles.Note));
        }

        if (totals.Legacy) {
            rows.Add(new Row(null, null));
            rows.Add(new Row("Echoes recorded before 0.4.0 name no bosses", styles.Note));
        }

        return rows.ToArray();
    }

    // Where a record was: its zone, and its timeline when it had one.
    private static string Where(ZoneRecord record) {
        string zone = record.Zone ?? record.Scene;
        return string.IsNullOrEmpty(record.Timeline) ? zone : $"{zone}, {record.Timeline}";
    }
}
