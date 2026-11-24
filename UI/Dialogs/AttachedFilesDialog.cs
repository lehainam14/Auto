using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Macro.Core;
using Macro.Localization;
using Macro.UI.Widgets;

namespace Macro.UI.Dialogs;

/// <summary>
/// Dialog to manage text (.txt) files attached specifically to a macro.
/// Files are stored per macro and used by the read_txt_line action.
/// </summary>
public class AttachedFilesDialog : Window
{
    private readonly Workflow _workflow;
    private readonly Action _onChanged;
    private readonly string _macroFilePath;
    private readonly StackPanel _fileListStack;
    private readonly TextBlock _emptyNotice;

    public AttachedFilesDialog(Workflow workflow, Window owner, string macroFilePath = null, Action onChanged = null)
    {
        _workflow = workflow;
        _macroFilePath = macroFilePath;
        _onChanged = onChanged;

        Title = Loc.T("attached.dialog.title", "Quản lý tệp TXT đính kèm cho macro");
        Width = 580;
        Height = 440;
        MinWidth = 480;
        MinHeight = 360;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Owner = owner;
        Background = (Brush)Application.Current.Resources["Brush.BgPanel"] ?? (Brush)new BrushConverter().ConvertFromString("#1D1F24");

        var root = new Grid { Margin = new Thickness(16) };
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        // Header
        var header = new TextBlock
        {
            Text = $"📄 Tệp TXT đính kèm riêng cho: {workflow.Name}",
            FontSize = 14.5,
            FontWeight = FontWeights.Bold,
            Foreground = (Brush)new BrushConverter().ConvertFromString("#F2F3F5"),
            Margin = new Thickness(0, 0, 0, 4),
        };
        Grid.SetRow(header, 0);
        root.Children.Add(header);

        var hint = new TextBlock
        {
            Text = "Các tệp .txt này được lưu cùng macro và độc lập với các macro khác. Thao tác 'Lấy văn bản' có thể truy xuất nội dung các tệp này.",
            FontSize = 11.5,
            Foreground = (Brush)new BrushConverter().ConvertFromString("#8F939C"),
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 12),
        };
        Grid.SetRow(hint, 1);
        root.Children.Add(hint);

        // Files container
        var scroll = new ScrollViewer
        {
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            Margin = new Thickness(0, 0, 0, 12),
        };
        var filesBorder = new Border
        {
            Background = (Brush)new BrushConverter().ConvertFromString("#151619"),
            BorderBrush = (Brush)new BrushConverter().ConvertFromString("#2F3034"),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(4),
            Padding = new Thickness(8),
        };

        var mainContent = new StackPanel();
        _fileListStack = new StackPanel();
        _emptyNotice = new TextBlock
        {
            Text = "Chưa có tệp .txt nào được đính kèm vào macro này.\nBấm nút '+ Đính kèm tệp .txt...' bên dưới để thêm.",
            Foreground = (Brush)new BrushConverter().ConvertFromString("#737680"),
            FontSize = 12,
            TextAlignment = TextAlignment.Center,
            Margin = new Thickness(0, 40, 0, 40),
        };
        mainContent.Children.Add(_emptyNotice);
        mainContent.Children.Add(_fileListStack);
        filesBorder.Child = mainContent;
        scroll.Content = filesBorder;
        Grid.SetRow(scroll, 2);
        root.Children.Add(scroll);

        // Buttons row
        var btnRow = new DockPanel { LastChildFill = false };

        var btnAdd = new Button
        {
            Content = "➕ Đính kèm tệp .txt...",
            Style = (Style)Application.Current.Resources["Btn.Tool"],
            Padding = new Thickness(12, 6, 12, 6),
            FontWeight = FontWeights.SemiBold,
            Margin = new Thickness(0, 0, 8, 0),
        };
        btnAdd.Click += (_, _) => PromptAddFile();
        DockPanel.SetDock(btnAdd, Dock.Left);
        btnRow.Children.Add(btnAdd);

        var btnClose = new Button
        {
            Content = "Đóng",
            Style = (Style)Application.Current.Resources["Btn.Tool"],
            Padding = new Thickness(16, 6, 16, 6),
        };
        btnClose.Click += (_, _) => Close();
        DockPanel.SetDock(btnClose, Dock.Right);
        btnRow.Children.Add(btnClose);

        Grid.SetRow(btnRow, 3);
        root.Children.Add(btnRow);

