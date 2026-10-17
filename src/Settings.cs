using System;
using MelonLoader;
using UnityEngine;

namespace MorditasTithe;

// UserData/MelonPreferences.cfg, category MorditasTithe. ModSettings.json describes the keys Eterra's Gift shows; Left, Top
// and Scale are what dragging the panel and its corner grip set.
internal static class Settings {
    private static MelonPreferences_Category _category;
    private static MelonPreferences_Entry<bool> _shown;
    private static MelonPreferences_Entry<bool> _pinned;
    private static MelonPreferences_Entry<bool> _recordHistory;
    private static MelonPreferences_Entry<float> _left;
    private static MelonPreferences_Entry<float> _top;
    private static MelonPreferences_Entry<float> _scale;
    private static MelonPreferences_Entry<float> _opacity;

    public static void Load() {
        _category = MelonPreferences.CreateCategory("MorditasTithe");
        _shown = _category.CreateEntry("Shown", true, description: "Show the panel. F9 toggles in game.");
        _pinned = _category.CreateEntry("Pinned", false, description: "Pinned: the panel stays put and clicks pass through it.");
        _recordHistory = _category.CreateEntry("RecordHistory", true, description: "Append each zone to UserData/MorditasTithe/history.jsonl; the debrief adds it up.");
        _opacity = _category.CreateEntry("Opacity", 0.8f, description: "Panel background opacity, 0 to 1.");
        _left = _category.CreateEntry("Left", -1f, description: "Panel left edge as a share of the screen width; dragging sets it. -1 places it by default.");
        _top = _category.CreateEntry("Top", -1f, description: "Panel top edge as a share of the screen height; dragging sets it. -1 places it by default.");
        _scale = _category.CreateEntry("Scale", 1f, description: "Panel size; the corner grip sets it. 1 is the default at any resolution.");
    }

    public static bool Shown {
        get => _shown.Value;
        set => _shown.Value = value;
    }

    public static bool Pinned {
        get => _pinned.Value;
        set {
            _pinned.Value = value;
            _category.SaveToFile(false);
        }
    }

    public static bool RecordHistory => _recordHistory.Value;

    public static float Opacity => Math.Clamp(_opacity.Value, 0f, 1f);

    public static float Scale => Math.Clamp(_scale.Value, Panel.MinScale, Panel.MaxScale);

    // The saved position, or the default: on the left, a little above the middle of the screen.
    public static Vector2 Position => new(_left.Value < 0f ? 0.015f : _left.Value, _top.Value < 0f ? 0.22f : _top.Value);

    public static void SavePlacement(Vector2 position, float scale) {
        _left.Value = position.x;
        _top.Value = position.y;
        _scale.Value = scale;
        _category.SaveToFile(false);
    }
}
