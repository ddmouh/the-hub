using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Threading;
using TheHub.Common.Config;
using TheHub.Common.Models;
using TheHub.Core.Native;

namespace TheHub.Core.Monitors;

public class ClipboardMonitor : IDisposable
{
    private IntPtr _hwnd = IntPtr.Zero;
    private HwndSource? _hwndSource;
    private string? _lastText;
    private DateTime _lastCopyTime = DateTime.MinValue;
    private bool _isListening;
    private readonly object _lock = new();

    public event Action<DynamicIslandEvent>? ClipboardEventOccurred;

    public void Start(IntPtr hwnd)
    {
        lock (_lock)
        {
            if (_isListening || hwnd == IntPtr.Zero) return;

            _hwnd = hwnd;
            try
            {
                _hwndSource = HwndSource.FromHwnd(_hwnd);
                _hwndSource?.AddHook(WndProc);

                bool success = Win32Api.AddClipboardFormatListener(_hwnd);
                _isListening = success;
            }
            catch
            {
                _isListening = false;
            }
        }
    }

    public void Stop()
    {
        lock (_lock)
        {
            if (!_isListening || _hwnd == IntPtr.Zero) return;

            try
            {
                Win32Api.RemoveClipboardFormatListener(_hwnd);
                _hwndSource?.RemoveHook(WndProc);
            }
            catch
            {
            }

            _hwndSource = null;
            _isListening = false;
            _hwnd = IntPtr.Zero;
        }
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == Win32Api.WM_CLIPBOARDUPDATE)
        {
            OnClipboardUpdate();
        }
        return IntPtr.Zero;
    }

    private async void OnClipboardUpdate()
    {
        if (!SettingsManager.Current.IslandShowClipboard) return;

        // Small delay to allow the copying application to release the clipboard lock
        await Task.Delay(60);

        try
        {
            var dispatcher = Application.Current?.Dispatcher ?? Dispatcher.CurrentDispatcher;
            if (dispatcher != null)
            {
                dispatcher.Invoke(() => ProcessClipboardData());
            }
        }
        catch
        {
            // Transient error reading clipboard
        }
    }

    public void ProcessClipboardData()
    {
        for (int retry = 0; retry < 3; retry++)
        {
            try
            {
                if (Clipboard.ContainsText())
                {
                    string text = Clipboard.GetText();
                    if (string.IsNullOrWhiteSpace(text)) return;

                    // De-duplicate if identical text was copied within 2 seconds
                    if (text == _lastText && (DateTime.UtcNow - _lastCopyTime).TotalSeconds < 2)
                        return;

                    _lastText = text;
                    _lastCopyTime = DateTime.UtcNow;

                    string singleLine = text.Replace("\r", "").Replace("\n", " ").Trim();
                    if (singleLine.Length > 45)
                        singleLine = singleLine.Substring(0, 42) + "...";

                    ClipboardEventOccurred?.Invoke(new DynamicIslandEvent
                    {
                        Type = IslandEventType.Clipboard,
                        Title = "Copied to Clipboard",
                        Subtitle = singleLine,
                        Glyph = "📋",
                        DisplayDurationMs = SettingsManager.Current.IslandDurationMs
                    });
                    return;
                }

                if (Clipboard.ContainsFileDropList())
                {
                    var files = Clipboard.GetFileDropList();
                    if (files != null && files.Count > 0)
                    {
                        string firstFile = Path.GetFileName(files[0] ?? "");
                        string desc = files.Count == 1 ? firstFile : $"{files.Count} files ({firstFile})";

                        ClipboardEventOccurred?.Invoke(new DynamicIslandEvent
                        {
                            Type = IslandEventType.Clipboard,
                            Title = "Copied to Clipboard",
                            Subtitle = desc,
                            Glyph = "📁",
                            DisplayDurationMs = SettingsManager.Current.IslandDurationMs
                        });
                        return;
                    }
                }

                if (Clipboard.ContainsImage())
                {
                    ClipboardEventOccurred?.Invoke(new DynamicIslandEvent
                    {
                        Type = IslandEventType.Clipboard,
                        Title = "Copied to Clipboard",
                        Subtitle = "Image copied",
                        Glyph = "🖼️",
                        DisplayDurationMs = SettingsManager.Current.IslandDurationMs
                    });
                    return;
                }

                break;
            }
            catch (COMException)
            {
                // Clipboard temporarily locked by external process, sleep briefly
                Thread.Sleep(30);
            }
            catch
            {
                break;
            }
        }
    }

    public void Dispose()
    {
        Stop();
    }
}
