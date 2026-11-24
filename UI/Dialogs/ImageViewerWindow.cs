using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using Macro.Core;
using Macro.Core.Emulator;
using Macro.Core.Services;
using Macro.Localization;
using Macro.UI.Widgets;
using Path = System.IO.Path;

namespace Macro.UI.Dialogs;

/// <summary>Interaction modes of the image viewer.</summary>
public enum ViewerMode { Browse, PickPoint, SelectRegion }

/// <summary>
/// Interactive image viewer used for screenshots, templates and Find Image
/// results. Zoom/pan never corrupts coordinates: every mouse position is
/// converted to ORIGINAL image pixels by dividing by the zoom factor, and
/// screen coordinates are derived through the capture's screen offset
/// (CoordinateMapping). Supports:
///   zoom in/out, fit, 1:1, reset, pan, pick a point, drag a region,
///   crop + save as template, copy coordinates, highlight rectangles.
/// </summary>
public class ImageViewerWindow : Window
{
    private CaptureInfo _capture;             // may be null (file only)
    private BitmapSource _bitmap;
    private readonly Action<int, int, int, int> _onPicked;   // imgX,imgY,scrX,scrY
    private readonly Action<string> _onTemplateSaved;         // template path
    private LdInstanceInfo _targetInstance;

    private readonly Grid _overlay = new();
    private readonly Image _image;
    private readonly Rectangle _selectionRect;
    private readonly Canvas _highlightCanvas = new();
    private readonly ScaleTransform _scale = new();
    private readonly ScrollViewer _scroll;

    private readonly Border _bannerBorder = new();
    private readonly TextBlock _bannerTargetText = new();
    private readonly TextBlock _bannerTipText = new();

    private readonly TextBlock _sizeText = Ui.Label("", null, 11.5);
    private readonly TextBlock _coordText = Ui.Label("", null, 11.5);
    private readonly TextBlock _selText = Ui.Label("", null, 11.5);
    private readonly TextBlock _defaultBadgeText = Ui.Label("", null, 11);
    private readonly List<RadioButton> _modeButtons = new();
    private readonly CheckBox _chkUseScreenCoords;
    private Button _btnApplyRegion;
    private string _stagedTemplatePath;
    private int _stagedTabIndex = -1;

    private ViewerMode _mode;
    private double _zoom = 1.0;
    private bool _isMouseDown;
    private Point? _dragStart;          // region drag start (image pixels)
    private Point? _dragCurrent;
    private bool _panning;
    private Point _panStartScrollOffset;
    private Int32Rect _selection;
    private const int MinValidSelectionSize = 3;

    public bool IsApplyRegionEnabled => _btnApplyRegion?.IsEnabled ?? false;
    public Int32Rect CurrentSelection => _selection;
    public string StagedTemplatePath => _stagedTemplatePath;

    public ImageViewerWindow(string imagePath, CaptureInfo capture,
                             ViewerMode mode = ViewerMode.PickPoint,
                             Action<int, int, int, int> onPicked = null,
                             Action<string> onTemplateSaved = null,
                             string title = null)
        : this(imagePath, capture, EmulatorManager.SelectedInstance, mode, onPicked, onTemplateSaved, title)
    {
    }

