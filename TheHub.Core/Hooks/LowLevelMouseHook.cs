using System.Diagnostics;
using System.Runtime.InteropServices;
using TheHub.Common.Config;
using TheHub.Core.Native;

namespace TheHub.Core.Hooks;

public class LowLevelMouseHook : IDisposable
{
    private static IntPtr _hookId = IntPtr.Zero;
    private static Win32Api.HookProc? _staticHookProc;
    private static LowLevelMouseHook? _activeInstance;

    private bool _isTriggerDown;
    private Win32Api.POINT _downPoint;
    private CancellationTokenSource? _holdCts;
    private bool _isWheelActive;
    private readonly int _holdThresholdMs;

    public event Action<int, int>? RadialOpenRequested;
    public event Action<int, int>? RadialPointerMoved;
    public event Action? RadialReleaseRequested;

    public LowLevelMouseHook(int holdThresholdMs = 120)
    {
        _holdThresholdMs = holdThresholdMs;
    }

    private static bool _processExitRegistered;

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
            _hookId = Win32Api.SetWindowsHookEx(Win32Api.WH_MOUSE_LL, _staticHookProc, moduleHandle, 0);
            AppLogger.Log($"LowLevelMouseHook started successfully. HookId={_hookId}");

            if (!_processExitRegistered)
            {
                _processExitRegistered = true;
                AppDomain.CurrentDomain.ProcessExit += (_, _) => ForceUnhook();
            }
        }
        catch (Exception ex)
        {
            AppLogger.Log($"Failed to start LowLevelMouseHook: {ex}");
        }
    }

    public void Stop()
    {
        _holdCts?.Cancel();
        ForceUnhook();
    }

    private static void ForceUnhook()
    {
        if (_hookId != IntPtr.Zero)
        {
            try { Win32Api.UnhookWindowsHookEx(_hookId); } catch { }
            _hookId = IntPtr.Zero;
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
                var settings = SettingsManager.Current;
                if (!settings.RadialLauncherEnabled || !settings.EnableMouseTrigger)
                {
                    return Win32Api.CallNextHookEx(_hookId, nCode, wParam, lParam);
                }

                int msg = (int)(long)wParam;
                var inst = _activeInstance;
                var hookStruct = Marshal.PtrToStructure<Win32Api.MSLLHOOKSTRUCT>(lParam);

                bool isDown = IsMatchingMouseDown(msg, hookStruct.mouseData, settings.MouseTriggerButton);
                bool isUp = IsMatchingMouseUp(msg, hookStruct.mouseData, settings.MouseTriggerButton);

                if (isDown)
                {
                    bool isClickMode = string.Equals(settings.GestureBehavior, "Click", StringComparison.OrdinalIgnoreCase);

                    // Toggle off if trigger button is pressed again while wheel is active in Click mode
                    if (isClickMode && inst._isWheelActive)
                    {
                        inst._isWheelActive = false;
                        inst._isTriggerDown = false;
                        Task.Run(() => inst.RadialReleaseRequested?.Invoke());
                        return (IntPtr)1;
                    }

                    inst._isTriggerDown = true;
                    inst._downPoint = hookStruct.pt;
                    inst._holdCts?.Cancel();
                    inst._holdCts = new CancellationTokenSource();
                    var token = inst._holdCts.Token;

                    if (!isClickMode)
                    {
                        // In Hold mode: ACTIVATE IMMEDIATELY!
                        inst._isWheelActive = true;
                        inst.RadialOpenRequested?.Invoke(inst._downPoint.x, inst._downPoint.y);
                    }
                    else
                    {
                        // In Click mode: short delay before opening
                        Task.Run(async () =>
                        {
                            try
                            {
                                await Task.Delay(15, token);
                                if (!token.IsCancellationRequested)
                                {
                                    inst._isWheelActive = true;
                                    inst.RadialOpenRequested?.Invoke(inst._downPoint.x, inst._downPoint.y);
                                }
                            }
                            catch (TaskCanceledException) { }
                        }, token);
                    }

                    return (IntPtr)1;
                }
                else if (msg == Win32Api.WM_MOUSEMOVE)
                {
                    if (inst._isWheelActive)
                    {
                        inst.RadialPointerMoved?.Invoke(hookStruct.pt.x, hookStruct.pt.y);
                    }
                }
                else if (isUp)
                {
                    inst._isTriggerDown = false;
                    inst._holdCts?.Cancel();

                    bool isClickMode = string.Equals(settings.GestureBehavior, "Click", StringComparison.OrdinalIgnoreCase);
                    if (inst._isWheelActive)
                    {
                        if (!isClickMode)
                        {
                            // In Hold mode, releasing finger instantly confirms selection and closes wheel
                            inst._isWheelActive = false;
                            Task.Run(() => inst.RadialReleaseRequested?.Invoke());
                            return (IntPtr)1;
                        }
                        // In Click mode, wheel stays open waiting for user to click a slice
                    }

                    return (IntPtr)1;
                }
                else if (inst._isWheelActive && (msg == Win32Api.WM_LBUTTONDOWN || msg == Win32Api.WM_RBUTTONDOWN))
                {
                    bool isClickMode = string.Equals(settings.GestureBehavior, "Click", StringComparison.OrdinalIgnoreCase);
                    if (isClickMode)
                    {
                        double dx = hookStruct.pt.x - inst._downPoint.x;
                        double dy = hookStruct.pt.y - inst._downPoint.y;
                        double dist = Math.Sqrt(dx * dx + dy * dy);
                        if (dist > (settings.RadialRadius + 80))
                        {
                            inst._isWheelActive = false;
                            Task.Run(() => inst.RadialReleaseRequested?.Invoke());
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                AppLogger.Log($"Error in MouseHookCallback: {ex.Message}");
            }
        }

        return Win32Api.CallNextHookEx(_hookId, nCode, wParam, lParam);
    }

    private static bool IsMatchingMouseDown(int msg, uint mouseData, string? configuredButton)
    {
        string btn = configuredButton ?? "Middle";

        if (btn.Equals("Back", StringComparison.OrdinalIgnoreCase))
        {
            if (msg == Win32Api.WM_XBUTTONDOWN)
            {
                int xbutton = (int)(mouseData >> 16);
                return xbutton == Win32Api.XBUTTON1;
            }
            return false;
        }

        if (btn.Equals("Forward", StringComparison.OrdinalIgnoreCase))
        {
            if (msg == Win32Api.WM_XBUTTONDOWN)
            {
                int xbutton = (int)(mouseData >> 16);
                return xbutton == Win32Api.XBUTTON2;
            }
            return false;
        }

        // Default: Middle button
        return msg == Win32Api.WM_MBUTTONDOWN;
    }

    private static bool IsMatchingMouseUp(int msg, uint mouseData, string? configuredButton)
    {
        string btn = configuredButton ?? "Middle";

        if (btn.Equals("Back", StringComparison.OrdinalIgnoreCase))
        {
            if (msg == Win32Api.WM_XBUTTONUP)
            {
                int xbutton = (int)(mouseData >> 16);
                return xbutton == Win32Api.XBUTTON1;
            }
            return false;
        }

        if (btn.Equals("Forward", StringComparison.OrdinalIgnoreCase))
        {
            if (msg == Win32Api.WM_XBUTTONUP)
            {
                int xbutton = (int)(mouseData >> 16);
                return xbutton == Win32Api.XBUTTON2;
            }
            return false;
        }

        // Default: Middle button
        return msg == Win32Api.WM_MBUTTONUP;
    }

    public void ResetWheelState()
    {
        _isWheelActive = false;
        _isTriggerDown = false;
        _holdCts?.Cancel();
    }

    public void Dispose()
    {
        Stop();
        _holdCts?.Dispose();
    }
}
