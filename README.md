# macro — Trình tạo Macro / Automation không cần viết code

**macro** là phần mềm desktop (Windows, C# / WPF / .NET 8) cho phép tạo tool
tự động hóa bằng cách **lựa chọn, cấu hình và sắp xếp các thao tác có sẵn**
— hoàn toàn không cần viết code. Lấy cảm hứng từ các phần mềm macro kiểu
QB nhưng thiết kế giao diện riêng: **bố cục ngang 3 panel chuẩn IDE,
dark theme**, tối ưu cho màn hình desktop.

![layout](https://img.shields.io/badge/layout-3%20panel%20horizontal-4F8CFF)
![dotnet](https://img.shields.io/badge/.NET-8.0%20WPF-5C2D91)

---

## 1. Chạy phần mềm

Yêu cầu: **Windows 10/11 + .NET 8 SDK** (đã test với SDK 8.0.408 / 9.0).

```bash
cd macro
dotnet build -c Release
bin\Release\net8.0-windows\macro.exe
```

Mở sẵn một file macro:

```bash
bin\Release\net8.0-windows\macro.exe examples\demo.macro
```

Chạy bộ test core (không cần giao diện — kết quả ghi ra `selftest-result.txt`):

```bash
bin\Release\net8.0-windows\macro.exe --selftest
```

Hiện tại: **46/46 test pass** (registry, param utils, lưu/mở file,
engine: chạy tuần tự, Loop, Break, step bị tắt, Stop, Pause/Stop).

---

## 2. Giao diện

```
┌───────────────────────────────────────────────────────────────┐
│ TOP BAR: ⚡ macro | New Open Save Save As | ▶Run ‖Pause ■Stop  │
│                          | ◤ ◥ panel toggles | ⚙ Settings     │
├──────────────┬──────────────────────────────┬────────────────┤
│ ACTION       │ WORKFLOW EDITOR              │ PROPERTIES     │
│ LIBRARY      │                              │                │
│              │ My Macro            3 steps  │ Action: Click  │
│ 🔍 Search... │ ① 🖱 Click                    │ Position X / Y │
│ ▾ MOUSE      │    Left click at (100, 100)  │ Clicks, Button │
│   Click      │ ② ⌨ Press Key                │ [Select...]    │
│   Double ... │    Ctrl + enter  x3          │ Delay...       │
│ ▾ KEYBOARD   │ ③ 🔁 Loop  (2 steps inside)  │                │
│   ...        │    ┊ children, thụt vào      │                │
├──────────────┴──────────────────────────────┴────────────────┤
│ ● Ready   Running: Wait          Mouse: 512, 340   file.macro │
└───────────────────────────────────────────────────────────────┘
```

- **Sidebar trái ~235px · Editor giữa co giãn · Properties phải ~315px**,
  resize được bằng GridSplitter, thu gọn bằng nút ◤ / ◥ trên top bar.
- Dark theme trung tính (`#191A1D` nền), accent xanh dương `#4F8CFF`,
  Run = xanh lá, Stop = đỏ, Pause = vàng — mọi style nằm trong
  `Theme/Theme.xaml`.
- Hotkeys: **F5** Run · **F6** Stop · **Ctrl+S** Save · **Ctrl+N/O**.

### Action Library (trái)
- Ô tìm kiếm lọc theo tên + mô tả, nhóm theo category (Mouse, Keyboard,
  Image/Screen, Timing, Logic, Application) với icon màu riêng.
- **Double-click** chip để thêm vào cuối workflow (hoặc ngay sau step đang
  chọn), **kéo-thả** chip vào editor (vào card = chèn trước card đó,
  vào Loop body = thêm vào trong block, vào vùng trống = thêm cuối).

### Workflow Editor (giữa)
- Mỗi step là một card: handle kéo, số thứ tự (có số lồng `2.1`, `2.2`
  cho step trong Loop), icon màu, tên, dòng mô tả tham số, và các nút:
  bật/tắt ⏻, lên ↓xuống, nhân bản ⧉, xóa 🗑.
- Card được **chọn** viền xanh, **đang chạy** viền vàng, **hoàn tất**
  viền xanh lá, **bị tắt** mờ 55%.
- Hàng `+` mảnh giữa các card: chèn step vào bất kỳ vị trí nào,
  kể cả bên trong Loop. Nút `＋ Add step` cuối danh sách.
- Kéo handle ≡ để sắp xếp lại; không thể drop block vào chính con của nó.

### Properties Panel (phải)
Form tự sinh từ `ParamSchema` của từng action — chọn action khác thì
form đổi theo. Có sẵn các loại điều khiển: số (min/max/unit), true/false,
dropdown, slider (Confidence), ô văn bản nhiều dòng, **Capture phím**
(bấm phím thật để lấy tên), **chọn file**, **chọn ảnh + preview**,
**Select…** click trực tiếp lên màn hình để lấy tọa độ, **Select…**
kéo khung chọn vùng tìm kiếm. Thay đổi được áp dụng ngay lập tức.

### Settings (⚙)
Lặp lại cả workflow N lần · Delay giữa các step · **Failsafe** (hất chuột
vào góc trên-trái màn hình để dừng khẩn cấp — khuyến nghị bật).

---

## 3. Các Action có sẵn (17)

| Nhóm | Action |
|---|---|
| Mouse | Click (số lần + nút chuột), Double Click, Right Click, Move Mouse (có duration), Drag and Drop, Scroll |
| Keyboard | Press Key (Ctrl/Shift/Alt, repeat, delay giữa các lần), Type Text (hỗ trợ tiếng Việt Unicode) |
| Image/Screen | Find Image (confidence, timeout, vùng tìm kiếm, không thấy → continue/fail), Click Image, Screenshot (lưu vào Documents\macro\screenshots), Get Pixel Color |
| Timing | Wait, Random Wait |
| Logic | Loop (block lồng step con, delay giữa các vòng), Break |
| Application | Open Application (mở file/folder/app) |

> Text tiếng Việt được gõ bằng keystroke Unicode thật (SendInput), không
> cần clipboard.

---

## 4. Kiến trúc

```
macro/
├── App.xaml(.cs)            Entry point + self-test switch
├── MainWindow.xaml(.cs)     Top bar, 3 panel, status bar, engine wiring
├── Theme/Theme.xaml         Toàn bộ dark theme (styles + templates)
├── Core/                    ← Layer thuần logic, không phụ thuộc UI
│   ├── Models.cs            ActionStep + Workflow + ParamUtil (đọc JSON an toàn)
│   ├── ActionDefinition.cs  ParamSpec (schema khai báo) + ActionDefinition
│   ├── ActionRegistry.cs    Registry trung tâm — mọi UI tra cứu ở đây
│   ├── Engine.cs            ExecutionContext + ExecutionEngine (thread riêng)
│   ├── Storage.cs           Lưu/mở JSON (*.macro), bỏ qua action lạ + warning
│   ├── Errors.cs            ActionFailure / StopRequested / BreakLoop
│   ├── Input/NativeInput.cs SendInput: chuột, bàn phím, hotkey, Unicode text
│   └── Screen/ImageFinder.cs GDI+ chụp màn hình + OpenCV template matching
├── Actions/                 ← Mỗi nhóm action một file, tự đăng ký
│   ├── ActionsModule.cs     RegisterAll()
│   ├── MouseActions.cs  KeyboardActions.cs  TimingActions.cs
│   ├── ScreenActions.cs LogicActions.cs      AppActions.cs
├── UI/
│   ├── Icons.cs             Vẽ icon Unicode → DrawingImage (không cần lib)
│   ├── Dialogs/             ScreenPickers (position/region overlay),
│   │                        KeyCaptureDialog, SettingsDialog
│   └── Widgets/             Common (IntegerBox, AddStepMenu, helpers),
│                            ActionLibraryPanel, WorkflowEditorPanel,
│                            PropertiesPanel
├── examples/demo.macro      Macro ví dụ
└── SelfTest.cs              46 test headless (--selftest)
```

### Thêm một Action mới (3 bước, không sửa UI)

```csharp
// 1. Tạo file Actions/MyActions.cs
public static class MyActions
{
    public static void Register()
    {
        ActionRegistry.Register(new ActionDefinition
        {
            TypeId = "my_action", Name = "My Action",
            Category = "application", Icon = "app",
            Description = "Làm điều gì đó.",
            ParamSchema = { new ParamSpec("count", "Count", ParamWidget.Int)
                            { Min = 1, Max = 10 } },
            DefaultParams = { ["count"] = 1 },
            Summary = p => $"Count {ParamUtil.ToInt(p.GetValueOrDefault("count"))}",
            Execute = (ctx, step) => { /* thao tác thật */ },
        });
    }
}

// 2. Gọi trong ActionsModule.RegisterAll()
// 3. Xong. Sidebar, menu add, properties panel tự nhận action mới.
```

Container (kiểu Loop) thì đặt `IsContainer = true` — engine tự lặp
`step.Children` và hỗ trợ Break bên trong.

### Execution Engine

- Chạy trên **background thread** — UI không bao giờ đứng hình.
- Mỗi action nhận `ExecutionContext`: `CheckStop()`, `WaitIfPaused()`,
  `Sleep()` (ngắt được, pause thì đóng băng đếm ngược), `Variables`
  (dữ liệu giữa các action, vd kết quả Find Image), `Log()`.
- Stop = CancellationToken → `StopRequestedException` thoát ngay.
- Pause = `ManualResetEventSlim` — resume tiếp tục đúng chỗ.
- Engine phát event `StepStarted/StepDone/LogMessage/Finished`;
  UI marshal qua `Dispatcher` để highlight card đang chạy.

### Lưu trữ

JSON UTF-8 đuôi `.macro` (mở được cả `.json`), chỉ lưu **cấu hình**,
không lưu code:

```json
{
  "format": "macro", "version": 1, "name": "My Macro",
  "settings": { "repeat_workflow": 1, "step_delay_ms": 100, "failsafe": true },
  "actions": [
    { "id": "a1", "type": "wait", "name": "", "enabled": true,
      "params": { "duration_ms": 1000 } },
    { "id": "a2", "type": "loop", "params": { "times": 3 },
      "children": [ { "type": "click", "params": { "x": 500, "y": 300 } } ] }
  ]
}
```

File chứa action lạ sẽ được mở, bỏ qua action đó và **báo warning** thay
vì hỏng cả file.

---

## 5. An toàn khi chạy macro

- **Failsafe**: bật trong Settings — hất chuột mạnh vào góc trên-trái
  màn hình là engine dừng ngay.
- **F6 / nút Stop** luôn truy cập được trên top bar.
- Có thể **tắt từng step** (⏻) để chạy thử từng phần.

## 6. Hạn chế của MVP & hướng phát triển

- Ghi hành động (Record): đã chừa kiến trúc (events → ActionStep qua
  registry) nhưng chưa làm, ưu tiên hoàn thiện builder thủ công.
- Chưa có If/Else, đa lựa chọn (multi-select), undo/redo, tìm ảnh nhiều
  monitor riêng lẻ, build một file .exe đóng gói (`dotnet publish -r
  win-x64 --self-contained -p:PublishSingleFile=true` chạy được ngay).
- Icon dùng glyph Unicode của Windows (Segoe UI) — có thể thay bằng bộ
  SVG sau này chỉ bằng cách sửa `UI/Icons.cs`.