    public ImageViewerWindow(string imagePath, CaptureInfo capture,
                             LdInstanceInfo targetInstance,
                             ViewerMode mode = ViewerMode.PickPoint,
                             Action<int, int, int, int> onPicked = null,
                             Action<string> onTemplateSaved = null,
                             string title = null)
    {
        _capture = capture;
        _targetInstance = targetInstance ?? EmulatorManager.SelectedInstance;
        _onPicked = onPicked;
        _onTemplateSaved = onTemplateSaved;
        _mode = mode;

        Title = title ?? Loc.T("viewer.title", "Image viewer")
                + (imagePath != null ? $" - {Path.GetFileName(imagePath)}" : "");
        Width = 1000;
        Height = 680;
        MinWidth = 550;
        MinHeight = 400;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        ShowInTaskbar = true;
        Topmost = false;
        Background = (Brush)new BrushConverter().ConvertFromString("#191A1D");

        var bitmap = new BitmapImage();
        bitmap.BeginInit();
        bitmap.CacheOption = BitmapCacheOption.OnLoad;
        bitmap.UriSource = new Uri(Path.GetFullPath(imagePath), UriKind.Absolute);
        bitmap.EndInit();
        bitmap.Freeze();
        _bitmap = bitmap;

        // ---------------- toolbar
        var toolbar = new WrapPanel { Margin = new Thickness(8) };
        toolbar.Children.Add(ToolButton("viewer.capture", "New capture",
            () => CaptureNew(), "\uD83D\uDCF7"));
        toolbar.Children.Add(ToolButton("viewer.openfile", "Open image...",
            () => OpenFile(), "\uD83D\uDCC2"));
        toolbar.Children.Add(Separator());

        toolbar.Children.Add(ToolButton(null, Loc.T("viewer.zoomin", "Zoom in"),
            () => SetZoom(_zoom * 1.25), "+"));
        toolbar.Children.Add(ToolButton(null, Loc.T("viewer.zoomout", "Zoom out"),
            () => SetZoom(_zoom / 1.25), "\u2212"));
        toolbar.Children.Add(ToolButton(null, Loc.T("viewer.fit", "Fit to screen"),
            () => ZoomFit(), "\u26F6"));
        toolbar.Children.Add(ToolButton(null, Loc.T("viewer.actual", "Actual size"),
            () => SetZoom(1.0), "1:1"));
        toolbar.Children.Add(Separator());

        _modeButtons.Add(ModeButton("viewer.mode.point", "🎯 Chọn tọa độ", ViewerMode.PickPoint));
        _modeButtons.Add(ModeButton("viewer.mode.region", "✂ Chọn vùng", ViewerMode.SelectRegion));
        _modeButtons.Add(ModeButton("viewer.mode.pan", "🔍 Cuộn & duyệt", ViewerMode.Browse));
        foreach (var button in _modeButtons) toolbar.Children.Add(button);
        toolbar.Children.Add(Separator());

        _chkUseScreenCoords = new CheckBox
        {
            Content = Loc.T("viewer.screencoords.check", "Tọa độ màn hình"),
            ToolTip = Loc.T("viewer.screencoords.tip", "Tích chọn để sử dụng tọa độ màn hình thực tế (để click chuột Windows), bỏ tích để dùng tọa độ gốc trên tab giả lập"),
            Foreground = T.Muted,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(6, 0, 8, 0),
            IsChecked = _targetInstance == null && _capture != null && (_capture.OffsetX != 0 || _capture.OffsetY != 0),
        };
        toolbar.Children.Add(_chkUseScreenCoords);
        toolbar.Children.Add(Separator());

        _btnApplyRegion = ToolButton(null, "✔ Chọn vùng này", ApplyCurrentSelectionAsTemplate, "✔");
        _btnApplyRegion.ToolTip = "Gán ảnh từ vùng vừa kéo vào ô 'Ảnh mẫu' của thao tác Tìm hình ảnh đang chọn";
        _btnApplyRegion.Foreground = (Brush)new BrushConverter().ConvertFromString("#98C379");
        _btnApplyRegion.FontWeight = FontWeights.SemiBold;
        _btnApplyRegion.IsEnabled = false;
        toolbar.Children.Add(_btnApplyRegion);
        toolbar.Children.Add(Separator());

        toolbar.Children.Add(ToolButton(null, "🖼️ Quản lý ảnh...", ShowDefaultImageManager, "\uD83D\uDDBC"));
        toolbar.Children.Add(Separator());
        var copy = ToolButton(null, Loc.T("viewer.copy", "Copy coordinates"),
            CopyCoordinates, "\u29C9");
        toolbar.Children.Add(copy);
        toolbar.Children.Add(ToolButton(null, Loc.T("viewer.reset", "Reset"),
            () => { ZoomFit(); ClearSelection(); ClearPickedPoint(); }, "\u21BA"));

        // ---------------- status row
        var status = new Grid { Margin = new Thickness(10, 2, 10, 4) };
        status.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        status.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        status.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        status.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        PlaceIn(status, _sizeText, 0);
        _coordText.MinWidth = 260;
        PlaceIn(status, _coordText, 1);
        _selText.Foreground = T.Accent;
        PlaceIn(status, _selText, 2);
        _coordText.Foreground = T.Muted;
        _sizeText.Foreground = T.Muted;

        _defaultBadgeText.Foreground = (Brush)new BrushConverter().ConvertFromString("#8F939C");
        _defaultBadgeText.HorizontalAlignment = HorizontalAlignment.Right;
        _defaultBadgeText.VerticalAlignment = VerticalAlignment.Center;
        PlaceIn(status, _defaultBadgeText, 3);

        // ---------------- viewer area
        _image = new Image
        {
            Source = _bitmap,
            Stretch = Stretch.Fill,
            Width = _bitmap.PixelWidth,
            Height = _bitmap.PixelHeight,
        };
        _selectionRect = new Rectangle
        {
            Stroke = T.Accent,
            StrokeThickness = 1.5,
            StrokeDashArray = new DoubleCollection { 3, 2 },
            Fill = new SolidColorBrush(Color.FromArgb(40, 0x4F, 0x8C, 0xFF)),
            Visibility = Visibility.Collapsed,
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Top,
            IsHitTestVisible = false,
        };
        _highlightCanvas.Width = _bitmap.PixelWidth;
        _highlightCanvas.Height = _bitmap.PixelHeight;
        _highlightCanvas.IsHitTestVisible = false;
        _overlay.Width = _bitmap.PixelWidth;
        _overlay.Height = _bitmap.PixelHeight;
        _overlay.Children.Add(_image);
        _overlay.Children.Add(_highlightCanvas);
        _overlay.Children.Add(_selectionRect);

        var host = new Grid
        {
            Background = (Brush)new BrushConverter().ConvertFromString("#141517"),
            LayoutTransform = _scale,
        };
        host.Children.Add(_overlay);
        host.HorizontalAlignment = HorizontalAlignment.Left;
        host.VerticalAlignment = VerticalAlignment.Top;

        _scroll = new ScrollViewer
        {
            Content = host,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
            Background = (Brush)new BrushConverter().ConvertFromString("#141517"),
        };

        // ---------------- events
        PreviewMouseLeftButtonUp += (s, e) =>
        {
            if (_isMouseDown)
            {
                OnOverlayUp(s, e);
            }
        };
        _overlay.MouseMove += OnOverlayMouseMove;
        _overlay.MouseLeave += (_, _) =>
        {
            UpdateCoordsLabel(null);
            if (!_isMouseDown && !_panning && _overlay.IsMouseCaptured)
                _overlay.ReleaseMouseCapture();
        };
        _overlay.LostMouseCapture += (_, _) =>
        {
            if (_mode == ViewerMode.SelectRegion && _isMouseDown && _dragStart is { } start && _dragCurrent is { } curr)
            {
                double dx = Math.Abs(curr.X - start.X);
                double dy = Math.Abs(curr.Y - start.Y);
                if (dx >= MinValidSelectionSize && dy >= MinValidSelectionSize)
                {
                    FinalizeSelection(start, curr);
                }
                else
                {
                    ClearSelection();
                }
            }
            _isMouseDown = false;
            _panning = false;
            _dragStart = null;
            _dragCurrent = null;
        };
        _overlay.MouseLeftButtonDown += OnOverlayDown;
        _overlay.MouseLeftButtonUp += OnOverlayUp;
        _overlay.MouseRightButtonDown += (_, e) => { ClearSelection(); e.Handled = true; };
        _scroll.PreviewMouseWheel += OnWheel;

        var root = new DockPanel();
        DockPanel.SetDock(toolbar, Dock.Top);
        root.Children.Add(toolbar);
        BuildBanner(root);
        DockPanel.SetDock(status, Dock.Bottom);
        root.Children.Add(status);
        root.Children.Add(_scroll);
        Content = root;

        SizeChanged += (_, _) => { if (_zoom <= 0.01) ZoomFit(); };
        Loaded += (_, _) => ZoomFit();
        SelectMode(_mode);
        UpdateSizeLabel();
        UpdateDefaultBadge();
        ActionDefaults.DefaultsChanged += UpdateDefaultBadge;
        Closed += (_, _) => ActionDefaults.DefaultsChanged -= UpdateDefaultBadge;
    }

    private void BuildBanner(DockPanel root)
    {
        _bannerBorder.Background = (Brush)new BrushConverter().ConvertFromString("#1D1F24");
        _bannerBorder.BorderBrush = (Brush)new BrushConverter().ConvertFromString("#2E3038");
        _bannerBorder.BorderThickness = new Thickness(0, 1, 0, 1);
        _bannerBorder.Padding = new Thickness(12, 6, 12, 6);

        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        _bannerTargetText.FontSize = 11.5;
        _bannerTargetText.FontWeight = FontWeights.SemiBold;
        _bannerTargetText.Foreground = (Brush)new BrushConverter().ConvertFromString("#61AFEF");
        _bannerTargetText.VerticalAlignment = VerticalAlignment.Center;
        _bannerTargetText.Margin = new Thickness(0, 0, 14, 0);

        _bannerTipText.FontSize = 11;
        _bannerTipText.Foreground = (Brush)new BrushConverter().ConvertFromString("#9DA3AE");
        _bannerTipText.VerticalAlignment = VerticalAlignment.Center;

        Grid.SetColumn(_bannerTargetText, 0);
        Grid.SetColumn(_bannerTipText, 1);
        grid.Children.Add(_bannerTargetText);
        grid.Children.Add(_bannerTipText);

        _bannerBorder.Child = grid;
        DockPanel.SetDock(_bannerBorder, Dock.Top);
        root.Children.Add(_bannerBorder);

        UpdateBannerContext();
    }

