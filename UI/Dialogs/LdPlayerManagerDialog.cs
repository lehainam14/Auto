using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Macro.Core.Emulator;
using Macro.Localization;
using Macro.UI.Widgets;
using Microsoft.Win32;

namespace Macro.UI.Dialogs;

/// <summary>
/// Dialog for managing LDPlayer instances/tabs:
/// - Viewing and refreshing instance list and real-time running statuses.
/// - Selecting single or multiple tabs.
/// - Launching tabs sequentially with configurable interval, timeout, and retries.
/// - Stopping the sequential launch.
/// - Quitting tabs.
/// - Configuring/changing LDPlayer base directory.
/// </summary>
public class LdPlayerManagerDialog : Window
{
    private readonly ObservableCollection<LdInstanceInfo> _instances = new();
    private readonly TextBlock _txtDirInfo;
    private readonly TextBlock _txtSummary;
    private readonly CheckBox _chkSelectAll;
    private readonly StackPanel _instanceListPanel;
    private readonly ScrollViewer _scrollInstances;
    private readonly TextBlock _txtEmptyNotice;
    private readonly IntegerBox _numInterval;
    private readonly IntegerBox _numTimeout;
    private readonly IntegerBox _numRetries;
    private readonly ProgressBar _progressBar;
    private readonly TextBlock _txtProgress;
    private readonly TextBox _txtLog;
    private readonly Button _btnFetch;
    private readonly Button _btnCaptureTab;
    private readonly Button _btnStartSelected;
    private readonly Button _btnStopSequence;
    private readonly Button _btnCloseSelected;
    private readonly Border _bannerNotice;
    private readonly TextBlock _bannerNoticeText;

    private CancellationTokenSource _cts;
    private bool _isBusy;
    private bool _suppressSelectAllChange;

