namespace FreeCamManager.Core.Services;

/// <summary>
/// Calculates Back-to-Top visibility in the ScrollViewer's own offset units.
/// WPF logical scrolling measures items, while physical scrolling measures pixels;
/// viewport and offset always share the same unit within one ScrollViewer.
/// </summary>
public static class BackToTopVisibilityPolicy
{
    public static bool ShouldShow(double verticalOffset, double viewportHeight,
        double scrollableHeight, bool currentlyVisible)
    {
        if (!double.IsFinite(verticalOffset) || !double.IsFinite(viewportHeight) ||
            !double.IsFinite(scrollableHeight) || viewportHeight <= 0 || scrollableHeight <= 0)
            return false;

        // Reveal after scrolling the equivalent of one viewport, not 80 items.
        // Hysteresis avoids blinking when scrolling near the reveal boundary.
        var threshold = viewportHeight * (currentlyVisible ? 0.5 : 1.0);
        return currentlyVisible ? verticalOffset > threshold : verticalOffset >= threshold;
    }
}
