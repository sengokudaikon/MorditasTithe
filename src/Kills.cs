using System;
using System.Linq;
using HarmonyLib;
using Il2Cpp;
using MelonLoader;

namespace MorditasTithe;

// What died in the zone under way: the player's deaths, and kills by kind, with every boss named.
internal static class Kills {
    internal static void OnDying(Dying dying) {
        ZoneRun run = Zones.Run;
        if (MorditasTitheMod.Faulted || run == null || run.Kind == ZoneKind.Hub || dying == null || dying.unsummoned) {
            return;
        }

        try {
            Actor dead = dying.getActor();
            Actor player = PlayerFinder.getPlayerActor();
            if (dead == null || player == null) {
                return;
            }

            if (dead.Pointer == player.Pointer) {
                run.Deaths++;
                return;
            }

            if (!IsKill(dead, player)) {
                return;
            }

            KillKind kind = Classify(dead);
            run.Kills[kind]++;
            // A boss is named once a zone, however many times it goes down (some rise again for another phase).
            if (kind == KillKind.Boss && run.BossActors.Add(dead.Pointer)) {
                var boss = new BossKill(BossName(dead), run.Seconds);
                run.Bosses.Add(boss);
                MelonLogger.Msg($"{boss.Name} slain at {Ui.Clock(boss.Seconds)} in {run.ZoneName ?? run.Scene}");
            }
        } catch (Exception exception) {
            MorditasTitheMod.Fault("stopped counting", exception);
        }
    }

    // Anything hostile to the player counts, including what enemies summon, as the game's own echo progress does; enemy
    // totems do not, nor (checked by the caller) anything unsummoned: a minion or totem that expired.
    private static bool IsKill(Actor dead, Actor player) {
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
}

// Dying.onEnter runs as any actor starts to die. It is virtual, so the game reaches it through the vtable and the detour
// sees every call.
[HarmonyPatch(typeof(Dying), nameof(Dying.onEnter))]
internal static class DyingOnEnterPatch {
    private static void Postfix(Dying __instance) {
        Kills.OnDying(__instance);
    }
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

// Kills by kind.
internal sealed class KillCounts {
    private readonly int[] _counts = new int[Enum.GetValues<KillKind>().Length];

    public int this[KillKind kind] {
        get => _counts[(int)kind];
        set => _counts[(int)kind] = value;
    }

    public int Total => _counts.Sum();

    public void Add(KillCounts other) {
        for (int kind = 0; kind < _counts.Length; kind++) {
            _counts[kind] += other._counts[kind];
        }
    }
}

internal readonly record struct BossKill(string Name, double Seconds);
