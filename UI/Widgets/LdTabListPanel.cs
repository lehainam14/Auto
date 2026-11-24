using System.Collections.ObjectModel;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Macro.Core;
using Macro.Core.Emulator;
using Macro.Localization;

namespace Macro.UI.Widgets;

/// <summary>
/// Main window panel for managing and selecting LDPlayer tabs/instances directly.
/// Displays active instances with checkboxes, status badges, refresh button, macro assignment,
/// multi-macro per tab management, repeat count configuration (0 = infinite), and multi-tab execution.
/// </summary>
public class LdTabListPanel : UserControl
{
    public event Action<LdInstanceInfo> CaptureRequested;
    public event Action ConfigureRequested;
    public event Action OpenAdvancedManagerRequested;
    public event Action SelectionChanged;

    private readonly ObservableCollection<LdInstanceInfo> _instances = new();
    private readonly StackPanel _itemsPanel;
    private readonly ScrollViewer _scrollViewer;
    private readonly TextBlock _txtTitle;
    private readonly TextBlock _txtSummary;
    private readonly TextBlock _emptyNotice;
    private readonly Button _btnEmptyAction;
    private readonly Button _btnRefresh;
    private readonly Button _btnMore;
    private readonly CheckBox _chkSelectAll;

    private readonly Button _btnAssignMacro;
    private readonly IntegerBox _numRepeat;
    private readonly Button _btnRunTabs;
    private readonly Button _btnStopTabs;

    private CancellationTokenSource _tabRunCts;
    private CancellationTokenSource _monitorCts;
    private bool _isPolling;
    private bool _suppressSelectAllChange;
    private bool _isBusy;

    public bool IsRunningTabs => _tabRunCts != null && !_tabRunCts.IsCancellationRequested;

