using System.Windows.Automation;
using CodexUsageOverlay.Core;

namespace CodexUsageOverlay.App;

public sealed class CodexStatusReader
{
    public Task<ContextUsage> ReadCurrentContextAsync(CancellationToken cancellationToken = default)
    {
        return Task.Run(ReadCurrentContext, cancellationToken);
    }

    private static ContextUsage ReadCurrentContext()
    {
        try
        {
            var window = CodexWindowDetector.FindCodexWindow();
            if (window is null)
            {
                return ContextUsage.Waiting;
            }

            return FindContextUsage(window);
        }
        catch
        {
            return ContextUsage.Waiting;
        }
    }

    private static ContextUsage FindContextUsage(AutomationElement window)
    {
        // window comes from FindCodexWindow and is still a live element. Only the cached descendants below use Cached.*.
        var windowBounds = window.Current.BoundingRectangle;

        var cacheRequest = new CacheRequest
        {
            AutomationElementMode = AutomationElementMode.None,
            TreeScope = TreeScope.Element
        };
        cacheRequest.Add(AutomationElement.NameProperty);
        cacheRequest.Add(AutomationElement.BoundingRectangleProperty);

        // This still enumerates descendants; the improvement is that needed properties are fetched in one cache pass.
        AutomationElementCollection descendants;
        using (cacheRequest.Activate())
        {
            descendants = window.FindAll(TreeScope.Descendants, Condition.TrueCondition);
        }

        string? bestName = null;
        var bestBottom = double.NegativeInfinity;

        foreach (AutomationElement element in descendants)
        {
            string name;
            System.Windows.Rect bounds;
            try
            {
                name = element.Cached.Name;
                bounds = element.Cached.BoundingRectangle;
            }
            catch
            {
                continue;
            }

            if (string.IsNullOrWhiteSpace(name) ||
                !UsageParsers.IsContextIndicator(name) ||
                !Intersects(windowBounds, bounds) ||
                !IsInCurrentComposerRegion(windowBounds, bounds))
            {
                continue;
            }

            if (bestName is null || bounds.Bottom > bestBottom)
            {
                bestName = name;
                bestBottom = bounds.Bottom;
            }
        }

        return bestName is null ? ContextUsage.Waiting : ParseIndicatorName(bestName);
    }

    private static ContextUsage ParseIndicatorName(string name)
    {
        try
        {
            return UsageParsers.ParseStatusText(name);
        }
        catch
        {
            return ContextUsage.Waiting;
        }
    }

    private static bool IsInCurrentComposerRegion(System.Windows.Rect windowBounds, System.Windows.Rect bounds)
    {
        if (windowBounds.IsEmpty || bounds.IsEmpty)
        {
            return false;
        }

        var minTop = windowBounds.Top + windowBounds.Height * 0.55;
        var minLeft = windowBounds.Left + windowBounds.Width * 0.25;
        return bounds.Top >= minTop && bounds.Left >= minLeft;
    }

    private static bool Intersects(System.Windows.Rect outer, System.Windows.Rect inner)
    {
        if (outer.IsEmpty || inner.IsEmpty)
        {
            return false;
        }

        return inner.Right >= outer.Left &&
               inner.Left <= outer.Right &&
               inner.Bottom >= outer.Top &&
               inner.Top <= outer.Bottom;
    }
}
