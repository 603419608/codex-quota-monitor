using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using CodexUsageOverlay.Core;
using ButtonBase = System.Windows.Controls.Primitives.ButtonBase;
using TextBox = System.Windows.Controls.TextBox;

namespace CodexUsageOverlay.App;

public partial class MainWindow : Window
{
    private const double FullWidth = 230;
    private const double FullHeight = 520;
    private const double FullHeightWithoutFiveHour = 390;
    private const double MiniWidth = 330;
    private const double ChineseMiniWidthWithoutFiveHour = 220;
    private const double MiniHeight = 48;
    private const double AccountSummaryHeight = 56;
    private static readonly TimeSpan HideDebounce = TimeSpan.FromSeconds(2);
    private static readonly TimeSpan PositionSaveDebounce = TimeSpan.FromMilliseconds(500);

    private readonly OverlaySettings _settings;
    private readonly SettingsStore _settingsStore;
    private readonly OverlayViewModel _viewModel;
    private readonly CodexWindowWatcher _codexWindowWatcher;
    private readonly TrayIconService _trayIconService;
    private readonly DispatcherTimer _positionSaveTimer;
    private double? _pendingWindowLeft;
    private double? _pendingWindowTop;
    private bool? _lastCodexWindowAvailable;
    private DateTimeOffset? _codexMissingSince;
    private bool _isHiddenToTray;
    private bool _isClosing;

    public MainWindow()
    {
        InitializeComponent();

        _settingsStore = new SettingsStore();
        _settings = _settingsStore.Load();
        _viewModel = new OverlayViewModel(_settings, _settingsStore, Dispatcher);
        DataContext = _viewModel;
        _viewModel.PropertyChanged += ViewModel_PropertyChanged;
        _codexWindowWatcher = new CodexWindowWatcher(ApplyCodexWindowPresence);
        _trayIconService = new TrayIconService(_viewModel.ShowButtonText, _viewModel.ExitButtonText, RestoreFromTray, ExitFromTray);
        UpdateTrayIcon();
        _positionSaveTimer = new DispatcherTimer(DispatcherPriority.Background, Dispatcher)
        {
            Interval = PositionSaveDebounce
        };
        _positionSaveTimer.Tick += (_, _) => FlushPendingWindowPosition();

        if (_settings.WindowLeft.HasValue && _settings.WindowTop.HasValue)
        {
            WindowStartupLocation = WindowStartupLocation.Manual;
            Left = _settings.WindowLeft.Value;
            Top = _settings.WindowTop.Value;
        }
        else
        {
            WindowStartupLocation = WindowStartupLocation.CenterScreen;
        }

        ApplyWindowMode();
        Loaded += async (_, _) =>
        {
            await _viewModel.InitializeAsync();
            ApplyCodexWindowPresence(_codexWindowWatcher.CurrentAvailability);
        };
        LocationChanged += (_, _) => ScheduleWindowPositionSave();
        Closing += async (_, _) =>
        {
            _isClosing = true;
            FlushPendingWindowPosition();
            _trayIconService.Dispose();
            _codexWindowWatcher.Dispose();
            await _viewModel.DisposeAsync();
        };
    }

