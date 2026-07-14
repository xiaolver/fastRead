using System.Globalization;
using System.Windows.Forms;

namespace FastRead.Core;

[Flags]
internal enum HotkeyModifiers : uint
{
    None = 0,
    Alt = 0x0001,
    Control = 0x0002,
    Shift = 0x0004,
    Win = 0x0008,
    NoRepeat = 0x4000
}

internal sealed record HotkeyDefinition(HotkeyModifiers Modifiers, Keys Key)
{
    public static bool TryParse(string? value, out HotkeyDefinition? definition, out string error)
    {
        definition = null;
        error = string.Empty;
        if (string.IsNullOrWhiteSpace(value))
        {
            error = "快捷键不能为空。";
            return false;
        }

        var parts = value.Split('+', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        var modifiers = HotkeyModifiers.None;
        Keys? key = null;

        foreach (var part in parts)
        {
            switch (part.ToUpperInvariant())
            {
                case "CTRL":
                case "CONTROL": modifiers |= HotkeyModifiers.Control; break;
                case "ALT": modifiers |= HotkeyModifiers.Alt; break;
                case "SHIFT": modifiers |= HotkeyModifiers.Shift; break;
                case "WIN":
                case "WINDOWS": modifiers |= HotkeyModifiers.Win; break;
                default:
                    if (key is not null)
                    {
                        error = "只能包含一个普通按键。";
                        return false;
                    }
                    if (!Enum.TryParse<Keys>(part, true, out var parsed) || IsModifierKey(parsed))
                    {
                        error = $"无法识别按键“{part}”。";
                        return false;
                    }
                    key = parsed;
                    break;
            }
        }

        if (modifiers == HotkeyModifiers.None)
        {
            error = "至少需要 Ctrl、Alt、Shift 或 Win 中的一个修饰键。";
            return false;
        }
        if (key is null)
        {
            error = "缺少普通按键。";
            return false;
        }

        definition = new HotkeyDefinition(modifiers, key.Value);
        return true;
    }

    public override string ToString()
    {
        var parts = new List<string>();
        if (Modifiers.HasFlag(HotkeyModifiers.Control)) parts.Add("Ctrl");
        if (Modifiers.HasFlag(HotkeyModifiers.Alt)) parts.Add("Alt");
        if (Modifiers.HasFlag(HotkeyModifiers.Shift)) parts.Add("Shift");
        if (Modifiers.HasFlag(HotkeyModifiers.Win)) parts.Add("Win");
        parts.Add(Key.ToString());
        return string.Join('+', parts);
    }

    private static bool IsModifierKey(Keys key) => key is
        Keys.Control or Keys.ControlKey or Keys.LControlKey or Keys.RControlKey or
        Keys.Alt or Keys.Menu or Keys.LMenu or Keys.RMenu or
        Keys.Shift or Keys.ShiftKey or Keys.LShiftKey or Keys.RShiftKey or
        Keys.LWin or Keys.RWin;
}
