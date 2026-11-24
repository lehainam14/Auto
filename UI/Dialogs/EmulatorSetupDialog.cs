using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Macro.Core.Emulator;
using Macro.Localization;
using Macro.UI.Widgets;
using Microsoft.Win32;

namespace Macro.UI.Dialogs;

/// <summary>
/// Dialog for initial setup or re-configuring the Android emulator path (LDPlayer).
/// Validates path, checks file permissions and components, and saves configuration.
/// </summary>
public class EmulatorSetupDialog : Window
{
    private readonly ComboBox _cboEmulator;
    private readonly TextBox _txtPath;
    private readonly TextBlock _lblStatus;
    private readonly Border _statusCard;
    private readonly Button _btnSave;
    private readonly Button _btnVerify;
    private EmulatorValidationResult _currentResult;

    public EmulatorSetupDialog(Window owner = null)
    {
        Owner = owner;
        Title = Loc.T("emu.setup.title", "Cấu hình trình giả lập Android");
        Width = 530;
        SizeToContent = SizeToContent.Height;
        WindowStartupLocation = owner != null ? WindowStartupLocation.CenterOwner : WindowStartupLocation.CenterScreen;
        ResizeMode = ResizeMode.NoResize;
        Background = (Brush)new BrushConverter().ConvertFromString("#1F2023");
        Foreground = (Brush)new BrushConverter().ConvertFromString("#E8E9EC");

        var root = new StackPanel { Margin = new Thickness(16, 14, 16, 16) };

        // ---- Header
        var header = new Grid { Margin = new Thickness(0, 0, 0, 10) };
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        var icon = Ui.IconImage("application", "#38B26C", 22);
        icon.Margin = new Thickness(0, 2, 10, 0);
        icon.VerticalAlignment = VerticalAlignment.Top;
        Grid.SetColumn(icon, 0);
        header.Children.Add(icon);

        var headerText = new StackPanel();
        var title = Ui.Label(Loc.T("emu.setup.header", "THIẾT LẬP TRÌNH GIẢ LẬP ANDROID"),
            (Brush)new BrushConverter().ConvertFromString("#F2F3F5"), 13, true);
        var desc = new TextBlock
        {
            Text = Loc.T("emu.setup.desc",
                "Macro yêu cầu liên kết với trình giả lập Android (LDPlayer) để hoạt động. Vui lòng chọn thư mục cài đặt LDPlayer trên máy tính của bạn."),
            Foreground = (Brush)new BrushConverter().ConvertFromString("#8F939C"),
            FontSize = 11,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 3, 0, 0),
        };
        headerText.Children.Add(title);
        headerText.Children.Add(desc);
        Grid.SetColumn(headerText, 1);
        header.Children.Add(headerText);
        root.Children.Add(header);

        // ---- Section divider
        root.Children.Add(Ui.SectionHeader(Loc.T("emu.section.config", "Thông tin giả lập")));