    public LdPlayerManagerDialog(Window owner = null)
    {
        Owner = owner;
        Title = Loc.T("ld.manager.title", "Quản lý trình giả lập LDPlayer");
        Width = 740;
        Height = 650;
        MinWidth = 620;
        MinHeight = 520;
        WindowStartupLocation = owner != null ? WindowStartupLocation.CenterOwner : WindowStartupLocation.CenterScreen;
        Background = (Brush)new BrushConverter().ConvertFromString("#1F2023");
        Foreground = (Brush)new BrushConverter().ConvertFromString("#E8E9EC");

        var mainGrid = new Grid { Margin = new Thickness(14, 12, 14, 12) };
        mainGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // Header & Path
        mainGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // Banner notice
        mainGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) }); // Instances list
        mainGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // Settings & Logs
        mainGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // Bottom action buttons

        // ==========================================
        // ROW 0: Header & Directory Card
        // ==========================================
        var topStack = new StackPanel { Margin = new Thickness(0, 0, 0, 8) };

        var headerGrid = new Grid { Margin = new Thickness(0, 0, 0, 8) };
        headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        var icon = Ui.IconImage("application", "#38B26C", 22);
        icon.Margin = new Thickness(0, 2, 8, 0);
        icon.VerticalAlignment = VerticalAlignment.Top;
        Grid.SetColumn(icon, 0);
        headerGrid.Children.Add(icon);

        var headerText = new StackPanel();
        var lblTitle = Ui.Label(Loc.T("ld.manager.header", "QUẢN LÝ TRÌNH GIẢ LẬP LDPLAYER"),
            (Brush)new BrushConverter().ConvertFromString("#F2F3F5"), 13, true);
        var lblSub = new TextBlock
        {
            Text = Loc.T("ld.manager.desc", "Danh sách các tab giả lập đã tạo, trạng thái thực tế và công cụ khởi động tuần tự."),
            Foreground = (Brush)new BrushConverter().ConvertFromString("#8F939C"),
            FontSize = 11,
            Margin = new Thickness(0, 2, 0, 0),
        };
        headerText.Children.Add(lblTitle);
        headerText.Children.Add(lblSub);
        Grid.SetColumn(headerText, 1);
        headerGrid.Children.Add(headerText);
        topStack.Children.Add(headerGrid);

        // Path Card
        var pathCard = new Border
        {
            Background = (Brush)new BrushConverter().ConvertFromString("#161719"),
            BorderBrush = (Brush)new BrushConverter().ConvertFromString("#2E3038"),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(4),
            Padding = new Thickness(8, 5, 8, 5),
            Margin = new Thickness(0, 0, 0, 4),
        };
        var pathGrid = new Grid();
        pathGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        pathGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        _txtDirInfo = new TextBlock
        {
            FontSize = 11,
            VerticalAlignment = VerticalAlignment.Center,
            Foreground = (Brush)new BrushConverter().ConvertFromString("#D0D3D9"),
            TextTrimming = TextTrimming.CharacterEllipsis,
        };
        Grid.SetColumn(_txtDirInfo, 0);
        pathGrid.Children.Add(_txtDirInfo);

        var btnChangeDir = new Button
        {
            Content = Loc.T("ld.manager.change_dir", "📁 Đổi thư mục..."),
            Padding = new Thickness(8, 2, 8, 2),
            FontSize = 10.5,
            Margin = new Thickness(8, 0, 0, 0),
        };
        btnChangeDir.Click += (_, _) => ChangeDirectory();
        Grid.SetColumn(btnChangeDir, 1);
        pathGrid.Children.Add(btnChangeDir);
        pathCard.Child = pathGrid;
        topStack.Children.Add(pathCard);

        Grid.SetRow(topStack, 0);
        mainGrid.Children.Add(topStack);

        // ==========================================
        // ROW 1: Notice Banner (for warnings/errors)
        // ==========================================
        _bannerNotice = new Border
        {
            Background = (Brush)new BrushConverter().ConvertFromString("#2C1D18"),
            BorderBrush = new SolidColorBrush(Color.FromRgb(0xF0, 0x76, 0x4F)),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(4),
            Padding = new Thickness(8, 6, 8, 6),
            Margin = new Thickness(0, 0, 0, 6),
            Visibility = Visibility.Collapsed,
        };
        _bannerNoticeText = new TextBlock
        {
            Foreground = new SolidColorBrush(Color.FromRgb(0xF0, 0x76, 0x4F)),
            FontSize = 11,
            TextWrapping = TextWrapping.Wrap,
        };
        _bannerNotice.Child = _bannerNoticeText;
        Grid.SetRow(_bannerNotice, 1);
        mainGrid.Children.Add(_bannerNotice);

        // ==========================================
        // ROW 2: Instances List Area
        // ==========================================
        var listContainer = new Grid { Margin = new Thickness(0, 2, 0, 6) };
        listContainer.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // Toolbar
        listContainer.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) }); // Scroll list

        // List Toolbar
        var listToolbar = new Grid { Margin = new Thickness(0, 0, 0, 6) };
        listToolbar.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        listToolbar.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        listToolbar.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        listToolbar.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        listToolbar.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        _btnFetch = new Button
        {
            Content = Loc.T("ld.manager.fetch_btn", "🔄 Lấy danh sách giả lập"),
            Padding = new Thickness(10, 3.5, 10, 3.5),
            FontSize = 11,
            FontWeight = FontWeights.SemiBold,
            Style = (Style)Application.Current.Resources["Btn.Tool"],
            Margin = new Thickness(0, 0, 8, 0),
        };
        _btnFetch.Click += async (_, _) => await LoadInstancesAsync();
        Grid.SetColumn(_btnFetch, 0);
        listToolbar.Children.Add(_btnFetch);

        _btnCaptureTab = new Button
        {
            Content = Loc.T("ld.manager.capture_btn", "📷 Chụp màn hình"),
            Padding = new Thickness(10, 3.5, 10, 3.5),
            FontSize = 11,
            FontWeight = FontWeights.SemiBold,
            Style = (Style)Application.Current.Resources["Btn.Tool"],
            Margin = new Thickness(0, 0, 10, 0),
            ToolTip = Loc.T("ld.manager.capture_tip", "Chụp màn hình tab giả lập đang được chọn và mở trong trình xem ảnh"),
        };
        _btnCaptureTab.Click += async (_, _) => await CaptureSelectedTabAsync();
        Grid.SetColumn(_btnCaptureTab, 1);
        listToolbar.Children.Add(_btnCaptureTab);

        _chkSelectAll = new CheckBox
        {
            Content = Loc.T("ld.manager.select_all", "Chọn tất cả"),
            FontSize = 11,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 10, 0),
        };
        _chkSelectAll.Click += (_, _) => OnSelectAllClicked();
        Grid.SetColumn(_chkSelectAll, 2);
        listToolbar.Children.Add(_chkSelectAll);

        _txtSummary = new TextBlock
        {
            FontSize = 11,
            Foreground = (Brush)new BrushConverter().ConvertFromString("#8F939C"),
            VerticalAlignment = VerticalAlignment.Center,
            Text = "Đang tải dữ liệu...",
        };
        Grid.SetColumn(_txtSummary, 3);
        listToolbar.Children.Add(_txtSummary);

        _btnCloseSelected = new Button
        {
            Content = Loc.T("ld.manager.close_selected", "⏹ Đóng các tab đã chọn"),
            Padding = new Thickness(8, 3, 8, 3),
            FontSize = 10.5,
            Margin = new Thickness(6, 0, 0, 0),
        };
        _btnCloseSelected.Click += async (_, _) => await CloseSelectedInstancesAsync();
        Grid.SetColumn(_btnCloseSelected, 4);
        listToolbar.Children.Add(_btnCloseSelected);

        Grid.SetRow(listToolbar, 0);
        listContainer.Children.Add(listToolbar);

        // Instance Items List
        var listBorder = new Border
        {
            Background = (Brush)new BrushConverter().ConvertFromString("#161719"),
            BorderBrush = (Brush)new BrushConverter().ConvertFromString("#2E3038"),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(4),
            Padding = new Thickness(6),
        };

        _scrollInstances = new ScrollViewer
        {
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
        };

        var contentHolder = new Grid();

        _txtEmptyNotice = new TextBlock
        {
            Text = Loc.T("ld.manager.empty", "Chưa có tab giả lập nào trong danh sách.\nNhấn 'Lấy danh sách giả lập' để quét các tab LDPlayer trên máy."),
            FontSize = 11,
            Foreground = (Brush)new BrushConverter().ConvertFromString("#6B707B"),
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            TextAlignment = TextAlignment.Center,
            Margin = new Thickness(20),
            Visibility = Visibility.Visible,
        };
        contentHolder.Children.Add(_txtEmptyNotice);

        _instanceListPanel = new StackPanel();
        contentHolder.Children.Add(_instanceListPanel);

        _scrollInstances.Content = contentHolder;
        listBorder.Child = _scrollInstances;
        Grid.SetRow(listBorder, 1);
        listContainer.Children.Add(listBorder);

        Grid.SetRow(listContainer, 2);
        mainGrid.Children.Add(listContainer);

        // ==========================================
        // ROW 3: Sequential Startup Settings & Progress & Log
        // ==========================================
        var settingsSection = new StackPanel { Margin = new Thickness(0, 4, 0, 6) };
        settingsSection.Children.Add(Ui.SectionHeader(Loc.T("ld.manager.section_seq", "CẤU HÌNH KHỞI ĐỘNG TUẦN TỰ")));

        // Settings Inputs Grid
        var savedOptions = EmulatorManager.LoadLaunchOptions();

        var configGrid = new Grid { Margin = new Thickness(0, 4, 0, 6) };
        configGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        configGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        configGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        // Setting 1: Interval
        var col1 = new StackPanel { Margin = new Thickness(0, 0, 6, 0) };
        col1.Children.Add(Ui.Label(Loc.T("ld.manager.interval_lbl", "Khoảng chờ giữa mỗi tab (giây):"),
            (Brush)new BrushConverter().ConvertFromString("#8F939C"), 10.5));
        _numInterval = new IntegerBox(0, 300)
        {
            Value = savedOptions.IntervalSeconds,
            ToolTip = Loc.T("ld.manager.interval_tip", "Số giây tạm dừng sau khi tab trước mở trước khi mở tab tiếp theo (ví dụ: 5s)."),
            Margin = new Thickness(0, 2, 0, 0),
        };
        _numInterval.ValueChanged += _ => PersistLaunchOptions();
        col1.Children.Add(_numInterval);
        Grid.SetColumn(col1, 0);
        configGrid.Children.Add(col1);

        // Setting 2: Max Wait Time
        var col2 = new StackPanel { Margin = new Thickness(6, 0, 6, 0) };
        col2.Children.Add(Ui.Label(Loc.T("ld.manager.timeout_lbl", "Thời gian tối đa chờ mở (giây):"),
            (Brush)new BrushConverter().ConvertFromString("#8F939C"), 10.5));
        _numTimeout = new IntegerBox(5, 300)
        {
            Value = savedOptions.MaxWaitSeconds,
            ToolTip = Loc.T("ld.manager.timeout_tip", "Thời gian tối đa chờ tab khởi động xong. Nếu quá hạn sẽ đóng tab và thử lại (ví dụ: 60s)."),
            Margin = new Thickness(0, 2, 0, 0),
        };
        _numTimeout.ValueChanged += _ => PersistLaunchOptions();
        col2.Children.Add(_numTimeout);
        Grid.SetColumn(col2, 1);
        configGrid.Children.Add(col2);

        // Setting 3: Retries
        var col3 = new StackPanel { Margin = new Thickness(6, 0, 0, 0) };
        col3.Children.Add(Ui.Label(Loc.T("ld.manager.retries_lbl", "Số lần thử lại khi quá hạn:"),
            (Brush)new BrushConverter().ConvertFromString("#8F939C"), 10.5));
        _numRetries = new IntegerBox(0, 10)
        {
            Value = savedOptions.MaxRetries,
            ToolTip = Loc.T("ld.manager.retries_tip", "Số lần thử khởi động lại nếu tab chưa chạy trong thời gian tối đa (mặc định: 1 lần)."),
            Margin = new Thickness(0, 2, 0, 0),
        };
        _numRetries.ValueChanged += _ => PersistLaunchOptions();
        col3.Children.Add(_numRetries);
        Grid.SetColumn(col3, 2);
        configGrid.Children.Add(col3);

        settingsSection.Children.Add(configGrid);

        // Progress Bar & Label
        var progGrid = new Grid { Margin = new Thickness(0, 2, 0, 4) };
        progGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        progGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        _txtProgress = new TextBlock
        {
            FontSize = 10.5,
            Foreground = (Brush)new BrushConverter().ConvertFromString("#8F939C"),
            Text = "Tiến trình khởi động: Sẵn sàng",
        };
        Grid.SetColumn(_txtProgress, 0);
        progGrid.Children.Add(_txtProgress);
        settingsSection.Children.Add(progGrid);

        _progressBar = new ProgressBar
        {
            Height = 4,
            Value = 0,
            Maximum = 100,
            Foreground = (Brush)Application.Current.Resources["Brush.SectionGreen"]
                         ?? new SolidColorBrush(Color.FromRgb(0x38, 0xB2, 0x6C)),
            Background = (Brush)new BrushConverter().ConvertFromString("#161719"),
            BorderThickness = new Thickness(0),
            Margin = new Thickness(0, 0, 0, 4),
        };
        settingsSection.Children.Add(_progressBar);

        // Activity Log Box
        _txtLog = new TextBox
        {
            Height = 70,
            FontSize = 10.5,
            FontFamily = new FontFamily("Consolas, Segoe UI"),
            Background = (Brush)new BrushConverter().ConvertFromString("#111214"),
            Foreground = (Brush)new BrushConverter().ConvertFromString("#B0B4BC"),
            BorderBrush = (Brush)new BrushConverter().ConvertFromString("#2E3038"),
            BorderThickness = new Thickness(1),
            IsReadOnly = true,
            TextWrapping = TextWrapping.Wrap,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            Padding = new Thickness(6, 4, 6, 4),
        };
        settingsSection.Children.Add(_txtLog);

        Grid.SetRow(settingsSection, 3);
        mainGrid.Children.Add(settingsSection);

        // ==========================================
        // ROW 4: Bottom Controls
        // ==========================================
        var bottomGrid = new Grid { Margin = new Thickness(0, 6, 0, 0) };
        bottomGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        bottomGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        bottomGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var leftBtns = new StackPanel { Orientation = Orientation.Horizontal };
        Grid.SetColumn(leftBtns, 0);
        bottomGrid.Children.Add(leftBtns);

        var rightBtns = new StackPanel { Orientation = Orientation.Horizontal };

        _btnStopSequence = new Button
        {
            Content = Loc.T("ld.manager.stop_btn", "⏹ Dừng khởi động"),
            Padding = new Thickness(12, 4, 12, 4),
            FontSize = 11,
            IsEnabled = false,
            Foreground = new SolidColorBrush(Color.FromRgb(0xF0, 0x76, 0x4F)),
            Margin = new Thickness(0, 0, 8, 0),
        };
        _btnStopSequence.Click += (_, _) => StopLaunchSequence();
        rightBtns.Children.Add(_btnStopSequence);

        _btnStartSelected = new Button
        {
            Content = Loc.T("ld.manager.start_selected", "▶ Khởi động các tab đã chọn"),
            Padding = new Thickness(14, 4, 14, 4),
            FontSize = 11.5,
            FontWeight = FontWeights.Bold,
            Background = (Brush)Application.Current.Resources["Brush.SectionGreen"]
                         ?? new SolidColorBrush(Color.FromRgb(0x38, 0xB2, 0x6C)),
            Foreground = Brushes.White,
            Margin = new Thickness(0, 0, 8, 0),
        };
        _btnStartSelected.Click += async (_, _) => await StartSelectedSequentiallyAsync();
        rightBtns.Children.Add(_btnStartSelected);

        var btnCloseWindow = new Button
        {
            Content = Loc.T("ld.manager.close_dialog", "Đóng"),
            Padding = new Thickness(12, 4, 12, 4),
            FontSize = 11,
        };
        btnCloseWindow.Click += (_, _) => Close();
        rightBtns.Children.Add(btnCloseWindow);

        Grid.SetColumn(rightBtns, 2);
        bottomGrid.Children.Add(rightBtns);

        Grid.SetRow(bottomGrid, 4);
        mainGrid.Children.Add(bottomGrid);

        Content = mainGrid;

        // Auto load on open
        Loaded += async (_, _) =>
        {
            UpdateDirectoryDisplay();
            await LoadInstancesAsync();
        };

        Closing += (_, e) =>
        {
            if (_isBusy)
            {
                var res = MessageBox.Show(this,
                    Loc.T("ld.manager.closing_busy", "Chuỗi khởi động tab đang chạy. Bạn có muốn dừng và đóng cửa sổ không?"),
                    Loc.T("dlg.app.title", "macro"),
                    MessageBoxButton.YesNo, MessageBoxImage.Question);
                if (res == MessageBoxResult.No)
                {
                    e.Cancel = true;
                    return;
                }
                StopLaunchSequence();
            }
        };
    }

    private void UpdateDirectoryDisplay()
    {
        var dir = EmulatorManager.CurrentPath;
        var console = EmulatorManager.GetLdConsolePath();
        if (!string.IsNullOrEmpty(console) && File.Exists(console))
        {
            _txtDirInfo.Text = $"Thư mục: {dir}  •  ldconsole.exe: Sẵn sàng";
            _bannerNotice.Visibility = Visibility.Collapsed;
        }
        else if (string.IsNullOrWhiteSpace(dir))
        {
            _txtDirInfo.Text = "Chưa cấu hình thư mục LDPlayer.";
            ShowBanner("Chưa cấu hình đường dẫn LDPlayer. Vui lòng nhấn 'Đổi thư mục...' để chọn.");
        }
        else
        {
            _txtDirInfo.Text = $"Thư mục: {dir}  •  Không tìm thấy ldconsole.exe";
            ShowBanner($"Không tìm thấy tệp ldconsole.exe tại thư mục: {dir}\nVui lòng kiểm tra lại hoặc đổi sang thư mục cài đặt LDPlayer chính xác.");
        }
    }

    private void ShowBanner(string message)
    {
        _bannerNoticeText.Text = message;
        _bannerNotice.Visibility = Visibility.Visible;
    }

    private void Log(string message)
    {
        Dispatcher.Invoke(() =>
        {
            var time = DateTime.Now.ToString("HH:mm:ss");
            _txtLog.AppendText($"[{time}] {message}\n");
            _txtLog.ScrollToEnd();
        });
    }

    private void PersistLaunchOptions()
    {
        var options = new LdLaunchOptions
        {
            IntervalSeconds = _numInterval.Value,
            MaxWaitSeconds = _numTimeout.Value,
            MaxRetries = _numRetries.Value,
        };
        EmulatorManager.SaveLaunchOptions(options);
    }

    private void ChangeDirectory()
    {
        var setup = new EmulatorSetupDialog(this);
        if (setup.ShowDialog() == true)
        {
            UpdateDirectoryDisplay();
            _ = LoadInstancesAsync();
        }
    }

    private async Task LoadInstancesAsync()
    {
        if (_isBusy) return;

        var consolePath = EmulatorManager.GetLdConsolePath();
        if (string.IsNullOrEmpty(consolePath) || !File.Exists(consolePath))
        {
            var currentDir = EmulatorManager.CurrentPath;
            var msg = string.IsNullOrWhiteSpace(currentDir)
                ? "Chưa cấu hình thư mục LDPlayer. Vui lòng chọn thư mục cài đặt LDPlayer."
                : $"Không tìm thấy công cụ ldconsole.exe trong thư mục: {currentDir}.\nHãy đảm bảo thư mục bạn chọn chứa tệp 'ldconsole.exe' hoặc 'LDPlayer9'.";

            ShowBanner(msg);
            _txtSummary.Text = "Lỗi: Không tìm thấy ldconsole.exe";
            _txtEmptyNotice.Text = msg;
            _txtEmptyNotice.Visibility = Visibility.Visible;
            _instanceListPanel.Children.Clear();
            _instances.Clear();
            return;
        }

        _btnFetch.IsEnabled = false;
        _txtSummary.Text = "Đang lấy danh sách tab LDPlayer...";
        Log("Đang truy vấn danh sách tab LDPlayer từ ldconsole...");

        var (instances, error) = await LdPlayerService.GetInstancesAsync(consolePath);

        _btnFetch.IsEnabled = true;

        if (!string.IsNullOrEmpty(error))
        {
            ShowBanner($"Lỗi khi lấy danh sách tab: {error}");
            Log($"✘ Lỗi: {error}");
            _txtSummary.Text = "Lỗi truy vấn";
            return;
        }

        _bannerNotice.Visibility = Visibility.Collapsed;
        _instances.Clear();
        foreach (var inst in instances)
        {
            // Default: keep selection if user wants or select none
            inst.IsSelected = false;
            _instances.Add(inst);
        }

        RenderInstanceCards();
        UpdateSummary();
        Log($"✔ Đã tải {instances.Count} tab LDPlayer thành công.");
    }

    private void RenderInstanceCards()
    {
        _instanceListPanel.Children.Clear();

        if (_instances.Count == 0)
        {
            _txtEmptyNotice.Text = "Không tìm thấy tab LDPlayer nào được tạo trên máy tính.";
            _txtEmptyNotice.Visibility = Visibility.Visible;
            return;
        }

        _txtEmptyNotice.Visibility = Visibility.Collapsed;

        foreach (var inst in _instances)
        {
            var card = CreateInstanceRow(inst);
            _instanceListPanel.Children.Add(card);
        }
    }

    private FrameworkElement CreateInstanceRow(LdInstanceInfo inst)
    {
        var border = new Border
        {
            Background = (Brush)new BrushConverter().ConvertFromString("#1D1E22"),
            BorderBrush = (Brush)new BrushConverter().ConvertFromString("#2E3038"),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(4),
            Padding = new Thickness(8, 6, 8, 6),
            Margin = new Thickness(0, 0, 0, 4),
        };

        var rowGrid = new Grid();
        rowGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto }); // Checkbox
        rowGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto }); // Index Badge
        rowGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(140) }); // Name
        rowGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto }); // Status Badge
        rowGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) }); // Detail status
        rowGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto }); // Actions

        // Checkbox
        var chk = new CheckBox
        {
            IsChecked = inst.IsSelected,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 8, 0),
        };
        chk.Click += (_, _) =>
        {
            inst.IsSelected = chk.IsChecked == true;
            UpdateSummary();
        };
        Grid.SetColumn(chk, 0);
        rowGrid.Children.Add(chk);

        // Index Badge
        var badge = new Border
        {
            Background = (Brush)new BrushConverter().ConvertFromString("#282A30"),
            CornerRadius = new CornerRadius(3),
            Padding = new Thickness(5, 1, 5, 1),
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 8, 0),
        };
        var badgeText = new TextBlock
        {
            Text = $"#{inst.Index}",
            FontSize = 10.5,
            FontWeight = FontWeights.Bold,
            Foreground = (Brush)new BrushConverter().ConvertFromString("#9DA3AE"),
        };
        badge.Child = badgeText;
        Grid.SetColumn(badge, 1);
        rowGrid.Children.Add(badge);

        // Name
        var lblName = new TextBlock
        {
            Text = string.IsNullOrEmpty(inst.Name) ? $"LDPlayer-{inst.Index}" : inst.Name,
            FontSize = 11.5,
            FontWeight = FontWeights.SemiBold,
            Foreground = (Brush)new BrushConverter().ConvertFromString("#F2F3F5"),
            VerticalAlignment = VerticalAlignment.Center,
            TextTrimming = TextTrimming.CharacterEllipsis,
            Margin = new Thickness(0, 0, 8, 0),
        };
        Grid.SetColumn(lblName, 2);
        rowGrid.Children.Add(lblName);

        // Status Badge
        var statusBorder = new Border
        {
            CornerRadius = new CornerRadius(3),
            Padding = new Thickness(6, 2, 6, 2),
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 8, 0),
        };
        var statusLabel = new TextBlock { FontSize = 10.5, FontWeight = FontWeights.Medium };
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

        // Operation detail text
        var lblOp = new TextBlock
        {
            Text = inst.OperationStatus,
            FontSize = 11,
            Foreground = (Brush)new BrushConverter().ConvertFromString("#9DA3AE"),
            VerticalAlignment = VerticalAlignment.Center,
            TextTrimming = TextTrimming.CharacterEllipsis,
            Margin = new Thickness(0, 0, 8, 0),
        };
        Grid.SetColumn(lblOp, 4);
        rowGrid.Children.Add(lblOp);

        // Hook PropertyChanged of inst to update card dynamically
        inst.PropertyChanged += (_, e) =>
        {
            Dispatcher.Invoke(() =>
            {
                if (e.PropertyName == nameof(inst.IsRunning))
                {
                    ApplyStatusStyle();
                    UpdateSummary();
                }
                else if (e.PropertyName == nameof(inst.OperationStatus))
                {
                    ApplyStatusStyle();
                    lblOp.Text = inst.OperationStatus;
                    if (inst.OperationStatus.Contains("thành công", StringComparison.OrdinalIgnoreCase))
                        lblOp.Foreground = (Brush)new BrushConverter().ConvertFromString("#38B26C");
                    else if (inst.OperationStatus.Contains("thất bại", StringComparison.OrdinalIgnoreCase) ||
                             inst.OperationStatus.Contains("quá thời gian", StringComparison.OrdinalIgnoreCase))
                        lblOp.Foreground = new SolidColorBrush(Color.FromRgb(0xF0, 0x76, 0x4F));
                    else if (inst.OperationStatus.Contains("Đang", StringComparison.OrdinalIgnoreCase) ||
                             inst.OperationStatus.Contains("Khởi động", StringComparison.OrdinalIgnoreCase))
                        lblOp.Foreground = (Brush)new BrushConverter().ConvertFromString("#61AFEF");
                    else
                        lblOp.Foreground = (Brush)new BrushConverter().ConvertFromString("#9DA3AE");
                }
                else if (e.PropertyName == nameof(inst.IsSelected))
                {
                    chk.IsChecked = inst.IsSelected;
                }
            });
        };

        // Quick Action Buttons
        var actionStack = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };

        var btnSingleLaunch = new Button
        {
            Content = "▶ Mở",
            Padding = new Thickness(6, 2, 6, 2),
            FontSize = 10.5,
            Margin = new Thickness(0, 0, 4, 0),
            ToolTip = "Khởi động tab này",
        };
        btnSingleLaunch.Click += async (_, _) => await LaunchSingleInstanceAsync(inst);
        actionStack.Children.Add(btnSingleLaunch);

        var btnSingleCapture = new Button
        {
            Content = "📷 Chụp",
            Padding = new Thickness(6, 2, 6, 2),
            FontSize = 10.5,
            Margin = new Thickness(0, 0, 4, 0),
            ToolTip = "Chụp màn hình tab này",
        };
        btnSingleCapture.Click += async (_, _) => await CaptureTabAsync(inst);
        actionStack.Children.Add(btnSingleCapture);

        var btnSingleQuit = new Button
        {
            Content = "⏹ Đóng",
            Padding = new Thickness(6, 2, 6, 2),
            FontSize = 10.5,
            ToolTip = "Đóng tab này",
        };
        btnSingleQuit.Click += async (_, _) => await QuitSingleInstanceAsync(inst);
        actionStack.Children.Add(btnSingleQuit);

        Grid.SetColumn(actionStack, 5);
        rowGrid.Children.Add(actionStack);

        border.Child = rowGrid;
        border.Cursor = Cursors.Hand;
        border.MouseLeftButtonDown += (_, e) =>
        {
            if (e.OriginalSource is not Button && e.OriginalSource is not CheckBox)
            {
                inst.IsSelected = !inst.IsSelected;
                chk.IsChecked = inst.IsSelected;
                UpdateSummary();
            }
        };
        return border;
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

        _txtSummary.Text = $"Đã chọn: {selected}/{total} tab  •  Đang chạy: {running}  •  Đã tắt: {stopped}";

        _suppressSelectAllChange = true;
        if (total > 0 && selected == total)
            _chkSelectAll.IsChecked = true;
        else if (selected == 0)
            _chkSelectAll.IsChecked = false;
        else
            _chkSelectAll.IsChecked = null; // Indeterminate
        _suppressSelectAllChange = false;
    }

    private async Task LaunchSingleInstanceAsync(LdInstanceInfo inst)
    {
        if (_isBusy) return;
        var console = EmulatorManager.GetLdConsolePath();
        if (string.IsNullOrEmpty(console)) return;

        int maxWait = _numTimeout.Value;
        inst.IsRunning = false;
        inst.OperationStatus = "Đang khởi động...";
        Log($"[Tab #{inst.Index} - {inst.Name}]: Đang khởi động (thời gian chờ tối đa: {maxWait}s)...");

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
            Log($"✔ [Tab #{inst.Index} - {inst.Name}]: Khởi động thành công, giả lập đã sẵn sàng (thời gian thực tế: {elapsedSec:F1}s / cấu hình: {maxWait}s).");
        }
        else
        {
            inst.IsRunning = false;
            inst.OperationStatus = "Khởi động thất bại hoặc quá thời gian";
            Log($"✘ [Tab #{inst.Index} - {inst.Name}]: Quá thời gian chờ {maxWait}s (thời gian thực tế đã chờ: {elapsedSec:F1}s). Tiến hành đóng tab...");
            await LdPlayerService.QuitInstanceAsync(console, inst.Index);
        }
    }

    private async Task QuitSingleInstanceAsync(LdInstanceInfo inst)
    {
        if (_isBusy) return;
        var console = EmulatorManager.GetLdConsolePath();
        if (string.IsNullOrEmpty(console)) return;

        inst.OperationStatus = "Đang đóng...";
        Log($"[Tab #{inst.Index} - {inst.Name}]: Đang đóng...");

        await LdPlayerService.QuitInstanceAsync(console, inst.Index);
        inst.IsRunning = false;
        inst.OperationStatus = "Đã tắt";
        Log($"✔ [Tab #{inst.Index} - {inst.Name}]: Đã đóng.");
    }

    private async Task StartSelectedSequentiallyAsync()
    {
        if (_isBusy) return;

        var selected = _instances.Where(i => i.IsSelected).ToList();
        if (selected.Count == 0)
        {
            MessageBox.Show(this,
                Loc.T("ld.manager.no_selection", "Vui lòng chọn ít nhất một tab giả lập để khởi động."),
                Loc.T("dlg.app.title", "macro"),
                MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var console = EmulatorManager.GetLdConsolePath();
        if (string.IsNullOrEmpty(console) || !File.Exists(console))
        {
            MessageBox.Show(this,
                Loc.T("ld.manager.no_console", "Không tìm thấy ldconsole.exe. Vui lòng kiểm tra lại cấu hình thư mục LDPlayer."),
                Loc.T("dlg.app.title", "macro"),
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        PersistLaunchOptions();

        var options = new LdLaunchOptions
        {
            IntervalSeconds = _numInterval.Value,
            MaxWaitSeconds = _numTimeout.Value,
            MaxRetries = _numRetries.Value,
        };

        SetBusyState(true);
        _cts = new CancellationTokenSource();
        var ct = _cts.Token;

        _progressBar.Value = 0;
        _progressBar.Maximum = selected.Count;
        _txtProgress.Text = $"Bắt đầu khởi động {selected.Count} tab tuần tự...";
        Log($"=== BẮT ĐẦU KHỞI ĐỘNG TUẦN TỰ {selected.Count} TAB (Khoảng chờ: {options.IntervalSeconds}s, Timeout: {options.MaxWaitSeconds}s, Thử lại: {options.MaxRetries} lần) ===");

        try
        {
            await LdPlayerService.LaunchSequentiallyAsync(
                console,
                selected,
                options,
                onInstanceStatus: (inst, status) =>
                {
                    Dispatcher.Invoke(() => inst.OperationStatus = status);
                },
                onLogMessage: msg => Log(msg),
                onOverallProgress: (current, total) =>
                {
                    Dispatcher.Invoke(() =>
                    {
                        _progressBar.Value = current;
                        _txtProgress.Text = $"Tiến trình: Đang xử lý tab {current}/{total}...";
                    });
                },
                ct);

            int successCount = selected.Count(i => i.IsRunning);
            Log($"=== KẾT THÚC CHUỖI KHỞI ĐỘNG: {successCount}/{selected.Count} tab đang chạy ===");
            _txtProgress.Text = ct.IsCancellationRequested
                ? "Chuỗi khởi động đã bị dừng."
                : $"Hoàn tất: {successCount}/{selected.Count} tab đã chạy.";
        }
        catch (OperationCanceledException)
        {
            Log("⏹ Chuỗi khởi động đã bị hủy bởi người dùng.");
            _txtProgress.Text = "Đã dừng chuỗi khởi động.";
        }
        catch (Exception ex)
        {
            Log($"✘ Lỗi không mong muốn: {ex.Message}");
            _txtProgress.Text = "Lỗi khi chạy chuỗi khởi động.";
        }
        finally
        {
            SetBusyState(false);
            _cts?.Dispose();
            _cts = null;
            UpdateSummary();
        }
    }

    private async Task CloseSelectedInstancesAsync()
    {
        if (_isBusy) return;

        var selected = _instances.Where(i => i.IsSelected).ToList();
        if (selected.Count == 0)
        {
            MessageBox.Show(this,
                Loc.T("ld.manager.no_selection_close", "Vui lòng chọn ít nhất một tab giả lập để đóng."),
                Loc.T("dlg.app.title", "macro"),
                MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var console = EmulatorManager.GetLdConsolePath();
        if (string.IsNullOrEmpty(console) || !File.Exists(console)) return;

        SetBusyState(true);
        _cts = new CancellationTokenSource();
        Log($"=== BẮT ĐẦU ĐÓNG {selected.Count} TAB ĐÃ CHỌN ===");

        try
        {
            await LdPlayerService.CloseInstancesAsync(
                console,
                selected,
                (inst, status) => Dispatcher.Invoke(() => inst.OperationStatus = status),
                msg => Log(msg),
                _cts.Token);

            Log($"✔ Đã đóng {selected.Count} tab thành công.");
        }
        finally
        {
            SetBusyState(false);
            _cts?.Dispose();
            _cts = null;
            UpdateSummary();
        }
    }

    private void StopLaunchSequence()
    {
        if (_cts != null && !_cts.IsCancellationRequested)
        {
            Log("Đang gửi yêu cầu dừng chuỗi khởi động...");
            _cts.Cancel();
            _btnStopSequence.IsEnabled = false;
        }
    }

    private async Task CaptureSelectedTabAsync()
    {
        if (_isBusy) return;

        var selected = _instances.Where(i => i.IsSelected).ToList();
        if (selected.Count == 0)
        {
            MessageBox.Show(this,
                Loc.T("ld.manager.no_selection_capture", "Vui lòng tích chọn một tab giả lập trong danh sách để chụp màn hình."),
                Loc.T("dlg.app.title", "macro"),
                MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        if (selected.Count > 1)
        {
            MessageBox.Show(this,
                Loc.T("ld.manager.multi_selection_capture", $"Bạn đang chọn {selected.Count} tab. Vui lòng chỉ chọn duy nhất 1 tab cần chụp màn hình."),
                Loc.T("dlg.app.title", "macro"),
                MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        await CaptureTabAsync(selected[0]);
    }

    private async Task CaptureTabAsync(LdInstanceInfo inst)
    {
        if (inst == null) return;

        if (!inst.IsRunning)
        {
            MessageBox.Show(this,
                Loc.T("ld.manager.tab_not_running", $"Tab #{inst.Index} ({inst.Name}) hiện đang tắt.\nVui lòng khởi động tab trước khi chụp màn hình."),
                Loc.T("dlg.app.title", "macro"),
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        try
        {
            EmulatorManager.SelectedInstance = inst;
            inst.OperationStatus = "Đang chụp màn hình...";
            Log($"[Tab #{inst.Index} - {inst.Name}]: Đang chụp màn hình...");

            var capture = await LdPlayerService.CaptureInstanceAsync(inst, EmulatorManager.CurrentPath);

            inst.OperationStatus = "Đã chụp màn hình";
            Log($"✔ [Tab #{inst.Index} - {inst.Name}]: Chụp màn hình thành công ({capture.Width}x{capture.Height}). Tệp: {Path.GetFileName(capture.Path)}");

            var mainWin = (Application.Current.MainWindow as MainWindow)
                ?? Application.Current?.Windows.OfType<MainWindow>().FirstOrDefault()
                ?? (Owner as MainWindow);
            if (mainWin != null)
            {
                await mainWin.CaptureTabAndOpenViewerAsync(inst);
                return;
            }

            var viewer = new ImageViewerWindow(
                capture.Path,
                capture,
                inst,
                mode: ViewerMode.PickPoint,
                title: $"macro - Chụp màn hình Tab #{inst.Index} ({inst.Name})");
            viewer.Show();
        }
        catch (Exception ex)
        {
            inst.OperationStatus = "Chụp ảnh thất bại";
            Log($"✘ [Tab #{inst.Index} - {inst.Name}]: Lỗi chụp ảnh: {ex.Message}");
            MessageBox.Show(this,
                $"Không thể chụp màn hình tab #{inst.Index} ({inst.Name}):\n\n{ex.Message}",
                Loc.T("dlg.app.title", "macro"),
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void SetBusyState(bool busy)
    {
        _isBusy = busy;
        _btnFetch.IsEnabled = !busy;
        _btnCaptureTab.IsEnabled = !busy;
        _btnStartSelected.IsEnabled = !busy;
        _btnCloseSelected.IsEnabled = !busy;
        _btnStopSequence.IsEnabled = busy;
        _numInterval.IsEnabled = !busy;
        _numTimeout.IsEnabled = !busy;
        _numRetries.IsEnabled = !busy;
    }
}
