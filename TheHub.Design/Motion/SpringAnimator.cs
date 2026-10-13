using System.Diagnostics;
using System.Windows;
using System.Windows.Media;

namespace TheHub.Design.Motion;

/// <summary>
/// Pure mathematical spring simulation solver supporting underdamped and critically damped configurations.
/// Completely decoupled from WPF types for rigorous unit testing.
/// </summary>
public class SpringSimulation
{
    private const double Tolerance = 0.001;
    private const double VelocityTolerance = 0.001;

    public double CurrentValue { get; private set; }
    public double TargetValue { get; private set; }
    public double Velocity { get; private set; }
    public SpringConfig Config { get; set; }
    public bool IsAtRest { get; private set; }

    public SpringSimulation(double initialValue, double targetValue, SpringConfig config, double initialVelocity = 0.0)
    {
        CurrentValue = initialValue;
        TargetValue = targetValue;
        Config = config;
        Velocity = initialVelocity;
        IsAtRest = Math.Abs(initialValue - targetValue) < Tolerance && Math.Abs(initialVelocity) < VelocityTolerance;
        if (IsAtRest)
        {
            CurrentValue = targetValue;
            Velocity = 0.0;
        }
    }

    /// <summary>
    /// Retargets the spring to a new destination, preserving existing velocity for seamless continuity.
    /// </summary>
    public void Retarget(double newTarget)
    {
        TargetValue = newTarget;
        IsAtRest = false;
    }

    /// <summary>
    /// Steps the simulation forward by dt seconds using stable semi-implicit Euler integration with sub-stepping.
    /// </summary>
    public void Step(double dt)
    {
        if (IsAtRest) return;
        if (dt <= 0) return;

        // Cap dt to prevent catastrophic explosion on long pauses/debugging
        double remaining = Math.Min(dt, 0.1);
        double subStep = 1.0 / 240.0; // 240Hz sub-stepping for ultra stability

        while (remaining > 0)
        {
            double step = Math.Min(remaining, subStep);
            remaining -= step;

            double displacement = CurrentValue - TargetValue;
            double springForce = -Config.Stiffness * displacement;
            double dampingForce = -Config.Damping * Velocity;
            double acceleration = (springForce + dampingForce) / Math.Max(0.01, Config.Mass);

            Velocity += acceleration * step;
            CurrentValue += Velocity * step;
        }

        if (Math.Abs(CurrentValue - TargetValue) < Tolerance && Math.Abs(Velocity) < VelocityTolerance)
        {
            CurrentValue = TargetValue;
            Velocity = 0;
            IsAtRest = true;
        }
    }
}

public interface IActiveAnimation
{
    bool Update(double dt);
}

/// <summary>
/// Central shared motion engine driven by CompositionTarget.Rendering.
/// Auto-stops ticking at rest (0 callbacks, CPU ~ 0%).
/// </summary>
public static class SpringAnimator
{
    private static readonly Dictionary<string, IActiveAnimation> ActiveAnimations = new();
    private static bool _isSubscribed = false;
    private static readonly Stopwatch Stopwatch = new();
    private static TimeSpan _lastRenderTime;

    public static int ActiveAnimationCount
    {
        get
        {
            lock (ActiveAnimations)
            {
                return ActiveAnimations.Count;
            }
        }
    }

    public static bool IsTicking => _isSubscribed;

    public static void StartAnimation(string key, IActiveAnimation animation)
    {
        lock (ActiveAnimations)
        {
            ActiveAnimations[key] = animation;
            EnsureSubscribed();
        }
    }

    public static void StopAnimation(string key)
    {
        lock (ActiveAnimations)
        {
            ActiveAnimations.Remove(key);
            CheckUnsubscribe();
        }
    }

    private static void EnsureSubscribed()
    {
        if (!_isSubscribed && ActiveAnimations.Count > 0)
        {
            _isSubscribed = true;
            Stopwatch.Restart();
            _lastRenderTime = TimeSpan.Zero;
            CompositionTarget.Rendering += OnRendering;
        }
    }

    private static void CheckUnsubscribe()
    {
        if (_isSubscribed && ActiveAnimations.Count == 0)
        {
            _isSubscribed = false;
            CompositionTarget.Rendering -= OnRendering;
            Stopwatch.Stop();
        }
    }

    private static void OnRendering(object? sender, EventArgs e)
    {
        TimeSpan current = Stopwatch.Elapsed;
        double dt = (current - _lastRenderTime).TotalSeconds;
        _lastRenderTime = current;

        // Skip erratic spikes
        if (dt <= 0 || dt > 0.1) dt = 1.0 / 60.0;

        List<string> finishedKeys = new();

        lock (ActiveAnimations)
        {
            foreach (var kvp in ActiveAnimations)
            {
                bool isFinished = kvp.Value.Update(dt);
                if (isFinished)
                {
                    finishedKeys.Add(kvp.Key);
                }
            }

            foreach (var key in finishedKeys)
            {
                ActiveAnimations.Remove(key);
            }

            CheckUnsubscribe();
        }
    }

    public static void AnimateValue(
        string key,
        double initial,
        double target,
        SpringConfig config,
        Action<double> onUpdate,
        Action? onCompleted = null)
    {
        var anim = new ValueSpringAnimation(initial, target, config, onUpdate, onCompleted);
        StartAnimation(key, anim);
    }

    private class ValueSpringAnimation : IActiveAnimation
    {
        private readonly SpringSimulation _sim;
        private readonly Action<double> _onUpdate;
        private readonly Action? _onCompleted;

        public ValueSpringAnimation(double initial, double target, SpringConfig config, Action<double> onUpdate, Action? onCompleted)
        {
            _sim = new SpringSimulation(initial, target, config);
            _onUpdate = onUpdate;
            _onCompleted = onCompleted;
            _onUpdate(initial);
        }

        public bool Update(double dt)
        {
            _sim.Step(dt);
            _onUpdate(_sim.CurrentValue);

            if (_sim.IsAtRest)
            {
                _onCompleted?.Invoke();
                return true;
            }
            return false;
        }
    }
}

public static class MotionExtensions
{
    public static void FadeTo(this UIElement element, double targetOpacity, int durationMs = MotionTokens.FadeBaseMs, Action? onCompleted = null)
    {
        string key = $"fade_{element.GetHashCode()}";
        double initial = element.Opacity;
        if (Math.Abs(initial - targetOpacity) < 0.001)
        {
            element.Opacity = targetOpacity;
            onCompleted?.Invoke();
            return;
        }

        var config = SpringConfig.Snappy;
        SpringAnimator.AnimateValue(key, initial, targetOpacity, config, val =>
        {
            element.Opacity = Math.Clamp(val, 0.0, 1.0);
        }, onCompleted);
    }
}
