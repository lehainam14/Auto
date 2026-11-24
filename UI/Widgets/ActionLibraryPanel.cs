using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Macro.Core;
using Macro.Localization;

namespace Macro.UI.Widgets;

/// <summary>Left sidebar: the searchable, draggable Action Library.</summary>
public class ActionLibraryPanel : UserControl
{
    public event Action<string> ActionSelected;    // single-clicked type id to configure & add

    private readonly TextBox _search;
    private readonly StackPanel _list;
    private readonly TextBlock _header;
    private readonly TextBlock _hint;
    private readonly List<CategorySection> _sections = new();
    private ActionChip _selectedChip;

    public ActionLibraryPanel()
    {
        var root = new Grid { Margin = new Thickness(0) };
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        _header = Ui.Label(Loc.T("lib.title", "ACTION LIBRARY"), T.Muted, 10.5, true);
        _header.Margin = new Thickness(10, 6, 10, 3);
        root.Children.Add(_header);
        Grid.SetRow(_header, 0);

        _search = new TextBox
        {
            Margin = new Thickness(8, 0, 8, 3),
            Tag = Loc.T("lib.search", "Search actions..."),
        };
        _search.TextChanged += (_, _) => ApplyFilter(_search.Text);
        root.Children.Add(_search);
        Grid.SetRow(_search, 1);

        var scroll = new ScrollViewer
        {
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            Margin = new Thickness(0, 2, 0, 0),
        };
        _list = new StackPanel { Margin = new Thickness(0, 0, 0, 0) };
        scroll.Content = _list;
        root.Children.Add(scroll);
        Grid.SetRow(scroll, 2);

        _hint = new TextBlock
        {
            Text = Loc.T("lib.hint", "Click to configure & add\nor drag into the workflow"),
            Foreground = T.Muted,
            FontSize = 10.5,
            Margin = new Thickness(8, 4, 8, 6),
            TextAlignment = TextAlignment.Center,
        };
        root.Children.Add(_hint);
        Grid.SetRow(_hint, 3);

        Content = root;
        Build();
    }

    /// <summary>Clear active chip selection highlight in library.</summary>
    public void ClearSelection()
    {
        if (_selectedChip != null)
        {
            _selectedChip.IsSelected = false;
            _selectedChip = null;
        }
    }

    /// <summary>Re-render all texts after the language changed.</summary>
    public void ApplyLanguage()
    {
        _header.Text = Loc.T("lib.title", "ACTION LIBRARY");
        _search.Tag = Loc.T("lib.search", "Search actions...");
        _hint.Text = Loc.T("lib.hint",
            "Click to configure & add\nor drag into the workflow");
        foreach (var section in _sections) section.RefreshTexts();
    }

    private void Build()
    {
        foreach (var categoryId in ActionRegistry.UsedCategories())
        {
            var section = new CategorySection(categoryId);
            foreach (var def in ActionRegistry.Definitions(categoryId))
            {
                var chip = new ActionChip(def);
                chip.ActionSelected += id =>
                {
                    if (_selectedChip != null && _selectedChip != chip)
                        _selectedChip.IsSelected = false;
                    _selectedChip = chip;
                    chip.IsSelected = true;
                    ActionSelected?.Invoke(id);
                };
                section.Chips.Add(chip);
            }
            _sections.Add(section);
            _list.Children.Add(section);
        }
    }

    private void ApplyFilter(string text)
    {
        text = (text ?? "").Trim().ToLowerInvariant();
        foreach (var section in _sections)
        {
            var visible = section.ApplyFilter(text);
            section.Visibility = visible > 0 ? Visibility.Visible : Visibility.Collapsed;
        }
    }
}

/// <summary>Collapsible category group with its chips.</summary>
public class CategorySection : StackPanel
{
    public readonly List<ActionChip> Chips = new();
    private readonly StackPanel _body;
    private readonly TextBlock _arrow;
    private readonly TextBlock _headerText;
    private readonly string _categoryId;
    private bool _expanded = true;

    public CategorySection(string categoryId)
    {
        _categoryId = categoryId;
        Margin = new Thickness(0, 1, 0, 1);

        var header = new Button
        {
            Background = Brushes.Transparent,
            BorderThickness = new Thickness(0),
            HorizontalContentAlignment = HorizontalAlignment.Stretch,
            Padding = new Thickness(8, 3, 8, 3),
            Cursor = Cursors.Hand,
        };
        var headerRow = new StackPanel { Orientation = Orientation.Horizontal };
        _arrow = new TextBlock
        {
            Text = "\u25BE",                       // ▾
            Foreground = T.Category(categoryId),
            Margin = new Thickness(0, 0, 5, 0),
            FontSize = 11,
            VerticalAlignment = VerticalAlignment.Center,
        };
        headerRow.Children.Add(_arrow);
        _headerText = Ui.Label(
            Loc.CategoryName(categoryId).ToUpperInvariant(),
            T.Category(categoryId), 10.5, true);
        headerRow.Children.Add(_headerText);
        header.Content = headerRow;
        header.Click += (_, _) =>
        {
            _expanded = !_expanded;
            _arrow.Text = _expanded ? "\u25BE" : "\u25B8";
            _body.Visibility = _expanded ? Visibility.Visible : Visibility.Collapsed;
        };
        Children.Add(header);

        _body = new StackPanel { Margin = new Thickness(4, 0, 4, 0) };
        Children.Add(_body);
    }

