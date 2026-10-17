using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json;
using MelonLoader;
using MelonLoader.Utils;

namespace MorditasTithe;

// Every zone left this session, and with history on every one recorded before, from UserData/MorditasTithe/history.jsonl:
// one JSON object per line. Lines from before 0.4.0 have no kind (they were all echoes), count bosses and minibosses
// together under "boss" and name no bosses. Those are read as what they were: Traveler's Rest (M_Rest, which 0.2.0 took
// for an echo) and a finished echo gone back into (its objective at 0:00) are dropped, and Echo of a World (M_Shade, the
// Shade's fight) is a boss fight.
internal static class History {
    private static readonly List<ZoneRecord> _records = [];

    internal static IReadOnlyList<ZoneRecord> Records => _records;

    // Bumps on every change, so what is worked out from the records is worked out again.
    internal static int Version { get; private set; }

    private static string FilePath => Path.Combine(MelonEnvironment.UserDataDirectory, "MorditasTithe", "history.jsonl");

    internal static void Load() {
        try {
            if (!File.Exists(FilePath)) {
                return;
            }

            int skipped = 0;
            foreach (string line in File.ReadLines(FilePath)) {
                if (string.IsNullOrWhiteSpace(line)) {
                    continue;
                }

                try {
                    ZoneRecord record = Parse(line);
                    if (!(record.Legacy && (record.Scene == "M_Rest" || record.ObjectiveSeconds == 0d))) {
                        _records.Add(record);
                    }
                } catch (Exception exception) when (exception is JsonException or InvalidOperationException or FormatException) {
                    skipped++;
                }
            }

            Version++;
            MelonLogger.Msg($"History: {_records.Count} zones{(skipped > 0 ? $", {skipped} unreadable lines skipped" : string.Empty)}");
        } catch (Exception exception) {
            MelonLogger.Warning($"Morditas' Tithe could not read its history: {exception.Message}");
        }
    }

    internal static void Add(ZoneRun run, bool persist) {
        var record = new ZoneRecord {
            Session = true,
            Left = DateTimeOffset.Now,
            Kind = run.Kind,
            Character = run.Character,
            Scene = run.Scene,
            Zone = run.ZoneName,
            Timeline = run.Timeline,
            Corruption = run.Corruption,
            Empowered = run.Empowered,
            Seconds = Math.Round(run.Seconds, 1),
            ObjectiveSeconds = run.ObjectiveSeconds is { } objective ? Math.Round(objective, 1) : null,
            Deaths = run.Deaths,
            Bosses = run.Bosses.Select(boss => boss with { Seconds = Math.Round(boss.Seconds, 1) }).ToList(),
        };
        record.Kills.Add(run.Kills);
        _records.Add(record);
        Version++;
        if (!persist) {
            return;
        }

        try {
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
            File.AppendAllText(FilePath, Serialize(record) + "\n");
        } catch (Exception exception) {
            MelonLogger.Warning($"Morditas' Tithe could not record the zone: {exception.Message}");
        }
    }

    private static string Serialize(ZoneRecord record) {
        return JsonSerializer.Serialize(new {
            left = record.Left.ToString("o", CultureInfo.InvariantCulture),
            kind = record.Kind.ToString().ToLowerInvariant(),
            character = record.Character,
            scene = record.Scene,
            zone = record.Zone,
            timeline = record.Timeline,
            corruption = record.Corruption,
            empowered = record.Empowered,
            seconds = record.Seconds,
            objectiveSeconds = record.ObjectiveSeconds,
            kills = record.Kills.Total,
            normal = record.Kills[KillKind.Normal],
            magic = record.Kills[KillKind.Magic],
            rare = record.Kills[KillKind.Rare],
            champion = record.Kills[KillKind.Champion],
            miniboss = record.Kills[KillKind.Miniboss],
            boss = record.Kills[KillKind.Boss],
            lizard = record.Kills[KillKind.Lizard],
            deaths = record.Deaths,
            bosses = record.Bosses.Select(boss => new { name = boss.Name, seconds = boss.Seconds }),
        });
    }

    private static ZoneRecord Parse(string line) {
        using JsonDocument document = JsonDocument.Parse(line);
        JsonElement root = document.RootElement;
        string kind = Text(root, "kind");
        string timeline = Text(root, "timeline");
        bool hasTimeline = !string.IsNullOrEmpty(timeline);
        var record = new ZoneRecord {
            Legacy = kind == null,
            Kind = kind != null ? Enum.Parse<ZoneKind>(kind, ignoreCase: true) : Text(root, "scene") == "M_Shade" ? ZoneKind.Boss : ZoneKind.Echo,
            Left = Text(root, "left") is { } left ? DateTimeOffset.Parse(left, CultureInfo.InvariantCulture) : DateTimeOffset.MinValue,
            Character = Text(root, "character"),
            Scene = Text(root, "scene"),
            Zone = Text(root, "zone"),
            Timeline = hasTimeline ? timeline : null,
            Corruption = !hasTimeline ? null : Number(root, "corruption") is { } corruption ? (int)corruption : null,
            Empowered = root.TryGetProperty("empowered", out JsonElement empowered) && empowered.ValueKind == JsonValueKind.True,
            Seconds = Number(root, "seconds") ?? 0d,
            ObjectiveSeconds = Number(root, "objectiveSeconds"),
            Deaths = (int)(Number(root, "deaths") ?? 0d),
        };
        foreach (KillKind killKind in Enum.GetValues<KillKind>()) {
            record.Kills[killKind] = (int)(Number(root, killKind.ToString().ToLowerInvariant()) ?? 0d);
        }

        if (root.TryGetProperty("bosses", out JsonElement bosses) && bosses.ValueKind == JsonValueKind.Array) {
            foreach (JsonElement boss in bosses.EnumerateArray()) {
                if (Text(boss, "name") is { } name) {
                    record.Bosses.Add(new BossKill(name, Number(boss, "seconds") ?? 0d));
                }
            }
        }

        return record;
    }

