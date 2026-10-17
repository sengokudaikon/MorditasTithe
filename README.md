# Morditas' Tithe

A MelonLoader mod for Last Epoch 1.5 that counts your kills and times each zone, with per-echo stats in the Monolith, a
record of every boss you slay, and a debrief that adds it all up. **F9** shows the panel, **F10** the debrief.

See [USAGE.md](USAGE.md) to install and use it. Licensed under the GNU AGPL 3.0 or later; see [NOTICE.md](NOTICE.md).
Unofficial, and not affiliated with Eleventh Hour Games.

## How it works

The code is split by concern: `Settings`, `Zones` (what zone this is and its run), `Kills` (what died), `History` (the
records and their totals), `Panel`, `Debrief` and `Ui` (what both draw with).

- **Kills**: a Harmony postfix on `Dying.onEnter` counts each actor as it enters its dying state. (The game's death event,
  `ActorManager.actorDiedEvent`, saw none of them in play.) Boss and miniboss are the actor's own data (`isBoss`,
  `isMiniboss`); a boss is named by `ActorData.GetLocalizedActorName`.
- **Zones**: a zone is one scene handle. Its kind comes from the game's scene list (`SceneList.GetSceneDetails`,
  `IsNonCombatZone`, `IsMonolithRestZone`, `IsMonolithEcho`), polled until the list has loaded. The timeline and
  corruption come from the character's current Monolith run, and the objective time from that scene's own
  `MonolithZoneManager`. A boss fight is known by that manager (`isTimelineBossEncounter`, a `Shade` objective), by the
  woven echo's own `WovenEchoZoneManager.wovenEchoType`, or by the scene's `Pinnacle` type; the log names what each echo
  turned out to be.
- **Clicks**: while the panel or debrief takes clicks, the area carries the game's own `UIMouseListener` on an invisible
  uGUI rectangle, so the game treats it as UI and a click on the panel does not walk your character there.
- **Fonts**: text uses the game's Noto Sans and LastEpoch faces, taken from its TextMeshPro font assets once it has
  loaded them; the log names what it found.
