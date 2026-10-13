using TheHub.Design.Motion;
using Xunit;

namespace TheHub.Tests;

public class SpringMotionTests
{
    [Fact]
    public void SpringSimulation_SettlesWithinTolerance()
    {
        var config = SpringConfig.Snappy;
        var sim = new SpringSimulation(0.0, 100.0, config);

        Assert.False(sim.IsAtRest);

        // Step through simulation at 60fps (dt ≈ 0.016s)
        for (int i = 0; i < 200; i++)
        {
            sim.Step(0.016);
            if (sim.IsAtRest) break;
        }

        Assert.True(sim.IsAtRest, "Simulation must settle to rest within 200 frames.");
        Assert.Equal(100.0, sim.CurrentValue, 2);
        Assert.Equal(0.0, sim.Velocity, 3);
    }

    [Fact]
    public void SpringSimulation_Retargeting_PreservesVelocity()
    {
        var config = SpringConfig.Smooth;
        var sim = new SpringSimulation(0.0, 100.0, config);

        // Run for 5 frames so velocity builds up
        for (int i = 0; i < 5; i++)
        {
            sim.Step(0.016);
        }

        double velocityBefore = sim.Velocity;
        Assert.True(velocityBefore > 0, "Velocity should be positive while moving towards 100");

        // Retarget mid-flight to 200
        sim.Retarget(200.0);

        Assert.False(sim.IsAtRest);
        Assert.Equal(200.0, sim.TargetValue);
        Assert.Equal(velocityBefore, sim.Velocity); // Velocity must be strictly preserved
    }

    [Fact]
    public void SpringSimulation_CriticallyDamped_HasNoOvershoot()
    {
        // For mass = 1, k = 100: critical damping c = 2 * sqrt(100) = 20
        var criticalConfig = new SpringConfig(100.0, 20.0, 1.0);
        var sim = new SpringSimulation(0.0, 50.0, criticalConfig);

        double maxSeen = 0.0;
        for (int i = 0; i < 300; i++)
        {
            sim.Step(0.016);
            if (sim.CurrentValue > maxSeen)
            {
                maxSeen = sim.CurrentValue;
            }
            if (sim.IsAtRest) break;
        }

        Assert.True(sim.IsAtRest);
        Assert.True(maxSeen <= 50.001, $"Critically damped spring must not overshoot target 50 (max was {maxSeen})");
    }

    [Fact]
    public void SpringAnimator_ActiveCount_IsZeroWhenNoAnimations()
    {
        Assert.Equal(0, SpringAnimator.ActiveAnimationCount);
        Assert.False(SpringAnimator.IsTicking);
    }
}