    private static string Text(JsonElement element, string name) {
        return element.TryGetProperty(name, out JsonElement value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;
    }

    private static double? Number(JsonElement element, string name) {
        return element.TryGetProperty(name, out JsonElement value) && value.ValueKind == JsonValueKind.Number ? value.GetDouble() : null;
    }
}

internal sealed class ZoneRecord {
    public bool Session { get; init; }
    public bool Legacy { get; init; }
    public DateTimeOffset Left { get; init; }
    public ZoneKind Kind { get; init; }
    public string Character { get; init; }
    public string Scene { get; init; }
    public string Zone { get; init; }
    public string Timeline { get; init; }
    public int? Corruption { get; init; }
    public bool Empowered { get; init; }
    public double Seconds { get; init; }
    public double? ObjectiveSeconds { get; init; }
    public KillCounts Kills { get; } = new();
    public int Deaths { get; init; }
    public List<BossKill> Bosses { get; init; } = [];
}

// What a set of records adds up to: the echoes, the boss fights and every boss slain, by name.
internal sealed class Totals {
    public Totals(IEnumerable<ZoneRecord> records) {
        foreach (ZoneRecord record in records) {
            Zones++;
            if (record.Character != null) {
                Characters.Add(record.Character);
            }

            Legacy |= record.Legacy;
            foreach (BossKill boss in record.Bosses) {
                if (!Bosses.TryGetValue(boss.Name, out BossTally tally)) {
                    tally = new BossTally(boss.Name);
                    Bosses[boss.Name] = tally;
                }

                tally.Kills++;
                // A kill's time is the time into its zone: the fight's length only where the zone is the fight.
                if (record.Kind == ZoneKind.Boss) {
                    tally.Endgame = true;
                    tally.Fastest = Math.Min(tally.Fastest ?? double.MaxValue, boss.Seconds);
                }
            }

            switch (record.Kind) {
                case ZoneKind.Echo:
                    Echoes++;
                    EchoSeconds += record.Seconds;
                    EchoKills += record.Kills.Total;
                    EchoDeaths += record.Deaths;
                    Kills.Add(record.Kills);

                    if (record.ObjectiveSeconds is { } objective) {
                        Objectives++;
                        ObjectiveSeconds += objective;
                        if (FastestObjective == null || objective < FastestObjective.ObjectiveSeconds) {
                            FastestObjective = record;
                        }
                    }

                    if (MostKills == null || record.Kills.Total > MostKills.Kills.Total) {
                        MostKills = record;
                    }

                    if (record.Corruption is { } corruption && corruption > HighestCorruption) {
                        HighestCorruption = corruption;
                    }

                    if (!string.IsNullOrEmpty(record.Timeline)) {
                        Timelines[record.Timeline] = Timelines.GetValueOrDefault(record.Timeline) + 1;
                    }

                    break;
                case ZoneKind.Boss:
                    BossFights++;
                    BossFightSeconds += record.Seconds;
                    BossFightDeaths += record.Deaths;
                    break;
                default:
                    OtherZones++;
                    OtherKills += record.Kills.Total;
                    break;
            }
        }
    }

    public int Zones { get; }
    public HashSet<string> Characters { get; } = [];
    public bool Legacy { get; }
    public int Echoes { get; }
    public double EchoSeconds { get; }
    public int EchoKills { get; }
    public int EchoDeaths { get; }
    public KillCounts Kills { get; } = new();
    public int Objectives { get; }
    public double ObjectiveSeconds { get; }
    public ZoneRecord FastestObjective { get; }
    public ZoneRecord MostKills { get; }
    public int HighestCorruption { get; }
    public Dictionary<string, int> Timelines { get; } = [];
    public int BossFights { get; }
    public double BossFightSeconds { get; }
    public int BossFightDeaths { get; }
    public Dictionary<string, BossTally> Bosses { get; } = [];
    public int OtherZones { get; }
    public int OtherKills { get; }
}

internal sealed class BossTally(string name) {
    public string Name { get; } = name;
    public int Kills { get; set; }
    public bool Endgame { get; set; }
    public double? Fastest { get; set; }
}