    public LdTabListPanel()
    {
        Loaded += (_, _) => StartMonitoring();
        Unloaded += (_, _) => StopMonitoring();

        var root = new Border
        {
            Background = (Brush)new BrushConverter().ConvertFromString("#18191D"),
            BorderBrush = (Brush)new BrushConverter().ConvertFromString("#2A2C33"),
            BorderThickness = new Thickness(0, 0, 0, 1),
            Padding = new Thickness(10, 6, 10, 6),
        };

        var mainGrid = new Grid();
        mainGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // Header
        mainGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) }); // Content

        // ================= HEADER =================
        var headerPanel = new StackPanel { Margin = new Thickness(0, 0, 0, 6) };

        // Header Row 1: Title + Summary on left, Refresh + Advanced on right
        var row1 = new DockPanel { LastChildFill = true, Margin = new Thickness(0, 0, 0, 4) };

        var leftStack = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        var iconText = new TextBlock
        {
            Text = "🎮 ",
            FontSize = 11,
            VerticalAlignment = VerticalAlignment.Center
        };
        leftStack.Children.Add(iconText);

        _txtTitle = new TextBlock
        {
            Text = "TAB GIẢ LẬP LDPLAYER",
            FontSize = 10.5,
            FontWeight = FontWeights.Bold,
            Foreground = (Brush)new BrushConverter().ConvertFromString("#9DA3AE"),
            VerticalAlignment = VerticalAlignment.Center
        };
        leftStack.Children.Add(_txtTitle);

        _txtSummary = new TextBlock
        {
            Text = "Chưa tải danh sách tab",
            FontSize = 10,
            Foreground = (Brush)new BrushConverter().ConvertFromString("#767B87"),
            Margin = new Thickness(8, 0, 0, 0),
            VerticalAlignment = VerticalAlignment.Center
        };
        leftStack.Children.Add(_txtSummary);
        DockPanel.SetDock(leftStack, Dock.Left);
        row1.Children.Add(leftStack);

        var row1Right = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };

        _btnRefresh = new Button
        {
            Content = "🔄 Làm mới",
            Style = (Style)Application.Current.Resources["Btn.Tool"],
            FontSize = 10,
            Padding = new Thickness(6, 2, 6, 2),
            Margin = new Thickness(0, 0, 4, 0),
            ToolTip = "Lấy danh sách các tab LDPlayer mới nhất trên máy"
        };
        _btnRefresh.Click += async (_, _) => await RefreshTabsAsync();
        row1Right.Children.Add(_btnRefresh);

        _btnMore = new Button
        {
            Content = "⚙ Quản lý...",
            Style = (Style)Application.Current.Resources["Btn.Tool"],
            FontSize = 10,
            Padding = new Thickness(6, 2, 6, 2),
            ToolTip = "Mở cửa sổ Quản lý nâng cao giả lập LDPlayer"
        };
        _btnMore.Click += (_, _) => OpenAdvancedManagerRequested?.Invoke();
        row1Right.Children.Add(_btnMore);

        DockPanel.SetDock(row1Right, Dock.Right);
        row1.Children.Add(row1Right);
        headerPanel.Children.Add(row1);

        // Header Row 2: Select All on left, Assign Macro + Repeat + Run + Stop on right
        var row2 = new DockPanel { LastChildFill = true };

        _chkSelectAll = new CheckBox
        {
            Content = "Chọn tất cả",
            FontSize = 10,
            Foreground = (Brush)new BrushConverter().ConvertFromString("#9DA3AE"),
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 8, 0),
            ToolTip = "Chọn hoặc bỏ chọn toàn bộ tab"
        };
        _chkSelectAll.Click += (_, _) => OnSelectAllClicked();
        DockPanel.SetDock(_chkSelectAll, Dock.Left);
        row2.Children.Add(_chkSelectAll);

        var row2Right = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };

        _btnAssignMacro = new Button
        {
            Content = "➕ Gán macro",
            Style = (Style)Application.Current.Resources["Btn.Tool"],
            FontSize = 10,
            FontWeight = FontWeights.Medium,
            Padding = new Thickness(6, 2, 6, 2),
            Margin = new Thickness(0, 0, 6, 0),
            ToolTip = "Gán macro cho các tab giả lập đang được tích chọn"
        };
        _btnAssignMacro.Click += (_, _) => OnAssignMacroClicked();
        row2Right.Children.Add(_btnAssignMacro);

        var lblRepeat = new TextBlock
        {
            Text = "Lặp:",
            FontSize = 10,
            Foreground = (Brush)new BrushConverter().ConvertFromString("#9DA3AE"),
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 4, 0),
            ToolTip = "Số lần lặp lại (0 = lặp vô hạn cho đến khi dừng)"
        };
        row2Right.Children.Add(lblRepeat);

        _numRepeat = new IntegerBox(0, 10000)
        {
            Value = 1,
            Width = 46,
            Margin = new Thickness(0, 0, 6, 0),
            ToolTip = "Số lần lặp lại khi chạy (0 = lặp vô hạn cho đến khi dừng)"
        };
        row2Right.Children.Add(_numRepeat);

        _btnRunTabs = new Button
        {
            Content = "▶ Chạy",
            Style = (Style)Application.Current.Resources["Btn.SectionGreen"] ?? (Style)Application.Current.Resources["Btn.Tool"],
            FontSize = 10,
            FontWeight = FontWeights.SemiBold,
            Padding = new Thickness(8, 2, 8, 2),
            Margin = new Thickness(0, 0, 2, 0),
            ToolTip = "Chạy các macro đã gán trên từng tab được chọn"
        };
        _btnRunTabs.Click += (_, _) => OnRunTabsClicked();
        row2Right.Children.Add(_btnRunTabs);

        _btnStopTabs = new Button
        {
            Content = "⏹ Dừng",
            Style = (Style)Application.Current.Resources["Btn.Tool"],
            FontSize = 10,
            FontWeight = FontWeights.SemiBold,
            Padding = new Thickness(8, 2, 8, 2),
            Foreground = new SolidColorBrush(Color.FromRgb(0xF0, 0x76, 0x4F)),
            Visibility = Visibility.Collapsed,
            ToolTip = "Dừng chạy macro trên các tab giả lập"
        };
        _btnStopTabs.Click += (_, _) => StopTabRuns();
        row2Right.Children.Add(_btnStopTabs);

        DockPanel.SetDock(row2Right, Dock.Right);
        row2.Children.Add(row2Right);
        headerPanel.Children.Add(row2);

        Grid.SetRow(headerPanel, 0);
        mainGrid.Children.Add(headerPanel);

        // ================= CONTENT =================
        var contentGrid = new Grid();

        _scrollViewer = new ScrollViewer
        {
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
        };
        _itemsPanel = new StackPanel { Margin = new Thickness(0, 2, 0, 2) };
        _scrollViewer.Content = _itemsPanel;
        contentGrid.Children.Add(_scrollViewer);

        // Empty state overlay
        var emptyPanel = new StackPanel
        {
            VerticalAlignment = VerticalAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Center,
            Margin = new Thickness(0, 10, 0, 10)
        };
        _emptyNotice = new TextBlock
        {
            Text = "Không có tab nào.",
            Foreground = (Brush)new BrushConverter().ConvertFromString("#888C96"),
            FontSize = 11,
            HorizontalAlignment = HorizontalAlignment.Center,
            TextAlignment = TextAlignment.Center
        };
        emptyPanel.Children.Add(_emptyNotice);

        _btnEmptyAction = new Button
        {
            Content = "Cấu hình ngay",
            Style = (Style)Application.Current.Resources["Btn.Tool"],
            FontSize = 10.5,
            Padding = new Thickness(8, 2, 8, 2),
            Margin = new Thickness(0, 6, 0, 0),
            HorizontalAlignment = HorizontalAlignment.Center,
            Visibility = Visibility.Collapsed
        };
        _btnEmptyAction.Click += (_, _) => ConfigureRequested?.Invoke();
        emptyPanel.Children.Add(_btnEmptyAction);

        contentGrid.Children.Add(emptyPanel);

        Grid.SetRow(contentGrid, 1);
        mainGrid.Children.Add(contentGrid);

        root.Child = mainGrid;
        Content = root;
    }

    public List<LdInstanceInfo> GetSelectedInstances() =>
        _instances.Where(i => i.IsSelected).ToList();

    public IReadOnlyList<LdInstanceInfo> GetAllInstances() => _instances;

    public void ApplyLanguage()
    {
        _txtTitle.Text = Loc.T("ld.panel.title", "TAB GIẢ LẬP LDPLAYER");
        _chkSelectAll.Content = Loc.T("ld.panel.select_all", "Chọn tất cả");
        _btnRefresh.Content = Loc.T("ld.panel.refresh", "🔄 Làm mới");
        _btnMore.Content = Loc.T("ld.panel.advanced", "⚙ Quản lý...");
        _btnAssignMacro.Content = Loc.T("ld.panel.assign", "➕ Gán macro");
        UpdateSummary();
    }

    public void StartMonitoring()
    {
        if (_monitorCts != null) return;
        _monitorCts = new CancellationTokenSource();
        var token = _monitorCts.Token;
        Task.Run(() => MonitorLoopAsync(token), token);
    }

    public void StopMonitoring()
    {
        if (_monitorCts != null)
        {
            try { _monitorCts.Cancel(); } catch { }
            _monitorCts = null;
        }
    }

    private async Task MonitorLoopAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(2000, ct);
            }
            catch (OperationCanceledException)
            {
                break;
            }

            if (_isBusy || _isPolling || !EmulatorManager.IsConfiguredAndValid)
                continue;

            var console = EmulatorManager.GetLdConsolePath();
            if (string.IsNullOrEmpty(console) || !File.Exists(console))
                continue;

            _isPolling = true;
            try
            {
                var (instances, error) = await LdPlayerService.GetInstancesAsync(console);
                if (ct.IsCancellationRequested) break;
                if (string.IsNullOrEmpty(error) && instances != null)
                {
                    await Dispatcher.InvokeAsync(() =>
                    {
                        UpdateInstanceStatesFromMonitor(instances);
                    }, System.Windows.Threading.DispatcherPriority.Background, ct);
                }
            }
            catch { }
            finally
            {
                _isPolling = false;
            }
        }
    }

    /// <summary>
    /// Updates live states of instances from background monitor query.
    /// Transitions booting -> "Đang chạy" and closed -> "Đã tắt" immediately.
    /// </summary>
    public void UpdateInstanceStatesFromMonitor(List<LdInstanceInfo> freshList)
    {
        if (freshList == null) return;

        // If list was empty and tabs now exist, load them
        if (_instances.Count == 0 && freshList.Count > 0)
        {
            _instances.Clear();
            foreach (var inst in freshList)
            {
                _instances.Add(inst);
            }
            if (_instances.Count > 0)
                _instances[0].IsSelected = true;
            RenderInstanceCards();
            UpdateSummary();
            return;
        }

        // If count or indices changed, resync full list while preserving selections & macros
        bool countOrIndexMismatch = freshList.Count != _instances.Count ||
            !freshList.Select(f => f.Index).SequenceEqual(_instances.Select(i => i.Index));

        if (countOrIndexMismatch)
        {
            var prevSelected = new HashSet<int>(_instances.Where(i => i.IsSelected).Select(i => i.Index));
            var prevMacros = _instances.ToDictionary(i => i.Index, i => i.AssignedMacros.ToList());
            _instances.Clear();
            foreach (var inst in freshList)
            {
                if (prevSelected.Contains(inst.Index)) inst.IsSelected = true;
                if (prevMacros.TryGetValue(inst.Index, out var macros))
                {
                    foreach (var m in macros) inst.AssignedMacros.Add(m);
                }
                _instances.Add(inst);
            }
            if (_instances.Count > 0 && !_instances.Any(i => i.IsSelected))
                _instances[0].IsSelected = true;
            RenderInstanceCards();
            UpdateSummary();
            return;
        }

        // Fast in-place property and state update
        foreach (var existing in _instances)
        {
            var fresh = freshList.FirstOrDefault(f => f.Index == existing.Index);
            if (fresh == null) continue;

            existing.TopHwnd = fresh.TopHwnd;
            existing.BindHwnd = fresh.BindHwnd;
            existing.Pid = fresh.Pid;
            existing.VBoxPid = fresh.VBoxPid;
            existing.ScreenWidth = fresh.ScreenWidth;
            existing.ScreenHeight = fresh.ScreenHeight;
            existing.Dpi = fresh.Dpi;
            if (!string.IsNullOrEmpty(fresh.Name) && existing.Name != fresh.Name)
                existing.Name = fresh.Name;

            bool topWindowGone = existing.TopHwnd != IntPtr.Zero && !LdPlayerService.IsWindow(existing.TopHwnd);

            // When a tab boots up: android_started == 1 AND window is physically visible & rendered
            if (fresh.IsRunning && !topWindowGone)
            {
                bool windowReady = fresh.TopHwnd != IntPtr.Zero && LdPlayerService.IsWindow(fresh.TopHwnd) && LdPlayerService.IsWindowVisible(fresh.TopHwnd);
                if (windowReady)
                {
                    if (!existing.IsRunning)
                    {
                        existing.IsRunning = true;
                        existing.OperationStatus = "Đang chạy";
                    }
                    else if (existing.OperationStatus.StartsWith("Đang khởi động", StringComparison.OrdinalIgnoreCase) ||
                             existing.OperationStatus.StartsWith("Thử lại", StringComparison.OrdinalIgnoreCase))
                    {
                        existing.OperationStatus = "Đang chạy";
                    }
                }
            }
            else
            {
                // When a tab is closed/stopped:
                if (existing.IsRunning)
                {
                    existing.IsRunning = false;
                    existing.OperationStatus = "Đã tắt";
                }
                else if (existing.OperationStatus.StartsWith("Đang chạy", StringComparison.OrdinalIgnoreCase))
                {
                    existing.OperationStatus = "Đã tắt";
                }
            }
        }
        UpdateSummary();
    }

    public async Task RefreshTabsAsync()
    {
        if (_isBusy) return;

        if (!EmulatorManager.IsConfiguredAndValid)
        {
            _instances.Clear();
            _itemsPanel.Children.Clear();
            _emptyNotice.Text = Loc.T("ld.panel.not_configured", "Chưa cấu hình trình giả lập LDPlayer. Vui lòng thiết lập để quản lý các tab.");
            _emptyNotice.Visibility = Visibility.Visible;
            _btnEmptyAction.Content = Loc.T("emu.banner.configure", "Cấu hình ngay");
            _btnEmptyAction.Visibility = Visibility.Visible;
            _txtSummary.Text = Loc.T("ld.panel.unconfigured_status", "Chưa cấu hình giả lập");
            return;
        }

        var consolePath = EmulatorManager.GetLdConsolePath();
        if (string.IsNullOrEmpty(consolePath) || !File.Exists(consolePath))
        {
            _instances.Clear();
            _itemsPanel.Children.Clear();
            _emptyNotice.Text = $"Không tìm thấy ldconsole.exe tại thư mục:\n{EmulatorManager.CurrentPath}";
            _emptyNotice.Visibility = Visibility.Visible;
            _btnEmptyAction.Content = "Cấu hình lại";
            _btnEmptyAction.Visibility = Visibility.Visible;
            _txtSummary.Text = "Lỗi đường dẫn";
            return;
        }

        _isBusy = true;
        _btnRefresh.IsEnabled = false;
        _txtSummary.Text = "Đang lấy danh sách tab...";

        try
        {
            var (instances, error) = await LdPlayerService.GetInstancesAsync(consolePath);

            if (!string.IsNullOrEmpty(error))
            {
                _emptyNotice.Text = $"Lỗi khi tải danh sách tab: {error}";
                _emptyNotice.Visibility = Visibility.Visible;
                _btnEmptyAction.Visibility = Visibility.Collapsed;
                _txtSummary.Text = "Lỗi truy vấn";
                return;
            }

            // Remember previous selections and assigned macros
            var prevSelectedIndices = new HashSet<int>(_instances.Where(i => i.IsSelected).Select(i => i.Index));
            var prevMacros = _instances.ToDictionary(i => i.Index, i => i.AssignedMacros.ToList());

            _instances.Clear();
            foreach (var inst in instances)
            {
                if (prevSelectedIndices.Contains(inst.Index))
                    inst.IsSelected = true;

                if (prevMacros.TryGetValue(inst.Index, out var macros))
                {
                    foreach (var m in macros)
                        inst.AssignedMacros.Add(m);
                }

                _instances.Add(inst);
            }

            // Default to selecting the first tab if none selected and tabs exist
            if (_instances.Count > 0 && !_instances.Any(i => i.IsSelected))
            {
                _instances[0].IsSelected = true;
            }

            RenderInstanceCards();
            UpdateSummary();
        }
        catch (Exception ex)
        {
            _emptyNotice.Text = $"Lỗi: {ex.Message}";
            _emptyNotice.Visibility = Visibility.Visible;
            _btnEmptyAction.Visibility = Visibility.Collapsed;
            _txtSummary.Text = "Lỗi";
        }
        finally
        {
            _isBusy = false;
            _btnRefresh.IsEnabled = true;
        }
    }

    public void RenderInstanceCards()
    {
        _itemsPanel.Children.Clear();

        if (_instances.Count == 0)
        {
            _emptyNotice.Text = "Không tìm thấy tab LDPlayer nào được tạo trên máy tính.";
            _emptyNotice.Visibility = Visibility.Visible;
            _btnEmptyAction.Visibility = Visibility.Collapsed;
            return;
        }

        _emptyNotice.Visibility = Visibility.Collapsed;
        _btnEmptyAction.Visibility = Visibility.Collapsed;

        foreach (var inst in _instances)
        {
            var card = CreateInstanceRow(inst);
            _itemsPanel.Children.Add(card);
        }
    }

    private FrameworkElement CreateInstanceRow(LdInstanceInfo inst)
    {
        var border = new Border
        {
            Background = (Brush)new BrushConverter().ConvertFromString(inst.IsSelected ? "#21252D" : "#1B1D22"),
            BorderBrush = (Brush)new BrushConverter().ConvertFromString(inst.IsSelected ? "#3E5879" : "#2A2D34"),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(4),
            Padding = new Thickness(6, 4, 6, 4),
            Margin = new Thickness(0, 0, 0, 4),
            Cursor = Cursors.Hand,
        };

        var cardStack = new StackPanel();

        // ------------------ Top Row: Tab Details & Quick Actions ------------------
        var rowGrid = new Grid();
        rowGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto }); // 0: CheckBox
        rowGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto }); // 1: Index Badge
        rowGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(135) }); // 2: Name
        rowGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto }); // 3: Status Badge
        rowGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) }); // 4: Operation status
        rowGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto }); // 5: Quick actions

        // 0: CheckBox
        var chk = new CheckBox
        {
            IsChecked = inst.IsSelected,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 6, 0),
        };
        chk.Click += (_, _) =>
        {
            inst.IsSelected = chk.IsChecked == true;
            UpdateCardAppearance(border, inst);
            UpdateSummary();
            SelectionChanged?.Invoke();
        };
        Grid.SetColumn(chk, 0);
        rowGrid.Children.Add(chk);

        // 1: Index Badge
        var badge = new Border
        {
            Background = (Brush)new BrushConverter().ConvertFromString("#272A31"),
            CornerRadius = new CornerRadius(3),
            Padding = new Thickness(5, 1, 5, 1),
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 6, 0),
        };
        badge.Child = new TextBlock
        {
            Text = $"#{inst.Index}",
            FontSize = 10,
            FontWeight = FontWeights.Bold,
            Foreground = (Brush)new BrushConverter().ConvertFromString("#9DA3AE"),
        };
        Grid.SetColumn(badge, 1);
        rowGrid.Children.Add(badge);

        // 2: Name
        var lblName = new TextBlock
        {
            Text = string.IsNullOrEmpty(inst.Name) ? $"LDPlayer-{inst.Index}" : inst.Name,
            FontSize = 11,
            FontWeight = FontWeights.SemiBold,
            Foreground = (Brush)new BrushConverter().ConvertFromString("#F2F3F5"),
            VerticalAlignment = VerticalAlignment.Center,
            TextTrimming = TextTrimming.CharacterEllipsis,
            Margin = new Thickness(0, 0, 6, 0),
        };
        Grid.SetColumn(lblName, 2);
        rowGrid.Children.Add(lblName);

        // 3: Status Badge
        var statusBorder = new Border
        {
            CornerRadius = new CornerRadius(3),
            Padding = new Thickness(5, 1, 5, 1),
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 8, 0),
        };
        var statusLabel = new TextBlock { FontSize = 10, FontWeight = FontWeights.Medium };
        statusBorder.Child = statusLabel;

        void ApplyStatusStyle()
        {
            if (inst.OperationStatus.StartsWith("Đang khởi động", StringComparison.OrdinalIgnoreCase) ||
                inst.OperationStatus.StartsWith("Thử lại", StringComparison.OrdinalIgnoreCase))
            {
                statusBorder.Background = (Brush)new BrushConverter().ConvertFromString("#2B2818");
                statusBorder.BorderBrush = (Brush)new BrushConverter().ConvertFromString("#E5C07B");
                statusBorder.BorderThickness = new Thickness(1);
                statusLabel.Foreground = (Brush)new BrushConverter().ConvertFromString("#E5C07B");
                statusLabel.Text = "🟡 Đang khởi động";
            }
            else if (inst.IsRunning)
            {
                statusBorder.Background = (Brush)new BrushConverter().ConvertFromString("#183222");
                statusBorder.BorderBrush = (Brush)Application.Current.Resources["Brush.SectionGreen"]
                                           ?? new SolidColorBrush(Color.FromRgb(0x38, 0xB2, 0x6C));
                statusBorder.BorderThickness = new Thickness(1);
                statusLabel.Foreground = statusBorder.BorderBrush;
                statusLabel.Text = "🟢 Đang chạy";
            }
            else
            {
                statusBorder.Background = (Brush)new BrushConverter().ConvertFromString("#23252A");
                statusBorder.BorderBrush = (Brush)new BrushConverter().ConvertFromString("#363842");
                statusBorder.BorderThickness = new Thickness(1);
                statusLabel.Foreground = (Brush)new BrushConverter().ConvertFromString("#8F939C");
                statusLabel.Text = "⚪ Đã tắt";
            }
        }
        ApplyStatusStyle();
        Grid.SetColumn(statusBorder, 3);
        rowGrid.Children.Add(statusBorder);

        // 4: Operation status text
        var lblOp = new TextBlock
        {
            Text = inst.OperationStatus,
            FontSize = 10.5,
            Foreground = (Brush)new BrushConverter().ConvertFromString("#8F939C"),
            VerticalAlignment = VerticalAlignment.Center,
            TextTrimming = TextTrimming.CharacterEllipsis,
            Margin = new Thickness(0, 0, 6, 0),
        };
        Grid.SetColumn(lblOp, 4);
        rowGrid.Children.Add(lblOp);

        // 5: Quick actions
        var actionsPanel = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };

        var btnLaunch = new Button
        {
            Content = "▶ Mở",
            Padding = new Thickness(5, 1, 5, 1),
            FontSize = 10,
            Margin = new Thickness(0, 0, 3, 0),
            ToolTip = "Khởi động tab này",
            Visibility = inst.IsRunning ? Visibility.Collapsed : Visibility.Visible
        };
        btnLaunch.Click += async (_, _) => await LaunchSingleInstanceAsync(inst);
        actionsPanel.Children.Add(btnLaunch);

        var btnQuit = new Button
        {
            Content = "⏹ Đóng",
            Padding = new Thickness(5, 1, 5, 1),
            FontSize = 10,
            Margin = new Thickness(0, 0, 3, 0),
            ToolTip = "Đóng tab này",
            Visibility = inst.IsRunning ? Visibility.Visible : Visibility.Collapsed
        };
        btnQuit.Click += async (_, _) => await QuitSingleInstanceAsync(inst);
        actionsPanel.Children.Add(btnQuit);

        var btnCapture = new Button
        {
            Content = "📷 Chụp",
            Padding = new Thickness(5, 1, 5, 1),
            FontSize = 10,
            ToolTip = "Chụp màn hình tab này và mở giao diện ảnh",
            IsEnabled = inst.IsRunning
        };
        btnCapture.Click += (_, _) => CaptureRequested?.Invoke(inst);
        actionsPanel.Children.Add(btnCapture);

        Grid.SetColumn(actionsPanel, 5);
        rowGrid.Children.Add(actionsPanel);
        cardStack.Children.Add(rowGrid);

        // ------------------ Bottom Row: Assigned Macros Chips ------------------
        var macrosRow = new DockPanel { Margin = new Thickness(24, 4, 0, 1), LastChildFill = true };

        var lblMacros = new TextBlock
        {
            Text = "Macro:",
            FontSize = 9.5,
            FontWeight = FontWeights.Medium,
            Foreground = (Brush)new BrushConverter().ConvertFromString("#767B87"),
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 5, 0),
        };
        DockPanel.SetDock(lblMacros, Dock.Left);
        macrosRow.Children.Add(lblMacros);

        var chipsWrap = new WrapPanel { VerticalAlignment = VerticalAlignment.Center };
        if (inst.AssignedMacros.Count == 0)
        {
            chipsWrap.Children.Add(new TextBlock
            {
                Text = "(Chưa gán macro)",
                FontSize = 9.5,
                FontStyle = FontStyles.Italic,
                Foreground = (Brush)new BrushConverter().ConvertFromString("#5C606B"),
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 6, 0),
            });
        }
        else
        {
            foreach (var m in inst.AssignedMacros)
            {
                var chipBorder = new Border
                {
                    Background = (Brush)new BrushConverter().ConvertFromString("#272A32"),
                    BorderBrush = (Brush)new BrushConverter().ConvertFromString("#3E4451"),
                    BorderThickness = new Thickness(1),
                    CornerRadius = new CornerRadius(3),
                    Padding = new Thickness(5, 1, 3, 1),
                    Margin = new Thickness(0, 0, 4, 2),
                    VerticalAlignment = VerticalAlignment.Center,
                };
                var chipStack = new StackPanel { Orientation = Orientation.Horizontal };
                chipStack.Children.Add(new TextBlock
                {
                    Text = $"📋 {m.Name}",
                    FontSize = 9.5,
                    Foreground = (Brush)new BrushConverter().ConvertFromString("#D7DAE0"),
                    VerticalAlignment = VerticalAlignment.Center,
                    Margin = new Thickness(0, 0, 4, 0),
                });
                var btnRemoveChip = new Button
                {
                    Content = "✕",
                    FontSize = 9,
                    FontWeight = FontWeights.Bold,
                    Foreground = new SolidColorBrush(Color.FromRgb(0xF0, 0x76, 0x4F)),
                    Background = Brushes.Transparent,
                    BorderThickness = new Thickness(0),
                    Padding = new Thickness(1, 0, 1, 0),
                    Cursor = Cursors.Hand,
                    ToolTip = $"Gỡ bỏ macro '{m.Name}' khỏi tab #{inst.Index}"
                };
                string macroId = m.Id;
                btnRemoveChip.Click += (s, e) =>
                {
                    e.Handled = true;
                    inst.RemoveMacro(macroId);
                    RenderInstanceCards();
                };
                chipStack.Children.Add(btnRemoveChip);
                chipBorder.Child = chipStack;
                chipsWrap.Children.Add(chipBorder);
            }
        }

        var btnAddMacroForTab = new Button
        {
            Content = "+ Thêm",
            FontSize = 9,
            Padding = new Thickness(4, 1, 4, 1),
            Margin = new Thickness(0, 0, 0, 2),
            Style = (Style)Application.Current.Resources["Btn.Tool"],
            ToolTip = $"Gán thêm macro cho Tab #{inst.Index}"
        };
        btnAddMacroForTab.Click += (s, e) =>
        {
            e.Handled = true;
            ShowAssignMacroMenu(new List<LdInstanceInfo> { inst }, btnAddMacroForTab);
        };
        chipsWrap.Children.Add(btnAddMacroForTab);

        macrosRow.Children.Add(chipsWrap);
        cardStack.Children.Add(macrosRow);

        // Hook property changes
        inst.PropertyChanged += (_, e) =>
        {
            Dispatcher.Invoke(() =>
            {
                if (e.PropertyName == nameof(inst.IsRunning))
                {
                    ApplyStatusStyle();
                    btnLaunch.Visibility = inst.IsRunning ? Visibility.Collapsed : Visibility.Visible;
                    btnQuit.Visibility = inst.IsRunning ? Visibility.Visible : Visibility.Collapsed;
                    btnCapture.IsEnabled = inst.IsRunning;
                    UpdateSummary();
                }
                else if (e.PropertyName == nameof(inst.OperationStatus))
                {
                    ApplyStatusStyle();
                    lblOp.Text = inst.OperationStatus;
                    if (inst.OperationStatus.Contains("thành công", StringComparison.OrdinalIgnoreCase) ||
                        inst.OperationStatus.Contains("hoàn tất", StringComparison.OrdinalIgnoreCase))
                        lblOp.Foreground = (Brush)new BrushConverter().ConvertFromString("#38B26C");
                    else if (inst.OperationStatus.Contains("thất bại", StringComparison.OrdinalIgnoreCase) ||
                             inst.OperationStatus.Contains("lỗi", StringComparison.OrdinalIgnoreCase) ||
                             inst.OperationStatus.Contains("quá thời gian", StringComparison.OrdinalIgnoreCase))
                        lblOp.Foreground = new SolidColorBrush(Color.FromRgb(0xF0, 0x76, 0x4F));
                    else if (inst.OperationStatus.Contains("Đang", StringComparison.OrdinalIgnoreCase) ||
                             inst.OperationStatus.Contains("Khởi động", StringComparison.OrdinalIgnoreCase))
                        lblOp.Foreground = (Brush)new BrushConverter().ConvertFromString("#61AFEF");
                    else
                        lblOp.Foreground = (Brush)new BrushConverter().ConvertFromString("#8F939C");
                }
                else if (e.PropertyName == nameof(inst.IsSelected))
                {
                    chk.IsChecked = inst.IsSelected;
                    UpdateCardAppearance(border, inst);
                }
            });
        };

        // Click row to toggle selection
        border.Child = cardStack;
        border.MouseLeftButtonDown += (_, e) =>
        {
            if (e.OriginalSource is not Button && e.OriginalSource is not CheckBox)
            {
                inst.IsSelected = !inst.IsSelected;
                chk.IsChecked = inst.IsSelected;
                UpdateCardAppearance(border, inst);
                UpdateSummary();
                SelectionChanged?.Invoke();
            }
        };

        return border;
    }

    private void UpdateCardAppearance(Border border, LdInstanceInfo inst)
    {
        border.Background = (Brush)new BrushConverter().ConvertFromString(inst.IsSelected ? "#21252D" : "#1B1D22");
        border.BorderBrush = (Brush)new BrushConverter().ConvertFromString(inst.IsSelected ? "#3E5879" : "#2A2D34");
    }

    private void OnSelectAllClicked()
    {
        if (_suppressSelectAllChange) return;
        bool check = _chkSelectAll.IsChecked == true;
        foreach (var inst in _instances)
        {
            inst.IsSelected = check;
        }
        UpdateSummary();
        SelectionChanged?.Invoke();
    }

    private void UpdateSummary()
    {
        int total = _instances.Count;
        int selected = _instances.Count(i => i.IsSelected);
        int running = _instances.Count(i => i.IsRunning);
        int stopped = total - running;

        var selectedList = _instances.Where(i => i.IsSelected).ToList();
        if (selectedList.Count == 1)
        {
            EmulatorManager.SelectedInstance = selectedList[0];
        }

        _txtSummary.Text = $"Đã chọn: {selected}/{total} tab  •  Chạy: {running}  •  Tắt: {stopped}";

        _suppressSelectAllChange = true;
        if (total > 0 && selected == total)
            _chkSelectAll.IsChecked = true;
        else if (selected == 0)
            _chkSelectAll.IsChecked = false;
        else
            _chkSelectAll.IsChecked = null; // Indeterminate
        _suppressSelectAllChange = false;
    }

    // ============================================================ MACRO ASSIGNMENT
    private void OnAssignMacroClicked()
    {
        var selected = GetSelectedInstances();
        if (selected.Count == 0)
        {
            MessageBox.Show(
                "Vui lòng chọn (tích ô) ít nhất một tab giả lập trong danh sách trước khi gán macro.",
                "Chưa chọn tab",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            return;
        }

        ShowAssignMacroMenu(selected, _btnAssignMacro);
    }

    private void ShowAssignMacroMenu(List<LdInstanceInfo> targetTabs, FrameworkElement placementTarget)
    {
        var mainWin = (Application.Current.MainWindow as MainWindow)
            ?? Application.Current?.Windows.OfType<MainWindow>().FirstOrDefault()
            ?? (Window.GetWindow(this) as MainWindow);
        var currentWf = mainWin?.CurrentWorkflow;

        var menu = new ContextMenu();

        if (currentWf != null && currentWf.CountSteps() > 0)
        {
            var miCurrent = new MenuItem
            {
                Header = $"📋 Gán quy trình đang mở: '{currentWf.Name}' ({currentWf.CountSteps()} bước)",
                FontWeight = FontWeights.SemiBold
            };
            miCurrent.Click += (_, _) =>
            {
                AssignWorkflowToTabs(targetTabs, currentWf.Name, null, currentWf);
            };
            menu.Items.Add(miCurrent);
            menu.Items.Add(new Separator());
        }

        var miFile = new MenuItem
        {
            Header = "📂 Chọn tệp macro từ máy tính (*.macro, *.json)..."
        };
        miFile.Click += (_, _) =>
        {
            var dlg = new Microsoft.Win32.OpenFileDialog
            {
                Title = "Chọn tệp macro để gán cho tab",
                Filter = WorkflowStorage.FileFilter,
                InitialDirectory = WorkflowStorage.DefaultFolder()
            };
            if (dlg.ShowDialog() == true)
            {
                try
                {
                    var (wf, warnings) = WorkflowStorage.Load(dlg.FileName);
                    string macroName = !string.IsNullOrWhiteSpace(wf.Name) ? wf.Name : Path.GetFileNameWithoutExtension(dlg.FileName);
                    AssignWorkflowToTabs(targetTabs, macroName, dlg.FileName, wf);
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Không thể mở tệp macro: {ex.Message}", "Lỗi tải macro", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        };
        menu.Items.Add(miFile);

        menu.PlacementTarget = placementTarget;
        menu.IsOpen = true;
    }

    private void AssignWorkflowToTabs(List<LdInstanceInfo> targets, string name, string filePath, Workflow workflow)
    {
        int successCount = 0;
        var skippedMessages = new List<string>();

        foreach (var inst in targets)
        {
            if (inst.TryAddMacro(name, filePath, workflow, out var reason))
            {
                successCount++;
            }
            else
            {
                skippedMessages.Add(reason);
            }
        }

        RenderInstanceCards();

        if (skippedMessages.Count > 0)
        {
            string skippedDetail = string.Join("\n• ", skippedMessages);
            if (successCount > 0)
            {
                MessageBox.Show(
                    $"Đã gán macro '{name}' thành công cho {successCount} tab.\n\nMột số tab bị bỏ qua để tránh trùng lặp:\n• {skippedDetail}",
                    "Gán macro cho tab",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
            }
            else
            {
                MessageBox.Show(
                    $"Không thể gán macro '{name}' do trùng lặp:\n• {skippedDetail}",
                    "Trùng lặp macro",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
            }
        }
    }

    // ============================================================ TAB MACRO RUNNER
    public void StopTabRuns()
    {
        if (_tabRunCts != null)
        {
            _tabRunCts.Cancel();
            _tabRunCts = null;
        }

        foreach (var inst in _instances)
        {
            if (inst.OperationStatus.StartsWith("Đang chạy", StringComparison.OrdinalIgnoreCase))
            {
                inst.OperationStatus = "Đã dừng";
            }
        }

        UpdateTabRunControls(false);
    }

    private void UpdateTabRunControls(bool running)
    {
        Dispatcher.Invoke(() =>
        {
            _btnRunTabs.Visibility = running ? Visibility.Collapsed : Visibility.Visible;
            _btnStopTabs.Visibility = running ? Visibility.Visible : Visibility.Collapsed;
            _btnAssignMacro.IsEnabled = !running;
            _numRepeat.IsEnabled = !running;
        });
    }

    private async void OnRunTabsClicked()
    {
        if (IsRunningTabs) return;

        var mainWin = (Application.Current?.MainWindow as MainWindow)
            ?? Application.Current?.Windows.OfType<MainWindow>().FirstOrDefault()
            ?? (Window.GetWindow(this) as MainWindow);
        if (mainWin != null && mainWin.IsEngineRunning)
        {
            MessageBox.Show(
                "Một macro đang chạy trên giao diện chính (F5).\nVui lòng dừng macro trước khi chạy hàng loạt trên các tab.",
                "Đang chạy macro",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            return;
        }

        var selected = GetSelectedInstances();
        if (selected.Count == 0)
        {
            MessageBox.Show(
                "Vui lòng chọn (tích ô) ít nhất một tab giả lập để chạy macro.",
                "Chạy macro trên tab",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            return;
        }

        // Check if any tab is not running
        var stopped = selected.Where(i => !i.IsRunning).ToList();
        if (stopped.Count > 0)
        {
            var stoppedNames = string.Join(", ", stopped.Select(s => $"#{s.Index} ({s.Name})"));
            MessageBox.Show(
                $"Các tab sau hiện đang tắt:\n{stoppedNames}\n\nVui lòng khởi động các tab này trước khi chạy macro.",
                "Tab chưa sẵn sàng",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            return;
        }

        // Check if any tab has no assigned macros
        var unassigned = selected.Where(i => i.AssignedMacros.Count == 0).ToList();
        if (unassigned.Count > 0)
        {
            var unassignedNames = string.Join(", ", unassigned.Select(u => $"Tab #{u.Index} ({u.Name})"));
            MessageBox.Show(
                $"Các tab sau chưa được gán macro nào:\n{unassignedNames}\n\nVui lòng nhấn 'Gán macro' cho các tab này trước khi chạy.",
                "Chưa gán macro",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            return;
        }

        int repeats = _numRepeat.Value; // 0 = infinite, >0 = exact count
        _tabRunCts = new CancellationTokenSource();
        var token = _tabRunCts.Token;

        UpdateTabRunControls(true);

        try
        {
            var tasks = selected.Select(inst => Task.Run(() => RunSingleTabMacros(inst, repeats, token), token)).ToList();
            await Task.WhenAll(tasks);
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            MessageBox.Show($"Lỗi trong quá trình chạy: {ex.Message}", "Lỗi", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            _tabRunCts = null;
            UpdateTabRunControls(false);
        }
    }

    private void RunSingleTabMacros(LdInstanceInfo inst, int repeats, CancellationToken ct)
    {
        var macros = inst.AssignedMacros.ToList();
        if (macros.Count == 0) return;

        var resume = new ManualResetEventSlim(true);
        var ctx = new Macro.Core.ExecutionContext(ct, resume, _ => { });

        int round = 0;
        try
        {
            while (true)
            {
                ct.ThrowIfCancellationRequested();
                round++;
                if (repeats > 0 && round > repeats)
                    break;

                string roundText = repeats == 0 ? $"Vòng {round} (Vô hạn)" : $"Vòng {round}/{repeats}";

                foreach (var macroItem in macros)
                {
                    ct.ThrowIfCancellationRequested();
                    var wf = macroItem.Workflow;
                    if (wf == null && !string.IsNullOrEmpty(macroItem.FilePath) && File.Exists(macroItem.FilePath))
                    {
                        var (loadedWf, _) = WorkflowStorage.Load(macroItem.FilePath);
                        wf = loadedWf;
                    }
                    if (wf == null || wf.Actions.Count == 0) continue;

                    var clonedWf = wf.Clone();
                    clonedWf.BindToTab(inst.Index);

                    var engine = new ExecutionEngine();
                    engine.StepStarted += stepId =>
                    {
                        var step = clonedWf.Find(stepId);
                        string stepName = step != null && !string.IsNullOrEmpty(step.Name) ? step.Name : (step != null ? step.Type : "");
                        inst.OperationStatus = $"Đang chạy: {macroItem.Name} ({roundText}) - {stepName}";
                    };

                    inst.OperationStatus = $"Đang chạy: {macroItem.Name} ({roundText})";
                    ctx.Workflow = clonedWf;
                    ctx.WorkflowFilePath = macroItem.FilePath;
                    engine.Workflow = clonedWf;
                    engine.WorkflowFilePath = macroItem.FilePath;
                    engine.RunWorkflowInline(clonedWf, ctx);
                }
            }

            string finishText = repeats == 0 ? $"Hoàn tất ({round - 1} vòng)" : $"Hoàn tất ({repeats} vòng)";
            inst.OperationStatus = finishText;
        }
        catch (StopRequestedException)
        {
            inst.OperationStatus = "Đã dừng";
        }
        catch (OperationCanceledException)
        {
            inst.OperationStatus = "Đã dừng";
        }
        catch (ActionFailureException ex)
        {
            inst.OperationStatus = $"Lỗi: {ex.Message}";
        }
        catch (Exception ex)
        {
            inst.OperationStatus = $"Lỗi: {ex.Message}";
        }
    }

    // ============================================================ INSTANCE LAUNCH/QUIT
    private async Task LaunchSingleInstanceAsync(LdInstanceInfo inst)
    {
        var console = EmulatorManager.GetLdConsolePath();
        if (string.IsNullOrEmpty(console) || !File.Exists(console)) return;

        var launchOptions = EmulatorManager.LoadLaunchOptions();
        int maxWait = launchOptions.MaxWaitSeconds;

        inst.IsRunning = false;
        inst.OperationStatus = "Đang khởi động...";

        await LdPlayerService.LaunchInstanceAsync(console, inst.Index);

        var (ok, elapsedSec) = await LdPlayerService.WaitUntilReadyDetailedAsync(console, inst.Index, maxWait, CancellationToken.None,
            elapsed =>
            {
                Dispatcher.Invoke(() => inst.OperationStatus = $"Đang khởi động... ({elapsed}s/{maxWait}s)");
            });

        if (ok)
        {
            inst.IsRunning = true;
            inst.OperationStatus = "Đang chạy";
        }
        else
        {
            inst.IsRunning = false;
            inst.OperationStatus = $"Quá thời gian ({elapsedSec:F0}s/{maxWait}s)";
            await LdPlayerService.QuitInstanceAsync(console, inst.Index);
        }
    }

    private async Task QuitSingleInstanceAsync(LdInstanceInfo inst)
    {
        var console = EmulatorManager.GetLdConsolePath();
        if (string.IsNullOrEmpty(console) || !File.Exists(console)) return;

        inst.OperationStatus = "Đang đóng...";
        await LdPlayerService.QuitInstanceAsync(console, inst.Index);
        inst.IsRunning = false;
        inst.OperationStatus = "Đã tắt";
    }
}
