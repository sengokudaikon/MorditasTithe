using System;
using MelonLoader;
using MorditasTithe;
using UnityEngine;

[assembly: MelonInfo(typeof(MorditasTitheMod), "Morditas' Tithe", "0.4.1", "sengokudaikon")]
[assembly: MelonGame("Eleventh Hour Games", "Last Epoch")]

namespace MorditasTithe;

// Counts the player's kills by kind and times each zone, with the echo's timeline, corruption and time to the objective in
// the Monolith, and names every boss slain. Endgame boss fights (a timeline's boss, the Shade, a pinnacle, a boss woven
// echo) are their own kind of zone, kept apart from the echoes. A panel shows the zone under way, the last echo and the
// session; the debrief (F10) adds up the session or everything recorded. Each zone left is appended to
// UserData/MorditasTithe/history.jsonl. By file: Settings, Zones (what zone this is and its run), Kills (what died),
// History (the records and their totals), Panel, Debrief and Ui (what both draw with).
public sealed class MorditasTitheMod : MelonMod {
    private static bool _panelKeyWasDown;
    private static bool _debriefKeyWasDown;
    private static bool _faulted;

    public override void OnInitializeMelon() {
        Settings.Load();
        if (Settings.RecordHistory) {
            History.Load();
        }

        LoggerInstance.Msg($"Morditas' Tithe panel {(Settings.Shown ? "shown" : "hidden")}; F9 toggles it, F10 the debrief");
    }

    internal static bool Faulted => _faulted;

    // In a game, with a character: the windows show only then.
    internal static bool InGame => !_faulted && Zones.Run is { HasPlayer: true };

    public override void OnUpdate() {
        const int VirtualKeyF9 = 0x78;
        const int VirtualKeyF10 = 0x79;
        // The keys are read from the whole system, so a press while another window has focus is not the game's.
        bool panelKey = Pressed(VirtualKeyF9, ref _panelKeyWasDown);
        bool debriefKey = Pressed(VirtualKeyF10, ref _debriefKeyWasDown);
        if (panelKey && Application.isFocused) {
            Settings.Shown = !Settings.Shown;
        }

        if (debriefKey && Application.isFocused) {
            Debrief.Toggle();
        }

        if (_faulted) {
            Ui.NoClickAreas();
            return;
        }

        try {
            Zones.Update();
            Panel.Update();
            Debrief.Update();
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
            Debrief.Draw();
        } catch (Exception exception) {
            Fault("stopped drawing", exception);
        }
    }

    public override void OnApplicationQuit() {
        Zones.Finish();
    }

    internal static void Fault(string what, Exception exception) {
        _faulted = true;
        Ui.NoClickAreas();
        MelonLogger.Error($"Morditas' Tithe {what}: {exception}");
    }

    private static bool Pressed(int virtualKey, ref bool wasDown) {
        bool down = (Ui.GetAsyncKeyState(virtualKey) & 0x8000) != 0;
        bool pressed = down && !wasDown;
        wasDown = down;
        return pressed;
    }
}