        // ---- Emulator Selector
        var formGrid = new Grid { Margin = new Thickness(0, 6, 0, 6) };
        formGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(110) });
        formGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        var lblEmu = Ui.Label(Loc.T("emu.type", "Trình giả lập:"),
            (Brush)new BrushConverter().ConvertFromString("#8F939C"), 11);
        lblEmu.VerticalAlignment = VerticalAlignment.Center;
        Grid.SetColumn(lblEmu, 0);
        formGrid.Children.Add(lblEmu);

        _cboEmulator = new ComboBox { Height = 25, FontSize = 11.5 };
        foreach (var validator in EmulatorManager.GetValidators())
        {
            _cboEmulator.Items.Add(new ComboBoxItem
            {
                Content = $"{validator.DisplayName} (Khuyến nghị)",
                Tag = validator.Id,
            });
        }
        _cboEmulator.SelectedIndex = 0;
        Grid.SetColumn(_cboEmulator, 1);
        formGrid.Children.Add(_cboEmulator);
        root.Children.Add(formGrid);

        // ---- Path Selector
        var pathLabel = Ui.Label(Loc.T("emu.dir.label", "Thư mục cài đặt LDPlayer:"),
            (Brush)new BrushConverter().ConvertFromString("#8F939C"), 11);
        pathLabel.Margin = new Thickness(0, 4, 0, 3);
        root.Children.Add(pathLabel);

        var pathGrid = new Grid { Margin = new Thickness(0, 0, 0, 4) };
        pathGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        pathGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        _txtPath = new TextBox
        {
            Height = 25,
            FontSize = 11.5,
            Padding = new Thickness(6, 2.5, 6, 2.5),
            Text = EmulatorManager.CurrentPath,
            Tag = Loc.T("emu.dir.placeholder", "Chọn hoặc dán đường dẫn thư mục LDPlayer..."),
        };
        _txtPath.TextChanged += (_, _) => OnPathChanged();
        Grid.SetColumn(_txtPath, 0);
        pathGrid.Children.Add(_txtPath);

        var btnBrowse = new Button
        {
            Content = Loc.T("emu.browse_folder", "Duyệt thư mục..."),
            Padding = new Thickness(10, 3, 10, 3),
            FontSize = 11,
            Margin = new Thickness(6, 0, 0, 0),
        };
        btnBrowse.Click += (_, _) => BrowseFolder();
        Grid.SetColumn(btnBrowse, 1);
        pathGrid.Children.Add(btnBrowse);
        root.Children.Add(pathGrid);

        // ---- Hint
        var hint = new TextBlock
        {
            Text = Loc.T("emu.hint.ldplayer_dir",
                "Gợi ý: Chọn thư mục cài đặt LDPlayer (ví dụ: C:\\LDPlayer\\LDPlayer9 hoặc D:\\leidian\\LDPlayer9). Chương trình sẽ tự động quét và kiểm tra các tệp cần thiết."),
            Foreground = (Brush)new BrushConverter().ConvertFromString("#6B707B"),
            FontSize = 10.5,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 8),
        };
        root.Children.Add(hint);

        // ---- Status Card
        _statusCard = new Border
        {
            Background = (Brush)new BrushConverter().ConvertFromString("#161719"),
            BorderBrush = (Brush)new BrushConverter().ConvertFromString("#2E3038"),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(4),
            Padding = new Thickness(10, 8, 10, 8),
            Margin = new Thickness(0, 2, 0, 12),
        };

        _lblStatus = new TextBlock
        {
            FontSize = 11,
            TextWrapping = TextWrapping.Wrap,
            Foreground = (Brush)new BrushConverter().ConvertFromString("#8F939C"),
            Text = Loc.T("emu.status.not_checked", "Vui lòng chọn hoặc nhập đường dẫn để kiểm tra."),
        };
        _statusCard.Child = _lblStatus;
        root.Children.Add(_statusCard);

        // ---- Bottom Buttons
        var bottomGrid = new Grid();
        bottomGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        bottomGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        bottomGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        _btnVerify = new Button
        {
            Content = Loc.T("emu.verify", "Kiểm tra"),
            Padding = new Thickness(12, 4, 12, 4),
            FontSize = 11,
            Style = (Style)Application.Current.Resources["Btn.Tool"],
        };
        _btnVerify.Click += (_, _) => VerifyPath(showSuccessToast: true);
        Grid.SetColumn(_btnVerify, 0);
        bottomGrid.Children.Add(_btnVerify);

        var rightButtons = new StackPanel { Orientation = Orientation.Horizontal };

        var btnCancel = new Button
        {
            Content = Loc.T("emu.cancel", "Đóng"),
            Padding = new Thickness(12, 4, 12, 4),
            FontSize = 11,
            Margin = new Thickness(0, 0, 8, 0),
        };
        btnCancel.Click += (_, _) => Close();
        rightButtons.Children.Add(btnCancel);

        _btnSave = new Button
        {
            Content = Loc.T("emu.save", "Lưu & Tiếp tục"),
            Padding = new Thickness(14, 4, 14, 4),
            FontSize = 11.5,
            FontWeight = FontWeights.Bold,
            Background = (Brush)Application.Current.Resources["Brush.SectionGreen"]
                         ?? new SolidColorBrush(Color.FromRgb(0x38, 0xB2, 0x6C)),
            Foreground = Brushes.White,
        };
        _btnSave.Click += (_, _) => SaveAndClose();
        rightButtons.Children.Add(_btnSave);

        Grid.SetColumn(rightButtons, 2);
        bottomGrid.Children.Add(rightButtons);
        root.Children.Add(bottomGrid);

        Content = root;

        // Auto verify on opening if a path already exists
        Loaded += (_, _) =>
        {
            if (!string.IsNullOrWhiteSpace(_txtPath.Text))
            {
                VerifyPath(showSuccessToast: false);
            }
        };
    }

    private string SelectedEmulatorId =>
        (_cboEmulator.SelectedItem as ComboBoxItem)?.Tag as string ?? "ldplayer";

    private void OnPathChanged()
    {
        // When text changes, clear old status if empty
        if (string.IsNullOrWhiteSpace(_txtPath.Text))
        {
            _currentResult = null;
            _lblStatus.Text = Loc.T("emu.status.not_checked", "Vui lòng chọn hoặc nhập đường dẫn để kiểm tra.");
            _lblStatus.Foreground = (Brush)new BrushConverter().ConvertFromString("#8F939C");
            _statusCard.BorderBrush = (Brush)new BrushConverter().ConvertFromString("#2E3038");
        }
    }

    private void BrowseFolder()
    {
        var dialog = new OpenFolderDialog
        {
            Title = Loc.T("emu.browse.folder_title", "Chọn thư mục cài đặt LDPlayer"),
            Multiselect = false,
        };

        if (!string.IsNullOrWhiteSpace(_txtPath.Text) && System.IO.Directory.Exists(_txtPath.Text))
        {
            dialog.InitialDirectory = _txtPath.Text;
        }

        if (dialog.ShowDialog(this) == true)
        {
            _txtPath.Text = dialog.FolderName;
            VerifyPath(showSuccessToast: true);
        }
    }

    private bool VerifyPath(bool showSuccessToast)
    {
        var rawPath = _txtPath.Text;
        _currentResult = EmulatorManager.Validate(SelectedEmulatorId, rawPath);

        if (_currentResult.IsValid)
        {
            var versionText = string.IsNullOrEmpty(_currentResult.VersionInfo)
                ? "" : $" - Phiên bản: {_currentResult.VersionInfo}";
            _lblStatus.Text = string.Format(
                Loc.T("emu.status.ok_dir", "✔ Đã xác thực thư mục LDPlayer thành công: {0} ({1})\nThư mục: {2}\nTệp thực thi: {3}"),
                _currentResult.EmulatorName, versionText, _currentResult.DirectoryPath, _currentResult.ExecutablePath);
            _lblStatus.Foreground = (Brush)Application.Current.Resources["Brush.SectionGreen"]
                                    ?? new SolidColorBrush(Color.FromRgb(0x38, 0xB2, 0x6C));
            _statusCard.BorderBrush = _lblStatus.Foreground;
            return true;
        }
        else
        {
            _lblStatus.Text = $"✘ Lỗi xác thực:\n{_currentResult.ErrorMessage}\n\nHướng dẫn: Vui lòng duyệt và chọn thư mục cài đặt LDPlayer (chứa tệp 'dnplayer.exe' hoặc 'ldconsole.exe').";
            _lblStatus.Foreground = new SolidColorBrush(Color.FromRgb(0xF0, 0x76, 0x4F));
            _statusCard.BorderBrush = _lblStatus.Foreground;
            return false;
        }
    }

    private void SaveAndClose()
    {
        if (!VerifyPath(showSuccessToast: false))
        {
            MessageBox.Show(this,
                _currentResult?.ErrorMessage
                ?? Loc.T("emu.err.not_configured", "Đường dẫn chưa hợp lệ. Vui lòng kiểm tra lại."),
                Loc.T("dlg.app.title", "macro"),
                MessageBoxButton.OK, MessageBoxImage.Warning);
            _txtPath.Focus();
            return;
        }

        var saveResult = EmulatorManager.SaveConfig(SelectedEmulatorId, _txtPath.Text);
        if (saveResult.IsValid)
        {
            DialogResult = true;
            Close();
        }
        else
        {
            MessageBox.Show(this, saveResult.ErrorMessage,
                Loc.T("dlg.app.title", "macro"),
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }
}
