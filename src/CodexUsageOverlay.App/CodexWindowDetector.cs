using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Automation;

namespace CodexUsageOverlay.App;

public static class CodexWindowDetector
{
    public static bool IsCodexWindowAvailable()
    {
        try
        {
            return FindCodexWindow() is not null;
        }
        catch
        {
            return false;
        }
    }

    public static AutomationElement? FindCodexWindow()
    {
        var window = FindCodexWindowByAutomation();
        if (window is not null)
        {
            return window;
        }

        var handle = FindCodexWindowHandle();
        if (handle != IntPtr.Zero)
        {
            try
            {
                return AutomationElement.FromHandle(handle);
            }
            catch
            {
            }
        }

        return null;
    }

    private static AutomationElement? FindCodexWindowByAutomation()
    {
        AutomationElementCollection windows;
        try
        {
            windows = AutomationElement.RootElement.FindAll(TreeScope.Children, Condition.TrueCondition);
        }
        catch
        {
            return null;
        }

        foreach (AutomationElement window in windows)
        {
            string className;
            int processId;
            string name;
            System.Windows.Rect bounds;
            try
            {
                var current = window.Current;
                className = current.ClassName;
                processId = current.ProcessId;
                name = current.Name ?? string.Empty;
                bounds = current.BoundingRectangle;
            }
            catch
            {
                continue;
            }

            if (!string.Equals(className, "Chrome_WidgetWin_1", StringComparison.Ordinal))
            {
                continue;
            }

            if (!IsChatGptProcess(processId) || !IsChatGptWindowTitle(name))
            {
                continue;
            }

            if (bounds.IsEmpty)
            {
                continue;
            }

            return window;
        }

        return null;
    }

    private static IntPtr FindCodexWindowHandle()
    {
        var result = IntPtr.Zero;
        EnumWindows((hwnd, _) =>
        {
            if (!IsCodexWindowHandle(hwnd))
            {
                return true;
            }

            result = hwnd;
            return false;
        }, IntPtr.Zero);

        return result;
    }

    private static bool IsCodexWindowHandle(IntPtr hwnd)
    {
        if (hwnd == IntPtr.Zero || !IsWindow(hwnd) || !IsWindowVisible(hwnd) || IsIconic(hwnd))
        {
            return false;
        }

        if (!TryGetClassName(hwnd, out var className) ||
            !string.Equals(className, "Chrome_WidgetWin_1", StringComparison.Ordinal))
        {
            return false;
        }

        _ = GetWindowThreadProcessId(hwnd, out var processId);
        var isChatGptProcess = IsChatGptProcess((int)processId);
        if (!isChatGptProcess ||
            !TryGetWindowText(hwnd, out var title) ||
            !IsChatGptWindowTitle(title))
        {
            return false;
        }

        return GetWindowRect(hwnd, out var rect) &&
               rect.Right > rect.Left &&
               rect.Bottom > rect.Top;
    }

    private static bool TryGetClassName(IntPtr hwnd, out string className)
    {
        var builder = new StringBuilder(256);
        var length = GetClassName(hwnd, builder, builder.Capacity);
        className = length > 0 ? builder.ToString() : string.Empty;
        return length > 0;
    }

    private static bool TryGetWindowText(IntPtr hwnd, out string title)
    {
        var builder = new StringBuilder(512);
        var length = GetWindowText(hwnd, builder, builder.Capacity);
        title = length > 0 ? builder.ToString() : string.Empty;
        return length > 0;
    }

