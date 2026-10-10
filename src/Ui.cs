using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using Il2Cpp;
using Il2CppInterop.Runtime;
using Il2CppTMPro;
using MelonLoader;
using UnityEngine;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace MorditasTithe;

// What the panel and the debrief draw with: IMGUI text and fills in the game's faces and colours, and the click areas that
// keep a click on them from reaching the game.
internal static class Ui {
    // Game-owned rarity colours (EpochColor MAGIC, RARE; UNIQUE for champions and bosses). The blood red is ours.
    internal static readonly Color Blood = new(0.80f, 0.16f, 0.20f, 1f);
    internal static readonly Color Magic = Hex(0x36A3E2);
    internal static readonly Color Rare = Hex(0xE3D157);
    internal static readonly Color Unique = Hex(0xEB730A);
    internal static readonly Color Muted = new(0.74f, 0.72f, 0.70f, 1f);
    internal static readonly Color Ground = new(0.04f, 0.035f, 0.035f, 1f);
    internal static readonly Color Rule = new(1f, 1f, 1f, 0.12f);
    private static readonly Regex _tags = new("<[^>]+>", RegexOptions.Compiled);

    private static readonly Dictionary<int, Styles> _styles = [];
    private static Font _bodyFont;
    private static Font _displayFont;
    private static int _fontSearches;
    private static float _nextFontSearch;

    private static GameObject _blockerRoot;
    private static readonly Dictionary<int, RectTransform> _areas = [];

    // The styles for one size unit (a share of 1080p), made once per size and again once the game's fonts are found.
    internal static Styles StylesFor(float unit) {
        FindFonts();
        int key = (int)MathF.Round(unit * 100f);
        if (!_styles.TryGetValue(key, out Styles styles)) {
            styles = new Styles(unit, _bodyFont, _displayFont);
            _styles[key] = styles;
        }

        return styles;
    }

    // A one-pixel shadow under every line keeps the text legible over bright ground.
    internal static void Text(Rect rect, string text, GUIStyle style) {
        Color color = style.normal.textColor;
        style.normal.textColor = new Color(0f, 0f, 0f, 0.85f);
        float offset = Math.Max(1f, Screen.height / 1080f);
        GUI.Label(new Rect(rect.x + offset, rect.y + offset, rect.width, rect.height), _tags.Replace(text, string.Empty), style);
        style.normal.textColor = color;
        GUI.Label(rect, text, style);
    }

    internal static void Fill(Rect rect, Color color) {
        Color previous = GUI.color;
        GUI.color = color;
        GUI.DrawTexture(rect, Texture2D.whiteTexture);
        GUI.color = previous;
    }

    // A caption-line control: muted, white under the pointer, blood red when it is on.
    internal static void Control(Rect rect, string label, bool on, GUIStyle style, float unit) {
        bool hover = rect.Contains(Event.current.mousePosition);
        if (hover) {
            Fill(rect, new Color(1f, 1f, 1f, 0.08f));
        }

        style.normal.textColor = on ? Blood : hover ? Color.white : Muted;
        Text(new Rect(rect.x, rect.y + 2f * unit, rect.width, rect.height), label, style);
    }

    internal static float Width(string text, GUIStyle style) {
        return style.CalcSize(new GUIContent(text)).x;
    }

    // The area that takes clicks, mirrored by an invisible uGUI rectangle carrying the game's own UIMouseListener, so the
    // game treats it as UI and does not walk the character to a click on it. Each window has its own slot.
    internal static void ClickArea(int slot, Rect area) {
        EnsureBlocker();
        _blockerRoot.SetActive(true);
        if (!_areas.TryGetValue(slot, out RectTransform rect)) {
            var block = new GameObject($"Area{slot}");
            block.transform.SetParent(_blockerRoot.transform, false);
            Image image = block.AddComponent<Image>();
            image.color = new Color(0f, 0f, 0f, 0f);
            image.raycastTarget = true;
            block.AddComponent<UIMouseListener>();
            rect = block.GetComponent<RectTransform>();
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.zero;
            rect.pivot = Vector2.zero;
            _areas[slot] = rect;
        }

        rect.gameObject.SetActive(true);
        rect.anchoredPosition = new Vector2(area.x, Screen.height - area.yMax);
        rect.sizeDelta = new Vector2(area.width, area.height);
    }

    internal static void NoClickArea(int slot) {
        if (_areas.TryGetValue(slot, out RectTransform rect) && rect != null) {
            rect.gameObject.SetActive(false);
        }
    }

    internal static void NoClickAreas() {
        if (_blockerRoot != null) {
            _blockerRoot.SetActive(false);
        }
    }

    private static void EnsureBlocker() {
        if (_blockerRoot != null) {
            return;
        }

        _areas.Clear();
        _blockerRoot = new GameObject("MorditasTithe.ClickArea");
        Object.DontDestroyOnLoad(_blockerRoot);
        Canvas canvas = _blockerRoot.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 30000;
        _blockerRoot.AddComponent<GraphicRaycaster>();
    }

