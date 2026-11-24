using Macro.Core;
using Macro.Core.Input;
using Macro.Localization;

namespace Macro.Actions;

/// <summary>Keyboard actions: press a key (with modifiers), hotkey, type
/// text.</summary>
public static class KeyboardActions
{
    public static void Register()
    {
        RegisterPressKey();
        RegisterHotkey();
        RegisterTypeText();
    }

    private static void RegisterPressKey()
    {
        ActionRegistry.Register(new ActionDefinition
        {
            TypeId = "press_key",
            Name = "Press Key",
            Category = "keyboard",
            Icon = "key",
            Description = "Press a single key, optionally with Ctrl/Shift/Alt.",
            ParamSchema =
            {
                new("key", Loc.T("param.key", "Key"), ParamWidget.Key)
                { Tooltip = "Click 'Capture...' then press the key you want." },
                new("ctrl", "Ctrl", ParamWidget.Bool),
                new("shift", "Shift", ParamWidget.Bool),
                new("alt", "Alt", ParamWidget.Bool),
                new("repeat", Loc.T("param.repeat", "Repeat"), ParamWidget.Int)
                { Min = 1, Max = 1000, Unit = "times" },
                new("interval_ms", Loc.T("param.interval_ms", "Delay between presses"),
                    ParamWidget.Int)
                { Min = 0, Max = 10000, Unit = "ms" },
            },
            DefaultParams = { ["key"] = "enter", ["ctrl"] = false,
                              ["shift"] = false, ["alt"] = false,
                              ["repeat"] = 1, ["interval_ms"] = 50 },
            Summary = p =>
            {
                var text = "";
                if (ParamUtil.ToBool(p.GetValueOrDefault("ctrl"))) text += "Ctrl + ";
                if (ParamUtil.ToBool(p.GetValueOrDefault("shift"))) text += "Shift + ";
                if (ParamUtil.ToBool(p.GetValueOrDefault("alt"))) text += "Alt + ";
                text += ParamUtil.ToStringValue(p.GetValueOrDefault("key"));
                var repeat = ParamUtil.ToInt(p.GetValueOrDefault("repeat"), 1);
                return repeat > 1 ? $"{text}  x{repeat}" : text;
            },
            Validate = step => string.IsNullOrWhiteSpace(step.GetStr("key"))
                ? Loc.T("msg.no.key", "No key is set - capture a key first")
                : null,
            Execute = (ctx, step) =>
            {
                var key = step.GetStr("key").Trim();
                if (key.Length == 0)
                    throw new ActionFailureException(Loc.T("msg.no.key",
                        "No key is set - capture a key first"));

                var repeat = Math.Max(1, step.GetInt("repeat", 1));
                var interval = step.GetInt("interval_ms", 50);

                int tabIndex = Macro.Core.Emulator.LdPlayerService.ResolveTabIndex(ctx, step);
                if (tabIndex < 0)
                {
                    throw new ActionFailureException(Loc.T("msg.keyboard.no_tab",
                        "Chưa xác định được tab giả lập LDPlayer cho thao tác nhấn phím. Vui lòng chọn hoặc gán tab giả lập cho macro."));
                }

                bool ok = Macro.Core.Emulator.LdPlayerService.PressKey(tabIndex, key, repeat, interval);
                if (!ok)
                {
                    throw new ActionFailureException(Loc.F("msg.tab.press_failed",
                        "Gửi phím '{0}' tới Tab #{1} thất bại. Vui lòng kiểm tra kết nối ADB.", key, tabIndex));
                }
                ctx.Log(Loc.F("msg.tab.pressed", "Đã gửi phím '{0}' x{1} vào Tab #{2}", key, repeat, tabIndex));
            },
        });
    }