    private static bool IsChatGptProcess(int processId)
    {
        try
        {
            using var process = Process.GetProcessById(processId);
            return process.ProcessName.Equals("ChatGPT", StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return false;
        }
    }

    private static bool IsChatGptWindowTitle(string title)
    {
        return title.Equals("ChatGPT", StringComparison.OrdinalIgnoreCase);
    }

    [DllImport("user32.dll")]
    private static extern bool EnumWindows(EnumWindowsProc enumProc, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern bool IsWindow(IntPtr hwnd);

    [DllImport("user32.dll")]
    private static extern bool IsWindowVisible(IntPtr hwnd);

    [DllImport("user32.dll")]
    private static extern bool IsIconic(IntPtr hwnd);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern int GetClassName(IntPtr hwnd, StringBuilder className, int maxCount);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern int GetWindowText(IntPtr hwnd, StringBuilder text, int maxCount);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint GetWindowThreadProcessId(IntPtr hwnd, out uint processId);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool GetWindowRect(IntPtr hwnd, out WindowRect rect);

    private delegate bool EnumWindowsProc(IntPtr hwnd, IntPtr lParam);

    [StructLayout(LayoutKind.Sequential)]
    private readonly struct WindowRect
    {
        public readonly int Left;
        public readonly int Top;
        public readonly int Right;
        public readonly int Bottom;
    }
}

public sealed class CodexWindowWatcher : IDisposable
{
    private const uint EventSystemForeground = 0x0003;
    private const uint WineventOutOfContext = 0x0000;
    private const uint WineventSkipOwnProcess = 0x0002;
    private const int ObjidWindow = 0;

    private static readonly TimeSpan DetectionInterval = TimeSpan.FromSeconds(2);
    private static readonly TimeSpan DetectionDebounce = TimeSpan.FromMilliseconds(250);

    private readonly SynchronizationContext? _syncContext;
    private readonly Action<bool> _onAvailabilityChanged;
    private readonly WinEventDelegate _callback;
    private readonly IntPtr _foregroundHook;
    private readonly SemaphoreSlim _wake = new(0, 1);
    private readonly CancellationTokenSource _cts = new();
    private readonly Task _detectionLoop;
    private volatile bool _lastAvailability;
    private volatile bool _isDisposed;

    public CodexWindowWatcher(Action<bool> onAvailabilityChanged)
    {
        _syncContext = SynchronizationContext.Current;
        Debug.Assert(_syncContext is not null, "CodexWindowWatcher must be created on a UI/message-pump thread.");
        _onAvailabilityChanged = onAvailabilityChanged;
        _callback = HandleWinEvent;

        // 仅启动时同步检测一次，给出初始可用性；此后所有检测都在后台循环里完成（不再有 UI 线程上的周期性全量扫描）。
        _lastAvailability = CodexWindowDetector.IsCodexWindowAvailable();

        // OUTOFCONTEXT callbacks are delivered through the thread that registered the hook.
        // Keep this watcher constructed/disposed on the WPF UI thread so the message pump stays active.
        // Only hook foreground changes; pure show/hide cases are covered by the 2s background poll.
        _foregroundHook = SetWinEventHook(
            EventSystemForeground,
            EventSystemForeground,
            IntPtr.Zero,
            _callback,
            0,
            0,
            WineventOutOfContext | WineventSkipOwnProcess);

        _detectionLoop = Task.Run(() => RunDetectionLoopAsync(_cts.Token));
    }

    public bool CurrentAvailability => _lastAvailability;

    // WinEvent 回调（在注册线程消息泵上执行）只置脏标记，绝不在此做 UIA 扫描。
    private void HandleWinEvent(
        IntPtr hook,
        uint eventType,
        IntPtr hwnd,
        int idObject,
        int idChild,
        uint eventThread,
        uint eventTime)
    {
        if (_isDisposed || idObject != ObjidWindow || idChild != 0)
        {
            return;
        }

        SignalDirty();
    }

    private void SignalDirty()
    {
        try
        {
            _wake.Release();
        }
        catch (SemaphoreFullException)
        {
            // 已有待处理信号；事件风暴在此合并为一次。
        }
        catch (ObjectDisposedException)
        {
            // 正在关闭。
        }
    }

    private async Task RunDetectionLoopAsync(CancellationToken cancellationToken)
    {
        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                // 等事件信号；无事件时每 DetectionInterval 兜底检测一次（替代原 2 秒 DispatcherTimer）。
                var signaled = await _wake.WaitAsync(DetectionInterval, cancellationToken);
                if (cancellationToken.IsCancellationRequested)
                {
                    break;
                }

                // 收到事件后小幅去抖，合并连续窗口事件。
                if (signaled)
                {
                    await Task.Delay(DetectionDebounce, cancellationToken);
                    if (cancellationToken.IsCancellationRequested)
                    {
                        break;
                    }
                }

                bool isAvailable;
                try
                {
                    isAvailable = CodexWindowDetector.IsCodexWindowAvailable();
                }
                catch
                {
                    continue; // 本轮检测异常忽略，下轮再试
                }

                // 每轮都发布：MainWindow 的 2 秒隐藏去抖依赖周期性重复调用（精确替代原定时器 force 行为）。
                Publish(isAvailable);
            }
        }
        catch (OperationCanceledException)
        {
            // 正常取消。
        }
        catch
        {
            // 兜底：避免后台任务未观察异常带来进程风险；吞掉后由 finally 清理并退出循环。
        }
        finally
        {
            _wake.Dispose();
            _cts.Dispose();
        }
    }

    private void Publish(bool isAvailable)
    {
        if (_isDisposed)
        {
            return;
        }

        _lastAvailability = isAvailable;
        var callback = _onAvailabilityChanged;
        if (_syncContext is not null)
        {
            _syncContext.Post(_ => callback(isAvailable), null);
            return;
        }

        ThreadPool.QueueUserWorkItem(_ => callback(isAvailable));
    }

    public void Dispose()
    {
        if (_isDisposed)
        {
            return;
        }

        _isDisposed = true;

        if (_foregroundHook != IntPtr.Zero)
        {
            _ = UnhookWinEvent(_foregroundHook);
        }

        try
        {
            _cts.Cancel();
        }
        catch (ObjectDisposedException)
        {
        }

        SignalDirty(); // 唤醒后台循环以尽快响应取消
    }

    private delegate void WinEventDelegate(
        IntPtr hook,
        uint eventType,
        IntPtr hwnd,
        int idObject,
        int idChild,
        uint eventThread,
        uint eventTime);

    [DllImport("user32.dll")]
    private static extern IntPtr SetWinEventHook(
        uint eventMin,
        uint eventMax,
        IntPtr eventHookAssemblyHandle,
        WinEventDelegate eventHookHandle,
        uint processId,
        uint threadId,
        uint flags);

    [DllImport("user32.dll")]
    private static extern bool UnhookWinEvent(IntPtr hook);
}
