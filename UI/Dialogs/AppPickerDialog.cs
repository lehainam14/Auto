using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Macro.Core.Emulator;
using Macro.Localization;
using Macro.UI.Widgets;

namespace Macro.UI.Dialogs;

/// <summary>
/// Modal dialog for browsing, searching, and selecting installed applications from an LDPlayer tab.
/// </summary>
public class AppPickerDialog : Window
{
    private readonly List<LdAppInfo> _allApps;
    private readonly ObservableCollection<LdAppInfo> _displayList = new();
    private readonly ListBox _listBox;
    private readonly TextBox _searchBox;
    private readonly RadioButton _rbThirdParty;
    private readonly RadioButton _rbAll;
    private readonly Button _btnSelect;

    public LdAppInfo SelectedApp { get; private set; }

    public AppPickerDialog(List<LdAppInfo> apps, string currentPackage, int tabIndex, string tabName, Window owner = null)
    {
        Owner = owner;
        _allApps = apps ?? new List<LdAppInfo>();

        Title = $"Danh sách ứng dụng - Tab #{tabIndex}" + (!string.IsNullOrEmpty(tabName) ? $" ({tabName})" : "");
        Width = 520;
        Height = 560;
        MinWidth = 420;
        MinHeight = 400;
        WindowStartupLocation = owner != null ? WindowStartupLocation.CenterOwner : WindowStartupLocation.CenterScreen;
        Background = (Brush)new BrushConverter().ConvertFromString("#18191D");
        Foreground = (Brush)new BrushConverter().ConvertFromString("#E8E9EC");

        var root = new Grid { Margin = new Thickness(16, 14, 16, 14) };
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // Header
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // Search & Filters
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) }); // List
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // Buttons

        // 1. Header
        var header = new DockPanel { Margin = new Thickness(0, 0, 0, 12), LastChildFill = true };
        var icon = new TextBlock
        {
            Text = "📱 ",
            FontSize = 18,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 8, 0),
        };
        DockPanel.SetDock(icon, Dock.Left);
        header.Children.Add(icon);

        var headerTextStack = new StackPanel();
        var title = new TextBlock
        {
            Text = "CHỌN ỨNG DỤNG ĐỂ MỞ",
            FontSize = 13.5,
            FontWeight = FontWeights.Bold,
            Foreground = (Brush)new BrushConverter().ConvertFromString("#F2F3F5"),
        };
        var subtitle = new TextBlock
        {
            Text = $"Đã tìm thấy {_allApps.Count} ứng dụng trên Tab #{tabIndex}. Hãy chọn ứng dụng để tự động gán vào thao tác.",
            FontSize = 11,
            Foreground = (Brush)new BrushConverter().ConvertFromString("#8F939C"),
            Margin = new Thickness(0, 2, 0, 0),
            TextWrapping = TextWrapping.Wrap,
        };
        headerTextStack.Children.Add(title);
        headerTextStack.Children.Add(subtitle);
        header.Children.Add(headerTextStack);
        Grid.SetRow(header, 0);
        root.Children.Add(header);

        // 2. Search & Filters
        var searchFilterGrid = new Grid { Margin = new Thickness(0, 0, 0, 10) };
        searchFilterGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        searchFilterGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        _searchBox = new TextBox
        {
            FontSize = 12,
            Padding = new Thickness(8, 6, 8, 6),
            Background = (Brush)new BrushConverter().ConvertFromString("#212328"),
            Foreground = Brushes.White,
            BorderBrush = (Brush)new BrushConverter().ConvertFromString("#363842"),
            BorderThickness = new Thickness(1),
            Margin = new Thickness(0, 0, 0, 8),
            ToolTip = "Nhập tên ứng dụng hoặc package để tìm nhanh",
        };
        _searchBox.TextChanged += (_, _) => ApplyFilter();
        Grid.SetRow(_searchBox, 0);
        searchFilterGrid.Children.Add(_searchBox);

        var filterRow = new StackPanel { Orientation = Orientation.Horizontal };
        int thirdPartyCount = _allApps.Count(a => a.IsThirdParty);
        int totalCount = _allApps.Count;

        _rbThirdParty = new RadioButton
        {
            Content = $"Ứng dụng cài thêm ({thirdPartyCount})",
            IsChecked = thirdPartyCount > 0,
            FontSize = 11,
            Foreground = (Brush)new BrushConverter().ConvertFromString("#C7CAD1"),
            Margin = new Thickness(0, 0, 16, 0),
            VerticalAlignment = VerticalAlignment.Center,
        };
        _rbThirdParty.Checked += (_, _) => ApplyFilter();
        filterRow.Children.Add(_rbThirdParty);

        _rbAll = new RadioButton
        {
            Content = $"Tất cả ứng dụng ({totalCount})",
            IsChecked = thirdPartyCount == 0,
            FontSize = 11,
            Foreground = (Brush)new BrushConverter().ConvertFromString("#C7CAD1"),
            VerticalAlignment = VerticalAlignment.Center,
        };
        _rbAll.Checked += (_, _) => ApplyFilter();
        filterRow.Children.Add(_rbAll);

        Grid.SetRow(filterRow, 1);
        searchFilterGrid.Children.Add(filterRow);
        Grid.SetRow(searchFilterGrid, 1);
        root.Children.Add(searchFilterGrid);

        // 3. List
        _listBox = new ListBox
        {
            ItemsSource = _displayList,
            Background = (Brush)new BrushConverter().ConvertFromString("#151619"),
            BorderBrush = (Brush)new BrushConverter().ConvertFromString("#2A2D34"),
            BorderThickness = new Thickness(1),
            Margin = new Thickness(0, 0, 0, 12),
            HorizontalContentAlignment = HorizontalAlignment.Stretch,
        };
        ScrollViewer.SetHorizontalScrollBarVisibility(_listBox, ScrollBarVisibility.Disabled);

        _listBox.ItemTemplate = CreateItemTemplate();
        _listBox.SelectionChanged += (_, _) =>
        {
            _btnSelect.IsEnabled = _listBox.SelectedItem is LdAppInfo;
        };
        _listBox.MouseDoubleClick += (_, e) =>
        {
            if (_listBox.SelectedItem is LdAppInfo app)
            {
                SelectedApp = app;
                DialogResult = true;
                Close();
            }
        };

        Grid.SetRow(_listBox, 2);
        root.Children.Add(_listBox);

        // 4. Buttons
        var btnGrid = new DockPanel { LastChildFill = false };

        var btnCancel = new Button
        {
            Content = Loc.T("dlg.cancel", "Hủy"),
            Padding = new Thickness(16, 6, 16, 6),
            FontSize = 11.5,
            Style = (Style)Application.Current.Resources["Btn.Tool"],
            Margin = new Thickness(0, 0, 8, 0),
        };
        btnCancel.Click += (_, _) =>
        {
            DialogResult = false;
            Close();
        };
        DockPanel.SetDock(btnCancel, Dock.Right);
        btnGrid.Children.Add(btnCancel);

        _btnSelect = new Button
        {
            Content = "✔ Chọn ứng dụng",
            Padding = new Thickness(18, 6, 18, 6),
            FontSize = 11.5,
            FontWeight = FontWeights.Bold,
            Background = (Brush)new BrushConverter().ConvertFromString("#38B26C"),
            Foreground = Brushes.White,
            BorderThickness = new Thickness(0),
            Cursor = Cursors.Hand,
            IsEnabled = false,
        };
        _btnSelect.Click += (_, _) =>
        {
            if (_listBox.SelectedItem is LdAppInfo app)
            {
                SelectedApp = app;
                DialogResult = true;
                Close();
            }
        };
        DockPanel.SetDock(_btnSelect, Dock.Right);
        btnGrid.Children.Add(_btnSelect);

        Grid.SetRow(btnGrid, 3);
        root.Children.Add(btnGrid);

        Content = root;

        ApplyFilter();

        // Select current if matched
        if (!string.IsNullOrEmpty(currentPackage))
        {
            var match = _displayList.FirstOrDefault(a => string.Equals(a.PackageName, currentPackage.Trim(), StringComparison.OrdinalIgnoreCase));
            if (match != null)
            {
                _listBox.SelectedItem = match;
                _listBox.ScrollIntoView(match);
            }
        }
    }

    private void ApplyFilter()
    {
        string query = _searchBox?.Text?.Trim().ToLowerInvariant() ?? "";
        bool thirdPartyOnly = _rbThirdParty?.IsChecked == true;

        _displayList.Clear();

        var filtered = _allApps.Where(a =>
        {
            if (thirdPartyOnly && !a.IsThirdParty) return false;
            if (string.IsNullOrEmpty(query)) return true;
            return a.PackageName.ToLowerInvariant().Contains(query) ||
                   a.DisplayName.ToLowerInvariant().Contains(query);
        });

        foreach (var app in filtered)
        {
            _displayList.Add(app);
        }

        if (_btnSelect != null)
        {
            _btnSelect.IsEnabled = _listBox?.SelectedItem is LdAppInfo;
        }
    }

    private DataTemplate CreateItemTemplate()
    {
        var template = new DataTemplate(typeof(LdAppInfo));
        var factory = new FrameworkElementFactory(typeof(Border));
        factory.SetValue(Border.PaddingProperty, new Thickness(10, 8, 10, 8));
        factory.SetValue(Border.BorderThicknessProperty, new Thickness(0, 0, 0, 1));
        factory.SetValue(Border.BorderBrushProperty, (Brush)new BrushConverter().ConvertFromString("#212328"));

        var grid = new FrameworkElementFactory(typeof(Grid));
        var col0 = new FrameworkElementFactory(typeof(ColumnDefinition));
        col0.SetValue(ColumnDefinition.WidthProperty, new GridLength(1, GridUnitType.Star));
        var col1 = new FrameworkElementFactory(typeof(ColumnDefinition));
        col1.SetValue(ColumnDefinition.WidthProperty, GridLength.Auto);
        grid.AppendChild(col0);
        grid.AppendChild(col1);

        var stack = new FrameworkElementFactory(typeof(StackPanel));
        stack.SetValue(Grid.ColumnProperty, 0);

        var txtName = new FrameworkElementFactory(typeof(TextBlock));
        txtName.SetBinding(TextBlock.TextProperty, new System.Windows.Data.Binding(nameof(LdAppInfo.DisplayName)));
        txtName.SetValue(TextBlock.FontWeightProperty, FontWeights.SemiBold);
        txtName.SetValue(TextBlock.FontSizeProperty, 12.0);
        txtName.SetValue(TextBlock.ForegroundProperty, (Brush)new BrushConverter().ConvertFromString("#F2F3F5"));
        stack.AppendChild(txtName);

        var txtPkg = new FrameworkElementFactory(typeof(TextBlock));
        txtPkg.SetBinding(TextBlock.TextProperty, new System.Windows.Data.Binding(nameof(LdAppInfo.PackageName)));
        txtPkg.SetValue(TextBlock.FontSizeProperty, 10.5);
        txtPkg.SetValue(TextBlock.FontFamilyProperty, new FontFamily("Consolas, Courier New, monospace"));
        txtPkg.SetValue(TextBlock.ForegroundProperty, (Brush)new BrushConverter().ConvertFromString("#8F939C"));
        txtPkg.SetValue(TextBlock.MarginProperty, new Thickness(0, 2, 0, 0));
        stack.AppendChild(txtPkg);

        grid.AppendChild(stack);

        // Badge
        var badge = new FrameworkElementFactory(typeof(Border));
        badge.SetValue(Grid.ColumnProperty, 1);
        badge.SetValue(Border.CornerRadiusProperty, new CornerRadius(3));
        badge.SetValue(Border.PaddingProperty, new Thickness(6, 2, 6, 2));
        badge.SetValue(Border.VerticalAlignmentProperty, VerticalAlignment.Center);

        var badgeText = new FrameworkElementFactory(typeof(TextBlock));
        badgeText.SetValue(TextBlock.FontSizeProperty, 9.5);
        badgeText.SetValue(TextBlock.FontWeightProperty, FontWeights.Medium);

        badge.AppendChild(badgeText);
        grid.AppendChild(badge);

        factory.AppendChild(grid);
        template.VisualTree = factory;
        return template;
    }
}