    public void RefreshTexts()
    {
        _headerText.Text = Loc.CategoryName(_categoryId).ToUpperInvariant();
        foreach (var chip in Chips) chip.RefreshText();
    }

    public int ApplyFilter(string text)
    {
        var visible = 0;
        foreach (var chip in Chips)
        {
            var match = text.Length == 0
                || chip.DisplayName.ToLowerInvariant().Contains(text)
                || chip.Definition.Description.ToLowerInvariant().Contains(text)
                || Loc.ActionDesc(chip.Definition.TypeId).ToLowerInvariant()
                   .Contains(text);
            chip.Visibility = match ? Visibility.Visible : Visibility.Collapsed;
            if (match) visible++;
        }
        return visible;
    }

    protected override void OnVisualParentChanged(DependencyObject oldParent)
    {
        base.OnVisualParentChanged(oldParent);
        if (_body.Children.Count == 0)
            foreach (var chip in Chips)
                _body.Children.Add(chip);
    }
}

/// <summary>One draggable action chip: icon + name.</summary>
public class ActionChip : Border
{
    public readonly ActionDefinition Definition;
    public event Action<string> ActionSelected;
    private readonly TextBlock _nameLabel;
    private Point? _dragStart;
    private bool _isSelected;

    public bool IsSelected
    {
        get => _isSelected;
        set
        {
            _isSelected = value;
            UpdateSelectionVisual();
        }
    }

    private void UpdateSelectionVisual()
    {
        if (_isSelected)
        {
            Background = (Brush)new BrushConverter().ConvertFromString("#252E3E");
            BorderBrush = (Brush)new BrushConverter().ConvertFromString("#4F8CFF");
        }
        else
        {
            Background = T.Field;
            BorderBrush = (Brush)new BrushConverter().ConvertFromString("#2E3036");
        }
    }

    public ActionChip(ActionDefinition definition)
    {
        Definition = definition;
        Background = T.Field;
        BorderBrush = (Brush)new BrushConverter().ConvertFromString("#2E3036");
        BorderThickness = new Thickness(1);
        CornerRadius = new CornerRadius(4);
        Margin = new Thickness(0, 0, 0, 2);
        Padding = new Thickness(7, 3.5, 7, 3.5);
        Cursor = Cursors.Hand;

        var row = new StackPanel { Orientation = Orientation.Horizontal };
        row.Children.Add(Ui.IconImage(definition.Icon,
            Categories.ColorOf(definition.Category), 14));
        _nameLabel = Ui.Label(Loc.ActionName(definition.TypeId), T.Text, 11.5);
        _nameLabel.Margin = new Thickness(7, 0, 0, 0);
        row.Children.Add(_nameLabel);
        Child = row;

        MouseLeftButtonDown += OnDown;
        MouseMove += OnMove;
        MouseLeftButtonUp += (_, _) => _dragStart = null;
        RefreshText();
    }

    public string DisplayName => Loc.ActionName(Definition.TypeId);

    public void RefreshText()
    {
        _nameLabel.Text = Loc.ActionName(Definition.TypeId);
        ToolTip = Loc.ActionDesc(Definition.TypeId);
    }

    private void OnDown(object sender, MouseButtonEventArgs e)
    {
        _dragStart = e.GetPosition(this);
        ActionSelected?.Invoke(Definition.TypeId);
    }

    private void OnMove(object sender, MouseEventArgs e)
    {
        if (e.LeftButton != MouseButtonState.Pressed || _dragStart is not { } start)
            return;
        var here = e.GetPosition(this);
        if (Math.Abs(here.X - start.X) + Math.Abs(here.Y - start.Y) < 6)
            return;

        var data = new DataObject("macro-action-type", Definition.TypeId);
        DragDrop.DoDragDrop(this, data, DragDropEffects.Copy);  // blocks
        _dragStart = null;
    }
}

internal static class DragExtensions
{
    public static double ManipulationOffset(this Point delta) =>
        Math.Abs(delta.X) + Math.Abs(delta.Y);
}
