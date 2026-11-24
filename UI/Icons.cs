using System.Globalization;
using System.Windows;
using System.Windows.Media;

namespace Macro.UI;

/// <summary>
/// Central icon provider. Draws Unicode glyphs (Segoe UI Symbol / Emoji,
/// always present on Windows) as vector DrawingImages - no icon library
/// needed. Logical names are shared with the action definitions.
/// </summary>
public static class Icons
{
    private static readonly Dictionary<string, string> Glyphs = new()
    {
        ["logo"] = "\u26A1",            // ⚡
        ["new"] = "\u29C9",             // ⧉
        ["open"] = "\uD83D\uDCC2",      // 📂
        ["save"] = "\uD83D\uDCBE",      // 💾
        ["run"] = "\u25B6",             // ▶
        ["pause"] = "\u2016",           // ‖
        ["stop"] = "\u25A0",            // ■
        ["settings"] = "\u2699",        // ⚙
        ["search"] = "\uD83D\uDD0D",    // 🔍
        // mouse
        ["click"] = "\uD83D\uDDB1",     // 🖱
        ["dblclick"] = "\uD83D\uDDB1",
        ["rightclick"] = "\uD83D\uDC46",// 👆
        ["move"] = "\u271B",            // ✛
        ["drag"] = "\u21C4",            // ⇄
        ["scroll"] = "\u21F5",          // ⇵
        // keyboard
        ["key"] = "\u2328",             // ⌨
        ["text"] = "\u270E",            // ✎
        ["file"] = "\uD83D\uDCC4",      // 📄
        // screen
        ["image"] = "\uD83D\uDDBC",     // 🖼
        ["clickimage"] = "\uD83D\uDC46",
        ["camera"] = "\uD83D\uDCF7",    // 📷
        ["pixel"] = "\uD83D\uDCA7",     // 💧
        // timing
        ["wait"] = "\u23F1",            // ⏱
        ["random"] = "\u29D6",          // ⧖
        // logic
        ["loop"] = "\uD83D\uDD01",      // 🔁
        ["break"] = "\u23EE",           // ⏮
        ["goto"] = "\u21AA",            // ↪
        // application
        ["app"] = "\u2197",             // ↗
        // card controls
        ["trash"] = "\uD83D\uDDD1",     // 🗑
        ["copy"] = "\u29C9",            // ⧉
        ["up"] = "\u2191",              // ↑
        ["down"] = "\u2193",            // ↓
        ["power"] = "\u23FB",           // ⏻
        ["plus"] = "+",
        ["clear"] = "\u2715",           // ✕
        ["panel_left"] = "\u25E4",      // ◤
        ["panel_right"] = "\u25E5",     // ◥
    };

    private static readonly Dictionary<(string, string, double), ImageSource> Cache = new();

    public static ImageSource For(string logicalName, string color = "#C7CAD1",
                                  double size = 18)
    {
        var key = (logicalName, color, size);
        if (Cache.TryGetValue(key, out var cached)) return cached;

        var glyph = Glyphs.GetValueOrDefault(logicalName, "?");
        var foreground = (Brush)new BrushConverter().ConvertFromString(color);
        // Emoji (surrogate pairs / high code points) use the emoji font,
        // plain symbols use Segoe UI Symbol.
        var fontName = glyph.Length > 1 || glyph[0] > 0x2A00
            ? "Segoe UI Emoji" : "Segoe UI Symbol";
        var typeface = new Typeface(new FontFamily(fontName),
                                    FontStyles.Normal, FontWeights.Normal,
                                    FontStretches.Normal);
        var text = new FormattedText(glyph, CultureInfo.InvariantCulture,
            FlowDirection.LeftToRight, typeface, size, foreground, 1.0);

        var visual = new DrawingVisual();
        using (var context = visual.RenderOpen())
            context.DrawText(text, new Point(0, 0));
        var image = new DrawingImage(visual.Drawing);
        image.Freeze();
        Cache[key] = image;
        return image;
    }
}
