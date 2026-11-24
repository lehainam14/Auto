using System.Runtime.InteropServices;

namespace Macro.Core.Input;

/// <summary>
/// Mouse + keyboard control through the Windows SendInput API.
/// No external libraries needed - this is the same mechanism AutoHotkey
/// style tools use. TypeText sends real UNICODE characters, so Vietnamese
/// text works without the clipboard.
/// </summary>
public static class NativeInput
{
    // ---------------------------------------------------------------- interop
    [StructLayout(LayoutKind.Sequential)]
    private struct INPUT { public uint type; public InputUnion union; }

    [StructLayout(LayoutKind.Explicit)]
    private struct InputUnion
    {
        [FieldOffset(0)] public MOUSEINPUT mi;
        [FieldOffset(0)] public KEYBDINPUT ki;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MOUSEINPUT
    {
        public int dx, dy;
        public uint mouseData, dwFlags, time;
        public IntPtr dwExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct KEYBDINPUT
    {
        public ushort wVk, wScan;
        public uint dwFlags, time;
        public IntPtr dwExtraInfo;
    }

    private const uint INPUT_MOUSE = 0;
    private const uint INPUT_KEYBOARD = 1;
    private const uint MOUSEEVENTF_MOVE = 0x0001;
    private const uint MOUSEEVENTF_LEFTDOWN = 0x0002;
    private const uint MOUSEEVENTF_LEFTUP = 0x0004;
    private const uint MOUSEEVENTF_RIGHTDOWN = 0x0008;
    private const uint MOUSEEVENTF_RIGHTUP = 0x0010;
    private const uint MOUSEEVENTF_MIDDLEDOWN = 0x0020;
    private const uint MOUSEEVENTF_MIDDLEUP = 0x0040;
    private const uint MOUSEEVENTF_WHEEL = 0x0800;
    private const uint MOUSEEVENTF_ABSOLUTE = 0x8000;
    private const uint KEYEVENTF_KEYUP = 0x0002;
    private const uint KEYEVENTF_UNICODE = 0x0004;

    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint SendInput(uint inputCount, INPUT[] inputs, int size);

    [DllImport("user32.dll")]
    private static extern bool SetCursorPos(int x, int y);

    [DllImport("user32.dll")]
    private static extern bool GetCursorPos(out POINT point);

    [StructLayout(LayoutKind.Sequential)]
    public struct POINT { public int X, Y; }

    // ------------------------------------------------------------------ mouse
    public static (int x, int y) GetCursorPosition()
    {
        GetCursorPos(out var point);
        return (point.X, point.Y);
    }

    public static void MoveTo(int x, int y) => SetCursorPos(x, y);

    public static void MoveToSmooth(int x, int y, double durationSeconds)
    {
        if (durationSeconds <= 0) { MoveTo(x, y); return; }
        var (startX, startY) = GetCursorPosition();
        var steps = Math.Max(2, (int)(durationSeconds * 100));
        for (var i = 1; i <= steps; i++)
        {
            var t = i / (double)steps;
            var ease = t * t * (3 - 2 * t);              // smoothstep easing
            MoveTo((int)(startX + (x - startX) * ease),
                   (int)(startY + (y - startY) * ease));
            Thread.Sleep((int)(durationSeconds * 1000 / steps));
        }
        MoveTo(x, y);
    }

    public static void Click(int x, int y, string button = "left",
                             int clicks = 1, int intervalMs = 60)
    {
        MoveTo(x, y);
        for (var i = 0; i < clicks; i++)
        {
            MouseDown(button);
            MouseUp(button);
            if (i < clicks - 1) Thread.Sleep(intervalMs);
        }
    }

    public static void DoubleClick(int x, int y)
        => Click(x, y, "left", clicks: 2, intervalMs: 40);

    public static void MouseDown(string button = "left")
        => SendMouseButton(button, isDown: true);

    public static void MouseUp(string button = "left")
        => SendMouseButton(button, isDown: false);

    private static void SendMouseButton(string button, bool isDown)
    {
        uint flag = (button, isDown) switch
        {
            ("left", true) => MOUSEEVENTF_LEFTDOWN,
            ("left", false) => MOUSEEVENTF_LEFTUP,
            ("right", true) => MOUSEEVENTF_RIGHTDOWN,
            ("right", false) => MOUSEEVENTF_RIGHTUP,
            ("middle", true) => MOUSEEVENTF_MIDDLEDOWN,
            ("middle", false) => MOUSEEVENTF_MIDDLEUP,
            _ => MOUSEEVENTF_LEFTDOWN,
        };
        Send(new INPUT { type = INPUT_MOUSE, union = new InputUnion
        {
            mi = new MOUSEINPUT { dwFlags = flag },
        }});
    }

    /// <summary>Scroll the wheel. Positive = up, negative = down.</summary>
    public static void Scroll(int amountClicks)
    {
        Send(new INPUT { type = INPUT_MOUSE, union = new InputUnion
        {
            mi = new MOUSEINPUT
            {
                dwFlags = MOUSEEVENTF_WHEEL,
                mouseData = unchecked((uint)(amountClicks * 120)),
            },
        }});
    }

    public static void Drag(int x1, int y1, int x2, int y2,
                            double durationSeconds, string button = "left")
    {
        MoveTo(x1, y1);
        Thread.Sleep(60);
        MouseDown(button);
        MoveToSmooth(x2, y2, Math.Max(0.05, durationSeconds));
        Thread.Sleep(60);
        MouseUp(button);
    }

    // --------------------------------------------------------------- keyboard
    /// <summary>Key name -> virtual key code. Names follow the common
    /// automation convention: enter, esc, tab, f1..f24, a..z, 0..9 ...</summary>
    public static ushort VkFromName(string name)
    {
        name = (name ?? "").Trim().ToLowerInvariant();
        if (VkTable.TryGetValue(name, out var vk)) return vk;
        if (name.Length == 1)
        {
            char c = name[0];
            if (c is >= 'a' and <= 'z') return (ushort)('A' + c - 'a');
            if (c is >= '0' and <= '9') return (ushort)c;
        }
        throw new ActionFailureException($"Unknown key name '{name}'");
    }

    private static readonly Dictionary<string, ushort> VkTable = new()
    {
        ["ctrl"] = 0x11, ["control"] = 0x11,
        ["shift"] = 0x10, ["alt"] = 0x12, ["win"] = 0x5B,
        ["enter"] = 0x0D, ["return"] = 0x0D,
        ["esc"] = 0x1B, ["escape"] = 0x1B,
        ["tab"] = 0x09, ["space"] = 0x20,
        ["backspace"] = 0x08, ["delete"] = 0x2E, ["del"] = 0x2E,
        ["insert"] = 0x2D, ["home"] = 0x24, ["end"] = 0x23,
        ["pageup"] = 0x21, ["pagedown"] = 0x22,
        ["up"] = 0x26, ["down"] = 0x28, ["left"] = 0x25, ["right"] = 0x27,
        ["printscreen"] = 0x2C, ["pause"] = 0x13,
        ["capslock"] = 0x14, ["numlock"] = 0x90, ["scrolllock"] = 0x91,
        ["f1"] = 0x70, ["f2"] = 0x71, ["f3"] = 0x72, ["f4"] = 0x73,
        ["f5"] = 0x74, ["f6"] = 0x75, ["f7"] = 0x76, ["f8"] = 0x77,
        ["f9"] = 0x78, ["f10"] = 0x79, ["f11"] = 0x7A, ["f12"] = 0x7B,
        ["f13"] = 0x7C, ["f14"] = 0x7D, ["f15"] = 0x7E, ["f16"] = 0x7F,
        ["f17"] = 0x80, ["f18"] = 0x81, ["f19"] = 0x82, ["f20"] = 0x83,
        ["f21"] = 0x84, ["f22"] = 0x85, ["f23"] = 0x86, ["f24"] = 0x87,
    };

    public static void KeyDown(ushort vk)
        => SendKey(vk, 0);

    public static void KeyUp(ushort vk)
        => SendKey(vk, KEYEVENTF_KEYUP);

    public static void PressKey(string name)
    {
        var vk = VkFromName(name);
        SendKey(vk, 0);
        Thread.Sleep(20);
        SendKey(vk, KEYEVENTF_KEYUP);
    }

    /// <summary>Hold modifiers, tap the key, release modifiers.</summary>
    public static void Hotkey(string[] modifiers, string key)
    {
        var modifierVks = modifiers.Select(VkFromName).ToList();
        var keyVk = VkFromName(key);
        foreach (var vk in modifierVks) SendKey(vk, 0);
        Thread.Sleep(20);
        SendKey(keyVk, 0);
        Thread.Sleep(20);
        SendKey(keyVk, KEYEVENTF_KEYUP);
        foreach (var vk in modifierVks) SendKey(vk, KEYEVENTF_KEYUP);
    }

    /// <summary>Type text as real UNICODE keystrokes (Vietnamese is fine).</summary>
    public static void TypeText(string text, double intervalSeconds = 0.03)
    {
        foreach (var character in text)
        {
            Send(new INPUT { type = INPUT_KEYBOARD, union = new InputUnion
            {
                ki = new KEYBDINPUT
                {
                    wScan = character,
                    dwFlags = KEYEVENTF_UNICODE,
                },
            }});
            Send(new INPUT { type = INPUT_KEYBOARD, union = new InputUnion
            {
                ki = new KEYBDINPUT
                {
                    wScan = character,
                    dwFlags = KEYEVENTF_UNICODE | KEYEVENTF_KEYUP,
                },
            }});
            if (intervalSeconds > 0) Thread.Sleep((int)(intervalSeconds * 1000));
        }
    }

    // ---------------------------------------------------------------- helpers
    private static void SendKey(ushort vk, uint flags)
        => Send(new INPUT { type = INPUT_KEYBOARD, union = new InputUnion
        {
            ki = new KEYBDINPUT { wVk = vk, dwFlags = flags },
        }});

    private static void Send(INPUT input)
    {
        var result = SendInput(1, new[] { input }, Marshal.SizeOf<INPUT>());
        if (result != 1)
            throw new ActionFailureException(
                "The system blocked the input (SendInput failed).");
    }
}
