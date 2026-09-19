using System;
using System.Drawing;
using System.Windows.Forms;
using TheHub.Common.Config;

namespace TheHub.App.Services;

public class TrayIconService : IDisposable
{
    private readonly NotifyIcon _notifyIcon;
    private readonly ContextMenuStrip _contextMenu;
    private readonly ToolStripMenuItem _islandToggleItem;

    public event Action? OpenMainWindowRequested;
    public event Action? OpenRadialRequested;
    public event Action<bool>? ToggleIslandRequested;
    public event Action? ExitRequested;

    public TrayIconService()
    {
        _contextMenu = new ContextMenuStrip();
        
        // Open
        var openItem = new ToolStripMenuItem("فتح The Hub");
        openItem.Font = new Font(openItem.Font, FontStyle.Bold);
        openItem.Click += (s, e) => OpenMainWindowRequested?.Invoke();
        _contextMenu.Items.Add(openItem);

        // Radial Launcher
        var radialItem = new ToolStripMenuItem("المشغل الدائري (Radial Launcher)");
        radialItem.Click += (s, e) => OpenRadialRequested?.Invoke();
        _contextMenu.Items.Add(radialItem);

        // Dynamic Island toggle
        _islandToggleItem = new ToolStripMenuItem("الجزيرة التفاعلية (Dynamic Island)");
        _islandToggleItem.Checked = true;
        _islandToggleItem.Click += (s, e) =>
        {
            _islandToggleItem.Checked = !_islandToggleItem.Checked;
            ToggleIslandRequested?.Invoke(_islandToggleItem.Checked);
        };
        _contextMenu.Items.Add(_islandToggleItem);

        _contextMenu.Items.Add(new ToolStripSeparator());

        // Exit
        var exitItem = new ToolStripMenuItem("إغلاق التطبيق بالكامل (Exit)");
        exitItem.Click += (s, e) => ExitRequested?.Invoke();
        _contextMenu.Items.Add(exitItem);

        _notifyIcon = new NotifyIcon
        {
            Icon = CreateHubIcon(),
            Text = "The Hub — Desktop Companion",
            ContextMenuStrip = _contextMenu,
            Visible = true
        };

        _notifyIcon.MouseClick += (s, e) =>
        {
            if (e.Button == MouseButtons.Left)
            {
                OpenMainWindowRequested?.Invoke();
            }
        };

        _notifyIcon.DoubleClick += (s, e) =>
        {
            OpenMainWindowRequested?.Invoke();
        };
    }

    public void SetIslandChecked(bool isVisible)
    {
        _islandToggleItem.Checked = isVisible;
    }

    private static Icon CreateHubIcon()
    {
        try
        {
            using var bmp = new Bitmap(32, 32);
            using (var g = Graphics.FromImage(bmp))
            {
                g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
                g.Clear(Color.Transparent);

                // Modern glowing hub circle (Blue to Purple)
                using var brush = new System.Drawing.Drawing2D.LinearGradientBrush(
                    new Point(0, 0),
                    new Point(32, 32),
                    Color.FromArgb(59, 130, 246),
                    Color.FromArgb(139, 92, 246)
                );
                g.FillEllipse(brush, 2, 2, 28, 28);

                // Outer subtle ring
                using var ringPen = new Pen(Color.FromArgb(200, 255, 255, 255), 1.5f);
                g.DrawEllipse(ringPen, 5, 5, 22, 22);

                // Center glowing dot
                using var centerBrush = new SolidBrush(Color.White);
                g.FillEllipse(centerBrush, 12, 12, 8, 8);
            }

            IntPtr hIcon = bmp.GetHicon();
            return Icon.FromHandle(hIcon);
        }
        catch (Exception ex)
        {
            AppLogger.Log($"Error creating tray icon: {ex.Message}");
            return SystemIcons.Application;
        }
    }

    public void Dispose()
    {
        _notifyIcon.Visible = false;
        _notifyIcon.ContextMenuStrip = null;
        _contextMenu.Dispose();
        _notifyIcon.Dispose();
    }
}
