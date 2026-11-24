using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Macro.Localization;
using Macro.UI.Widgets;

namespace Macro.UI.Dialogs;

/// <summary>
/// Modal preview dialog for confirming or re-selecting a cropped image region.
/// </summary>
public class CropPreviewDialog : Window
{
    public CropPreviewDialog(BitmapSource croppedBitmap, int width, int height, Window owner = null)
    {
        Owner = owner;
        Title = Loc.T("crop.preview.title", "Xem trước vùng ảnh đã chọn");
        Width = 460;
        SizeToContent = SizeToContent.Height;
        MinWidth = 380;
        WindowStartupLocation = owner != null ? WindowStartupLocation.CenterOwner : WindowStartupLocation.CenterScreen;
        ResizeMode = ResizeMode.NoResize;
        Background = (Brush)new BrushConverter().ConvertFromString("#1F2023");
        Foreground = (Brush)new BrushConverter().ConvertFromString("#E8E9EC");

        var root = new StackPanel { Margin = new Thickness(16, 14, 16, 16) };

        // Header
        var header = new Grid { Margin = new Thickness(0, 0, 0, 10) };
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        var icon = Ui.IconImage("image", "#38B26C", 20);
        icon.Margin = new Thickness(0, 2, 8, 0);
        icon.VerticalAlignment = VerticalAlignment.Top;
        Grid.SetColumn(icon, 0);
        header.Children.Add(icon);

        var headerText = new StackPanel();
        var title = Ui.Label(Loc.T("crop.preview.header", "XÁC NHẬN VÙNG ẢNH MẪU"),
            (Brush)new BrushConverter().ConvertFromString("#F2F3F5"), 13, true);
        var desc = new TextBlock
        {
            Text = Loc.T("crop.preview.desc",
                "Xem trước vùng ảnh bạn vừa cắt. Bấm 'Xác nhận' để lưu làm ảnh mẫu và tự động gán vào thao tác tìm ảnh."),
            Foreground = (Brush)new BrushConverter().ConvertFromString("#8F939C"),
            FontSize = 11,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 2, 0, 0),
        };
        headerText.Children.Add(title);
        headerText.Children.Add(desc);
        Grid.SetColumn(headerText, 1);
        header.Children.Add(headerText);
        root.Children.Add(header);

        // Preview box
        var previewBorder = new Border
        {
            Background = (Brush)new BrushConverter().ConvertFromString("#141517"),
            BorderBrush = (Brush)new BrushConverter().ConvertFromString("#2E3038"),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(4),
            Padding = new Thickness(10),
            MinHeight = 120,
            MaxHeight = 240,
            Margin = new Thickness(0, 4, 0, 8),
        };

        var img = new Image
        {
            Source = croppedBitmap,
            Stretch = Stretch.Uniform,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
        };
        previewBorder.Child = img;
        root.Children.Add(previewBorder);

        // Dimension Info
        var infoPanel = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 14) };
        var lblDim = new TextBlock
        {
            Text = $"Độ phân giải ảnh mẫu: {width} x {height} px  •  Chất lượng: Nguyên bản (1:1)",
            Foreground = (Brush)new BrushConverter().ConvertFromString("#38B26C"),
            FontSize = 11,
            FontWeight = FontWeights.SemiBold,
        };
        infoPanel.Children.Add(lblDim);
        root.Children.Add(infoPanel);

        // Bottom buttons
        var btnGrid = new Grid();
        btnGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        btnGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var rightStack = new StackPanel { Orientation = Orientation.Horizontal };

        var btnCancel = new Button
        {
            Content = Loc.T("crop.preview.reselect", "✕ Chọn lại"),
            Padding = new Thickness(12, 4, 12, 4),
            FontSize = 11,
            Margin = new Thickness(0, 0, 8, 0),
        };
        btnCancel.Click += (_, _) =>
        {
            DialogResult = false;
            Close();
        };
        rightStack.Children.Add(btnCancel);

        var btnConfirm = new Button
        {
            Content = Loc.T("crop.preview.confirm", "✔ Xác nhận dùng làm ảnh mẫu"),
            Padding = new Thickness(14, 4, 14, 4),
            FontSize = 11.5,
            FontWeight = FontWeights.Bold,
            Background = (Brush)Application.Current.Resources["Brush.SectionGreen"]
                         ?? new SolidColorBrush(Color.FromRgb(0x38, 0xB2, 0x6C)),
            Foreground = Brushes.White,
        };
        btnConfirm.Click += (_, _) =>
        {
            DialogResult = true;
            Close();
        };
        rightStack.Children.Add(btnConfirm);

        Grid.SetColumn(rightStack, 1);
        btnGrid.Children.Add(rightStack);
        root.Children.Add(btnGrid);

        Content = root;
    }
}
