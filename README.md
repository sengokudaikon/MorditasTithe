# Echo Tracker

A MelonLoader mod for Last Epoch 1.5 that counts your kills and times each zone, with per-echo stats in the Monolith.

- A panel (top left, F9 toggles) shows the zone under way: its time, kills and kills per minute, then magic, rare,
  champion and boss kills in the game's rarity colours, and loot lizards and deaths once there are any.
- In an echo it also shows the timeline, the corruption and when the objective was done, plus the last echo and the
  session's average echo time and kills.
- Each echo you leave is appended to `UserData/EchoTracker/history.jsonl`, one JSON object per line:
  `left`, `character`, `scene`, `timeline`, `corruption`, `empowered`, `seconds`, `objectiveSeconds` (null if the
  objective was not done), `kills`, `normal`, `magic`, `rare`, `champion`, `boss`, `lizard`, `deaths`.

## What counts

Kills come from the game's own death event (`ActorManager.actorDiedEvent`), which fires once for every actor that dies,
whoever killed it: you, your minions, or an enemy's own explosion. Anything hostile to you counts, including what enemies
summon. Unsummoned actors (expired minions) and enemy totems do not. Each kill has one kind, the most notable first: loot
lizard, boss (bosses and minibosses), champion, rare, magic, normal.

A zone is one load of a scene: re-entering the same zone starts a new one. Time runs while the game does; a paused game
(time scale 0) does not count. An echo is a zone with a `MonolithZoneManager`; the timeline and corruption come from the
character's current Monolith run, and the objective time from the zone manager's `objectiveComplete`.

## Settings

`UserData/MelonPreferences.cfg`, category `EchoTracker`: `Shown` (default true), `RecordHistory` (default true), `Left`
and `Top` (panel position in pixels, default 16 and 160), `Width` (default 330), `FontSize` (default 14), `Opacity`
(background, default 0.6).

## Build

Same as the other mods: the .NET SDK and a Last Epoch install where MelonLoader 0.7.x has generated its assemblies (with
the 1.5 CoreModule repair).

```
dotnet build src/EchoTracker.csproj -c Release
```

`-p:GamePath=...` or `LAST_EPOCH_PATH` overrides the game folder; `-p:DeployToGame=false` skips the copy into `Mods`.
