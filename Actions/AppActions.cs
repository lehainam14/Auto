using System.Diagnostics;
using System.IO;
using Macro.Core;
using Macro.Core.Emulator;
using Macro.Localization;

namespace Macro.Actions;

/// <summary>
/// Application actions: open an Android application on LDPlayer tab or a Windows file/app.
/// </summary>
public static class AppActions
{
    private static string TabSuffix(IDictionary<string, object> p) =>
        p.TryGetValue("tab_index", out var ti) && ParamUtil.ToInt(ti, -1) >= 0
            ? $" [Tab #{ParamUtil.ToInt(ti)}]"
            : "";

    public static void Register()
    {
        ActionRegistry.Register(new ActionDefinition
        {
            TypeId = "open_application",
            Name = "Open Application",
            Category = "application",
            Icon = "app",
            Description = "Mở ứng dụng trên tab giả lập LDPlayer hoặc tệp/ứng dụng trên máy tính.",
            ParamSchema =
            {
                new("target_type", Loc.T("param.target_type", "Loại ứng dụng"), ParamWidget.Enum)
                {
                    Options = new List<(string, object)>
                    {
                        ("opt.target.emulator", "emulator"),
                        ("opt.target.windows", "windows"),
                    },
                    Tooltip = "Chọn mở ứng dụng Android trên tab LDPlayer hoặc ứng dụng trên Windows.",
                },
                new("package_name", Loc.T("param.package_name", "Gói ứng dụng (Package Name)"), ParamWidget.Text)
                {
                    Tooltip = "Tên package Android của ứng dụng cần mở. Bấm nút 'Lấy danh sách ứng dụng' để chọn nhanh.",
                },
                new("app_name", Loc.T("param.app_name", "Tên ứng dụng"), ParamWidget.Text)
                {
                    Tooltip = "Tên hiển thị của ứng dụng trên tab giả lập.",
                },
                new("path", Loc.T("param.path", "Đường dẫn Windows"), ParamWidget.File)
                {
                    Tooltip = "Đường dẫn tệp, thư mục hoặc ứng dụng trên Windows (.exe, .bat, ...).",
                },
            },
            DefaultParams =
            {
                ["target_type"] = "emulator",
                ["package_name"] = "",
                ["app_name"] = "",
                ["path"] = "",
            },
            Summary = p =>
            {
                var targetType = ParamUtil.ToStringValue(p.GetValueOrDefault("target_type"), "emulator");
                var pkg = ParamUtil.ToStringValue(p.GetValueOrDefault("package_name"));
                var appName = ParamUtil.ToStringValue(p.GetValueOrDefault("app_name"));
                var path = ParamUtil.ToStringValue(p.GetValueOrDefault("path"));

                string summaryText;
                if (targetType == "windows" || (string.IsNullOrEmpty(pkg) && !string.IsNullOrEmpty(path) &&
                    (path.Contains('\\') || path.Contains('/') || path.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))))
                {
                    summaryText = string.IsNullOrEmpty(path)
                        ? Loc.T("sum.noapp", "(chưa chọn ứng dụng)")
                        : Path.GetFileName(path);
                }
                else
                {
                    if (!string.IsNullOrEmpty(appName) && !string.IsNullOrEmpty(pkg))
                        summaryText = $"{appName} ({pkg})";
                    else if (!string.IsNullOrEmpty(pkg))
                        summaryText = pkg;
                    else if (!string.IsNullOrEmpty(path))
                        summaryText = path;
                    else
                        summaryText = Loc.T("sum.noapp", "(chưa chọn ứng dụng)");
                }
                return summaryText + TabSuffix(p);
            },
            Validate = step =>
            {
                var targetType = step.GetStr("target_type", "emulator");
                if (targetType == "windows")
                {
                    return string.IsNullOrWhiteSpace(step.GetStr("path"))
                        ? Loc.T("msg.no.app.set", "Chưa chọn tệp hoặc ứng dụng Windows cần mở.")
                        : null;
                }
                else
                {
                    var pkg = step.GetStr("package_name").Trim();
                    var path = step.GetStr("path").Trim();
                    if (string.IsNullOrWhiteSpace(pkg) && string.IsNullOrWhiteSpace(path))
                    {
                        return Loc.T("msg.no.package.set", "Chưa nhập hoặc chưa chọn gói ứng dụng (package) cần mở.");
                    }
                    return null;
                }
            },
            Execute = (ctx, step) =>
            {
                var targetType = step.GetStr("target_type", "emulator");
                var path = step.GetStr("path").Trim();
                var pkg = step.GetStr("package_name").Trim();

                bool isWindows = targetType == "windows" ||
                    (string.IsNullOrEmpty(pkg) && !string.IsNullOrEmpty(path) &&
                     (path.Contains('\\') || path.Contains('/') || path.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) || File.Exists(path) || Directory.Exists(path)));

                if (isWindows)
                {
                    if (path.Length == 0)
                        throw new ActionFailureException(Loc.T("msg.no.app.set", "Chưa chọn tệp hoặc ứng dụng Windows cần mở."));
                    try
                    {
                        if (Directory.Exists(path))
                            Process.Start(new ProcessStartInfo("explorer.exe", path));
                        else
                            Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
                    }
                    catch (Exception ex)
                    {
                        throw new ActionFailureException(Loc.F("msg.cannot.open", "Không thể mở '{0}': {1}", path, ex.Message));
                    }
                    return;
                }

                // Emulator application launch
                string packageName = !string.IsNullOrEmpty(pkg) ? pkg : path;
                if (string.IsNullOrWhiteSpace(packageName))
                    throw new ActionFailureException(Loc.T("msg.no.package.set", "Chưa nhập hoặc chưa chọn gói ứng dụng (package) cần mở."));

                int tabIndex = -1;
                if (step.Params.TryGetValue("tab_index", out var tiObj))
                    tabIndex = ParamUtil.ToInt(tiObj, -1);
                if (tabIndex < 0 && ctx.Variables.TryGetValue("tab_index", out var viObj))
                    tabIndex = ParamUtil.ToInt(viObj, -1);
                if (tabIndex < 0 && EmulatorManager.SelectedInstance != null)
                    tabIndex = EmulatorManager.SelectedInstance.Index;

                if (tabIndex < 0)
                {
                    throw new ActionFailureException(Loc.T(
                        "msg.open_app.no_tab",
                        "Chưa xác định tab giả lập LDPlayer để mở ứng dụng. Vui lòng chọn hoặc gán tab giả lập cho macro."));
                }

                var console = EmulatorManager.GetLdConsolePath();
                if (string.IsNullOrEmpty(console) || !File.Exists(console))
                {
                    throw new ActionFailureException(Loc.T(
                        "msg.open_app.no_console",
                        "Không tìm thấy ldconsole.exe. Vui lòng kiểm tra lại cấu hình đường dẫn LDPlayer."));
                }

                // Check tab running
                bool isRunning = LdPlayerService.IsRunningAsync(console, tabIndex).GetAwaiter().GetResult();
                if (!isRunning)
                {
                    throw new ActionFailureException(Loc.F(
                        "msg.open_app.tab_not_running",
                        "Tab #{0} hiện đang tắt. Vui lòng khởi động tab trước khi mở ứng dụng.", tabIndex));
                }

                // Multi-tab verification: verify app exists on this tab and report status
                bool isInstalled = LdPlayerService.IsPackageInstalledAsync(console, tabIndex, packageName).GetAwaiter().GetResult();
                if (!isInstalled)
                {
                    throw new ActionFailureException(Loc.F(
                        "msg.open_app.not_installed",
                        "Ứng dụng '{0}' không tồn tại hoặc chưa được cài đặt trên Tab #{1}. Vui lòng cài đặt ứng dụng trên tab này trước.",
                        packageName, tabIndex));
                }

                bool launched = LdPlayerService.LaunchAppAsync(console, tabIndex, packageName).GetAwaiter().GetResult();
                if (!launched)
                {
                    throw new ActionFailureException(Loc.F(
                        "msg.open_app.launch_failed",
                        "Khởi chạy ứng dụng '{0}' trên Tab #{1} thất bại.", packageName, tabIndex));
                }

                var appName = step.GetStr("app_name");
                string display = !string.IsNullOrEmpty(appName) ? $"{appName} ({packageName})" : packageName;
                ctx.Log(Loc.F("msg.open_app.success", "Đã mở ứng dụng '{0}' trên Tab #{1}.", display, tabIndex));
            },
        });
    }
}
