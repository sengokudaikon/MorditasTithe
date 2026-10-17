# Using Morditas' Tithe

Morditas' Tithe counts your kills and times each zone, with per-echo stats in the Monolith, a record of every boss you
slay, and a debrief that adds it all up. It only reads the game: it changes no gameplay.

## Requirements

- Last Epoch 1.5.
- [MelonLoader](https://github.com/LavaGang/MelonLoader) 0.7.x installed into the game. Run the game once after
  installing it so MelonLoader generates its assemblies.

## Install

Eterra's Gift's Mods page can build and install the mod for you, and shows its settings.

To build it yourself you need the .NET 8 SDK or later:

```
dotnet build src/MorditasTithe.csproj -c Release
```

The build looks for Last Epoch in Steam's default folder, `C:\Program Files (x86)\Steam\steamapps\common\Last Epoch`.
If yours is elsewhere, set the `LAST_EPOCH_PATH` environment variable to it or add `-p:GamePath="<Last Epoch folder>"`;
the build says so when it cannot find the game. Copy `src/bin/Release/net6.0/MorditasTithe.dll` into the game's `Mods`
folder, or add `-p:DeployToGame=true` to the build to copy it there. Close the game first: a running game holds the DLL.

Start the game. `MelonLoader/Latest.log` lists Morditas' Tithe and says whether the panel is shown.

## The panel

**F9** shows or hides the panel. It shows the zone under way: its time, kills and kills a minute, then magic, rare,
champion and boss kills in the game's rarity colours, and minibosses, loot lizards and deaths once there are any.

- **In an echo**, the timeline heads the panel, with the corruption under it and then the zone. It shows when the
  objective was done, the last echo, and the session's average echo time and kills.
- **In a boss fight** (see [What counts](#what-counts)), the panel is headed by the boss once it falls, and the fight is
  timed to its fall. The session line lists the bosses felled in boss fights.
- **In a hub** (towns, the End of Time, Traveler's Rest), nothing is timed or counted. The panel shows the last echo in
  full and the session.

The panel scales with the screen height, so it reads the same at 1080p, 1440p and 4K. Drag it anywhere to move it, and
drag the grip in its bottom-right corner to resize it. **PIN** in its top-right corner pins it: it stays put, the grip
goes, and clicks on it reach the game. Only **PINNED** still takes a click, to unpin it. The position and size are kept
as shares of the screen, so they survive a change of resolution.

## The debrief

**F10**, or **DEBRIEF** on the panel, opens the debrief in the middle of the screen. **SESSION** and **ALL TIME** switch
between this session's zones and everything recorded; **CLOSE** or F10 closes it. It counts zones you have left, not the
one under way.

- **Echoes**: how many, their time and objective time, the fastest objective, kills by kind, the most kills in one echo,
  the highest corruption, deaths and the timelines you ran.
- **Bosses**: the boss fights, then every boss slain by name, the endgame bosses first with their fastest kill, then those
  felled elsewhere.

## What counts

- **A kill** is anything hostile to you that dies, whoever killed it: you, your minions, or an enemy's own explosion. That
  includes what enemies summon. Expired minions and enemy totems do not count. Each kill has one kind, the most notable
  first: loot lizard, boss, miniboss, champion, rare, magic, normal.
- **A boss** is one the game itself makes a boss (not a miniboss). Each is named once a zone, even if it rises again for
  another phase.
- **A zone** is one load of a scene: going back into the same zone starts a new one. Time runs while the game does; a
  paused game does not count.
- **An echo** is a Monolith echo. Going back into an echo whose objective is already done is not a new echo.
- **A boss fight** is its own kind of zone, kept out of the echo stats: a timeline's boss, the Shade (Echo of a World),
  pinnacle scenes, and the boss woven echoes (the endgame bosses, Uber Aberroth, the Shade's pinnacle, the Harbingers, the
  boss rush, Morditas and Uber Morditas).

## Settings

Settings live in `UserData/MelonPreferences.cfg` in the game folder, under `[MorditasTithe]`.

| Setting | Default | What it does |
| --- | --- | --- |
| `Shown` | true | Shows the panel. F9 toggles it in game. |
| `Pinned` | false | Pins the panel: it stays put and clicks pass through it. |
| `Opacity` | 0.8 | Panel background opacity, 0 to 1. |
| `RecordHistory` | true | Records each zone you leave. Off, nothing is written or read, and the debrief shows the session only. |
| `Left`, `Top`, `Scale` | -1, -1, 1 | What dragging the panel and its grip set. -1, -1 and 1 put the panel back where it starts. |

## History file

Each zone you leave is appended to `UserData/MorditasTithe/history.jsonl`, one JSON object per line: `left`, `kind`
(`echo`, `boss` or `combat`), `character`, `scene`, `zone`, `timeline`, `corruption`, `empowered`, `seconds`,
`objectiveSeconds` (null if the objective was not done), `kills`, `normal`, `magic`, `rare`, `champion`, `miniboss`,
`boss`, `lizard`, `deaths`, and `bosses` (each `name` and `seconds` into the zone).

Lines from before 0.4.0 have no `kind` and count bosses and minibosses together under `boss`. The debrief reads them as
echoes, except Echo of a World (a boss fight) and the Traveler's Rest and re-entered-echo lines that 0.2.0 and 0.3.x wrote
by mistake, which it drops.

## Troubleshooting

- **No panel**: press F9 with the game focused, and check that `Latest.log` lists Morditas' Tithe. The panel shows only in
  game with a character.
- **The panel and debrief vanish**: if the mod hits an error, it stops and logs one line starting with `Morditas' Tithe
  stopped`. Restart the game to try again, and include that line when you report the problem.
- **MelonLoader loads no mods after a game update**: on Last Epoch 1.5, MelonLoader can generate a broken
  `UnityEngine.CoreModule` ([MelonLoader #1218](https://github.com/LavaGang/MelonLoader/issues/1218)). Eterra's Gift
  repairs it automatically; otherwise use a CoreModule fix until MelonLoader ships one.

## License

GNU AGPL 3.0 or later, with a permission for combining the mod with the game. Eleventh Hour Games may use the code in
Last Epoch without the AGPL's conditions. See [NOTICE.md](NOTICE.md) and [LICENSE](LICENSE).
