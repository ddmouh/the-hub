using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows.Input;
using TheHub.Common.Config;
using TheHub.Core.Native;

namespace TheHub.Core.Hooks;

public class LowLevelKeyboardHook : IDisposable
{
    private static IntPtr _hookId = IntPtr.Zero;
    private static Win32Api.HookProc? _staticHookProc;
    private static LowLevelKeyboardHook? _activeInstance;

    public event Action? ShortcutTriggered;
    public event Action? ShortcutReleased;

    private static bool _processExitRegistered;
    private bool _isShortcutPressed;

    public void Start()
    {
        if (_hookId != IntPtr.Zero) return;

        try
        {
            _activeInstance = this;
            _staticHookProc = HookCallback;
            using var curProcess = Process.GetCurrentProcess();
            using var curModule = curProcess.MainModule;
            var moduleHandle = Win32Api.GetModuleHandle(curModule?.ModuleName);
            _hookId = Win32Api.SetWindowsHookEx(Win32Api.WH_KEYBOARD_LL, _staticHookProc, moduleHandle, 0);
            AppLogger.Log($"LowLevelKeyboardHook started successfully. HookId={_hookId}");

            if (!_processExitRegistered)
            {
                _processExitRegistered = true;
                AppDomain.CurrentDomain.ProcessExit += (_, _) => ForceUnhook();
            }
        }
        catch (Exception ex)
        {
            AppLogger.Log($"Failed to start LowLevelKeyboardHook: {ex}");
        }
    }

    public void Stop()
    {
        _isShortcutPressed = false;
        ForceUnhook();
    }

    private static void ForceUnhook()
    {
        if (_hookId != IntPtr.Zero)
        {
            try { Win32Api.UnhookWindowsHookEx(_hookId); } catch { }
            _hookId = IntPtr.Zero;
        }
        if (_activeInstance != null)
        {
            _activeInstance._isShortcutPressed = false;
        }
        _activeInstance = null;
        _staticHookProc = null;
    }

    private static IntPtr HookCallback(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode >= 0 && _activeInstance != null)
        {
            try
            {
                int msg = (int)(long)wParam;
                if (msg == Win32Api.WM_KEYDOWN || msg == Win32Api.WM_SYSKEYDOWN)
                {
                    var kbd = Marshal.PtrToStructure<Win32Api.KBDLLHOOKSTRUCT>(lParam);
                    int vkCode = (int)kbd.vkCode;

                    if (MatchesShortcut(vkCode))
                    {
                        // Ignore typematic auto-repeat when key is held down!
                        if (!_activeInstance._isShortcutPressed)
                        {
                            _activeInstance._isShortcutPressed = true;
                            _activeInstance.ShortcutTriggered?.Invoke();
                        }
                        return (IntPtr)1; // Consume key to prevent default actions & auto-repeat flicker
                    }
                }
                else if (msg == Win32Api.WM_KEYUP || msg == Win32Api.WM_SYSKEYUP)
                {
                    var kbd = Marshal.PtrToStructure<Win32Api.KBDLLHOOKSTRUCT>(lParam);
                    int vkCode = (int)kbd.vkCode;

                    if (_activeInstance._isShortcutPressed)
                    {
                        // When any key involved in the shortcut is released, release the hold
                        _activeInstance._isShortcutPressed = false;
                        _activeInstance.ShortcutReleased?.Invoke();
                        return (IntPtr)1;
                    }
                }
            }
            catch (Exception ex)
            {
                AppLogger.Log($"Error in KeyboardHookCallback: {ex.Message}");
            }
        }

        return Win32Api.CallNextHookEx(_hookId, nCode, wParam, lParam);
    }

    private static bool MatchesShortcut(int pressedVk)
    {
        var settings = SettingsManager.Current;
        string shortcut = settings.GlobalShortcut;
        if (string.IsNullOrWhiteSpace(shortcut)) shortcut = "Alt+Z";

        // Check if modifiers match
        bool reqCtrl = shortcut.Contains("Ctrl", StringComparison.OrdinalIgnoreCase);
        bool reqAlt = shortcut.Contains("Alt", StringComparison.OrdinalIgnoreCase);
        bool reqShift = shortcut.Contains("Shift", StringComparison.OrdinalIgnoreCase);

        // VK_CONTROL: 0x11, VK_MENU: 0x12 (Alt), VK_SHIFT: 0x10
        bool isCtrlDown = (Win32Api.GetAsyncKeyState(0x11) & 0x8000) != 0;
        bool isAltDown = (Win32Api.GetAsyncKeyState(0x12) & 0x8000) != 0;
        bool isShiftDown = (Win32Api.GetAsyncKeyState(0x10) & 0x8000) != 0;

        if (reqCtrl != isCtrlDown) return false;
        if (reqAlt != isAltDown) return false;
        if (reqShift != isShiftDown) return false;

        // Find primary key in shortcut string (e.g. "Alt+Z" -> "Z", or just "Z")
        var parts = shortcut.Split('+', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        string keyName = parts.LastOrDefault(p =>
            !p.Equals("Ctrl", StringComparison.OrdinalIgnoreCase) &&
            !p.Equals("Alt", StringComparison.OrdinalIgnoreCase) &&
            !p.Equals("Shift", StringComparison.OrdinalIgnoreCase)) ?? "";

        if (string.IsNullOrEmpty(keyName)) return false;

        // Convert keyName to Virtual-Key code
        int targetVk = ParseKeyToVk(keyName);
        return pressedVk == targetVk;
    }

    private static int ParseKeyToVk(string keyName)
    {
        if (keyName.Length == 1)
        {
            char c = char.ToUpperInvariant(keyName[0]);
            if (c >= 'A' && c <= 'Z') return (int)c;
            if (c >= '0' && c <= '9') return (int)c;
        }

        return keyName.ToUpperInvariant() switch
        {
            "SPACE" => Win32Api.VK_SPACE,
            "TAB" => 0x09,
            "ESCAPE" or "ESC" => 0x1B,
            "ENTER" or "RETURN" => 0x0D,
            "BACKSPACE" or "BACK" => 0x08,
            "F1" => 0x70, "F2" => 0x71, "F3" => 0x72, "F4" => 0x73,
            "F5" => 0x74, "F6" => 0x75, "F7" => 0x76, "F8" => 0x77,
            "F9" => 0x78, "F10" => 0x79, "F11" => 0x7A, "F12" => 0x7B,
            _ => (int)c_to_vk(keyName)
        };
    }

    private static int c_to_vk(string name)
    {
        if (Enum.TryParse<Key>(name, true, out var wpfKey))
        {
            return KeyInterop.VirtualKeyFromKey(wpfKey);
        }
        return 0;
    }

    public void Dispose()
    {
        Stop();
    }
}