    private MainWindow FindMainWindow()
    {
        return (Application.Current.MainWindow as MainWindow)
            ?? Application.Current?.Windows.OfType<MainWindow>().FirstOrDefault()
            ?? (Owner as MainWindow);
    }

    public void NotifyTemplateApplied(string stepName, int tabIdx = -1)
    {
        string tabMsg = tabIdx >= 0 ? $" [Tab #{tabIdx}]" : "";
        _selText.Text = $"✔ Đã gán ảnh mẫu vào {stepName}{tabMsg} ở phần bên phải.";
        _bannerTipText.Text = $"💡 Đã gán ảnh mẫu vào {stepName}. Thao tác đã cập nhật và sẵn sàng thực thi khi chạy macro.";
        UpdateBannerContext();
    }

    private void UpdateBannerContext()
    {
        var mainWin = FindMainWindow();
        var step = mainWin?.GetCurrentlyTargetedStep();

        string tabTitle = _targetInstance != null
            ? $"Tab #{_targetInstance.Index} ({_targetInstance.Name})"
            : "Màn hình";

        string modeBadge = _mode switch
        {
            ViewerMode.PickPoint => "🎯 CHẾ ĐỘ: CHỌN TỌA ĐỘ",
            ViewerMode.SelectRegion => "✂ CHẾ ĐỘ: CHỌN VÙNG",
            _ => "🔍 CHẾ ĐỘ: CUỘN & DUYỆT ẢNH"
        };

        if (_mode == ViewerMode.PickPoint)
        {
            string stepNote = step != null && (step.Params.ContainsKey("x") || step.Type is "click" or "double_click" or "right_click" or "move_mouse" or "drag")
                ? $" và cập nhật vào lệnh '{step.Name ?? step.Type}'"
                : "";
            _bannerTargetText.Text = $"{modeBadge}  •  {tabTitle}";
            _bannerTipText.Text = $"💡 Click chuột trái để chọn tọa độ{stepNote}. Áp dụng trực tiếp cho các lệnh Click.";
        }
        else if (_mode == ViewerMode.SelectRegion)
        {
            string stepNote = step != null && (step.Params.ContainsKey("image") || step.Type is "find_image" or "click_image" or "wait_for_image" or "image_exists")
                ? $" cho lệnh '{step.Name ?? step.Type}'"
                : "";
            _bannerTargetText.Text = $"{modeBadge}  •  {tabTitle}";
            _bannerTipText.Text = $"💡 Kéo chuột để chọn vùng ảnh{stepNote}. Thả chuột rồi bấm nút '✔ Chọn vùng này' ở thanh công cụ để gán vào lệnh.";
        }
        else
        {
            _bannerTargetText.Text = $"{modeBadge}  •  {tabTitle}";
            _bannerTipText.Text = "💡 Nhấn giữ chuột trái để kéo và di chuyển vị trí xem ảnh.";
        }
    }

    // ============================================================ layout helpers
    private static void PlaceIn(Grid grid, UIElement element, int column)
    {
        Grid.SetColumn(element, column);
        grid.Children.Add(element);
    }

