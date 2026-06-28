using System.Drawing;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;
using WinForms = System.Windows.Forms;

namespace CodexUsageOverlay.App;

public sealed class TrayIconService : IDisposable
{
    private const int TooltipMaxLength = 63;

    private readonly WinForms.NotifyIcon _notifyIcon;
    private Icon? _currentIcon;
    private string? _lastTooltipText;
    private TrayStatusKind? _lastStatusKind;
    private bool _isDisposed;

    public TrayIconService(string showText, string exitText, Action show, Action exit)
    {
        var menu = new WinForms.ContextMenuStrip();
        menu.Items.Add(new WinForms.ToolStripMenuItem(showText, null, (_, _) => show()));
        menu.Items.Add(new WinForms.ToolStripMenuItem(exitText, null, (_, _) => exit()));

        _notifyIcon = new WinForms.NotifyIcon
        {
            ContextMenuStrip = menu,
            Text = "Codex quota monitor",
            Visible = false
        };
        _notifyIcon.MouseClick += (_, e) =>
        {
            if (e.Button == WinForms.MouseButtons.Left)
            {
                show();
            }
        };

        Update(null, null);
    }

    public void SetVisible(bool isVisible)
    {
        if (!_isDisposed)
        {
            _notifyIcon.Visible = isVisible;
        }
    }

    public void Update(string? tooltipText, double? remainingPercent)
    {
        if (_isDisposed)
        {
            return;
        }

        var normalizedTooltip = TrimTooltip(string.IsNullOrWhiteSpace(tooltipText)
            ? "Codex quota monitor"
            : tooltipText);
        if (!string.Equals(_lastTooltipText, normalizedTooltip, StringComparison.Ordinal))
        {
            _notifyIcon.Text = normalizedTooltip;
            _lastTooltipText = normalizedTooltip;
        }

        var statusKind = StatusKindFor(remainingPercent);
        if (_lastStatusKind == statusKind)
        {
            return;
        }

        var icon = CreateCircleIcon(ColorFor(statusKind));
        var previousIcon = _currentIcon;
        _notifyIcon.Icon = icon;
        _currentIcon = icon;
        _lastStatusKind = statusKind;
        previousIcon?.Dispose();
    }

    public void Dispose()
    {
        if (_isDisposed)
        {
            return;
        }

        _isDisposed = true;
        _notifyIcon.Visible = false;
        _notifyIcon.ContextMenuStrip?.Dispose();
        _notifyIcon.Dispose();
        _currentIcon?.Dispose();
    }

    private static string TrimTooltip(string text)
    {
        return text.Length <= TooltipMaxLength
            ? text
            : string.Concat(text.AsSpan(0, TooltipMaxLength - 3), "...");
    }

    private static TrayStatusKind StatusKindFor(double? remainingPercent)
    {
        if (!remainingPercent.HasValue)
        {
            return TrayStatusKind.Unavailable;
        }

        if (remainingPercent.Value < 20)
        {
            return TrayStatusKind.Low;
        }

        return remainingPercent.Value < 60
            ? TrayStatusKind.Medium
            : TrayStatusKind.Good;
    }

    private static Color ColorFor(TrayStatusKind statusKind)
    {
        return statusKind switch
        {
            TrayStatusKind.Good => Color.FromArgb(141, 219, 58),
            TrayStatusKind.Medium => Color.FromArgb(242, 201, 76),
            TrayStatusKind.Low => Color.FromArgb(255, 77, 77),
            _ => Color.FromArgb(95, 95, 95)
        };
    }

    private static Icon CreateCircleIcon(Color color)
    {
        using var bitmap = new Bitmap(32, 32);
        using var graphics = Graphics.FromImage(bitmap);
        graphics.SmoothingMode = SmoothingMode.AntiAlias;
        graphics.Clear(Color.Transparent);

        using var fill = new SolidBrush(color);
        using var border = new Pen(Color.FromArgb(230, 24, 24, 24), 1);
        graphics.FillEllipse(fill, 2, 2, 28, 28);
        graphics.DrawEllipse(border, 2, 2, 28, 28);

        var handle = bitmap.GetHicon();
        try
        {
            using var icon = Icon.FromHandle(handle);
            return (Icon)icon.Clone();
        }
        finally
        {
            DestroyIcon(handle);
        }
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool DestroyIcon(IntPtr handle);

    private enum TrayStatusKind
    {
        Unavailable,
        Low,
        Medium,
        Good
    }
}