    private static void RegisterHotkey()
    {
        ActionRegistry.Register(new ActionDefinition
        {
            TypeId = "hotkey",
            Name = "Hotkey",
            Category = "keyboard",
            Icon = "key",
            Description = "Press a key combination (e.g. Ctrl+C).",
            ParamSchema =
            {
                new("key", Loc.T("param.key", "Key"), ParamWidget.Key)
                { Tooltip = "The main key, e.g. c, v, f5." },
                new("ctrl", "Ctrl", ParamWidget.Bool),
                new("shift", "Shift", ParamWidget.Bool),
                new("alt", "Alt", ParamWidget.Bool),
                new("win", "Win", ParamWidget.Bool),
            },
            DefaultParams = { ["key"] = "c", ["ctrl"] = true,
                              ["shift"] = false, ["alt"] = false, ["win"] = false },
            Summary = p =>
            {
                var text = "";
                if (ParamUtil.ToBool(p.GetValueOrDefault("ctrl"))) text += "Ctrl+";
                if (ParamUtil.ToBool(p.GetValueOrDefault("shift"))) text += "Shift+";
                if (ParamUtil.ToBool(p.GetValueOrDefault("alt"))) text += "Alt+";
                if (ParamUtil.ToBool(p.GetValueOrDefault("win"))) text += "Win+";
                text += ParamUtil.ToStringValue(p.GetValueOrDefault("key"));
                return Loc.F("sum.hotkey", "Hotkey {0}", text);
            },
            Validate = step => string.IsNullOrWhiteSpace(step.GetStr("key"))
                ? Loc.T("msg.no.key", "No key is set - capture a key first")
                : null,
            Execute = (ctx, step) =>
            {
                var key = step.GetStr("key").Trim();
                if (key.Length == 0)
                    throw new ActionFailureException(Loc.T("msg.no.key",
                        "No key is set - capture a key first"));

                int tabIndex = Macro.Core.Emulator.LdPlayerService.ResolveTabIndex(ctx, step);
                if (tabIndex < 0)
                {
                    throw new ActionFailureException(Loc.T("msg.keyboard.no_tab",
                        "Chưa xác định được tab giả lập LDPlayer cho thao tác tổ hợp phím. Vui lòng chọn hoặc gán tab giả lập cho macro."));
                }

                var modifiers = new[]
                    { ("ctrl", "ctrl"), ("shift", "shift"), ("alt", "alt"),
                      ("win", "win") }
                    .Where(pair => step.GetBool(pair.Item1))
                    .Select(pair => pair.Item2).ToArray();
                ctx.CheckStop();

                bool ok = Macro.Core.Emulator.LdPlayerService.PressHotkey(tabIndex, modifiers, key);
                if (!ok)
                {
                    throw new ActionFailureException(Loc.F("msg.tab.hotkey_failed",
                        "Gửi tổ hợp phím tới Tab #{0} thất bại. Vui lòng kiểm tra kết nối ADB.", tabIndex));
                }
                ctx.Log(Loc.F("msg.tab.hotkey_pressed", "Đã gửi tổ hợp phím tới Tab #{0}", tabIndex));
            },
        });
    }