    private Button ToolButton(string locKey, string fallback, Action onClick,
                              string glyph = null)
    {
        var text = locKey == null ? fallback : Loc.T(locKey, fallback);
        var button = new Button
        {
            Style = (Style)Application.Current.Resources["Btn.Tool"],
            Padding = new Thickness(8, 5, 8, 5),
            Margin = new Thickness(2, 0, 2, 2),
            ToolTip = text,
            Content = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Children =
                {
                    new TextBlock
                    {
                        Text = glyph ?? "",
                        FontSize = 13,
                        Margin = new Thickness(0, 0, text.Length > 0 ? 6 : 0, 0),
                        VerticalAlignment = VerticalAlignment.Center,
                    },
                    new TextBlock { Text = text, VerticalAlignment = VerticalAlignment.Center },
                },
            },
        };
        button.Click += (_, _) => onClick();
        return button;
    }

    private FrameworkElement Separator() => new Border
    {
        Width = 1,
        Height = 22,
        Background = T.Border,
        Margin = new Thickness(4, 0, 4, 2),
        VerticalAlignment = VerticalAlignment.Center,
    };

    private RadioButton ModeButton(string locKey, string fallback,
                                   ViewerMode mode)
    {
        var button = new RadioButton
        {
            Content = Loc.T(locKey, fallback),
            Foreground = T.Text,
            Margin = new Thickness(2, 0, 8, 2),
            VerticalAlignment = VerticalAlignment.Center,
            GroupName = "ViewerMode",
            Tag = mode,
            Cursor = Cursors.Hand,
        };
        button.Checked += (_, _) => SelectMode(mode);
        return button;
    }

    // ================================================================= modes
    public void SelectMode(ViewerMode mode)
    {
        _mode = mode;
        foreach (var button in _modeButtons)
            button.IsChecked = Equals(button.Tag, mode);

        _overlay.Cursor = mode switch
        {
            ViewerMode.Browse => Cursors.Hand,
            ViewerMode.SelectRegion => Cursors.Cross,
            _ => Cursors.Cross,
        };

        UpdateBannerContext();

        if (mode == ViewerMode.SelectRegion)
        {
            // Ẩn hoàn toàn dấu chọn tọa độ và thông tin tọa độ điểm; chỉ hiển thị khung chữ nhật
            _highlightCanvas.Visibility = Visibility.Collapsed;
            _coordText.Visibility = Visibility.Collapsed;
            _coordText.Text = "";

            if (_selection.Width >= MinValidSelectionSize && _selection.Height >= MinValidSelectionSize)
            {
                _selectionRect.Visibility = Visibility.Visible;
                _selText.Text = $"Vùng đã chọn: {_selection.Width} \u00D7 {_selection.Height} px";
                if (_btnApplyRegion != null)
                {
                    _btnApplyRegion.IsEnabled = true;
                }
            }
            else
            {
                if (_btnApplyRegion != null)
                {
                    _btnApplyRegion.IsEnabled = !string.IsNullOrEmpty(_stagedTemplatePath) && File.Exists(_stagedTemplatePath);
                }
                _selText.Text = "💡 Chế độ Chọn vùng: Kéo chuột trái để tạo hình chữ nhật. Thả chuột sẽ cắt ảnh và bật nút '✔ Chọn vùng này'.";
            }
        }
        else if (mode == ViewerMode.PickPoint)
        {
            // Hiển thị lại dấu chọn vòng tròn tại tọa độ đã chọn trước đó (nếu đã có)
            _highlightCanvas.Visibility = Visibility.Visible;
            _coordText.Visibility = Visibility.Visible;

            if (_pickedPoint.HasValue)
            {
                DrawPickMarker(_pickedPoint.Value);
                if (!string.IsNullOrEmpty(_lastPickedStatusText))
                {
                    _selText.Text = _lastPickedStatusText;
                }
            }
            else
            {
                _highlightCanvas.Children.Clear();
                _selText.Text = "💡 Chế độ Chọn tọa độ: Click chuột vào ảnh để lấy tọa độ. Tự động áp dụng trực tiếp.";
            }
        }
        else // Browse
        {
            _highlightCanvas.Visibility = Visibility.Visible;
            _coordText.Visibility = Visibility.Visible;
            _selText.Text = "💡 Nhấn giữ chuột trái để cuộn / di chuyển ảnh.";
        }
    }

    // ============================================================ coordinates
    private Point ImagePixel(Point mouseInOverlay)
    {
        return new Point(
            Math.Clamp(mouseInOverlay.X, 0, _bitmap.PixelWidth),
            Math.Clamp(mouseInOverlay.Y, 0, _bitmap.PixelHeight));
    }

    private (int screenX, int screenY)? ScreenOf(Point imagePixel)
    {
        if (_capture == null) return null;
        return CoordinateMapping.ImageToScreen(_capture,
            (int)Math.Round(imagePixel.X), (int)Math.Round(imagePixel.Y));
    }

    private void UpdateCoordsLabel(Point? mouseInOverlay)
    {
        if (mouseInOverlay is not { } mouse)
        {
            _coordText.Text = "";
            return;
        }
        var pixel = ImagePixel(mouse);
        string text;
        if (_targetInstance != null)
        {
            int devW = _targetInstance.ScreenWidth > 0 ? _targetInstance.ScreenWidth : 960;
            int devH = _targetInstance.ScreenHeight > 0 ? _targetInstance.ScreenHeight : 540;
            var (tabX, tabY) = LdPlayerService.TranslateImageToTab(
                _bitmap.PixelWidth, _bitmap.PixelHeight, devW, devH, pixel.X, pixel.Y);
            text = $"Tab #{_targetInstance.Index}: ({tabX}, {tabY})  |  Ảnh: ({(int)pixel.X}, {(int)pixel.Y})";
        }
        else
        {
            text = $"{Loc.T("viewer.pickcoords", "Cursor:")} {(int)pixel.X}, {(int)pixel.Y}";
        }

        if (ScreenOf(pixel) is { } screen)
            text += $"    {Loc.T("viewer.screencoords", "Screen:")} {screen.screenX}, {screen.screenY}";

        _coordText.Text = text;
    }

    private void UpdateSizeLabel()
    {
        string tabInfo = "";
        if (_targetInstance != null)
        {
            int devW = _targetInstance.ScreenWidth > 0 ? _targetInstance.ScreenWidth : 960;
            int devH = _targetInstance.ScreenHeight > 0 ? _targetInstance.ScreenHeight : 540;
            tabInfo = $"Tab #{_targetInstance.Index} ({_targetInstance.Name}) [{devW} x {devH}]  |  ";
        }
        _sizeText.Text = tabInfo + Loc.F("viewer.image.size",
            "Original size: {0} x {1}", _bitmap.PixelWidth, _bitmap.PixelHeight)
            + (_capture != null
                ? $"   |   {Loc.T("viewer.screencoords", "Screen:")} "
                  + $"{_capture.OffsetX}, {_capture.OffsetY}"
                : "");
    }

    // ================================================================== zoom
    private void SetZoom(double zoom)
    {
        _zoom = Math.Clamp(zoom, 0.05, 10);
        _scale.ScaleX = _zoom;
        _scale.ScaleY = _zoom;
    }

    private void ZoomFit()
    {
        var viewport = _scroll.ViewportWidth > 10
            ? new Size(_scroll.ViewportWidth, _scroll.ViewportHeight)
            : new Size(ActualWidth - 30, ActualHeight - 110);
        var fit = Math.Min(viewport.Width / _bitmap.PixelWidth,
                           viewport.Height / _bitmap.PixelHeight);
        SetZoom(Math.Min(fit, 1.0));
    }

    private void OnWheel(object sender, MouseWheelEventArgs e)
    {
        if (Keyboard.Modifiers.HasFlag(ModifierKeys.Control))
        {
            SetZoom(_zoom * (e.Delta > 0 ? 1.15 : 1 / 1.15));
            e.Handled = true;
        }
    }

    // ================================================================= mouse
    private void OnOverlayMouseMove(object sender, MouseEventArgs e)
    {
        var position = e.GetPosition(_overlay);
        if (_mode != ViewerMode.SelectRegion)
        {
            UpdateCoordsLabel(position);
        }
        else
        {
            _coordText.Text = "";
        }

        if (_mode == ViewerMode.SelectRegion && _isMouseDown && _dragStart is { } start)
        {
            var current = ImagePixel(position);
            _dragCurrent = current;
            double dx = Math.Abs(current.X - start.X);
            double dy = Math.Abs(current.Y - start.Y);
            if (dx >= MinValidSelectionSize || dy >= MinValidSelectionSize)
            {
                ShowSelection(start, current);
            }
        }
        else if (_mode == ViewerMode.Browse && _panning)
        {
            var here = e.GetPosition(_scroll);
            _scroll.ScrollToHorizontalOffset(
                _panStartScrollOffset.X - (here.X - _panStart.X));
            _scroll.ScrollToVerticalOffset(
                _panStartScrollOffset.Y - (here.Y - _panStart.Y));
        }
        // In ViewerMode.PickPoint: do not show or create selection rectangles
    }

    private Point _panStart;

    private void OnOverlayDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton != MouseButton.Left) return;

        var position = e.GetPosition(_overlay);
        if (_mode == ViewerMode.Browse)
        {
            // Browse: left-drag pans the view
            _panning = true;
            _panStart = e.GetPosition(_scroll);
            _panStartScrollOffset = new Point(_scroll.HorizontalOffset,
                                              _scroll.VerticalOffset);
            _overlay.CaptureMouse();
            return;
        }

        if (_mode == ViewerMode.PickPoint)
        {
            _isMouseDown = true;
            _dragStart = ImagePixel(position);
            _dragCurrent = _dragStart;
            _selectionRect.Visibility = Visibility.Collapsed;
            _overlay.CaptureMouse();
            e.Handled = true;
            return;
        }

        if (_mode == ViewerMode.SelectRegion)
        {
            _isMouseDown = true;
            _dragStart = ImagePixel(position);
            _dragCurrent = _dragStart;
            _selectionRect.Visibility = Visibility.Collapsed;
            _overlay.CaptureMouse();
            e.Handled = true;
            return;
        }
    }

    private void OnOverlayUp(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton != MouseButton.Left) return;

        if (_panning)
        {
            _panning = false;
            if (_overlay.IsMouseCaptured)
                _overlay.ReleaseMouseCapture();
            return;
        }

        if (!_isMouseDown) return;

        // 1. Cache coordinates and mode locally before clearing fields and releasing capture
        Point? startPt = _dragStart;
        Point? prevDrag = _dragCurrent;
        var endPt = ImagePixel(e.GetPosition(_overlay));
        var currentMode = _mode;

        // 2. Clear drag state before releasing capture so LostMouseCapture handler knows drag is handled
        _isMouseDown = false;
        _dragStart = null;
        _dragCurrent = null;

        // 3. Release mouse capture safely (LostMouseCapture won't interfere because _isMouseDown is now false)
        if (_overlay.IsMouseCaptured)
            _overlay.ReleaseMouseCapture();

        if (currentMode == ViewerMode.PickPoint)
        {
            StorePickedPoint(endPt);
            UsePickedPoint();
        }
        else if (currentMode == ViewerMode.SelectRegion)
        {
            if (startPt is { } start)
            {
                double dx = Math.Abs(endPt.X - start.X);
                double dy = Math.Abs(endPt.Y - start.Y);

                if (dx >= MinValidSelectionSize && dy >= MinValidSelectionSize)
                {
                    FinalizeSelection(start, endPt);
                }
                else if (prevDrag is { } pd && Math.Abs(pd.X - start.X) >= MinValidSelectionSize && Math.Abs(pd.Y - start.Y) >= MinValidSelectionSize)
                {
                    FinalizeSelection(start, pd);
                }
                else
                {
                    ClearSelection();
                    if (dx < MinValidSelectionSize && dy < MinValidSelectionSize)
                    {
                        _selText.Text = "💡 Chế độ Chọn vùng: Hãy nhấn giữ và kéo chuột trái để tạo khung chữ nhật vùng ảnh mẫu (kéo tối thiểu 3x3 pixel).";
                    }
                    else
                    {
                        _selText.Text = $"⚠ Vùng chọn quá nhỏ ({(int)dx} \u00D7 {(int)dy} px). Vui lòng kéo vùng lớn hơn 3x3 pixel.";
                    }
                }
            }
            else
            {
                ClearSelection();
            }
        }
        e.Handled = true;
    }

    // ================================================================ picking
    private Point? _pickedPoint;
    private string _lastPickedStatusText;

    private void StorePickedPoint(Point pixel)
    {
        _pickedPoint = pixel;

        string tabText;
        if (_targetInstance != null)
        {
            int devW = _targetInstance.ScreenWidth > 0 ? _targetInstance.ScreenWidth : 960;
            int devH = _targetInstance.ScreenHeight > 0 ? _targetInstance.ScreenHeight : 540;
            var (tabX, tabY) = LdPlayerService.TranslateImageToTab(
                _bitmap.PixelWidth, _bitmap.PixelHeight, devW, devH, pixel.X, pixel.Y);
            tabText = $"Tab #{_targetInstance.Index}: ({tabX}, {tabY})";
        }
        else
        {
            tabText = $"({(int)pixel.X}, {(int)pixel.Y})";
        }

        var screen = ScreenOf(pixel);
        var screenText = screen.HasValue ? $"  |  Màn hình: ({screen.Value.screenX}, {screen.Value.screenY})" : "";
        _lastPickedStatusText = $"Điểm đã chọn: {tabText}{screenText}";
        _selText.Text = _lastPickedStatusText;
        DrawPickMarker(pixel);
    }

    private void ClearPickedPoint()
    {
        _pickedPoint = null;
        _lastPickedStatusText = null;
        _highlightCanvas.Children.Clear();
        _selText.Text = "";
    }

    private void UsePickedPoint()
    {
        if (!_pickedPoint.HasValue) return;
        var pixel = _pickedPoint.Value;
        int tabX, tabY;
        if (_targetInstance != null)
        {
            int devW = _targetInstance.ScreenWidth > 0 ? _targetInstance.ScreenWidth : 960;
            int devH = _targetInstance.ScreenHeight > 0 ? _targetInstance.ScreenHeight : 540;
            var tabCoord = LdPlayerService.TranslateImageToTab(
                _bitmap.PixelWidth, _bitmap.PixelHeight, devW, devH, pixel.X, pixel.Y);
            tabX = tabCoord.TabX;
            tabY = tabCoord.TabY;
        }
        else
        {
            tabX = (int)pixel.X;
            tabY = (int)pixel.Y;
        }

        var screen = ScreenOf(pixel);
        int screenX = screen?.screenX ?? tabX;
        int screenY = screen?.screenY ?? tabY;
        int chosenX = (_chkUseScreenCoords.IsChecked == true) ? screenX : tabX;
        int chosenY = (_chkUseScreenCoords.IsChecked == true) ? screenY : tabY;

        int tabIdx = (_chkUseScreenCoords.IsChecked == true) ? -1 : (_targetInstance?.Index ?? -1);

        // 1. Luôn lưu làm tọa độ mặc định dùng chung cho Click chuột
        ActionDefaults.SetDefaultCoordinates(chosenX, chosenY, tabIdx);

        if (_onPicked != null)
        {
            _onPicked(chosenX, chosenY, screenX, screenY);
            _lastPickedStatusText = $"✔ Đã chọn tọa độ: ({chosenX}, {chosenY}).";
            _selText.Text = _lastPickedStatusText;
            UpdateBannerContext();
            UpdateDefaultBadge();
            return;
        }

        var mainWin = FindMainWindow();
        bool updated = false;
        if (mainWin != null)
        {
            if (mainWin.Dispatcher.CheckAccess())
            {
                updated = mainWin.UpdateTargetStepCoordinates(chosenX, chosenY, tabIdx);
            }
            else
            {
                updated = mainWin.Dispatcher.Invoke(() => mainWin.UpdateTargetStepCoordinates(chosenX, chosenY, tabIdx));
            }
        }

        string tabMsg = tabIdx >= 0 ? $" [Tab #{tabIdx}]" : "";
        string updateMsg = updated ? " & đã cập nhật vào lệnh đang chọn." : " (tự động áp dụng cho các lệnh Click mới).";
        _lastPickedStatusText = $"✔ Đã chọn tọa độ: ({chosenX}, {chosenY}){tabMsg}{updateMsg}";
        _selText.Text = _lastPickedStatusText;
        UpdateBannerContext();
        UpdateDefaultBadge();
    }

    private void DrawPickMarker(Point pixel)
    {
        _highlightCanvas.Children.Clear();
        const double outerDiameter = 18;
        const double innerDiameter = 4;

        // Vòng tròn ngoài (outer circle ring)
        var ring = new Ellipse
        {
            Stroke = Brushes.LimeGreen,
            StrokeThickness = 2,
            Fill = new SolidColorBrush(Color.FromArgb(45, 50, 205, 50)),
            Width = outerDiameter,
            Height = outerDiameter,
            IsHitTestVisible = false,
        };
        Canvas.SetLeft(ring, pixel.X - outerDiameter / 2);
        Canvas.SetTop(ring, pixel.Y - outerDiameter / 2);

        // Chấm tròn tâm (center dot)
        var dot = new Ellipse
        {
            Fill = Brushes.LimeGreen,
            Width = innerDiameter,
            Height = innerDiameter,
            IsHitTestVisible = false,
        };
        Canvas.SetLeft(dot, pixel.X - innerDiameter / 2);
        Canvas.SetTop(dot, pixel.Y - innerDiameter / 2);

        _highlightCanvas.Children.Add(ring);
        _highlightCanvas.Children.Add(dot);
    }

    // ================================================================ region
    private void ShowSelection(Point start, Point current)
    {
        double x = Math.Min(start.X, current.X);
        double y = Math.Min(start.Y, current.Y);
        double w = Math.Abs(current.X - start.X);
        double h = Math.Abs(current.Y - start.Y);

        _selectionRect.Visibility = Visibility.Visible;
        _selectionRect.Width = w;
        _selectionRect.Height = h;
        _selectionRect.Margin = new Thickness(x, y, 0, 0);

        _selText.Text = $"Đang chọn vùng: {(int)w} x {(int)h} px (tại {(int)x}, {(int)y})";
    }

    internal void FinalizeSelection(Point start, Point current)
    {
        int minX = (int)Math.Round(Math.Min(start.X, current.X));
        int minY = (int)Math.Round(Math.Min(start.Y, current.Y));
        int maxX = (int)Math.Round(Math.Max(start.X, current.X));
        int maxY = (int)Math.Round(Math.Max(start.Y, current.Y));

        minX = Math.Clamp(minX, 0, _bitmap.PixelWidth - 1);
        minY = Math.Clamp(minY, 0, _bitmap.PixelHeight - 1);
        maxX = Math.Clamp(maxX, minX + 1, _bitmap.PixelWidth);
        maxY = Math.Clamp(maxY, minY + 1, _bitmap.PixelHeight);

        int w = maxX - minX;
        int h = maxY - minY;

        if (w < MinValidSelectionSize || h < MinValidSelectionSize)
        {
            ClearSelection();
            return;
        }

        _selection = new Int32Rect(minX, minY, w, h);
        _selectionRect.Visibility = Visibility.Visible;
        _selectionRect.Width = w;
        _selectionRect.Height = h;
        _selectionRect.Margin = new Thickness(minX, minY, 0, 0);

        CropAndStageSelection();
    }

    internal void ClearSelection()
    {
        _selectionRect.Visibility = Visibility.Collapsed;
        _dragStart = null;
        _dragCurrent = null;
        _isMouseDown = false;
        _selection = Int32Rect.Empty;
        _stagedTemplatePath = null;
        if (_btnApplyRegion != null)
        {
            _btnApplyRegion.IsEnabled = false;
        }

        if (_mode == ViewerMode.Browse)
            _selText.Text = "💡 Nhấn giữ chuột trái để cuộn / di chuyển ảnh.";
        else if (_mode == ViewerMode.PickPoint)
        {
            if (_pickedPoint.HasValue && !string.IsNullOrEmpty(_lastPickedStatusText))
                _selText.Text = _lastPickedStatusText;
            else
                _selText.Text = "💡 Click vào ảnh để lấy tọa độ. Tự động áp dụng trực tiếp.";
        }
        else
        {
            _selText.Text = "💡 Nhấn giữ và kéo chuột trái để chọn vùng ảnh mẫu.";
        }
    }

    // ================================================================== crop
    internal void CropAndStageSelection()
    {
        if (_selection.Width < MinValidSelectionSize || _selection.Height < MinValidSelectionSize)
            return;

        string templatePath = null;
        string fileName = null;

        try
        {
            var crop = new CroppedBitmap(_bitmap, _selection);
            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(crop));
            fileName = $"template_{DateTime.Now:yyyyMMdd_HHmmss}.png";
            var templatesDir = ScreenshotService.TemplatesFolder();
            if (!Directory.Exists(templatesDir))
            {
                Directory.CreateDirectory(templatesDir);
            }
            templatePath = Path.Combine(templatesDir, fileName);
            using (var stream = File.Create(templatePath))
            {
                encoder.Save(stream);
            }

            if (!File.Exists(templatePath) || new FileInfo(templatePath).Length == 0)
            {
                throw new IOException($"Tệp ảnh mẫu không được tạo hoặc có dung lượng 0 byte: {templatePath}");
            }
        }
        catch (Exception ex)
        {
            _selText.Text = $"❌ Lỗi khi cắt/lưu ảnh mẫu: {ex.Message}";
            _bannerTipText.Text = $"❌ Không thể lưu ảnh mẫu: {ex.Message}";
            MessageBox.Show(this,
                $"Đã xảy ra lỗi khi cắt hoặc ghi tệp ảnh mẫu:\n\n{ex.Message}\n\nVui lòng thử lại.",
                Loc.T("dlg.app.title", "macro"),
                MessageBoxButton.OK, MessageBoxImage.Error);
            return;
        }

        _stagedTemplatePath = templatePath;
        _stagedTabIndex = _targetInstance?.Index ?? -1;

        if (_btnApplyRegion != null)
        {
            _btnApplyRegion.IsEnabled = true;
        }

        var mainWin = FindMainWindow();
        if (mainWin != null)
        {
            mainWin.PendingCroppedImageTemplate = (_stagedTemplatePath, _stagedTabIndex);
        }

        _selText.Text = $"✂ Đã cắt ảnh mẫu ({_selection.Width} \u00D7 {_selection.Height} px): {fileName}. Hãy bấm nút '✔ Chọn vùng này' ở trên để áp dụng vào lệnh Tìm hình ảnh.";
        _bannerTipText.Text = "💡 Đã tạo khung vùng chọn. Nhấn nút '✔ Chọn vùng này' ở thanh công cụ để gán ảnh vào lệnh Tìm hình ảnh đang chọn.";
        UpdateBannerContext();
    }

    public void ApplyCurrentSelectionAsTemplate()
    {
        // Fail-safe: if _stagedTemplatePath is missing or deleted, but a valid selection rectangle exists, crop & stage on-the-fly
        if (string.IsNullOrEmpty(_stagedTemplatePath) || !File.Exists(_stagedTemplatePath))
        {
            if (_selection.Width >= MinValidSelectionSize && _selection.Height >= MinValidSelectionSize)
            {
                CropAndStageSelection();
            }
        }

        if (string.IsNullOrEmpty(_stagedTemplatePath) || !File.Exists(_stagedTemplatePath))
        {
            _selText.Text = "💡 Vui lòng dùng chuột kéo chọn một vùng ảnh trước khi bấm '✔ Chọn vùng này'.";
            return;
        }

        int tabIdx = _stagedTabIndex;

        // Lưu ảnh đã cắt làm ảnh mẫu tạm thời trong phiên hiện tại (không lưu sang phiên sau)
        ActionDefaults.SetDefaultImageTemplate(_stagedTemplatePath, tabIdx);

        if (_onTemplateSaved != null)
        {
            _onTemplateSaved(_stagedTemplatePath);
        }

        var mainWin = FindMainWindow();
        if (mainWin != null)
        {
            mainWin.PendingCroppedImageTemplate = (_stagedTemplatePath, tabIdx);
        }

        var targetStep = mainWin?.GetCurrentlyTargetedStep();

        if (targetStep != null && MainWindow.IsImageAction(targetStep))
        {
            if (mainWin != null)
            {
                if (mainWin.Dispatcher.CheckAccess())
                {
                    mainWin.UpdateTargetStepImageTemplate(_stagedTemplatePath, tabIdx);
                }
                else
                {
                    mainWin.Dispatcher.Invoke(() => mainWin.UpdateTargetStepImageTemplate(_stagedTemplatePath, tabIdx));
                }
            }

            string stepName = targetStep.Name is { Length: > 0 }
                ? $"lệnh '{targetStep.Name}'"
                : $"thao tác '{Loc.ActionName(targetStep.Type)}'";
            string tabMsg = tabIdx >= 0 ? $" [Tab #{tabIdx}]" : "";
            string draftNote = (mainWin?.IsDraftTargeted() == true) ? " (nhấn nút 'Thêm' bên phải để đưa vào macro)" : "";

            _selText.Text = $"✔ Đã cập nhật ảnh mẫu cho {stepName}{tabMsg}{draftNote}.";
            _bannerTipText.Text = $"✔ Đã cập nhật ảnh mẫu cho {stepName}{draftNote}.";
        }
        else
        {
            string fileName = Path.GetFileName(_stagedTemplatePath);
            _selText.Text = $"✔ Đã lưu ảnh tạm ({fileName}). Ảnh sẽ tự động được dùng khi bạn thêm thao tác Tìm hình ảnh.";
            _bannerTipText.Text = "💡 Đã lưu ảnh mẫu tạm thời trong phiên. Nhấn 'Thêm' ở thao tác Tìm hình ảnh bên phải để tạo lệnh mới với ảnh này.";
        }

        UpdateBannerContext();
        UpdateDefaultBadge();
    }

    // =========================================================== capture/file
    private async void CaptureNew()
    {
        try
        {
            var target = _targetInstance;
            if (target == null || !target.IsRunning)
            {
                if (EmulatorManager.SelectedInstance != null && EmulatorManager.SelectedInstance.IsRunning)
                {
                    target = EmulatorManager.SelectedInstance;
                }
                else
                {
                    var live = EmulatorManager.GetLiveInstances().Where(i => i.IsRunning).ToList();
                    if (live.Count == 1)
                    {
                        target = live[0];
                    }
                }
            }

            if (target != null && target.IsRunning)
            {
                var cap = await LdPlayerService.CaptureInstanceAsync(target, EmulatorManager.CurrentPath);
                UpdateImage(cap.Path, cap, target);
            }
            else
            {
                MessageBox.Show(this,
                    "Không thể chụp lại màn hình: Không có tab giả lập LDPlayer nào đang chạy.",
                    Loc.T("dlg.app.title", "macro"),
                    MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, Loc.T("dlg.app.title", "macro"),
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void OpenFile()
    {
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Title = Loc.T("dlg.chooseimage", "Choose an image"),
            Filter = Loc.T("filter.image",
                "Images (*.png;*.jpg;*.jpeg;*.bmp)|*.png;*.jpg;*.jpeg;*.bmp|All files (*.*)|*.*"),
            InitialDirectory = ScreenshotService.TemplatesFolder(),
        };
        if (dialog.ShowDialog(this) != true) return;
        UpdateImage(dialog.FileName, null);
    }

    private void SwitchImage(string path, CaptureInfo capture)
    {
        UpdateImage(path, capture);
    }

    /// <summary>
    /// Updates the image displayed in this viewer without closing or reopening the window.
    /// Preserves the window state, updates title, context banner, and fits zoom.
    /// </summary>
    public void UpdateImage(string imagePath, CaptureInfo capture, LdInstanceInfo targetInstance = null)
    {
        if (string.IsNullOrEmpty(imagePath) || !File.Exists(imagePath)) return;

        _capture = capture;
        if (targetInstance != null)
        {
            _targetInstance = targetInstance;
        }

        var bitmap = new BitmapImage();
        bitmap.BeginInit();
        bitmap.CacheOption = BitmapCacheOption.OnLoad;
        bitmap.UriSource = new Uri(Path.GetFullPath(imagePath), UriKind.Absolute);
        bitmap.EndInit();
        bitmap.Freeze();
        _bitmap = bitmap;

        _image.Source = _bitmap;
        _image.Width = _bitmap.PixelWidth;
        _image.Height = _bitmap.PixelHeight;

        _highlightCanvas.Width = _bitmap.PixelWidth;
        _highlightCanvas.Height = _bitmap.PixelHeight;
        _overlay.Width = _bitmap.PixelWidth;
        _overlay.Height = _bitmap.PixelHeight;

        Title = (_targetInstance != null
            ? $"macro - Chụp màn hình Tab #{_targetInstance.Index} ({_targetInstance.Name})"
            : Loc.T("viewer.title", "Image viewer")) + $" - {Path.GetFileName(imagePath)}";

        _chkUseScreenCoords.IsChecked = _targetInstance == null && _capture != null && (_capture.OffsetX != 0 || _capture.OffsetY != 0);

        ClearSelection();
        ClearPickedPoint();
        UpdateBannerContext();
        ZoomFit();

        _sizeText.Text = $"{_bitmap.PixelWidth} \u00D7 {_bitmap.PixelHeight} px";
        _coordText.Text = "";
    }

    private void CopyCoordinates()
    {
        var text = _coordText.Text;
        var index = text.IndexOf("Screen:", StringComparison.Ordinal);
        if (index > 0) text = text.Substring(0, index).Trim();
        text = text.Replace(Loc.T("viewer.pickcoords", "Cursor:") + " ", "");
        try
        {
            Clipboard.SetText(text);
            _selText.Text = Loc.F("viewer.copied", "Copied {0}", text);
        }
        catch { /* clipboard can be locked */ }
    }

    /// <summary>Draw result rectangles (image coordinates) over the image -
    /// used by the Find Image test to show where the template matched.</summary>
    public void AddHighlight(int x, int y, int w, int h, string label = null)
    {
        var rect = new Rectangle
        {
            Stroke = Brushes.LimeGreen,
            StrokeThickness = 2,
            StrokeDashArray = new DoubleCollection { 4, 2 },
            IsHitTestVisible = false,
        };
        rect.Width = w;
        rect.Height = h;
        rect.Margin = new Thickness(x, y, 0, 0);
        _highlightCanvas.Children.Add(rect);
        _selText.Text = $"{x}, {y}  ({w} x {h})"
                        + (label != null ? $"  {label}" : "");
    }

    private void UpdateDefaultBadge()
    {
        _defaultBadgeText.Text = ActionDefaults.HasDefaultImageTemplate
            ? $"🖼️ Ảnh tạm phiên này: {Path.GetFileName(ActionDefaults.DefaultImagePath)}"
            : "";
    }

    private void ShowDefaultImageManager()
    {
        var win = new Window
        {
            Title = "Quản lý ảnh mặc định (Tìm ảnh)",
            Owner = this,
            Width = 460,
            SizeToContent = SizeToContent.Height,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            ResizeMode = ResizeMode.NoResize,
            Background = (Brush)new BrushConverter().ConvertFromString("#1F2023"),
            Foreground = (Brush)new BrushConverter().ConvertFromString("#E8E9EC"),
        };

        var root = new StackPanel { Margin = new Thickness(16) };

        var title = new TextBlock
        {
            Text = "ẢNH MẪU MẶC ĐỊNH (TÌM ẢNH)",
            FontWeight = FontWeights.Bold,
            FontSize = 13,
            Foreground = (Brush)new BrushConverter().ConvertFromString("#F2F3F5"),
            Margin = new Thickness(0, 0, 0, 8),
        };
        root.Children.Add(title);

        var desc = new TextBlock
        {
            Text = "Ảnh này sẽ được tự động sử dụng khi bạn thêm các thao tác Tìm ảnh mới vào macro.",
            Foreground = (Brush)new BrushConverter().ConvertFromString("#8F939C"),
            FontSize = 11,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 12),
        };
        root.Children.Add(desc);

        if (ActionDefaults.HasDefaultImageTemplate)
        {
            var fileName = Path.GetFileName(ActionDefaults.DefaultImagePath);
            var tabInfo = ActionDefaults.DefaultImageTabIndex >= 0 ? $"  |  Tab #{ActionDefaults.DefaultImageTabIndex}" : "";
            var nameBlock = new TextBlock
            {
                Text = $"Tệp ảnh: {fileName}{tabInfo}",
                FontWeight = FontWeights.SemiBold,
                FontSize = 11.5,
                Margin = new Thickness(0, 0, 0, 6),
            };
            root.Children.Add(nameBlock);

            var previewBorder = new Border
            {
                Background = (Brush)new BrushConverter().ConvertFromString("#141517"),
                BorderBrush = (Brush)new BrushConverter().ConvertFromString("#35363B"),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(4),
                Height = 140,
                Margin = new Thickness(0, 0, 0, 12),
            };
            try
            {
                var bmp = new BitmapImage();
                bmp.BeginInit();
                bmp.CacheOption = BitmapCacheOption.OnLoad;
                bmp.UriSource = new System.Uri(Path.GetFullPath(ActionDefaults.DefaultImagePath));
                bmp.EndInit();
                bmp.Freeze();
                previewBorder.Child = new Image
                {
                    Source = bmp,
                    Stretch = Stretch.Uniform,
                    Margin = new Thickness(4),
                };
            }
            catch
            {
                previewBorder.Child = new TextBlock
                {
                    Text = "Không thể tải ảnh xem trước",
                    Foreground = (Brush)new BrushConverter().ConvertFromString("#D24B41"),
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center,
                };
            }
            root.Children.Add(previewBorder);

            var btnPanel = new WrapPanel { HorizontalAlignment = HorizontalAlignment.Right };

            var btnChange = new Button
            {
                Content = "📂 Đổi ảnh khác...",
                Padding = new Thickness(10, 4, 10, 4),
                Margin = new Thickness(0, 0, 8, 0),
            };
            btnChange.Click += (_, _) =>
            {
                var ofd = new Microsoft.Win32.OpenFileDialog
                {
                    Title = "Chọn ảnh mẫu mặc định mới",
                    Filter = "Tệp hình ảnh (*.png;*.jpg;*.bmp)|*.png;*.jpg;*.bmp|Tất cả tệp (*.*)|*.*",
                };
                if (ofd.ShowDialog() == true)
                {
                    ActionDefaults.SetDefaultImageTemplate(ofd.FileName, ActionDefaults.DefaultImageTabIndex);
                    win.Close();
                    ShowDefaultImageManager();
                }
            };
            btnPanel.Children.Add(btnChange);

            var btnDelete = new Button
            {
                Content = "🗑️ Xóa ảnh mặc định",
                Padding = new Thickness(10, 4, 10, 4),
                Margin = new Thickness(0, 0, 8, 0),
            };
            btnDelete.Click += (_, _) =>
            {
                ActionDefaults.ClearDefaultImageTemplate();
                win.Close();
                _selText.Text = "✔ Đã xóa ảnh mẫu mặc định.";
                UpdateBannerContext();
                UpdateDefaultBadge();
            };
            btnPanel.Children.Add(btnDelete);

            var btnClose = new Button
            {
                Content = "Đóng",
                Padding = new Thickness(12, 4, 12, 4),
                IsCancel = true,
            };
            btnClose.Click += (_, _) => win.Close();
            btnPanel.Children.Add(btnClose);

            root.Children.Add(btnPanel);
        }
        else
        {
            var emptyText = new TextBlock
            {
                Text = "Hiện tại chưa thiết lập ảnh mẫu mặc định nào.\nBạn có thể dùng chế độ '✂ Chọn vùng' và kéo thả trên ảnh chụp để cắt ảnh mẫu, hoặc chọn một tệp ảnh có sẵn từ máy.",
                Foreground = (Brush)new BrushConverter().ConvertFromString("#D4A72C"),
                FontSize = 11.5,
                Margin = new Thickness(0, 0, 0, 14),
                TextWrapping = TextWrapping.Wrap,
            };
            root.Children.Add(emptyText);

            var btnPanel = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
            var btnBrowse = new Button
            {
                Content = "📂 Chọn tệp ảnh làm mặc định...",
                Padding = new Thickness(10, 4, 10, 4),
                Margin = new Thickness(0, 0, 8, 0),
            };
            btnBrowse.Click += (_, _) =>
            {
                var ofd = new Microsoft.Win32.OpenFileDialog
                {
                    Title = "Chọn tệp ảnh làm mặc định",
                    Filter = "Tệp hình ảnh (*.png;*.jpg;*.bmp)|*.png;*.jpg;*.bmp|Tất cả tệp (*.*)|*.*",
                };
                if (ofd.ShowDialog() == true)
                {
                    ActionDefaults.SetDefaultImageTemplate(ofd.FileName, _targetInstance?.Index ?? -1);
                    win.Close();
                    _selText.Text = $"✔ Đã lưu ảnh mặc định: {Path.GetFileName(ofd.FileName)}.";
                    UpdateBannerContext();
                    UpdateDefaultBadge();
                }
            };
            btnPanel.Children.Add(btnBrowse);

            var btnClose = new Button
            {
                Content = "Đóng",
                Padding = new Thickness(12, 4, 12, 4),
                IsCancel = true,
            };
            btnClose.Click += (_, _) => win.Close();
            btnPanel.Children.Add(btnClose);

            root.Children.Add(btnPanel);
        }

        win.Content = root;
        win.ShowDialog();
    }
}
