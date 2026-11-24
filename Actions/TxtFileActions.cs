using System.IO;
using System.Text;
using Macro.Core;
using Macro.Localization;

namespace Macro.Actions;

public static class TxtFileManager
{
    public static (List<string> lines, Encoding encoding) ReadLinesWithEncoding(string path)
    {
        if (!File.Exists(path))
            throw new FileNotFoundException($"Không tìm thấy tệp TXT tại: {path}", path);

        using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        using var reader = new StreamReader(fs, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);

        var lines = new List<string>();
        string line;
        while ((line = reader.ReadLine()) != null)
        {
            lines.Add(line);
        }
        return (lines, reader.CurrentEncoding);
    }

    public static void WriteLinesAtomic(string path, IEnumerable<string> lines, Encoding encoding)
    {
        var dir = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
            Directory.CreateDirectory(dir);

        var tempPath = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            using (var writer = new StreamWriter(tempPath, false, encoding ?? Encoding.UTF8))
            {
                foreach (var line in lines)
                {
                    writer.WriteLine(line);
                }
                writer.Flush();
            }

            if (!File.Exists(tempPath))
                throw new IOException($"Không thể tạo tệp tạm để ghi: {tempPath}");

            // Atomic file replace / move
            File.Move(tempPath, path, overwrite: true);
        }
        catch
        {
            try { if (File.Exists(tempPath)) File.Delete(tempPath); } catch { }
            throw;
        }
    }
}

