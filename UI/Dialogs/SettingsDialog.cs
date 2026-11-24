using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Macro.Core;
using Macro.Localization;
using Macro.UI.Widgets;

namespace Macro.UI.Dialogs;

/// <summary>Workflow settings: repeat count, delay between steps, failsafe
/// and the UI language.</summary>
public class SettingsDialog : Window
{
    private readonly Workflow _workflow;
    private readonly IntegerBox _repeat;
    private readonly IntegerBox _delay;
    private readonly CheckBox _failsafe;
    private readonly ComboBox _language;
    private readonly string _initialLanguage;

    public SettingsDialog(Workflow workflow, Window owner)
    {
        _workflow = workflow;
        _initialLanguage = Loc.Language;
        Title = Loc.T("set.title", "Workflow Settings");
        Width = 440;
        SizeToContent = SizeToContent.Height;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ResizeMode = ResizeMode.NoResize;
        Owner = owner;

        var stack = new StackPanel { Margin = new Thickness(16) };
        stack.Children.Add(new TextBlock
        {
            Text = Loc.F("set.for", "Settings for '{0}'", workflow.Name),
            FontSize = 15,
            FontWeight = FontWeights.Bold,
            Foreground = (Brush)new BrushConverter().ConvertFromString("#F2F3F5"),
            Margin = new Thickness(0, 0, 0, 14),
        });

        _repeat = new IntegerBox(0, 10000)
        {
            Value = ParamUtil.ToInt(workflow.Settings.GetValueOrDefault("repeat_workflow"), 1),
        };
        _repeat.ValueChanged += v => _workflow.Settings["repeat_workflow"] = v;
        stack.Children.Add(FormRow(Loc.T("set.repeat", "Repeat whole workflow"),
            _repeat, Loc.T("set.repeat.tip",
                "Số lần chạy quy trình (0 = lặp vô hạn cho đến khi dừng).")));

        _delay = new IntegerBox(0, 600000)
        {
            Value = ParamUtil.ToInt(workflow.Settings.GetValueOrDefault("step_delay_ms")),
        };
        _delay.ValueChanged += v => _workflow.Settings["step_delay_ms"] = v;
        stack.Children.Add(FormRow(Loc.T("set.delay", "Delay between steps"),
            _delay, Loc.T("set.delay.tip", "Extra pause after every step.")));

        _failsafe = new CheckBox
        {
            IsChecked = ParamUtil.ToBool(workflow.Settings.GetValueOrDefault("failsafe"), true),
            Content = Loc.T("set.failsafe", "Enable failsafe (recommended)"),
            Foreground = (Brush)new BrushConverter().ConvertFromString("#E8E9EC"),
            Margin = new Thickness(0, 4, 0, 0),
        };
        _failsafe.Checked += (_, _) => _workflow.Settings["failsafe"] = true;
        _failsafe.Unchecked += (_, _) => _workflow.Settings["failsafe"] = false;
        ToolTipService.SetToolTip(_failsafe, Loc.T("set.failsafe.tip",
            "When enabled, slamming the mouse into the top-left corner of the "
            + "screen immediately stops the macro."));
        stack.Children.Add(_failsafe);

        // ---- language
        _language = new ComboBox { MinWidth = 160 };
        _language.Items.Add(new ComboBoxItem { Content = "Tiếng Việt", Tag = Loc.Vi });
        _language.Items.Add(new ComboBoxItem { Content = "English", Tag = Loc.En });
        _language.SelectedIndex = Loc.Language == Loc.En ? 1 : 0;
        _language.SelectionChanged += (_, _) => ApplyLanguage();
        stack.Children.Add(FormRow(Loc.T("set.language", "Language / Ngôn ngữ"),
            _language, Loc.T("set.language.tip",
                "Changes the language of the whole interface")));

        // ---- emulator section
        stack.Children.Add(Ui.SectionHeader(Loc.T("emu.section.config", "Thông tin giả lập")));
        var emuGrid = new Grid { Margin = new Thickness(0, 4, 0, 8) };
        emuGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        emuGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var emuInfo = new StackPanel();
        var emuTitle = Ui.Label(
            Macro.Core.Emulator.EmulatorManager.IsConfiguredAndValid
                ? $"🎮 {Macro.Core.Emulator.EmulatorManager.LastResult?.EmulatorName ?? "LDPlayer"}"
                : Loc.T("emu.pill.unconfigured", "⚠ Giả lập: Chưa cấu hình"),
            Macro.Core.Emulator.EmulatorManager.IsConfiguredAndValid
                ? (Brush)Application.Current.Resources["Brush.SectionGreen"] ?? Brushes.LightGreen
                : new SolidColorBrush(Color.FromRgb(0xF0, 0x76, 0x4F)),
            11.5, true);
        var emuPath = new TextBlock
        {
            Text = string.IsNullOrEmpty(Macro.Core.Emulator.EmulatorManager.CurrentPath)
                ? Loc.T("emu.status.not_checked", "Chưa chọn tệp thực thi.")
                : Macro.Core.Emulator.EmulatorManager.CurrentPath,
            Foreground = (Brush)new BrushConverter().ConvertFromString("#8F939C"),
            FontSize = 10.5,
            TextTrimming = TextTrimming.CharacterEllipsis,
            Margin = new Thickness(0, 2, 0, 0),
        };
        emuInfo.Children.Add(emuTitle);
        emuInfo.Children.Add(emuPath);
        Grid.SetColumn(emuInfo, 0);
        emuGrid.Children.Add(emuInfo);

        var btnChangeEmu = new Button
        {
            Content = Loc.T("emu.change", "Đổi đường dẫn..."),
            Padding = new Thickness(8, 2, 8, 2),
            FontSize = 11,
            Margin = new Thickness(8, 0, 0, 0),
        };
        btnChangeEmu.Click += (_, _) =>
        {
            var setup = new EmulatorSetupDialog(this);
            setup.ShowDialog();
            emuTitle.Text = Macro.Core.Emulator.EmulatorManager.IsConfiguredAndValid
                ? $"🎮 {Macro.Core.Emulator.EmulatorManager.LastResult?.EmulatorName ?? "LDPlayer"}"
                : Loc.T("emu.pill.unconfigured", "⚠ Giả lập: Chưa cấu hình");
            emuTitle.Foreground = Macro.Core.Emulator.EmulatorManager.IsConfiguredAndValid
                ? (Brush)Application.Current.Resources["Brush.SectionGreen"] ?? Brushes.LightGreen
                : new SolidColorBrush(Color.FromRgb(0xF0, 0x76, 0x4F));
            emuPath.Text = string.IsNullOrEmpty(Macro.Core.Emulator.EmulatorManager.CurrentPath)
                ? Loc.T("emu.status.not_checked", "Chưa chọn tệp thực thi.")
                : Macro.Core.Emulator.EmulatorManager.CurrentPath;
        };
        Grid.SetColumn(btnChangeEmu, 1);
        emuGrid.Children.Add(btnChangeEmu);
        stack.Children.Add(emuGrid);

        Content = stack;
    }

    /// <summary>Apply the chosen language on OK (fires LanguageChanged,
    /// which re-renders the whole UI).</summary>
    public void ApplyLanguage()
    {
        if (_language.SelectedItem is ComboBoxItem item
            && item.Tag is string lang)
        {
            Loc.Language = lang;    // no-op when unchanged
        }
    }

    private static StackPanel FormRow(string label, FrameworkElement control,
                                      string tooltip)
    {
        var row = new StackPanel { Margin = new Thickness(0, 0, 0, 12) };
        var text = new TextBlock
        {
            Text = label,
            Foreground = (Brush)new BrushConverter().ConvertFromString("#8F939C"),
            Margin = new Thickness(0, 0, 0, 4),
        };
        if (tooltip is { Length: > 0 })
        {
            ToolTipService.SetToolTip(text, tooltip);
            ToolTipService.SetToolTip(control, tooltip);
        }
        row.Children.Add(text);
        row.Children.Add(control);
        return row;
    }
}
