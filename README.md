# Morditas' Tithe

A MelonLoader mod for Last Epoch 1.5 that counts your kills and times each zone, with per-echo stats in the Monolith, a
record of every boss you slay, and a debrief that adds it all up.

- A panel (F9 shows or hides it) shows the zone under way: its time, kills and kills a minute, then magic, rare,
  champion and boss kills in the game's rarity colours, and minibosses, loot lizards and deaths once there are any.
- In an echo it is headed by the timeline, with the corruption under it and then the zone, and shows when the objective
  was done, plus the last echo and the session's average echo time and kills.
- Endgame boss fights are their own kind of zone, kept out of the echo stats: a timeline's boss and the Shade (Echo of a
  World), pinnacle scenes, and the boss woven echoes (the endgame bosses, Uber Aberroth, the Shade's pinnacle, the
  Harbingers, the boss rush, Morditas and Uber Morditas). The panel heads one with the boss once it falls and times the
  fight to its fall; the session line lists the bosses felled in them.
- Every boss the game makes a boss (not minibosses) is named as it dies, wherever it falls.
- Hubs (towns, the End of Time, Traveler's Rest: the game's Town and non-combat scenes and the Monolith rest zone) are not
  timed or counted and never count as an echo; there the panel shows the last echo in full and the session. Going back
  into an echo whose objective is already done is not a new echo either.
- **The debrief** (F10, or **DEBRIEF** on the panel) adds up this session's zones or everything recorded (**SESSION** /
  **ALL TIME**): echoes, their time and objective time, the fastest objective, kills by kind, the most kills in one echo,
  the highest corruption, deaths and timelines; then the boss fights and every boss slain, by name, the endgame bosses
  first with their fastest kill. It counts zones you have left, not the one under way.
- Each zone you leave is appended to `UserData/MorditasTithe/history.jsonl`, one JSON object per line: `left`, `kind`
  (`echo`, `boss` or `combat`), `character`, `scene`, `zone`, `timeline`, `corruption`, `empowered`, `seconds`,
  `objectiveSeconds` (null if the objective was not done), `kills`, `normal`, `magic`, `rare`, `champion`, `miniboss`,
  `boss`, `lizard`, `deaths`, and `bosses` (each `name` and `seconds` into the zone). Lines from before 0.4.0 have no
  `kind` and count bosses and minibosses together under `boss`; the debrief reads them as echoes, except Echo of a World
  (a boss fight) and the Traveler's Rest and re-entered-echo lines 0.2.0 and 0.3.x wrote by mistake, which it drops.

## The panel

It is sized from the screen height, so it reads the same at 1080p, 1440p and 4K. Unpinned, drag it anywhere to move it
and drag the grip in its bottom-right corner to resize it. **PIN** in its top-right corner pins it: it stays put, the
grip goes, and clicks on it reach the game; only **PINNED** still takes a click, to unpin it. Position and size are kept
as shares of the screen, so they survive a change of resolution. **DEBRIEF** beside it opens the debrief, which sits in
the middle of the screen and takes every click on it until **CLOSE** or F10.

While it takes clicks, the area carries the game's own `UIMouseListener` on an invisible uGUI rectangle, so the game
treats it as UI and a click on the panel does not walk your character there. Text uses the game's Noto Sans and
LastEpoch faces, taken from its TextMeshPro font assets once it has loaded them (the log names what it found).

## What counts

A kill is counted as the actor enters its dying state (a Harmony postfix on `Dying.onEnter`), whoever killed it: you,
your minions, or an enemy's own explosion. (The game's death event, `ActorManager.actorDiedEvent`, saw none of them in
play.) Anything hostile to you counts, including what enemies summon. Unsummoned actors (expired minions) and enemy totems
do not. Each kill has one kind, the most notable first: loot lizard, boss, miniboss, champion, rare, magic, normal. Boss and miniboss are the actor's own data (`isBoss`,
`isMiniboss`); a boss is named once a zone by its localized name (`ActorData.GetLocalizedActorName`), even if it goes down
again for another phase.

A zone is one load of a scene: re-entering the same zone starts a new one. Time runs while the game does; a paused game
(time scale 0) does not count. What kind of zone it is comes from the game's scene list
(`SceneList.GetSceneDetails`, `IsNonCombatZone`, `IsMonolithRestZone`, `IsMonolithEcho`); the timeline and corruption
come from the character's current Monolith run, and the objective time from that scene's own `MonolithZoneManager`. A boss
fight is known by that manager (`isTimelineBossEncounter`, a `Shade` objective), by the woven echo's own
`WovenEchoZoneManager.wovenEchoType`, or by the scene's `Pinnacle` type; the log names what each echo turned out to be.

## Settings

`UserData/MelonPreferences.cfg`, category `MorditasTithe`: `Shown` (default true), `Pinned` (default false),
`Opacity` (background, default 0.8), `RecordHistory` (default true; off, nothing is written or read, and the debrief has
the session only). `Left`, `Top` and `Scale` are what dragging and the grip set; `-1`, `-1` and `1` put the panel back
where it starts.

## Build

Same as the other mods: the .NET SDK and a Last Epoch install where MelonLoader 0.7.x has generated its assemblies (with
the 1.5 CoreModule repair).

```
dotnet build src/MorditasTithe.csproj -c Release
```

`-p:GamePath=...` or `LAST_EPOCH_PATH` overrides the game folder. Eterra's Blessing's Mods page builds and installs it from
these sources; `-p:DeployToGame=true` copies a build straight into the game's `Mods` folder instead.