    // The game's own faces, once it has loaded them. Its Latin faces live in TextMeshPro font assets (their source fonts);
    // the loose Font objects are fallbacks for other scripts (NotoSansArabic, NotoSansJP, ...) and are not used. The
    // default IMGUI font stands in until then. The search stops after a minute and logs what it found once.
    private static void FindFonts() {
        if ((_bodyFont != null && _displayFont != null) || _fontSearches >= 30 || Time.unscaledTime < _nextFontSearch) {
            return;
        }

        _nextFontSearch = Time.unscaledTime + 2f;
        _fontSearches++;
        var fonts = Resources.FindObjectsOfTypeAll(Il2CppType.Of<TMP_FontAsset>())
            .Select(found => found.TryCast<TMP_FontAsset>())
            .Where(asset => asset != null && asset.sourceFontFile != null && asset.sourceFontFile.dynamic)
            .Select(asset => asset.sourceFontFile)
            .GroupBy(font => font.name)
            .Select(group => group.First())
            .ToList();
        Font body = fonts.FirstOrDefault(font => IsLatinNoto(font.name) && font.name.Contains("Regular", StringComparison.OrdinalIgnoreCase))
            ?? fonts.FirstOrDefault(font => IsLatinNoto(font.name));
        Font display = fonts.FirstOrDefault(font => font.name.StartsWith("LastEpoch", StringComparison.OrdinalIgnoreCase) && !font.name.Contains("Alt", StringComparison.OrdinalIgnoreCase))
            ?? fonts.FirstOrDefault(font => font.name.StartsWith("LastEpoch", StringComparison.OrdinalIgnoreCase));
        bool changed = (_bodyFont == null && body != null) || (_displayFont == null && display != null);
        _bodyFont ??= body;
        _displayFont ??= display;
        if (changed) {
            _styles.Clear();
        }

        if ((_bodyFont != null && _displayFont != null) || _fontSearches >= 30) {
            MelonLogger.Msg($"Fonts: text {_bodyFont?.name ?? "default"}, display {_displayFont?.name ?? "default"} (from {string.Join(", ", fonts.Select(font => font.name))})");
        }
    }

    private static bool IsLatinNoto(string name) {
        string[] scripts = ["Arabic", "Devanagari", "JP", "SC", "TC", "KR", "Thai", "Hebrew", "Mono"];
        return name.StartsWith("NotoSans", StringComparison.OrdinalIgnoreCase)
            && !scripts.Any(script => name.Contains(script, StringComparison.Ordinal));
    }

    internal static string Clock(double seconds) {
        var time = TimeSpan.FromSeconds(Math.Max(0d, seconds));
        return time.TotalHours >= 1d
            ? time.ToString(@"h\:mm\:ss", CultureInfo.InvariantCulture)
            : time.ToString(@"mm\:ss", CultureInfo.InvariantCulture);
    }

    internal static string Count(double value) {
        return value.ToString("N0", CultureInfo.InvariantCulture);
    }

    internal static string Html(Color color) {
        return "#" + ColorUtility.ToHtmlStringRGB(color);
    }

    internal static string Tint(string text, Color color) {
        return $"<color={Html(color)}>{text}</color>";
    }

    private static Color Hex(int rgb) {
        return new Color(((rgb >> 16) & 0xFF) / 255f, ((rgb >> 8) & 0xFF) / 255f, (rgb & 0xFF) / 255f, 1f);
    }

    [DllImport("user32.dll")]
    internal static extern short GetAsyncKeyState(int virtualKey);
}

// The text styles at one size: the caption and controls in the display face, the rest in the text face.
internal sealed class Styles {
    public Styles(float unit, Font body, Font display) {
        Unit = unit;
        Caption = Make(display ?? body, 13f * unit, Ui.Blood, FontStyle.Bold);
        Control = Make(display ?? body, 13f * unit, Ui.Muted, FontStyle.Bold);
        Control.alignment = TextAnchor.UpperCenter;
        Heading = Make(display ?? body, 23f * unit, Color.white, FontStyle.Normal);
        Figure = Make(body, 26f * unit, Color.white, FontStyle.Bold);
        Body = Make(body, 19f * unit, new Color(0.94f, 0.92f, 0.90f, 1f), FontStyle.Normal);
        Note = Make(body, 15f * unit, Ui.Muted, FontStyle.Normal);
    }

    public float Unit { get; }
    public GUIStyle Caption { get; }
    public GUIStyle Control { get; }
    public GUIStyle Heading { get; }
    public GUIStyle Figure { get; }
    public GUIStyle Body { get; }
    public GUIStyle Note { get; }

    // A line's height in its style; a divider (null) is a short gap with a rule through it.
    public float Height(GUIStyle style) {
        return style == null ? 10f * Unit : style.fontSize * 1.35f;
    }

    private static GUIStyle Make(Font font, float size, Color color, FontStyle fontStyle) {
        var style = new GUIStyle { fontSize = Math.Max(8, (int)MathF.Round(size)), richText = true, wordWrap = false, fontStyle = fontStyle, clipping = TextClipping.Overflow };
        if (font != null) {
            style.font = font;
        }

        style.normal.textColor = color;
        return style;
    }
}

// One line of a window: its text in a style; a null style is a divider.
internal readonly record struct Row(string Text, GUIStyle Style);
