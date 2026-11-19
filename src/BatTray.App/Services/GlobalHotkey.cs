using System.Runtime.InteropServices;
using System.Windows.Input;
using System.Windows.Interop;

namespace BatTray.App.Services;

/// <summary>A key combination such as Ctrl+Alt+B.</summary>
public sealed record HotkeyGesture(ModifierKeys Modifiers, Key Key)
{
    /// <summary>Parses "Ctrl+Alt+B" (also Shift and Win); null when it is not a valid shortcut.</summary>
    public static HotkeyGesture? Parse(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return null;
        var modifiers = ModifierKeys.None;
        Key? key = null;
        foreach (var raw in text.Split('+', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            switch (raw.ToLowerInvariant())
            {
                case "ctrl" or "control": modifiers |= ModifierKeys.Control; break;
                case "alt": modifiers |= ModifierKeys.Alt; break;
                case "shift": modifiers |= ModifierKeys.Shift; break;
                case "win" or "windows": modifiers |= ModifierKeys.Windows; break;
                default:
                    if (key is not null || !Enum.TryParse<Key>(raw, ignoreCase: true, out var k) || IsModifierKey(k) || k == Key.None)
                        return null;
                    key = k;
                    break;
            }
        }
        return key is { } found && IsValid(modifiers, found) ? new HotkeyGesture(modifiers, found) : null;
    }

    /// <summary>A global shortcut needs Ctrl or Alt, so it does not steal plain typing.</summary>
    public static bool IsValid(ModifierKeys modifiers, Key key) =>
        (modifiers & (ModifierKeys.Control | ModifierKeys.Alt)) != 0 && !IsModifierKey(key) && key != Key.None;

    public static bool IsModifierKey(Key key) => key is Key.LeftCtrl or Key.RightCtrl or Key.LeftAlt or Key.RightAlt
        or Key.LeftShift or Key.RightShift or Key.LWin or Key.RWin or Key.System;

    public override string ToString()
    {
        var parts = new List<string>();
        if (Modifiers.HasFlag(ModifierKeys.Control)) parts.Add("Ctrl");
        if (Modifiers.HasFlag(ModifierKeys.Alt)) parts.Add("Alt");
        if (Modifiers.HasFlag(ModifierKeys.Shift)) parts.Add("Shift");
        if (Modifiers.HasFlag(ModifierKeys.Windows)) parts.Add("Win");
        parts.Add(Key switch
        {
            >= Key.D0 and <= Key.D9 => ((int)(Key - Key.D0)).ToString(),
            _ => Key.ToString(),
        });
        return string.Join("+", parts);
    }
}

/// <summary>A system-wide shortcut (RegisterHotKey) delivered to a message-only window.</summary>
internal sealed class GlobalHotkey : IDisposable
{
    private const int WM_HOTKEY = 0x0312;
    private const int HotkeyId = 0xB47;
    private const uint MOD_ALT = 0x1, MOD_CONTROL = 0x2, MOD_SHIFT = 0x4, MOD_WIN = 0x8, MOD_NOREPEAT = 0x4000;

    private readonly HwndSource _window;
    private bool _registered;

    public GlobalHotkey()
    {
        _window = new HwndSource(new HwndSourceParameters("BatTray.Hotkey") { ParentWindow = new IntPtr(-3), Width = 0, Height = 0 });
        _window.AddHook(WndProc);
    }

    public event Action? Pressed;

    /// <summary>Registers <paramref name="gesture"/> (or nothing when null). Returns false when another app already uses it.</summary>
    public bool Set(HotkeyGesture? gesture)
    {
        Clear();
        if (gesture is null)
            return true;
        uint modifiers = MOD_NOREPEAT;
        if (gesture.Modifiers.HasFlag(ModifierKeys.Alt)) modifiers |= MOD_ALT;
        if (gesture.Modifiers.HasFlag(ModifierKeys.Control)) modifiers |= MOD_CONTROL;
        if (gesture.Modifiers.HasFlag(ModifierKeys.Shift)) modifiers |= MOD_SHIFT;
        if (gesture.Modifiers.HasFlag(ModifierKeys.Windows)) modifiers |= MOD_WIN;
        var vk = (uint)KeyInterop.VirtualKeyFromKey(gesture.Key);
        _registered = RegisterHotKey(_window.Handle, HotkeyId, modifiers, vk);
        return _registered;
    }

    private void Clear()
    {
        if (_registered)
            UnregisterHotKey(_window.Handle, HotkeyId);
        _registered = false;
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == WM_HOTKEY && wParam.ToInt32() == HotkeyId)
        {
            handled = true;
            Pressed?.Invoke();
        }
        return IntPtr.Zero;
    }

    public void Dispose()
    {
        Clear();
        _window.Dispose();
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool UnregisterHotKey(IntPtr hWnd, int id);
}
