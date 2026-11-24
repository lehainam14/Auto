using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Macro.Core;
using Macro.Localization;

namespace Macro.UI.Widgets;

/// <summary>Shared brushes/colors resolved from the theme dictionary.</summary>
public static class T
{
    public static Brush B(string key) =>
        (Brush)Application.Current.Resources[key];

    public static Brush Text => B("Brush.Text");
    public static Brush Muted => B("Brush.Muted");
    public static Brush Accent => B("Brush.Accent");
    public static Brush Field => B("Brush.Field");
    public static Brush Border => B("Brush.Border");
    public static Brush BorderHi => B("Brush.BorderHi");
    public static Brush Panel => B("Brush.BgPanel");
    public static Brush Hover => B("Brush.Hover");

    public static Brush Category(string categoryId)
        => (Brush)new BrushConverter().ConvertFromString(
               Categories.ColorOf(categoryId));
}

/// <summary>
/// A TextBox that only accepts integers within a range. Raises ValueChanged
/// when the content is a valid number.
/// </summary>
public class IntegerBox : TextBox
{
    private readonly int _min;
    private readonly int _max;
    private bool _updating;

    public event Action<int> ValueChanged;

    public IntegerBox(int min, int max)
    {
        _min = min;
        _max = max;
        MinWidth = 48;
        Height = 24;
        Padding = new Thickness(4, 2, 4, 2);
        FontSize = 11.5;
        VerticalContentAlignment = VerticalAlignment.Center;
        TextChanged += (_, _) => OnChanged();
        LostFocus += (_, _) => Normalize();
    }

    public int Value
    {
        get => int.TryParse(Text, out var value) ? Math.Clamp(value, _min, _max) : _min;
        set
        {
            _updating = true;
            Text = value.ToString();
            _updating = false;
        }
    }

    private void OnChanged()
    {
        if (_updating) return;
        if (int.TryParse(Text, out var value))
            ValueChanged?.Invoke(Math.Clamp(value, _min, _max));
    }

    private void Normalize()
    {
        if (!int.TryParse(Text, out var value))
            Value = _min;
        else if (value < _min || value > _max)
            Value = Math.Clamp(value, _min, _max);
    }
}

/// <summary>
/// Context menu listing every registered action grouped by category.
/// onPick receives the chosen action type id.
/// </summary>
public static class AddStepMenu
{
    public static void Show(UIElement anchor, Window owner,
                            Action<string> onPick)
    {
        var menu = new ContextMenu();
        foreach (var categoryId in ActionRegistry.UsedCategories())
        {
            var header = new MenuItem
            {
                Header = Loc.CategoryName(categoryId),
                IsEnabled = false,
                FontWeight = FontWeights.Bold,
                Foreground = T.Muted,
            };
            menu.Items.Add(header);
            foreach (var def in ActionRegistry.Definitions(categoryId))
            {
                var item = new MenuItem
                {
                    Header = BuildHeader(def, Categories.ColorOf(categoryId)),
                    Tag = def.TypeId,
                    ToolTip = Loc.ActionDesc(def.TypeId),
                };
                item.Click += (_, _) => onPick(def.TypeId);
                menu.Items.Add(item);
            }
            menu.Items.Add(new Separator());
        }
        if (menu.Items.Count > 0) menu.Items.RemoveAt(menu.Items.Count - 1);
        menu.PlacementTarget = anchor;
        menu.Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom;
        menu.IsOpen = true;
    }

    private static StackPanel BuildHeader(ActionDefinition def, string color)
    {
        var panel = new StackPanel { Orientation = Orientation.Horizontal };
        panel.Children.Add(new Image
        {
            Source = Icons.For(def.Icon, color, 14),
            Width = 15,
            Height = 15,
            Margin = new Thickness(0, 0, 8, 0),
            VerticalAlignment = VerticalAlignment.Center,
        });
        panel.Children.Add(new TextBlock
        {
            Text = Loc.ActionName(def.TypeId),
            VerticalAlignment = VerticalAlignment.Center,
        });
        return panel;
    }
}

/// <summary>Small helper used by the panels to build styled labels.</summary>
public static class Ui
{
    public static TextBlock Label(string text, Brush brush = null,
                                  double size = 11.5, bool bold = false)
    {
        return new TextBlock
        {
            Text = text,
            Foreground = brush ?? T.Text,
            FontSize = size,
            FontWeight = bold ? FontWeights.Bold : FontWeights.Normal,
            VerticalAlignment = VerticalAlignment.Center,
        };
    }

    public static Image IconImage(string logicalName, string color,
                                  double size = 15)
    {
        return new Image
        {
            Source = Icons.For(logicalName, color, size),
            Width = size,
            Height = size,
            VerticalAlignment = VerticalAlignment.Center,
        };
    }

    public static Button IconButton(string glyphName, string tooltip,
                                    Action onClick, string color = "#8F939C")
    {
        var button = new Button
        {
            Style = (Style)Application.Current.Resources["Btn.Icon"],
            Content = IconImage(glyphName, color, 12),
            ToolTip = tooltip,
        };
        button.Click += (_, _) => onClick();
        return button;
    }

    /// <summary>
    /// Compact green section header styled like ── Title ────────────────
    /// matching the reference image.
    /// </summary>
    public static FrameworkElement SectionHeader(string title)
    {
        var grid = new Grid { Margin = new Thickness(0, 7, 0, 3) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(10) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        var leftLine = new System.Windows.Shapes.Rectangle
        {
            Height = 1,
            Fill = (Brush)Application.Current.Resources["Brush.Border"],
            VerticalAlignment = VerticalAlignment.Center,
        };
        var text = new TextBlock
        {
            Text = title,
            Foreground = (Brush)Application.Current.Resources["Brush.SectionGreen"]
                         ?? new SolidColorBrush(Color.FromRgb(0x38, 0xB2, 0x6C)),
            FontWeight = FontWeights.SemiBold,
            FontSize = 11,
            Margin = new Thickness(5, 0, 6, 0),
            VerticalAlignment = VerticalAlignment.Center,
        };
        var rightLine = new System.Windows.Shapes.Rectangle
        {
            Height = 1,
            Fill = (Brush)Application.Current.Resources["Brush.Border"],
            VerticalAlignment = VerticalAlignment.Center,
        };
        Grid.SetColumn(leftLine, 0);
        Grid.SetColumn(text, 1);
        Grid.SetColumn(rightLine, 2);
        grid.Children.Add(leftLine);
        grid.Children.Add(text);
        grid.Children.Add(rightLine);
        return grid;
    }
}
