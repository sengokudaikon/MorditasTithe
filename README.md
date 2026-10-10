# Morditas' Tithe

A MelonLoader mod for Last Epoch 1.5 that counts your kills and times each zone, with per-echo stats in the Monolith.

- A panel (F9 shows or hides it) shows the zone under way: its time, kills and kills a minute, then magic, rare,
  champion and boss kills in the game's rarity colours, and loot lizards and deaths once there are any.
- In an echo it also shows the timeline, the zone, the corruption and when the objective was done, plus the last echo
  and the session's average echo time and kills.
- Each echo you leave is appended to `UserData/MorditasTithe/history.jsonl`, one JSON object per line: `left`,
  `character`, `scene`, `zone`, `timeline`, `corruption`, `empowered`, `seconds`, `objectiveSeconds` (null if the
  objective was not done), `kills`, `normal`, `magic`, `rare`, `champion`, `boss`, `lizard`, `deaths`.

## The panel

It is sized from the screen height, so it reads the same at 1080p, 1440p and 4K. Unpinned, drag it anywhere to move it
and drag the grip in its bottom-right corner to resize it. **PIN** in its top-right corner pins it: it stays put, the
grip goes, and clicks on it reach the game; only **PINNED** still takes a click, to unpin it. Position and size are kept
as shares of the screen, so they survive a change of resolution.

While it takes clicks, the area carries the game's own `UIMouseListener` on an invisible uGUI rectangle, so the game
treats it as UI and a click on the panel does not walk your character there. Text uses the game's Noto Sans and
LastEpoch faces once the game has loaded them (the log names what it found).

## What counts

Kills come from the game's own death event (`ActorManager.actorDiedEvent`), which fires once for every actor that dies,
whoever killed it: you, your minions, or an enemy's own explosion. Anything hostile to you counts, including what enemies
summon. Unsummoned actors (expired minions) and enemy totems do not. Each kill has one kind, the most notable first: loot
lizard, boss (bosses and minibosses), champion, rare, magic, normal.

A zone is one load of a scene: re-entering the same zone starts a new one. Time runs while the game does; a paused game
(time scale 0) does not count. An echo is a zone with a `MonolithZoneManager`; the timeline and corruption come from the
character's current Monolith run, and the objective time from the zone manager's `objectiveComplete`.

## Settings

`UserData/MelonPreferences.cfg`, category `MorditasTithe`: `Shown` (default true), `Pinned` (default false),
`Opacity` (background, default 0.8), `RecordHistory` (default true). `Left`, `Top` and `Scale` are what dragging and the
grip set; `-1`, `-1` and `1` put the panel back where it starts.

## Build

Same as the other mods: the .NET SDK and a Last Epoch install where MelonLoader 0.7.x has generated its assemblies (with
the 1.5 CoreModule repair).

```
dotnet build src/MorditasTithe.csproj -c Release
```

`-p:GamePath=...` or `LAST_EPOCH_PATH` overrides the game folder; `-p:DeployToGame=false` skips the copy into `Mods`.