    private static void RegisterTypeText()
    {
        ActionRegistry.Register(new ActionDefinition
        {
            TypeId = "type_text",
            Name = "Type Text",
            Category = "keyboard",
            Icon = "text",
            Description = "Type a block of text into the active window. "
                        + "Vietnamese and other Unicode text is supported.",
            ParamSchema =
            {
                new("text_source", Loc.T("param.text_source", "Nguồn nội dung"), ParamWidget.Enum)
                {
                    Options = new List<(string, object)>
                    {
                        ("opt.text_source.static", "static"),
                        ("opt.text_source.variable", "variable"),
                    },
                    Tooltip = "Chọn nhập chuỗi văn bản cố định hoặc lấy nội dung từ một biến",
                },
                new("text", Loc.T("param.text", "Văn bản cố định"), ParamWidget.MultilineText)
                {
                    Tooltip = "Nội dung văn bản cần gõ (hỗ trợ chèn biến dạng {tên_biến})",
                },
                new("variable_name", Loc.T("param.variable_name", "Tên biến"), ParamWidget.Text)
                {
                    Tooltip = "Tên biến chứa nội dung cần nhập (ví dụ: var1, account, password)",
                },
                new("interval_ms", Loc.T("param.interval_ms", "Delay between characters"),
                    ParamWidget.Int)
                { Min = 0, Max = 500, Unit = "ms" },
            },
            DefaultParams = { ["text_source"] = "static", ["text"] = "Hello world", ["variable_name"] = "var1", ["interval_ms"] = 30 },
            Summary = p =>
            {
                var source = ParamUtil.ToStringValue(p.GetValueOrDefault("text_source"), "static");
                if (source == "variable")
                {
                    var varName = ParamUtil.ToStringValue(p.GetValueOrDefault("variable_name"), "var1");
                    return Loc.F("sum.type_var", "Nhập từ biến '{0}'", varName);
                }
                var text = ParamUtil.ToStringValue(p.GetValueOrDefault("text"));
                var quote = text.Length > 36 ? text.Substring(0, 36) + "..." : text;
                return Loc.F("sum.type", "Type \"{0}\"", quote);
            },
            Validate = step =>
            {
                var source = step.GetStr("text_source", "static");
                if (source == "variable")
                {
                    if (string.IsNullOrWhiteSpace(step.GetStr("variable_name")))
                        return Loc.T("val.type_text.no_var", "Chưa chỉ định tên biến cần lấy nội dung để nhập văn bản.");
                }
                return null;
            },
            Execute = (ctx, step) =>
            {
                string textToType;
                string sourceDesc;
                var source = step.GetStr("text_source", "static");
                if (source == "variable")
                {
                    var varName = step.GetStr("variable_name").Trim();
                    if (string.IsNullOrEmpty(varName))
                        throw new ActionFailureException(Loc.F("val.type_text.no_var", "Chưa chỉ định tên biến trong thao tác 'Nhập văn bản' (ID: {0}, Tên: '{1}').", step.Id, step.Name));

                    if (!ctx.Variables.TryGetValue(varName, out var val) || val == null)
                    {
                        var stepDisplay = step.Name is { Length: > 0 } ? $"lệnh '{step.Name}'" : $"thao tác ID {step.Id}";
                        throw new ActionFailureException(
                            $"Biến '{varName}' chưa có giá trị hoặc chưa được gán trước khi chạy thao tác 'Nhập văn bản' ({stepDisplay}). Vui lòng kiểm tra lại thao tác 'Lấy văn bản'.");
                    }
                    textToType = ParamUtil.ToStringValue(val);
                    sourceDesc = $"biến '{varName}'";
                }
                else
                {
                    textToType = step.GetStr("text");
                    if (textToType.Contains("{") && textToType.Contains("}"))
                    {
                        foreach (var kvp in ctx.Variables)
                        {
                            textToType = textToType.Replace($"{{{kvp.Key}}}", ParamUtil.ToStringValue(kvp.Value));
                        }
                    }
                    sourceDesc = "cố định (static)";
                }

                int tabIndex = Macro.Core.Emulator.LdPlayerService.ResolveTabIndex(ctx, step);
                if (tabIndex < 0)
                {
                    throw new ActionFailureException(Loc.T("msg.keyboard.no_tab",
                        "Chưa xác định được tab giả lập LDPlayer cho thao tác nhập văn bản. Vui lòng chọn hoặc gán tab giả lập cho macro."));
                }

                string stepName = step.Name is { Length: > 0 } ? step.Name : Loc.T("action.type_text", "Nhập văn bản");
                string invocationKey = $"{step.Id}_tab{tabIndex}";
                int callCount = _typeTextStepInvocations.AddOrUpdate(invocationKey, 1, (_, count) => count + 1);

                if (textToType.Length == 0)
                {
                    ctx.Log(Loc.F("msg.tab.type_empty",
                        "[Tab #{0}] [Lệnh '{1}' (ID: {2})] Nội dung văn bản rỗng, bỏ qua nhập liệu (lần gọi #{3}, nguồn: {4}).",
                        tabIndex, stepName, step.Id, callCount, sourceDesc));
                    return;
                }

                var snippet = textToType.Length > 30 ? textToType.Substring(0, 30) + "..." : textToType;
                ctx.Log($"[Tab #{tabIndex}] [Lệnh '{stepName}' (ID: {step.Id})] Bắt đầu nhập văn bản (lần gọi #{callCount}, nguồn: {sourceDesc}, độ dài: {textToType.Length} ký tự): \"{snippet}\"");

                var result = Macro.Core.Emulator.LdPlayerService.InputTextDetailed(tabIndex, textToType, msg => ctx.Log(msg));
                if (!result.Success)
                {
                    ctx.Log($"✘ [Tab #{tabIndex}] [Lệnh '{stepName}' (ID: {step.Id})] Gửi văn bản thất bại qua nhánh '{result.Branch}'. Không thử lại để tránh nhập trùng.");
                    throw new ActionFailureException(Loc.F("msg.tab.type_failed",
                        "Gửi văn bản tới Tab #{0} thất bại qua nhánh '{1}' ({2}).", tabIndex, result.Branch, result.Error));
                }

                ctx.Log($"✔ [Tab #{tabIndex}] [Lệnh '{stepName}' (ID: {step.Id})] Nhập văn bản thành công duy nhất 1 lần qua nhánh '{result.Branch}' (lần gọi #{callCount}, nội dung: \"{snippet}\").");
            },
        });
    }

    private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, int> _typeTextStepInvocations = new();

    public static int GetStepInvocationCount(string stepId, int tabIndex) =>
        _typeTextStepInvocations.TryGetValue($"{stepId}_tab{tabIndex}", out var count) ? count : 0;

    public static void ResetStepInvocations() =>
        _typeTextStepInvocations.Clear();
}
