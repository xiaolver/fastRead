using System.ComponentModel;
using System.Runtime.InteropServices;
using FastRead.Core;

namespace FastRead.Infrastructure;

internal sealed class HotkeyWindow : NativeWindow, IDisposable
{
    private const int WmHotkey = 0x0312;
    private const int SummaryId = 101;
    private const int ExplanationId = 102;
    private bool _disposed;

    public event EventHandler<ActionKind>? HotkeyPressed;

    public HotkeyWindow()
    {
        CreateHandle(new CreateParams
        {
            Caption = "FastReadHotkeyWindow",
            Parent = new IntPtr(-3)
        });
    }

    public bool TryRegister(AppSettings settings, out string error)
    {
        UnregisterAll();
        if (!HotkeyDefinition.TryParse(settings.SummaryHotkey, out var summary, out error) || summary is null)
            return false;
        if (!HotkeyDefinition.TryParse(settings.ExplanationHotkey, out var explanation, out error) || explanation is null)
            return false;

        if (!Register(SummaryId, summary))
        {
            error = $"快捷键 {summary} 已被其他程序占用。";
            return false;
        }
        if (!Register(ExplanationId, explanation))
        {
            UnregisterHotKey(Handle, SummaryId);
            error = $"快捷键 {explanation} 已被其他程序占用。";
            return false;
        }
        error = string.Empty;
        return true;
    }

    public void UnregisterAll()
    {
        if (Handle == IntPtr.Zero) return;
        UnregisterHotKey(Handle, SummaryId);
        UnregisterHotKey(Handle, ExplanationId);
    }

    protected override void WndProc(ref Message m)
    {
        if (m.Msg == WmHotkey)
        {
            var action = m.WParam.ToInt32() switch
            {
                SummaryId => ActionKind.Summarize,
                ExplanationId => ActionKind.Explain,
                _ => (ActionKind?)null
            };
            if (action is not null) HotkeyPressed?.Invoke(this, action.Value);
        }
        base.WndProc(ref m);
    }

    private bool Register(int id, HotkeyDefinition definition) => RegisterHotKey(
        Handle, id, (uint)(definition.Modifiers | HotkeyModifiers.NoRepeat), (uint)definition.Key);

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        UnregisterAll();
        DestroyHandle();
        GC.SuppressFinalize(this);
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint modifiers, uint key);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool UnregisterHotKey(IntPtr hWnd, int id);
}
