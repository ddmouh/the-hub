namespace TheHub.Design.Controls;

/// <summary>
/// Pure mathematical calculations for dual-thumb range sliders.
/// Fully decoupled for rigorous unit testing.
/// </summary>
public static class RangeSliderMath
{
    public static double SnapToStep(double value, double min, double step)
    {
        if (step <= 0.0) return value;
        double steps = Math.Round((value - min) / step);
        return min + (steps * step);
    }

    public static double ClampLower(
        double candidateLower,
        double currentUpper,
        double min,
        double max,
        double minDistance,
        double step)
    {
        if (max < min) (min, max) = (max, min);
        double validUpperLimit = Math.Max(min, currentUpper - Math.Max(0.0, minDistance));
        double snapped = SnapToStep(candidateLower, min, step);
        return Math.Clamp(snapped, min, validUpperLimit);
    }

    public static double ClampUpper(
        double currentLower,
        double candidateUpper,
        double min,
        double max,
        double minDistance,
        double step)
    {
        if (max < min) (min, max) = (max, min);
        double validLowerLimit = Math.Min(max, currentLower + Math.Max(0.0, minDistance));
        double snapped = SnapToStep(candidateUpper, min, step);
        return Math.Clamp(snapped, validLowerLimit, max);
    }

    public static double ValueToRatio(double value, double min, double max)
    {
        if (max <= min) return 0.0;
        return Math.Clamp((value - min) / (max - min), 0.0, 1.0);
    }

    public static double RatioToValue(double ratio, double min, double max)
    {
        if (max <= min) return min;
        return min + (Math.Clamp(ratio, 0.0, 1.0) * (max - min));
    }
}
