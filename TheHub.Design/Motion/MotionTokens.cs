namespace TheHub.Design.Motion;

public readonly record struct SpringConfig(double Stiffness, double Damping, double Mass = 1.0)
{
    public static readonly SpringConfig Snappy = new(520, 34, 1.0);
    public static readonly SpringConfig Smooth = new(320, 30, 1.0);
    public static readonly SpringConfig Bouncy = new(420, 20, 1.0);
    public static readonly SpringConfig Gentle = new(220, 26, 1.0);
}

public static class MotionTokens
{
    public const int FadeFastMs = 100;
    public const int FadeBaseMs = 160;
    public const int FadeSlowMs = 260;

    public const int StaggerDelayMs = 40;
    public const int MaxStaggerItems = 6;

    public const double PressScale = 0.96;
    public const double HoverScale = 1.02;
    public const double HoverTranslateY = -1.0;
}
