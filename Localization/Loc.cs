using System.IO;
using System.Text.Json;
using System.Windows;

namespace Macro.Localization;

/// <summary>
/// Localization Service (i18n). English strings live inline in the UI as
/// the fallback; this dictionary holds the Vietnamese translations keyed by
/// string ids. Language choice is persisted in %APPDATA%\macro\config.json
/// and applied live through the LanguageChanged event (every panel
/// re-renders its texts).
///
/// Usage:  Loc.T("top.save", "Save")          -> "Lưu" (vi) / "Save" (en)
///         Loc.F("editor.count.n", "x steps", 3)  -> format + translate
/// </summary>
public static class Loc
{
    public const string Vi = "vi";
    public const string En = "en";

    private static string _language = Vi;

    public static event Action LanguageChanged;

    /// <summary>Current language id ("vi" or "en").</summary>
    public static string Language
    {
        get => _language;
        set
        {
            value = value == En ? En : Vi;
            if (value == _language) return;
            _language = value;
            SaveConfig();
            LanguageChanged?.Invoke();
        }
    }

    public static bool IsVietnamese => _language == Vi;

    /// <summary>Translate a key. English fallback must be provided inline.</summary>
    public static string T(string key, string englishFallback)
        => IsVietnamese
            ? Vi_Strings.GetValueOrDefault(key, englishFallback)
            : englishFallback;

    /// <summary>Translate then string.Format.</summary>
    public static string F(string key, string englishFallback, params object[] args)
        => string.Format(T(key, englishFallback), args);

