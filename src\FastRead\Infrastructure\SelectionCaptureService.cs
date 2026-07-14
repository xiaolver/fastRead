using System.Collections.Specialized;
using System.Drawing;
using System.Runtime.InteropServices;

namespace FastRead.Infrastructure;

internal sealed class SelectionCaptureService
{
    private const int MaxWaitMilliseconds = 1200;

    public async Task<string> CaptureAsync(CancellationToken cancellationToken)
    {
        await WaitForModifierKeysReleasedAsync(cancellationToken);
        var snapshot = CaptureClipboardSnapshot();
        var marker = $"__FASTREAD_{Guid.NewGuid():N}__";
        try
        {
            SetClipboardText(marker);
            SendCopyShortcut();

            var started = Environment.TickCount64;
            while (Environment.TickCount64 - started < MaxWaitMilliseconds)
            {
                cancellationToken.ThrowIfCancellationRequested();
                await Task.Delay(40, cancellationToken);
                var text = TryGetClipboardText();
                if (text is not null && !string.Equals(text, marker, StringComparison.Ordinal))
                {
                    if (string.IsNullOrWhiteSpace(text))
                        throw new SelectionCaptureException("选中的内容为空。请重新选择文本后再试。");
                    return text.Trim();
                }
            }
            throw new SelectionCaptureException("没有读取到选中文本。请确认目标应用支持复制，并在按快捷键前选中文字。");
        }
        finally
        {
            RestoreClipboard(snapshot);
        }
    }

    private static async Task WaitForModifierKeysReleasedAsync(CancellationToken cancellationToken)
    {
        var started = Environment.TickCount64;
        while (AreModifierKeysDown())
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (Environment.TickCount64 - started > 1000)
                throw new SelectionCaptureException("请松开快捷键后重试。");
            await Task.Delay(20, cancellationToken);
        }
    }

    private static bool AreModifierKeysDown() =>
        IsKeyDown(0x10) || IsKeyDown(0x11) || IsKeyDown(0x12) ||
        IsKeyDown(0x5B) || IsKeyDown(0x5C);

    private static bool IsKeyDown(int virtualKey) => (GetAsyncKeyState(virtualKey) & 0x8000) != 0;

    private static DataObject? CaptureClipboardSnapshot()
    {
        try
        {
            var source = Clipboard.GetDataObject();
            if (source is null) return null;
            var copy = new DataObject();
            foreach (var format in source.GetFormats(autoConvert: false))
            {
                try
                {
                    var data = source.GetData(format, autoConvert: false);
                    if (data is not null) copy.SetData(format, autoConvert: false, data);
                }
                catch
                {
                    // A clipboard owner may expose formats that cannot be materialized.
                }
            }
            return copy;
        }
        catch
        {
            return null;
        }
    }

    private static void RestoreClipboard(DataObject? snapshot)
    {
        try
        {
            if (snapshot is null) Clipboard.Clear();
            else Clipboard.SetDataObject(snapshot, true, 5, 40);
        }
        catch
        {
            // Clipboard restoration is best-effort because another app may lock it.
        }
    }

    private static string? TryGetClipboardText()
    {
        try { return Clipboard.ContainsText() ? Clipboard.GetText() : null; }
        catch { return null; }
    }

    private static void SetClipboardText(string text) => Clipboard.SetText(text);

    private static void SendCopyShortcut()
    {
        var inputs = new[]
        {
            Input.Keyboard(0x11, false),
            Input.Keyboard(0x43, false),
            Input.Keyboard(0x43, true),
            Input.Keyboard(0x11, true)
        };
        if (SendInput((uint)inputs.Length, inputs, Marshal.SizeOf<Input>()) != inputs.Length)
            throw new SelectionCaptureException("无法向当前应用发送复制命令。请检查应用权限后重试。");
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Input
    {
        public uint Type;
        public InputUnion Union;

        public static Input Keyboard(ushort key, bool keyUp) => new()
        {
            Type = 1,
            Union = new InputUnion
            {
                Keyboard = new KeyboardInput { VirtualKey = key, Flags = keyUp ? 0x0002u : 0 }
            }
        };
    }

    [StructLayout(LayoutKind.Explicit)]
    private struct InputUnion
    {
        [FieldOffset(0)] public KeyboardInput Keyboard;
        [FieldOffset(0)] public MouseInput Mouse;
        [FieldOffset(0)] public HardwareInput Hardware;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct KeyboardInput
    {
        public ushort VirtualKey;
        public ushort ScanCode;
        public uint Flags;
        public uint Time;
        public IntPtr ExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MouseInput
    {
        public int X, Y;
        public uint MouseData, Flags, Time;
        public IntPtr ExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct HardwareInput { public uint Message; public ushort ParamL, ParamH; }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint SendInput(uint count, Input[] inputs, int size);

    [DllImport("user32.dll")]
    private static extern short GetAsyncKeyState(int virtualKey);
}

internal sealed class SelectionCaptureException(string message) : Exception(message);
