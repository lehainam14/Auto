using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Macro.Localization;

namespace Macro.UI.Widgets;

/// <summary>
/// Output / log panel at the bottom of the IDE: shows engine log lines and
/// per-action Test results with timestamps. Collapsible via the toolbar
/// button in the top bar.
/// </summary>
public class OutputPanel : UserControl
{
    private readonly StackPanel _lines = new();
    private readonly ScrollViewer _scroll;

    public OutputPanel()
    {
        var root = new DockPanel { LastChildFill = true };

        var header = new Grid
        {
            Background = T.Panel,
            Margin = new Thickness(0, 0, 0, 1),
        };
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var title = Ui.Label(Loc.T("output.title", "OUTPUT / LOG"), T.Muted, 10.5, true);
        title.Margin = new Thickness(8, 3, 0, 3);
        Grid.SetColumn(title, 0);
        header.Children.Add(title);

        var clear = new Button
        {
            Content = Loc.T("output.clear", "Clear log"),
            Style = (Style)Application.Current.Resources["Btn.Tool"],
            FontSize = 10.5,
            Padding = new Thickness(6, 1.5, 6, 1.5),
            Margin = new Thickness(0, 1, 6, 1),
        };
        clear.Click += (_, _) => Clear();
        Grid.SetColumn(clear, 1);
        header.Children.Add(clear);
        DockPanel.SetDock(header, Dock.Top);
        root.Children.Add(header);

        _scroll = new ScrollViewer
        {
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            Background = (Brush)new BrushConverter().ConvertFromString("#1B1C1F"),
        };
        _scroll.Content = _lines;
        root.Children.Add(_scroll);

        Content = root;
    }

    public void ApplyLanguage()
    {
        // The header is rebuilt by re-rendering its label.
        if (_lines.Parent is ScrollViewer && Content is DockPanel dock
            && dock.Children.Count > 0 && dock.Children[0] is Grid headerGrid
            && headerGrid.Children[0] is TextBlock title)
        {
            title.Text = Loc.T("output.title", "OUTPUT / LOG");
        }
    }

    public void Append(string message)
    {
        var line = new TextBlock
        {
            Text = $"{DateTime.Now:HH:mm:ss}  {message}",
            Foreground = T.Text,
            FontSize = 11.5,
            Padding = new Thickness(8, 0.5, 8, 0.5),
            TextWrapping = TextWrapping.NoWrap,
            TextTrimming = TextTrimming.CharacterEllipsis,
        };
        if (message.StartsWith("[Test]"))
            line.Foreground = new SolidColorBrush(
                Color.FromRgb(0x8F, 0xD3, 0x9C));
        _lines.Children.Add(line);
        // keep memory bounded
        while (_lines.Children.Count > 500)
            _lines.Children.RemoveAt(0);
        _scroll.ScrollToEnd();
    }

    public void Clear() => _lines.Children.Clear();
}