    private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        TryDragWindow(e);
    }

    private void WindowDragArea_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        TryDragWindow(e);
    }

    private void TryDragWindow(MouseButtonEventArgs e)
    {
        if (e.ButtonState == MouseButtonState.Pressed)
        {
            if (IsInteractiveElement(e.OriginalSource as DependencyObject))
            {
                return;
            }

            if (e.ClickCount == 2)
            {
                HideToTray();
                e.Handled = true;
                return;
            }

            DragMove();
            e.Handled = true;
        }
    }

    private static bool IsInteractiveElement(DependencyObject? source)
    {
        while (source is not null)
        {
            if (source is TextBox or ButtonBase or Selector)
            {
                return true;
            }

            // Run 等内容元素不是 Visual，VisualTreeHelper.GetParent 会抛 InvalidOperationException；
            // 对非 Visual 元素改走逻辑树（Run 的逻辑父级就是承载它的 TextBlock）。
            source = source is Visual or System.Windows.Media.Media3D.Visual3D
                ? VisualTreeHelper.GetParent(source)
                : LogicalTreeHelper.GetParent(source);
        }

        return false;
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }

    private void HideToTrayButton_Click(object sender, RoutedEventArgs e)
    {
        HideToTray();
    }

    private async void RefreshResetCreditsButton_Click(object sender, RoutedEventArgs e)
    {
        await _viewModel.RefreshResetCreditsManuallyAsync();
    }

    private void CollapseButton_Click(object sender, RoutedEventArgs e)
    {
        _viewModel.IsCollapsed = true;
    }

    private void ExpandButton_Click(object sender, RoutedEventArgs e)
    {
        _viewModel.IsCollapsed = false;
    }

    private void ViewModel_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(OverlayViewModel.IsCollapsed) ||
            e.PropertyName == nameof(OverlayViewModel.FiveHourLimitVisibility) ||
            e.PropertyName == nameof(OverlayViewModel.AccountSummaryVisibility))
        {
            ApplyWindowMode();
        }

        if (e.PropertyName == nameof(OverlayViewModel.TrayTooltipText) ||
            e.PropertyName == nameof(OverlayViewModel.TrayStatusPercent))
        {
            UpdateTrayIcon();
        }
    }

    private void ScheduleWindowPositionSave()
    {
        if (double.IsNaN(Left) || double.IsNaN(Top))
        {
            return;
        }

        _pendingWindowLeft = Left;
        _pendingWindowTop = Top;
        _positionSaveTimer.Stop();
        _positionSaveTimer.Start();
    }

    private void FlushPendingWindowPosition()
    {
        _positionSaveTimer.Stop();

        if (!_pendingWindowLeft.HasValue || !_pendingWindowTop.HasValue)
        {
            return;
        }

        var left = _pendingWindowLeft.Value;
        var top = _pendingWindowTop.Value;
        _pendingWindowLeft = null;
        _pendingWindowTop = null;

        _viewModel.SaveWindowPosition(left, top);
    }

    private void ApplyCodexWindowPresence(bool isAvailable)
    {
        if (!Dispatcher.CheckAccess())
        {
            Dispatcher.Invoke(() => ApplyCodexWindowPresence(isAvailable));
            return;
        }

        if (_isClosing)
        {
            return;
        }

        _viewModel.SetCodexWindowAvailable(isAvailable);

        if (_isHiddenToTray)
        {
            return;
        }

        if (isAvailable)
        {
            _codexMissingSince = null;
            _trayIconService.SetVisible(false);
            if (_lastCodexWindowAvailable == true)
            {
                return;
            }

            _lastCodexWindowAvailable = true;
            if (!_isClosing)
            {
                Show();
            }

            return;
        }

        _codexMissingSince ??= DateTimeOffset.UtcNow;
        if (DateTimeOffset.UtcNow - _codexMissingSince < HideDebounce)
        {
            return;
        }

        if (_lastCodexWindowAvailable == false)
        {
            return;
        }

        _lastCodexWindowAvailable = false;
        _trayIconService.SetVisible(true);
        Hide();
    }

    private void HideToTray()
    {
        _isHiddenToTray = true;
        UpdateTrayIcon();
        _trayIconService.SetVisible(true);
        Hide();
    }

    private void RestoreFromTray()
    {
        if (!Dispatcher.CheckAccess())
        {
            Dispatcher.BeginInvoke(new Action(RestoreFromTray));
            return;
        }

        if (_isClosing)
        {
            return;
        }

        _isHiddenToTray = false;
        _lastCodexWindowAvailable = null;
        _codexMissingSince = null;
        _trayIconService.SetVisible(false);
        Show();
        Activate();
    }

    private void ExitFromTray()
    {
        if (!Dispatcher.CheckAccess())
        {
            Dispatcher.BeginInvoke(new Action(ExitFromTray));
            return;
        }

        Close();
    }

    private void UpdateTrayIcon()
    {
        _trayIconService.Update(_viewModel.TrayTooltipText, _viewModel.TrayStatusPercent);
    }

    private void ApplyWindowMode()
    {
        if (_viewModel.IsCollapsed)
        {
            var miniWidth = _viewModel.FiveHourLimitVisibility == Visibility.Visible
                ? MiniWidth
                : string.Equals(
                    System.Globalization.CultureInfo.CurrentUICulture.TwoLetterISOLanguageName,
                    "zh",
                    StringComparison.OrdinalIgnoreCase)
                    ? ChineseMiniWidthWithoutFiveHour
                    : MiniWidth;
            MinWidth = miniWidth;
            MinHeight = MiniHeight;
            Width = miniWidth;
            Height = MiniHeight;
        }
        else
        {
            var hasFiveHourLimit = _viewModel.FiveHourLimitVisibility == Visibility.Visible;
            var accountSummaryHeight = _viewModel.AccountSummaryVisibility == Visibility.Visible
                ? AccountSummaryHeight
                : 0;
            MinWidth = 220;
            MinHeight = (hasFiveHourLimit ? 500 : 370) + accountSummaryHeight;
            Width = FullWidth;
            Height = (hasFiveHourLimit ? FullHeight : FullHeightWithoutFiveHour) + accountSummaryHeight;
        }

        KeepWindowInsideWorkArea();
    }

    private void KeepWindowInsideWorkArea()
    {
        if (WindowStartupLocation != WindowStartupLocation.Manual)
        {
            return;
        }

        var workArea = SystemParameters.WorkArea;
        if (Left + Width > workArea.Right)
        {
            Left = Math.Max(workArea.Left, workArea.Right - Width - 8);
        }

        if (Top + Height > workArea.Bottom)
        {
            Top = Math.Max(workArea.Top, workArea.Bottom - Height - 8);
        }

        if (Left < workArea.Left)
        {
            Left = workArea.Left + 8;
        }

        if (Top < workArea.Top)
        {
            Top = workArea.Top + 8;
        }
    }

}