        Content = root;
        RefreshFileList();
    }

    private void RefreshFileList()
    {
        _fileListStack.Children.Clear();
        if (_workflow.AttachedFiles.Count == 0)
        {
            _emptyNotice.Visibility = Visibility.Visible;
            return;
        }

        _emptyNotice.Visibility = Visibility.Collapsed;

        foreach (var file in _workflow.AttachedFiles)
        {
            var card = BuildFileCard(file);
            _fileListStack.Children.Add(card);
        }
    }

    private Border BuildFileCard(AttachedTxtFile file)
    {
        var card = new Border
        {
            Background = (Brush)new BrushConverter().ConvertFromString("#1D1F24"),
            BorderBrush = (Brush)new BrushConverter().ConvertFromString("#2F3034"),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(4),
            Padding = new Thickness(10, 8, 10, 8),
            Margin = new Thickness(0, 0, 0, 6),
        };

        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var infoStack = new StackPanel();

        var nameRow = new StackPanel { Orientation = Orientation.Horizontal };
        nameRow.Children.Add(new TextBlock
        {
            Text = "📄 " + file.Name,
            FontWeight = FontWeights.Bold,
            Foreground = (Brush)new BrushConverter().ConvertFromString("#F2F3F5"),
            FontSize = 12.5,
        });

        string resolved = _workflow.ResolveAttachedFilePath(file, _macroFilePath);
        bool exists = !string.IsNullOrEmpty(resolved) && File.Exists(resolved);
        int lineCount = 0;
        if (exists)
        {
            try { lineCount = File.ReadAllLines(resolved).Length; } catch { }
        }

        var statusBadge = new Border
        {
            Background = exists
                ? new SolidColorBrush(Color.FromArgb(40, 63, 185, 111))
                : new SolidColorBrush(Color.FromArgb(40, 210, 75, 65)),
            CornerRadius = new CornerRadius(3),
            Padding = new Thickness(5, 1, 5, 1),
            Margin = new Thickness(8, 0, 0, 0),
            VerticalAlignment = VerticalAlignment.Center,
        };
        statusBadge.Child = new TextBlock
        {
            Text = exists ? $"✔ Có sẵn ({lineCount} dòng)" : "❌ Không tìm thấy tệp",
            FontSize = 10.5,
            Foreground = exists
                ? (Brush)Application.Current.Resources["Brush.SectionGreen"] ?? Brushes.LightGreen
                : new SolidColorBrush(Color.FromRgb(0xD2, 0x4B, 0x41)),
        };
        nameRow.Children.Add(statusBadge);
        infoStack.Children.Add(nameRow);

        var pathText = new TextBlock
        {
            Text = file.FilePath,
            FontSize = 11,
            Foreground = (Brush)new BrushConverter().ConvertFromString("#8F939C"),
            TextTrimming = TextTrimming.CharacterEllipsis,
            ToolTip = exists ? resolved : $"Không tìm thấy tại: {file.FilePath}",
            Margin = new Thickness(0, 3, 0, 0),
        };
        infoStack.Children.Add(pathText);

        Grid.SetColumn(infoStack, 0);
        grid.Children.Add(infoStack);

        var actions = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };

        var btnView = new Button
        {
            Content = "👁️",
            Style = (Style)Application.Current.Resources["Btn.Tool"],
            ToolTip = "Xem trước nội dung tệp",
            Padding = new Thickness(6, 3, 6, 3),
            Margin = new Thickness(4, 0, 0, 0),
            IsEnabled = exists,
        };
        btnView.Click += (_, _) =>
        {
            try
            {
                if (exists)
                {
                    System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(resolved) { UseShellExecute = true });
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, ex.Message, "macro", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        };
        actions.Children.Add(btnView);

        var btnRemove = new Button
        {
            Content = "🗑️",
            Style = (Style)Application.Current.Resources["Btn.Tool"],
            ToolTip = "Xóa tệp khỏi danh sách đính kèm của macro",
            Padding = new Thickness(6, 3, 6, 3),
            Margin = new Thickness(4, 0, 0, 0),
        };
        btnRemove.Click += (_, _) =>
        {
            if (MessageBox.Show(this,
                $"Bạn có chắc chắn muốn bỏ đính kèm tệp '{file.Name}' khỏi macro này?",
                "macro", MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes)
            {
                _workflow.AttachedFiles.Remove(file);
                _onChanged?.Invoke();
                RefreshFileList();
            }
        };
        actions.Children.Add(btnRemove);

        Grid.SetColumn(actions, 1);
        grid.Children.Add(actions);

        card.Child = grid;
        return card;
    }

    private void PromptAddFile()
    {
        var ofd = new Microsoft.Win32.OpenFileDialog
        {
            Title = "Chọn tệp văn bản .txt để đính kèm vào macro",
            Filter = "Tệp văn bản (*.txt)|*.txt|Tất cả tệp (*.*)|*.*",
            Multiselect = true,
        };
        if (ofd.ShowDialog(this) == true)
        {
            foreach (var fn in ofd.FileNames)
            {
                var name = Path.GetFileName(fn);
                // Avoid duplicates by path
                if (!_workflow.AttachedFiles.Any(f => f.FilePath == fn || f.Name == name))
                {
                    _workflow.AttachedFiles.Add(new AttachedTxtFile
                    {
                        Id = Guid.NewGuid().ToString("N").Substring(0, 8),
                        Name = name,
                        FilePath = fn,
                    });
                }
            }
            _onChanged?.Invoke();
            RefreshFileList();
        }
    }
}
