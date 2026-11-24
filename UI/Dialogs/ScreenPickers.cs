using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;

namespace Macro.UI.Dialogs;

/// <summary>
/// Fullscreen click-through overlays used to pick a screen position or
/// region. Esc cancels. ShowDialog() then read Position / Region.
/// </summary>
public class PickerOverlay : Window
{
    private const int CrossLen = 14;

    private readonly Visual _host;                  // for DPI of the drawing
    protected Point? Anchor;                        // drag start (region)
    protected Point? Current;

    public (int x, int y)? Position;                // PositionPicker result
    public (int x, int y, int w, int h)? Region;    // RegionPicker result

    private readonly string _hint;
    private readonly bool _isRegion;
    private readonly Grid _root;
    private readonly TextBlock _hintBlock;

    public PickerOverlay(bool isRegion)
    {
        _isRegion = isRegion;
        _hint = isRegion
            ? "Drag to select a region   |   Esc to cancel"
            : "Click to pick a position   |   Esc to cancel";

        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        Background = new SolidColorBrush(Color.FromArgb(70, 0, 0, 0));
        Topmost = true;
        Cursor = Cursors.Cross;
        WindowState = WindowState.Maximized;

        _root = new Grid();
        _hintBlock = new TextBlock
        {
            Text = _hint,
            Foreground = Brushes.White,
            FontWeight = FontWeights.Bold,
            FontSize = 15,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Top,
            Margin = new Thickness(0, 34, 0, 0),
        };
        _root.Children.Add(_hintBlock);
        Content = _root;
        _host = _root;

        KeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape) Close();
        };
        MouseMove += (_, e) => { Current = e.GetPosition(this); Invalidate(); };
        MouseLeave += (_, _) => { Current = null; Invalidate(); };
        MouseLeftButtonDown += (_, e) =>
        {
            if (e.ButtonState == MouseButtonState.Pressed)
            {
                Anchor = e.GetPosition(this);
                Invalidate();
            }
        };
        MouseLeftButtonUp += OnUp;
    }

    private void OnUp(object sender, MouseButtonEventArgs e)
    {
        var point = e.GetPosition(this);
        if (_isRegion)
        {
            if (Anchor is { } a)
            {
                int x = (int)Math.Min(a.X, point.X);
                int y = (int)Math.Min(a.Y, point.Y);
                int w = (int)Math.Abs(point.X - a.X);
                int h = (int)Math.Abs(point.Y - a.Y);
                if (w >= 4 && h >= 4)
                {
                    Region = (x, y, w, h);
                    Close();
                }
                else
                {
                    Anchor = null;
                    Invalidate();
                }
            }
        }
        else
        {
            Position = ((int)point.X, (int)point.Y);
            Close();
        }
    }

    private void Invalidate() => InvalidateVisual();

    protected override void OnRender(DrawingContext dc)
    {
        base.OnRender(dc);
        var accent = new SolidColorBrush(Color.FromRgb(0x4F, 0x8C, 0xFF));

        if (_isRegion && Anchor is { } a && Current is { } c)
        {
            var rect = new Rect(
                Math.Min(a.X, c.X), Math.Min(a.Y, c.Y),
                Math.Abs(c.X - a.X), Math.Abs(c.Y - a.Y));
            dc.DrawRectangle(
                new SolidColorBrush(Color.FromArgb(46, 0x4F, 0x8C, 0xFF)),
                new Pen(accent, 2), rect);
            var sizeText = $"{(int)rect.Width} x {(int)rect.Height}";
            DrawLabel(dc, sizeText, rect.X + 8,
                rect.Y > 34 ? rect.Y - 22 : rect.Y + rect.Height + 6);
        }
        else if (!_isRegion && Current is { } p)
        {
            var pen = new Pen(accent, 2);
            dc.DrawLine(pen, new Point(p.X - CrossLen, p.Y),
                             new Point(p.X + CrossLen, p.Y));
            dc.DrawLine(pen, new Point(p.X, p.Y - CrossLen),
                             new Point(p.X, p.Y + CrossLen));
            DrawLabel(dc, $"{(int)p.X}, {(int)p.Y}", p.X + CrossLen + 8,
                p.Y - 24);
        }
    }

    private void DrawLabel(DrawingContext dc, string text, double x, double y)
    {
        var typeface = new Typeface(new FontFamily("Segoe UI"),
                                    FontStyles.Normal, FontWeights.Bold,
                                    FontStretches.Normal);
        var formatted = new FormattedText(text,
            System.Globalization.CultureInfo.InvariantCulture,
            FlowDirection.LeftToRight, typeface, 14, Brushes.White,
            VisualTreeHelper.GetDpi(_host).PixelsPerDip);
        dc.DrawRoundedRectangle(
            new SolidColorBrush(Color.FromArgb(235, 20, 21, 24)), null,
            new Rect(x - 6, y - 4, formatted.Width + 12, formatted.Height + 8), 4, 4);
        dc.DrawText(formatted, new Point(x, y));
    }
}

