using System.Windows;
using Telemetry;

namespace InkDispersion.Tests;

public sealed class HostIntegrationTests
{
    [Fact]
    public void OutsideAWpfApplicationNoTelemetryIsStartedOrSent()
    {
        Assert.Null(Application.Current);

        InkDispersionTelemetry.EnsureStartedOnce();
        InkDispersionTelemetry.Report(new InvalidOperationException());

        Assert.Null(ProcessState.Read("DrainClaimed"));
        Assert.Null(ProcessState.Read("SentCount"));
    }

    [Fact]
    public void OutsideAWpfApplicationTheEffectCanStillBeCreated()
    {
        Assert.Null(Application.Current);

        var effect = new InkDispersionEffect();

        Assert.Equal(Texts.InkDispersion, effect.Label);
        Assert.Null(ProcessState.Read("DrainClaimed"));
    }
}