public static class TxtFileActions
{
    public static void Register()
    {
        ActionRegistry.Register(new ActionDefinition
        {
            TypeId = "read_txt_line",
            Name = "Lấy văn bản",
            Category = "keyboard",
            Icon = "file",
            Description = "Lấy một dòng văn bản từ tệp TXT đính kèm (hoặc tệp ngoài) và lưu nội dung vào biến.",
            ParamSchema =
            {
                new("file_source", Loc.T("param.file_source", "Nguồn tệp TXT"), ParamWidget.Enum)
                {
                    Options = new List<(string, object)>
                    {
                        ("opt.source.attached", "attached"),
                        ("opt.source.custom", "custom"),
                    },
                    Tooltip = "Chọn tệp TXT đính kèm riêng của macro hoặc chọn đường dẫn tệp ngoài",
                },
                new("attached_file_id", Loc.T("param.attached_file", "Tệp đính kèm"), ParamWidget.Text)
                {
                    Tooltip = "Chọn hoặc nhập tên/ID tệp TXT đính kèm trong macro này",
                },
                new("custom_file_path", Loc.T("param.custom_file", "Đường dẫn tệp TXT"), ParamWidget.File)
                {
                    Tooltip = "Đường dẫn đến tệp văn bản .txt",
                },
                new("line_number", Loc.T("param.line_number", "Số dòng cần lấy"), ParamWidget.Int)
                {
                    Min = 1, Max = 10000000,
                    Tooltip = "Số thứ tự dòng cần lấy (1 = dòng đầu tiên, 2 = dòng thứ 2...)",
                },
                new("mode", Loc.T("param.mode.txt", "Chế độ xử lý dòng"), ParamWidget.Enum)
                {
                    Options = new List<(string, object)>
                    {
                        ("opt.mode.keep", "keep"),
                        ("opt.mode.delete", "delete"),
                    },
                    Tooltip = "Giữ nguyên dòng sau khi lấy, hoặc lấy rồi xóa dòng đó khỏi tệp",
                },
                new("variable_name", Loc.T("param.variable_name", "Tên biến lưu giá trị"), ParamWidget.Text)
                {
                    Tooltip = "Tên biến để lưu nội dung dòng lấy được (ví dụ: var1, account, password)",
                },
            },
            DefaultParams =
            {
                ["file_source"] = "attached",
                ["attached_file_id"] = "",
                ["custom_file_path"] = "",
                ["line_number"] = 1,
                ["mode"] = "keep",
                ["variable_name"] = "var1",
            },
            Summary = p =>
            {
                var source = ParamUtil.ToStringValue(p.GetValueOrDefault("file_source"), "attached");
                var fileName = source == "attached"
                    ? ParamUtil.ToStringValue(p.GetValueOrDefault("attached_file_id"))
                    : Path.GetFileName(ParamUtil.ToStringValue(p.GetValueOrDefault("custom_file_path")));
                if (string.IsNullOrEmpty(fileName)) fileName = "(chưa chọn tệp)";
                var lineNum = ParamUtil.ToInt(p.GetValueOrDefault("line_number"), 1);
                var varName = ParamUtil.ToStringValue(p.GetValueOrDefault("variable_name"), "var1");
                var mode = ParamUtil.ToStringValue(p.GetValueOrDefault("mode"), "keep");
                var modeStr = mode == "delete" ? "xóa dòng" : "giữ nguyên";
                return $"Lấy dòng {lineNum} từ '{fileName}' -> biến '{varName}' ({modeStr})";
            },
            Validate = step =>
            {
                var source = step.GetStr("file_source", "attached");
                if (source == "attached")
                {
                    if (string.IsNullOrWhiteSpace(step.GetStr("attached_file_id")))
                        return Loc.T("val.read_txt.no_attached", "Chưa chọn tệp TXT đính kèm trong macro.");
                }
                else
                {
                    if (string.IsNullOrWhiteSpace(step.GetStr("custom_file_path")))
                        return Loc.T("val.read_txt.no_path", "Chưa chọn đường dẫn tệp TXT tùy chỉnh.");
                }

                if (string.IsNullOrWhiteSpace(step.GetStr("variable_name")))
                    return Loc.T("val.read_txt.no_var", "Chưa đặt tên biến để lưu nội dung.");

                if (step.GetInt("line_number", 1) < 1)
                    return Loc.T("val.read_txt.invalid_line", "Số dòng cần lấy phải lớn hơn hoặc bằng 1.");

                return null;
            },
            Execute = (ctx, step) =>
            {
                var source = step.GetStr("file_source", "attached");
                string targetPath = null;
                string fileDisplayName = "";

                if (source == "attached")
                {
                    var fileIdOrName = step.GetStr("attached_file_id");
                    if (string.IsNullOrWhiteSpace(fileIdOrName))
                        throw new ActionFailureException(Loc.T("val.read_txt.no_attached", "Chưa chọn tệp TXT đính kèm cho thao tác 'Lấy văn bản'."));

                    var attachedFile = ctx.Workflow?.FindAttachedFile(fileIdOrName);
                    if (attachedFile == null)
                        throw new ActionFailureException($"Tệp TXT '{fileIdOrName}' không có trong danh sách tệp đính kèm của macro '{ctx.Workflow?.Name}'. Vui lòng kiểm tra lại phần Tệp TXT.");

                    fileDisplayName = attachedFile.Name;
                    targetPath = ctx.Workflow?.ResolveAttachedFilePath(attachedFile, ctx.WorkflowFilePath);
                    if (string.IsNullOrEmpty(targetPath) || !File.Exists(targetPath))
                        throw new ActionFailureException($"Không tìm thấy tệp TXT đính kèm '{attachedFile.Name}' tại: '{attachedFile.FilePath}'. Tệp không còn tồn tại hoặc không thể truy cập.");
                }
                else
                {
                    targetPath = step.GetStr("custom_file_path");
                    targetPath = Workflow.ResolveTxtPath(targetPath, ctx.WorkflowFilePath);
                    fileDisplayName = Path.GetFileName(targetPath);
                    if (string.IsNullOrEmpty(targetPath) || !File.Exists(targetPath))
                        throw new ActionFailureException($"Không tìm thấy tệp TXT tại: '{targetPath}'.");
                }

                List<string> lines;
                Encoding encoding;
                try
                {
                    (lines, encoding) = TxtFileManager.ReadLinesWithEncoding(targetPath);
                }
                catch (Exception ex)
                {
                    throw new ActionFailureException($"Lỗi khi lấy dữ liệu từ tệp TXT '{fileDisplayName}': {ex.Message}");
                }

                if (lines.Count == 0)
                {
                    throw new ActionFailureException($"Tệp TXT rỗng (không có dòng nào để lấy): '{fileDisplayName}' (thao tác ID: {step.Id}).");
                }

                int lineNum = step.GetInt("line_number", 1);
                if (lineNum < 1 || lineNum > lines.Count)
                {
                    throw new ActionFailureException($"Dòng số {lineNum} không tồn tại trong tệp '{fileDisplayName}' (tệp hiện chỉ có {lines.Count} dòng).");
                }

                int lineIndex = lineNum - 1;
                string content = lines[lineIndex];

                var mode = step.GetStr("mode", "keep");
                if (mode == "delete")
                {
                    lines.RemoveAt(lineIndex);
                    try
                    {
                    TxtFileManager.WriteLinesAtomic(targetPath, lines, encoding);
                    }
                    catch (Exception ex)
                    {
                        throw new ActionFailureException($"Lỗi khi ghi lại tệp TXT '{fileDisplayName}' sau khi xóa dòng {lineNum}: {ex.Message}");
                    }
                }

                var varName = step.GetStr("variable_name", "var1").Trim();
                ctx.Variables[varName] = content;
                ctx.Log($"✔ [Lấy văn bản] Đã lấy dòng {lineNum} từ '{fileDisplayName}' -> biến '{varName}': \"{content}\" {(mode == "delete" ? "(Đã xóa dòng)" : "(Giữ nguyên)")}");
            },
        });
    }
}