/// <summary>Click anywhere on the screen -> (x, y) or null.</summary>
public static class PositionPicker
{
    public static (int x, int y)? Pick(Window owner)
    {
        var picker = new PickerOverlay(isRegion: false);
        if (owner != null)
        {
            picker.Owner = owner;
            picker.WindowStartupLocation = WindowStartupLocation.Manual;
            picker.Left = SystemParameters.VirtualScreenLeft;
            picker.Top = SystemParameters.VirtualScreenTop;
            picker.Width = SystemParameters.VirtualScreenWidth;
            picker.Height = SystemParameters.VirtualScreenHeight;
        }
        picker.ShowDialog();
        return picker.Position;
    }
}

/// <summary>Drag a rectangle on the screen -> (x, y, w, h) or null.</summary>
public static class RegionPicker
{
    public static (int x, int y, int w, int h)? Pick(Window owner)
    {
        var picker = new PickerOverlay(isRegion: true);
        if (owner != null)
        {
            picker.Owner = owner;
            picker.WindowStartupLocation = WindowStartupLocation.Manual;
            picker.Left = SystemParameters.VirtualScreenLeft;
            picker.Top = SystemParameters.VirtualScreenTop;
            picker.Width = SystemParameters.VirtualScreenWidth;
            picker.Height = SystemParameters.VirtualScreenHeight;
        }
        picker.ShowDialog();
        return picker.Region;
    }
}

/// <summary>Small modal: press any key to record its name.</summary>
public class KeyCaptureDialog : Window
{
    private static readonly Dictionary<Key, string> SpecialNames = new()
    {
        [Key.Enter] = "enter", [Key.Space] = "space", [Key.Tab] = "tab",
        [Key.Back] = "backspace", [Key.Delete] = "delete",
        [Key.Up] = "up", [Key.Down] = "down", [Key.Left] = "left",
        [Key.Right] = "right", [Key.Home] = "home", [Key.End] = "end",
        [Key.PageUp] = "pageup", [Key.PageDown] = "pagedown",
        [Key.Insert] = "insert", [Key.CapsLock] = "capslock",
        [Key.NumLock] = "numlock", [Key.Scroll] = "scrolllock",
        [Key.Pause] = "pause",
    };

    public string KeyName;

    public KeyCaptureDialog(string current)
    {
        Title = "Capture key";
        Width = 340;
        SizeToContent = SizeToContent.Height;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ResizeMode = ResizeMode.NoResize;
        Background = (Brush)new BrushConverter()
            .ConvertFromString("#191A1D");

        var stack = new StackPanel { Margin = new Thickness(16) };
        stack.Children.Add(new TextBlock
        {
            Text = "Press the key you want to use.\nEsc closes - type the key "
                 + "name manually if you need Esc.",
            Foreground = (Brush)new BrushConverter().ConvertFromString("#8F939C"),
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 12),
        });
        var value = new TextBlock
        {
            Text = string.IsNullOrEmpty(current) ? "-" : current,
            FontSize = 22,
            FontWeight = FontWeights.Bold,
            Foreground = (Brush)new BrushConverter().ConvertFromString("#4F8CFF"),
            HorizontalAlignment = HorizontalAlignment.Center,
            Padding = new Thickness(10, 14, 10, 14),
        };
        var box = new Border
        {
            Background = (Brush)new BrushConverter().ConvertFromString("#26272B"),
            BorderBrush = (Brush)new BrushConverter().ConvertFromString("#35363B"),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8),
            Child = value,
        };
        stack.Children.Add(box);
        Content = stack;

        KeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape) { Close(); return; }
            if (e.Key is Key.LeftCtrl or Key.RightCtrl or Key.LeftShift
                or Key.RightShift or Key.LeftAlt or Key.RightAlt
                or Key.LWin or Key.RWin or Key.System) return;
            var name = KeyToName(e.Key);
            if (name != null)
            {
                KeyName = name;
                Close();
            }
        };
    }

    public static string KeyToName(Key key)
    {
        if (SpecialNames.TryGetValue(key, out var name)) return name;
        if (key is >= Key.F1 and <= Key.F24)
            return "f" + (key - Key.F1 + 1);
        var text = key.ToString();
        if (text.Length == 1) return text.ToLowerInvariant();
        if (text.StartsWith("D") && text.Length == 2
            && char.IsDigit(text[1])) return text[1].ToString();
        if (text.StartsWith("NumPad")) return text.ToLowerInvariant();
        return null;    // unsupported key
    }
}