    // ================================================================ config
    private static string ConfigPath()
    {
        var folder = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "macro");
        Directory.CreateDirectory(folder);
        return Path.Combine(folder, "config.json");
    }

    /// <summary>Load persisted settings (language) at application start.</summary>
    public static void LoadConfig()
    {
        try
        {
            var path = ConfigPath();
            if (!File.Exists(path)) return;
            using var doc = JsonDocument.Parse(File.ReadAllText(path));
            if (doc.RootElement.TryGetProperty("language", out var lang)
                && lang.ValueKind == JsonValueKind.String)
            {
                _language = lang.GetString() == En ? En : Vi;
            }
        }
        catch { /* corrupt config -> keep defaults */ }
    }

    private static void SaveConfig()
    {
        try
        {
            var configPath = ConfigPath();
            Dictionary<string, object> root = new();
            if (File.Exists(configPath))
            {
                try
                {
                    using var doc = JsonDocument.Parse(File.ReadAllText(configPath));
                    foreach (var prop in doc.RootElement.EnumerateObject())
                    {
                        if (prop.Name != "language")
                            root[prop.Name] = JsonSerializer.Deserialize<object>(prop.Value.GetRawText());
                    }
                }
                catch { }
            }
            root["language"] = _language;

            var options = new JsonSerializerOptions { WriteIndented = true };
            File.WriteAllText(configPath, JsonSerializer.Serialize(root, options));
        }
        catch { /* non-fatal */ }
    }

    // ================================================== VIETNAMESE STRINGS
    private static readonly Dictionary<string, string> Vi_Strings = new()
    {
        // ---- top bar
        ["top.new"] = "Mới",
        ["top.open"] = "Mở",
        ["top.save"] = "Lưu",
        ["top.saveas"] = "Lưu thành",
        ["top.run"] = "Chạy",
        ["top.pause"] = "Tạm dừng",
        ["top.stop"] = "Dừng",
        ["top.settings"] = "Cài đặt",
        ["top.new.tip"] = "Tạo macro mới",
        ["top.open.tip"] = "Mở macro đã lưu",
        ["top.save.tip"] = "Lưu macro (Ctrl+S)",
        ["top.saveas.tip"] = "Lưu macro thành tệp mới",
        ["top.run.tip"] = "Chạy quy trình (F5)",
        ["top.pause.tip"] = "Tạm dừng / tiếp tục",
        ["top.stop.tip"] = "Dừng quy trình đang chạy (F6)",
        ["top.capture_screen"] = "Mở giao diện chụp màn hình",
        ["top.capture_screen.tip"] = "Chụp màn hình tab giả lập đang chọn và mở giao diện chụp màn hình",
        ["top.attached_files"] = "Tệp TXT...",
        ["top.attached_files.tip"] = "Quản lý các tệp văn bản TXT đính kèm riêng cho macro này",
        ["top.settings.tip"] = "Cài đặt quy trình, ngôn ngữ...",
        ["top.leftpanel.tip"] = "Hiện / ẩn thư viện thao tác",
        ["top.rightpanel.tip"] = "Hiện / ẩn bảng thuộc tính",
        ["top.output.tip"] = "Hiện / ẩn bảng kết quả",
        ["emu.capture.no_selection"] = "Vui lòng chọn một tab trước.",
        ["ld.panel.title"] = "TAB GIẢ LẬP LDPLAYER",
        ["ld.panel.select_all"] = "Chọn tất cả",
        ["ld.panel.refresh"] = "🔄 Làm mới",
        ["ld.panel.advanced"] = "⚙ Quản lý chi tiết...",

        // ---- status bar
        ["status.ready"] = "Sẵn sàng",
        ["status.running"] = "Đang chạy",
        ["status.paused"] = "Tạm dừng",
        ["status.stopping"] = "Đang dừng...",
        ["status.finished"] = "Hoàn tất",
        ["status.running_step"] = "Đang chạy",
        ["status.unsaved"] = "Macro chưa lưu",
        ["output.title"] = "KẾT QUẢ / NHẬT KÝ",
        ["output.clear"] = "Xóa nhật ký",

        // ---- action library
        ["lib.title"] = "THƯ VIỆN THAO TÁC",
        ["lib.search"] = "Tìm kiếm thao tác...",
        ["lib.hint"] = "Click để xem & chỉnh sửa\nNhấn 'Thêm' hoặc kéo vào quy trình",

        // ---- workflow editor
        ["editor.caption"] = "QUY TRÌNH",
        ["editor.name.placeholder"] = "Tên macro",
        ["editor.count.1"] = "1 bước",
        ["editor.count.n"] = "{0} bước",
        ["editor.clear"] = "Xóa",
        ["editor.clear.tip"] = "Xóa tất cả thao tác",
        ["editor.theme.light"] = "☀️ Sáng",
        ["editor.theme.dark"] = "🌙 Tối",
        ["editor.theme.tip"] = "Chuyển đổi giao diện Sáng / Tối cho khu vực quy trình",
        ["editor.empty.title"] = "Chưa có thao tác",
        ["editor.empty.hint"] = "Nhấp đúp thao tác ở thư viện bên trái,\nhoặc kéo thả vào vùng này.",
        ["editor.add"] = "＋  Thêm thao tác",
        ["editor.addfirst"] = "＋  Thêm thao tác đầu tiên",
        ["editor.add.tip"] = "Thêm thao tác vào cuối quy trình",
        ["editor.insert.tip"] = "Chèn thao tác tại đây",
        ["editor.emptyblock"] = "Khối trống - thêm thao tác vào trong",
        ["editor.grip.tip"] = "Kéo để di chuyển thao tác này",
        ["editor.else"] = "NGƯỢC LẠI (ELSE)",
        ["editor.then"] = "NẾU ĐÚNG (THEN)",
        ["card.inside"] = "{0} bước bên trong",
        ["card.toggle.on"] = "Đang bật - bấm để tắt",
        ["card.toggle.off"] = "Đang tắt - bấm để bật",
        ["card.up.tip"] = "Chuyển lên trên",
        ["card.down.tip"] = "Chuyển xuống dưới",
        ["card.copy.tip"] = "Nhân bản thao tác",
        ["card.delete.tip"] = "Xóa thao tác",
        ["card.addchild.tip"] = "Thêm thao tác vào trong khối",

        // ---- properties panel
        ["prop.title"] = "THUỘC TÍNH",
        ["prop.none"] = "Chưa chọn thao tác",
        ["prop.none.hint"] = "Chọn một thao tác trong quy trình hoặc thư viện để chỉnh sửa thuộc tính.",
        ["prop.changes"] = "Thay đổi được áp dụng tự động",
        ["prop.stepname"] = "Tên thao tác",
        ["prop.stepname.placeholder"] = "Mặc định",
        ["prop.add"] = "Thêm",
        ["prop.add.tip"] = "Thêm thao tác này cùng các thông số hiện tại vào macro",
        ["prop.draft.desc"] = "Tùy chỉnh thông số rồi nhấn nút 'Thêm' để đưa vào macro.",
        ["prop.test"] = "▶ Thử",
        ["prop.test.tip"] = "Chạy thật thao tác này ngay lập tức với tham số hiện tại",
        ["prop.testing"] = "Đang thử...",
        ["prop.test.ok"] = "✔ Thử thành công",
        ["prop.test.fail"] = "✘ Thử thất bại",
        ["prop.test.container"] = "Khối lệnh được chạy trong quy trình - không thể thử riêng lẻ.",
        ["prop.container.hint"] = "Khối này chứa {0} thao tác.\nThêm hoặc sắp xếp chúng trong trình soạn thảo.",
        ["prop.select"] = "Chọn...",
        ["prop.browse"] = "Chọn tệp...",
        ["prop.capture"] = "Chụp & chọn điểm...",
        ["prop.capture.tip"] = "Chụp màn hình rồi click chọn điểm - tọa độ tự điền vào X/Y",
        ["prop.capturekey"] = "Chụp phím...",
        ["prop.crop"] = "Chụp & cắt ảnh mẫu...",
        ["prop.enabled"] = "Bật",
        ["prop.options"] = "Tùy chọn",
        ["prop.general"] = "Cài đặt chung",
        ["prop.step_selector.none"] = "-- Chọn lệnh đích --",
        ["prop.step_selector.deleted"] = "[Lệnh đã bị xóa: {0}]",
        ["prop.defaults.title"] = "Giá trị mặc định dùng chung",
        ["prop.defaults.desc"] = "Tọa độ và ảnh mẫu mặc định sẽ tự động điền vào các lệnh mới được thêm vào macro.",
        ["prop.defaults.desc_image_only"] = "Ảnh mẫu mặc định sẽ tự động điền vào các lệnh Tìm ảnh mới được thêm vào macro.",
        ["prop.defaults.coord_header"] = "TỌA ĐỘ MẶC ĐỊNH (CLICK CHUỘT)",
        ["prop.defaults.image_header"] = "ẢNH MẪU MẶC ĐỊNH (TÌM ẢNH)",
        ["prop.defaults.no_coord"] = "Chưa có tọa độ mặc định",
        ["prop.defaults.no_coord_tip"] = "Click vào ảnh chụp màn hình ở chế độ '🎯 Chọn tọa độ' để lưu tọa độ mặc định.",
        ["prop.defaults.no_image"] = "Chưa có ảnh mẫu mặc định",
        ["prop.defaults.no_image_tip"] = "Kéo chọn vùng ở chế độ '✂ Chọn vùng' để cắt và lưu ảnh mặc định.",
        ["prop.defaults.clear_coord"] = "Xóa tọa độ mặc định",
        ["prop.defaults.clear_image"] = "Xóa ảnh mặc định",
        ["prop.defaults.change_image"] = "Đổi ảnh khác...",
        ["prop.defaults.preview_image"] = "Xem trước",
        ["prop.defaults.choose_image"] = "Chọn tệp ảnh làm mặc định...",
        ["prop.defaults.use_default_coord"] = "Dùng tọa độ mặc định",
        ["prop.defaults.use_default_image"] = "Dùng ảnh mặc định",

        // ---- dialogs
        ["dlg.delete.title"] = "Xóa khối",
        ["dlg.delete.text"] = "'{0}' đang chứa {1} thao tác bên trong. Xóa tất cả?",
        ["dlg.clear.title"] = "Xóa quy trình",
        ["dlg.clear.text"] = "Xóa tất cả thao tác khỏi quy trình?",
        ["dlg.discard.text"] = "'{0}' có thay đổi chưa lưu.\nLưu lại trước không?",
        ["dlg.app.title"] = "macro",
        ["dlg.save.title"] = "Lưu macro",
        ["dlg.open.title"] = "Mở macro",
        ["dlg.choosefile"] = "Chọn một tệp",
        ["dlg.chooseimage"] = "Chọn ảnh",
        ["dlg.cantopen"] = "Không thể mở tệp:\n{0}",
        ["dlg.cantsave"] = "Không thể lưu tệp:\n{0}",
        ["dlg.fileproblems"] = "Tệp có một số vấn đề:\n\n{0}",
        ["dlg.cannotopenfile"] = "Không thể mở '{0}':\n{1}",
        ["dlg.unexpected"] = "Đã xảy ra lỗi không mong muốn:\n\n{0}",

        // ---- settings dialog
        ["set.title"] = "Cài đặt quy trình",
        ["set.for"] = "Cài đặt cho '{0}'",
        ["set.repeat"] = "Lặp lại cả quy trình",
        ["set.repeat.tip"] = "Chạy toàn bộ quy trình số lần này.",
        ["set.delay"] = "Giãn cách giữa các thao tác",
        ["set.delay.tip"] = "Tạm nghỉ thêm sau mỗi thao tác.",
        ["set.failsafe"] = "Bật failsafe (khuyến nghị)",
        ["set.failsafe.tip"] = "Khi bật, hất mạnh chuột vào góc trên-trái màn hình sẽ dừng macro ngay lập tức.",
        ["set.language"] = "Ngôn ngữ / Language",
        ["set.language.tip"] = "Thay đổi ngôn ngữ toàn bộ giao diện phần mềm",

        // ---- emulator setup & status
        ["emu.setup.title"] = "Cấu hình trình giả lập Android",
        ["emu.setup.header"] = "THIẾT LẬP TRÌNH GIẢ LẬP ANDROID",
        ["emu.setup.desc"] = "Macro yêu cầu liên kết với trình giả lập Android (LDPlayer) để hoạt động. Vui lòng chọn tệp thực thi của LDPlayer trên máy tính của bạn.",
        ["emu.section.config"] = "Thông tin giả lập",
        ["emu.type"] = "Trình giả lập",
        ["emu.path"] = "Đường dẫn tệp chạy LDPlayer (dnplayer.exe):",
        ["emu.hint.ldplayer"] = "Gợi ý: Tìm tệp dnplayer.exe hoặc ldconsole.exe trong thư mục cài đặt LDPlayer (ví dụ: C:\\LDPlayer\\LDPlayer9\\dnplayer.exe).",
        ["emu.filter.ldplayer"] = "LDPlayer (dnplayer.exe, ldconsole.exe)|dnplayer.exe;ldconsole.exe;dnconsole.exe|Tệp thực thi (*.exe)|*.exe|Tất cả tệp (*.*)|*.*",
        ["emu.browse"] = "Duyệt tệp...",
        ["emu.verify"] = "Kiểm tra",
        ["emu.save"] = "Lưu & Tiếp tục",
        ["emu.cancel"] = "Đóng",
        ["emu.status.ok"] = "✔ Đã xác thực thành công: {0} ({1})\nĐường dẫn: {2}",
        ["emu.status.ok_dir"] = "✔ Đã xác thực thư mục LDPlayer thành công: {0} ({1})\nThư mục: {2}\nTệp thực thi: {3}",
        ["emu.status.not_checked"] = "Vui lòng chọn hoặc nhập đường dẫn thư mục để kiểm tra.",
        ["emu.pill.ready"] = "LDPlayer: Sẵn sàng",
        ["emu.pill.unconfigured"] = "Giả lập: Chưa cấu hình",
        ["emu.pill.tip.ready"] = "Trình giả lập {0} đã được liên kết:\n{1}\nBấm để đổi thư mục.",
        ["emu.pill.tip.unconfigured"] = "Chưa cấu hình trình giả lập Android hợp lệ.\nBấm để thiết lập thư mục LDPlayer.",
        ["emu.banner.unconfigured"] = "Chưa cấu hình trình giả lập Android (LDPlayer). Vui lòng thiết lập thư mục để chạy macro.",
        ["emu.banner.configure"] = "Cấu hình ngay",
        ["emu.dir.label"] = "Thư mục cài đặt LDPlayer:",
        ["emu.dir.placeholder"] = "Chọn hoặc dán đường dẫn thư mục LDPlayer...",
        ["emu.browse_folder"] = "Duyệt thư mục...",
        ["emu.browse.folder_title"] = "Chọn thư mục cài đặt LDPlayer",
        ["emu.hint.ldplayer_dir"] = "Gợi ý: Chọn thư mục cài đặt LDPlayer (ví dụ: C:\\LDPlayer\\LDPlayer9 hoặc D:\\leidian\\LDPlayer9). Chương trình sẽ tự động quét và kiểm tra các tệp cần thiết.",
        ["emu.err.not_configured"] = "Chưa cấu hình đường dẫn trình giả lập.",
        ["emu.err.cannot_run"] = "Chưa cấu hình trình giả lập Android (LDPlayer) hợp lệ.\n\nVui lòng thiết lập thư mục LDPlayer trước khi chạy quy trình.",
        ["emu.err.test_blocked"] = "✘ Cần cấu hình giả lập trước khi thử",
        ["emu.err.empty"] = "Đường dẫn không được để trống.",
        ["emu.err.not_found"] = "Đường dẫn không tồn tại trên hệ thống:\n{0}",
        ["emu.err.not_exe"] = "Tệp được chọn phải là tệp thực thi (.exe) của LDPlayer.",
        ["emu.err.no_exe_in_dir"] = "Thư mục được chọn không chứa tệp cần thiết của LDPlayer (dnplayer.exe hoặc ldconsole.exe).\nVui lòng chọn đúng thư mục cài đặt LDPlayer.",
        ["emu.err.not_ldplayer"] = "Thư mục đã chọn không thuộc trình giả lập LDPlayer hợp lệ. Không tìm thấy dnplayer.exe hoặc ldconsole.exe. Vui lòng chọn lại thư mục cài đặt LDPlayer.",
        ["emu.err.zero_byte"] = "Tệp thực thi bị rỗng (0 bytes). Vui lòng kiểm tra lại quá trình cài đặt LDPlayer.",
        ["emu.err.no_permission"] = "Không có quyền truy cập tệp. Vui lòng kiểm tra quyền người dùng hoặc khởi chạy với quyền Administrator.",
        ["emu.err.access_error"] = "Không thể truy cập tệp thực thi: {0}",
        ["emu.change"] = "Đổi thư mục...",

        // ---- workflow / engine messages
        ["msg.workflow.started"] = "Quy trình '{0}' đã bắt đầu",
        ["msg.workflow.repeats"] = "Quy trình '{0}' đã bắt đầu ({1}x)",
        ["msg.workflow.finished"] = "Quy trình hoàn tất thành công",
        ["msg.stopped.by.user"] = "Đã dừng bởi người dùng",
        ["msg.failsafe"] = "Failsafe đã kích hoạt - chuột bị đưa vào góc màn hình",
        ["msg.break.outside"] = "'Break' chỉ được dùng bên trong Loop",
        ["msg.break.used"] = "break",
        ["msg.iteration"] = "{0}: vòng lặp {1}/{2}",
        ["msg.breakloop"] = "{0}: dừng vòng lặp",
        ["msg.step.failed"] = "Thao tác '{0}' thất bại: {1}",
        ["msg.unknown.type"] = "Loại thao tác không rõ '{0}'",
        ["msg.unexpected.error"] = "Lỗi không mong muốn: {0}",
        ["msg.empty.workflow"] = "Quy trình đang trống.\nThêm ít nhất một thao tác từ thư viện bên trái.",
        ["msg.image.found"] = "Tìm thấy ảnh tại ({0}, {1}), độ khớp {2}",
        ["msg.image.notfound"] = "Không tìm thấy ảnh trong {0} ms: {1}",
        ["msg.image.notfound.continue"] = "Không tìm thấy ảnh - tiếp tục",
        ["msg.click.image.at"] = "Nhấp vào ảnh tại ({0}, {1})",
        ["msg.skip.click"] = "Không tìm thấy ảnh - bỏ qua nhấp chuột",
        ["msg.screenshot.saved"] = "Đã lưu ảnh chụp: {0}",
        ["msg.pixel.at"] = "Điểm ảnh tại ({0}, {1}) = {2}",
        ["msg.cannot.scroll0"] = "Lượng cuộn không thể bằng 0",
        ["msg.no.key"] = "Chưa đặt phím - hãy bấm Capture trước",
        ["msg.cannot.press"] = "Không thể nhấn phím '{0}': {1}",
        ["msg.cannot.open"] = "Không thể mở '{0}': {1}",
        ["msg.no.app.set"] = "Chưa chọn tệp hoặc ứng dụng",
        ["msg.no.image.set"] = "Chưa chọn tệp ảnh - hãy chọn PNG/JPG trước",
        ["msg.image.file.missing"] = "Không tìm thấy tệp ảnh: {0}",
        ["msg.cannot.read.image"] = "Không đọc được tệp ảnh: {0}",
        ["msg.region.too.small"] = "Vùng tìm kiếm nhỏ hơn ảnh mẫu",
        ["msg.break.only.loop"] = "Break chỉ được dùng bên trong một Loop",
        ["msg.stop.by.workflow"] = "Quy trình tự dừng (Stop Workflow)",
        ["msg.cannot.type"] = "Không thể nhập văn bản: {0}",
        ["msg.condition"] = "Điều kiện '{0}' {1} '{2}' => {3}",
        ["msg.if.then"] = "chính xác - chạy nhánh THEN",
        ["msg.if.else"] = "sai - chạy nhánh ELSE",
        ["msg.if.no.else"] = "sai - không có nhánh ELSE, bỏ qua",
        ["msg.input.blocked"] = "Hệ thống đã chặn thao tác nhập (SendInput thất bại).",
        ["msg.unknown.key"] = "Tên phím không rõ '{0}'",
        ["msg.goto.jump"] = "Chuyển luồng đến lệnh #{0} (ID: {1})",
        ["msg.goto.max_jumps"] = "Đã vượt quá giới hạn chuyển lệnh tối đa (100,000 lần). Dừng quy trình để tránh lặp vô hạn.",
        ["msg.goto.not_found"] = "Không tìm thấy lệnh đích '{0}' trong quy trình.",
        ["msg.goto.executing"] = "Chuyển luồng thực thi đến lệnh {0}",
        ["val.goto.no_target"] = "Chưa chọn dòng lệnh đích.",
        ["val.goto.self_target"] = "Không thể chọn chuyển đến chính dòng lệnh này.",
        ["val.find_image.no_found_target"] = "Chưa chọn lệnh đích khi tìm thấy ảnh.",
        ["val.find_image.self_found_target"] = "Không thể chọn chuyển đến chính dòng lệnh tìm ảnh.",
        ["val.find_image.no_notfound_target"] = "Chưa chọn lệnh đích khi không tìm thấy ảnh.",
        ["val.find_image.self_notfound_target"] = "Không thể chọn chuyển đến chính dòng lệnh tìm ảnh.",

        // ---- image viewer
        ["viewer.title"] = "Trình xem ảnh",
        ["viewer.capture"] = "Chụp màn hình",
        ["viewer.openfile"] = "Mở ảnh...",
        ["viewer.zoomin"] = "Phóng to (+)",
        ["viewer.zoomout"] = "Thu nhỏ (-)",
        ["viewer.fit"] = "Vừa khung",
        ["viewer.actual"] = "1:1",
        ["viewer.mode.pan"] = "Di chuyển ảnh",
        ["viewer.mode.pick"] = "Chọn điểm (lấy tọa độ)",
        ["viewer.mode.region"] = "Kéo chọn vùng",
        ["viewer.pickcoords"] = "Tọa độ:",
        ["viewer.screencoords"] = "Màn hình:",
        ["viewer.copy"] = "Sao chép",
        ["viewer.copied"] = "Đã sao chép {0}",
        ["viewer.savecrop"] = "Lưu vùng cắt...",
        ["viewer.clearselection"] = "Bỏ vùng chọn",
        ["viewer.reset"] = "Đặt lại",
        ["viewer.templates"] = "Ảnh mẫu",
        ["viewer.captures"] = "Ảnh chụp",
        ["viewer.usepoint"] = "Dùng điểm này",
        ["viewer.image.size"] = "Kích thước ảnh gốc: {0} x {1}",
        ["viewer.sel.size"] = "Vùng chọn: {0} x {1}",

        // ---- file filter names
        ["filter.workflow"] = "Tệp quy trình (*.macro)|*.macro|JSON (*.json)|*.json",
        ["filter.image"] = "Ảnh (*.png;*.jpg;*.jpeg;*.bmp)|*.png;*.jpg;*.jpeg;*.bmp|Tất cả tệp (*.*)|*.*",
        ["filter.all"] = "Tất cả tệp (*.*)|*.*",

        // ---- categories
        ["cat.mouse"] = "CHUỘT",
        ["cat.keyboard"] = "BÀN PHÍM",
        ["cat.screen"] = "HÌNH ẢNH / MÀN HÌNH",
        ["cat.timing"] = "THỜI GIAN",
        ["cat.logic"] = "ĐIỀU KHIỂN LUỒNG",
        ["cat.application"] = "ỨNG DỤNG",

        // ---- action names
        ["action.click"] = "Nhấp chuột",
        ["action.double_click"] = "Nhấp đúp",
        ["action.right_click"] = "Nhấp chuột phải",
        ["action.move_mouse"] = "Di chuyển chuột",
        ["action.drag"] = "Kéo và thả",
        ["action.scroll"] = "Cuộn",
        ["action.press_key"] = "Nhấn phím",
        ["action.type_text"] = "Nhập văn bản",
        ["action.hotkey"] = "Tổ hợp phím",
        ["action.find_image"] = "Tìm hình ảnh",
        ["action.click_image"] = "Nhấp vào hình ảnh",
        ["action.wait_for_image"] = "Chờ hình ảnh xuất hiện",
        ["action.image_exists"] = "Ảnh có tồn tại",
        ["action.screenshot"] = "Chụp màn hình",
        ["action.get_pixel_color"] = "Lấy màu điểm ảnh",
        ["action.wait"] = "Chờ",
        ["action.random_wait"] = "Chờ ngẫu nhiên",
        ["action.if_else"] = "Nếu / Ngược lại",
        ["action.loop"] = "Vòng lặp",
        ["action.repeat"] = "Lặp lại",
        ["action.break"] = "Thoát vòng lặp",
        ["action.stop_workflow"] = "Dừng quy trình",
        ["action.goto_step"] = "Chuyển đến dòng lệnh",
        ["action.open_application"] = "Mở ứng dụng",
        ["action.read_txt_line"] = "Lấy văn bản",

        // ---- action descriptions
        ["action.read_txt_line.desc"] = "Lấy một dòng văn bản từ tệp TXT đính kèm (hoặc tệp ngoài) và lưu nội dung vào biến.",
        ["action.click.desc"] = "Nhấp chuột tại một vị trí trên màn hình.",
        ["action.double_click.desc"] = "Nhấp đúp chuột tại một vị trí trên màn hình.",
        ["action.right_click.desc"] = "Nhấp chuột phải tại một vị trí trên màn hình.",
        ["action.move_mouse.desc"] = "Di chuyển con trỏ chuột đến một vị trí, có thể chuyển động mượt.",
        ["action.drag.desc"] = "Kéo từ vị trí này đến vị trí khác trong khi giữ nút chuột.",
        ["action.scroll.desc"] = "Cuộn bánh xe chuột tại vị trí hiện tại.",
        ["action.press_key.desc"] = "Nhấn một phím đơn, có thể kết hợp Ctrl/Shift/Alt.",
        ["action.type_text.desc"] = "Nhập một đoạn văn bản vào cửa sổ đang mở. Hỗ trợ tiếng Việt Unicode.",
        ["action.hotkey.desc"] = "Nhấn tổ hợp phím (ví dụ Ctrl+C).",
        ["action.find_image.desc"] = "Tìm một ảnh mẫu trên màn hình và ghi nhớ vị trí tìm được.",
        ["action.click_image.desc"] = "Tìm ảnh mẫu trên màn hình rồi nhấp vào vị trí đó.",
        ["action.wait_for_image.desc"] = "Chờ cho đến khi ảnh mẫu xuất hiện trên màn hình (hoặc hết thời gian chờ).",
        ["action.image_exists.desc"] = "Kiểm tra ảnh mẫu có tồn tại trên màn hình không (không chờ), lưu kết quả vào biến.",
        ["action.screenshot.desc"] = "Chụp toàn màn hình hoặc một màn hình cụ thể và lưu thành tệp PNG.",
        ["action.get_pixel_color.desc"] = "Đọc màu của một điểm ảnh và hiển thị trong nhật ký.",
        ["action.wait.desc"] = "Tạm dừng quy trình trong một khoảng thời gian cố định.",
        ["action.random_wait.desc"] = "Tạm dừng ngẫu nhiên giữa min và max (giống con người hơn chờ cố định).",
        ["action.if_else.desc"] = "Kiểm tra điều kiện trên biến: đúng thì chạy nhánh THEN, sai thì chạy nhánh ELSE.",
        ["action.loop.desc"] = "Lặp lại các thao tác được đặt bên trong khối này.",
        ["action.repeat.desc"] = "Lặp lại nhóm thao tác bên trong N lần (giống Loop, tên dễ hiểu hơn).",
        ["action.break.desc"] = "Thoát khỏi vòng lặp gần nhất ngay lập tức.",
        ["action.stop_workflow.desc"] = "Dừng toàn bộ quy trình tại bước này.",
        ["action.goto_step.desc"] = "Chuyển luồng thực thi đến một lệnh được chọn trong danh sách.",
        ["action.open_application.desc"] = "Mở tệp, thư mục hoặc ứng dụng (giống nhấp đúp).",

        // ---- parameter labels
        ["param.position"] = "Vị trí",
        ["param.from"] = "Từ",
        ["param.to"] = "Đến",
        ["param.clicks"] = "Số lần nhấn",
        ["param.button"] = "Nút chuột",
        ["param.duration_ms"] = "Thời lượng",
        ["param.amount"] = "Lượng cuộn",
        ["param.key"] = "Phím",
        ["param.ctrl"] = "Ctrl",
        ["param.shift"] = "Shift",
        ["param.alt"] = "Alt",
        ["param.repeat"] = "Số lần lặp",
        ["param.interval_ms"] = "Giãn cách",
        ["param.text"] = "Văn bản",
        ["param.image"] = "Ảnh mẫu",
        ["param.confidence"] = "Độ chính xác",
        ["param.timeout_ms"] = "Thời gian chờ",
        ["param.region"] = "Vùng tìm kiếm",
        ["param.not_found"] = "Nếu không tìm thấy",
        ["param.target_step"] = "Lệnh đích",
        ["param.on_found"] = "Nếu tìm thấy ảnh",
        ["param.found_target"] = "Lệnh đích khi tìm thấy",
        ["param.not_found_target"] = "Lệnh đích khi không thấy",
        ["param.times"] = "Số lần lặp",
        ["param.iteration_delay_ms"] = "Giãn cách giữa các vòng",
        ["param.variable"] = "Tên biến",
        ["param.op"] = "Điều kiện",
        ["param.value"] = "Giá trị so sánh",
        ["param.prefix"] = "Tiền tố tên tệp",
        ["param.monitor"] = "Số màn hình",
        ["param.source"] = "Nguồn chụp",
        ["param.name"] = "Tên ảnh",
        ["param.auto_save"] = "Tự động lưu",
        ["param.point_x"] = "Vị trí X trong ảnh mẫu",
        ["param.point_y"] = "Vị trí Y trong ảnh mẫu",
        ["param.path"] = "Tệp / ứng dụng",
        ["param.min_ms"] = "Tối thiểu",
        ["param.max_ms"] = "Tối đa",
        ["param.use_region"] = "Giới hạn vùng tìm kiếm",
        ["param.text_source"] = "Nguồn nội dung",
        ["param.file_source"] = "Nguồn tệp TXT",
        ["param.attached_file"] = "Tệp đính kèm",
        ["param.custom_file"] = "Đường dẫn tệp TXT",
        ["param.line_number"] = "Số dòng cần lấy",
        ["param.mode.txt"] = "Chế độ xử lý dòng",
        ["param.variable_name"] = "Tên biến",
        ["param.unit.times"] = "lần",
        ["param.unit.ms"] = "ms",
        ["param.unit.notches"] = "nấc",

        // ---- enum option labels
        ["opt.left"] = "Chuột trái",
        ["opt.middle"] = "Chuột giữa",
        ["opt.right"] = "Chuột phải",
        ["opt.fail"] = "Dừng và báo lỗi",
        ["opt.continue"] = "Bỏ qua, tiếp tục",
        ["opt.goto"] = "Chuyển đến dòng lệnh",
        ["opt.on_found.continue"] = "Tiếp tục bình thường",
        ["opt.on_found.click"] = "Click vào ảnh",
        ["opt.on_found.goto"] = "Chuyển đến dòng lệnh",
        ["opt.on_found.click_and_goto"] = "Click vào ảnh & Chuyển lệnh",
        ["opt.source.virtual"] = "Toàn bộ màn hình",
        ["opt.source.monitor"] = "Màn hình được chọn",
        ["opt.source.attached"] = "Tệp TXT đính kèm trong macro",
        ["opt.source.custom"] = "Đường dẫn tệp TXT tùy chỉnh",
        ["opt.mode.keep"] = "Lấy và giữ nguyên dòng trong tệp",
        ["opt.mode.delete"] = "Lấy và xóa dòng đó khỏi tệp",
        ["opt.text_source.static"] = "Văn bản cố định (tự gõ)",
        ["opt.text_source.variable"] = "Lấy nội dung từ biến",
        ["op.exists"] = "biến tồn tại",
        ["op.not_exists"] = "biến không tồn tại",
        ["op.equals"] = "bằng",
        ["op.not_equals"] = "khác",
        ["op.contains"] = "chứa",
        ["op.greater"] = "lớn hơn",
        ["op.less"] = "nhỏ hơn",

        // ---- summaries (dynamic card text)
        ["sum.click"] = "{0} tại ({1}, {2})",
        ["sum.clicks"] = "{0} lần",
        ["sum.double_click"] = "Nhấp đúp tại ({0}, {1})",
        ["sum.right_click"] = "Nhấp phải tại ({0}, {1})",
        ["sum.move"] = "Di chuyển đến ({0}, {1})",
        ["sum.move.over"] = "Di chuyển đến ({0}, {1}) trong {2} ms",
        ["sum.drag"] = "Kéo ({0}, {1}) đến ({2}, {3})",
        ["sum.scroll.up"] = "Cuộn lên {0} nấc",
        ["sum.scroll.down"] = "Cuộn xuống {0} nấc",
        ["sum.key"] = "{0}",
        ["sum.type"] = "Nhập \"{0}\"",
        ["sum.wait"] = "Chờ {0} ms",
        ["sum.random_wait"] = "Chờ {0}-{1} ms",
        ["sum.loop"] = "Lặp {0}x",
        ["sum.loop.delay"] = "Lặp {0}x, {1} ms giữa các vòng",
        ["sum.find_image"] = "{0}  |  chờ tối đa {1} ms",
        ["sum.click_image"] = "{0}  |  {1} nhấp",
        ["sum.screenshot"] = "Chụp PNG '{0}...'",
        ["sum.pixel"] = "Màu tại ({0}, {1})",
        ["sum.break"] = "Thoát vòng lặp hiện tại",
        ["sum.stop"] = "Dừng toàn bộ quy trình",
        ["sum.noimage"] = "(chưa chọn ảnh)",
        ["sum.if_else"] = "Nếu [{0} {1} '{2}']",
        ["sum.repeat"] = "Lặp lại {0}x",
        ["sum.hotkey"] = "Tổ hợp phím {0}",
        ["sum.goto.empty"] = "Chưa chọn lệnh đích",
        ["sum.goto.target"] = "Chuyển đến {0}",
        ["sum.found.click"] = "Click nếu thấy",
        ["sum.found.goto"] = "Chuyển lệnh nếu thấy",
        ["sum.found.click_goto"] = "Click & chuyển lệnh",
        ["sum.notfound.continue"] = "Bỏ qua nếu không thấy",
        ["sum.notfound.goto"] = "Chuyển lệnh nếu không thấy",
        ["sum.noapp"] = "(chưa chọn ứng dụng)",
        ["param.target_type"] = "Loại ứng dụng",
        ["opt.target.emulator"] = "Giả lập LDPlayer",
        ["opt.target.windows"] = "Máy tính Windows",
        ["param.package_name"] = "Gói ứng dụng (Package Name)",
        ["param.app_name"] = "Tên ứng dụng",
        ["prop.get_apps"] = "Lấy danh sách ứng dụng",
        ["prop.get_apps.tip"] = "Lấy danh sách ứng dụng đã cài đặt trên tab LDPlayer đang chọn",
        ["msg.no.package.set"] = "Chưa nhập hoặc chưa chọn gói ứng dụng (package) cần mở.",
        ["msg.open_app.no_tab"] = "Chưa xác định tab giả lập LDPlayer để mở ứng dụng. Vui lòng chọn hoặc gán tab giả lập cho macro.",
        ["msg.open_app.no_console"] = "Không tìm thấy ldconsole.exe. Vui lòng kiểm tra lại cấu hình đường dẫn LDPlayer.",
        ["msg.open_app.tab_not_running"] = "Tab #{0} hiện đang tắt. Vui lòng khởi động tab trước khi mở ứng dụng.",
        ["msg.open_app.not_installed"] = "Ứng dụng '{0}' không tồn tại hoặc chưa được cài đặt trên Tab #{1}. Vui lòng cài đặt ứng dụng trên tab này trước.",
        ["msg.open_app.launch_failed"] = "Khởi chạy ứng dụng '{0}' trên Tab #{1} thất bại.",
        ["msg.open_app.success"] = "Đã mở ứng dụng '{0}' trên Tab #{1}.",
        ["msg.find_image.no_tab"] = "Chưa xác định được tab giả lập LDPlayer cho thao tác tìm ảnh. Vui lòng chọn hoặc gán tab giả lập cho macro.",
        ["msg.find_image.tab_not_running"] = "Tab giả lập #{0} hiện đang tắt hoặc không hoạt động. Vui lòng khởi động tab trước khi tìm ảnh.",
        ["msg.image.found.tab"] = "Tìm thấy ảnh trên Tab #{0} tại tọa độ giả lập ({1}, {2}), độ khớp {3}",
        ["msg.click.image.tab"] = "Click vào ảnh tại tọa độ giả lập ({0}, {1}) trên Tab #{2}",
        ["msg.image.notfound.tab"] = "Không tìm thấy ảnh trên Tab #{0} trong vòng {1} ms: {2}",
    };

    // Handy accessors ------------------------------------------------------
    /// <summary>Display name of an action: Vietnamese when vi is active,
    /// otherwise the English name declared by the ActionDefinition.</summary>
    public static string ActionName(string typeId)
    {
        var english = Macro.Core.ActionRegistry.Get(typeId)?.Name ?? typeId;
        return T("action." + typeId, english);
    }

    public static string ActionDesc(string typeId)
    {
        var english = Macro.Core.ActionRegistry.Get(typeId)?.Description ?? "";
        return T("action." + typeId + ".desc", english);
    }

    public static string CategoryName(string categoryId)
        => T("cat." + categoryId, Macro.Core.Categories.LabelOf(categoryId));

    public static string ParamLabel(string key, string englishFallback)
        => T("param." + key, englishFallback);
}
