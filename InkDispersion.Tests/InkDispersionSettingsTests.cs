namespace InkDispersion.Tests;

public sealed class InkDispersionSettingsTests
{
    [Theory]
    [InlineData(InkDispersionQuality.Balanced, 160, 720)]
    [InlineData(InkDispersionQuality.High, 224, 1080)]
    [InlineData(InkDispersionQuality.Ultra, 288, 1440)]
    public void EveryQualityChoosesItsGridResolutionAndStepLimit(InkDispersionQuality quality, int resolution, int maxSteps)
    {
        var settings = InkDispersionSettings.GetQuality(quality);

        Assert.Equal(resolution, settings.GridResolution);
        Assert.Equal(maxSteps, settings.MaxSteps);
    }

    [Theory]
    [InlineData(1920, 1080, 224)]
    [InlineData(1080, 1920, 224)]
    [InlineData(8, 8, 224)]
    [InlineData(4096, 16, 160)]
    [InlineData(100, 100, 288)]
    public void TheGridCoversTheWholeCanvas(int width, int height, int resolution)
    {
        var (gridWidth, gridHeight, cellSize) = InkDispersionSettings.GetGridSize(width, height, resolution);

        Assert.True(gridWidth >= InkDispersionSettings.MinimumGridSize);
        Assert.True(gridHeight >= InkDispersionSettings.MinimumGridSize);
        Assert.True(cellSize > 0f);
        Assert.True(gridWidth * cellSize >= width);
        Assert.True(gridHeight * cellSize >= height);
    }

    [Theory]
    [InlineData(0f, 720, 192)]
    [InlineData(10f, 720, 228)]
    [InlineData(60f, 1080, 728)]
    [InlineData(500f, 1080, 1080)]
    [InlineData(500f, 720, 720)]
    public void TheStepCountGrowsWithTheReachWithinItsLimits(float reachCells, int maxSteps, int expected)
        => Assert.Equal(expected, InkDispersionSettings.GetStepCount(reachCells, maxSteps));

    [Theory]
    [InlineData(-5f, InkDispersionSettings.MinimumDeposit)]
    [InlineData(0f, InkDispersionSettings.MinimumDeposit)]
    [InlineData(1f, InkDispersionSettings.MaximumDeposit)]
    [InlineData(5f, InkDispersionSettings.MaximumDeposit)]
    public void TheWaterDepositStaysWithinItsRange(float water, float expected)
        => Assert.Equal(expected, InkDispersionSettings.GetDeposit(water), 5);

    [Fact]
    public void MoreWaterDepositsMore()
        => Assert.True(InkDispersionSettings.GetDeposit(0.75f) > InkDispersionSettings.GetDeposit(0.25f));

    [Theory]
    [InlineData(0f, InkDispersionSettings.MaximumOmega)]
    [InlineData(1f, InkDispersionSettings.MinimumOmega)]
    public void TheRelaxationSpansItsRangeOverTheViscosity(float viscosity, float expected)
        => Assert.Equal(expected, InkDispersionSettings.GetOmega(viscosity), 5);

    [Fact]
    public void AHigherViscosityRelaxesMoreSlowly()
        => Assert.True(InkDispersionSettings.GetOmega(0.25f) > InkDispersionSettings.GetOmega(0.75f));

    [Theory]
    [InlineData(0f, InkDispersionSettings.SigmaFloor)]
    [InlineData(1f, InkDispersionSettings.SigmaFloor + InkDispersionSettings.MaximumSigma)]
    public void ThePinningThresholdSpansItsRangeOverTheSizing(float sizing, float expected)
        => Assert.Equal(expected, InkDispersionSettings.GetPinningSigma(sizing), 6);

    [Fact]
    public void MoreSizingPinsTheFrontHarder()
        => Assert.True(InkDispersionSettings.GetPinningSigma(0.75f) > InkDispersionSettings.GetPinningSigma(0.25f));

    [Theory]
    [InlineData(-1f, 0f)]
    [InlineData(0f, 0f)]
    [InlineData(1f, InkDispersionSettings.MaximumSurfaceEvaporation)]
    [InlineData(2f, InkDispersionSettings.MaximumSurfaceEvaporation)]
    public void TheEvaporationStaysWithinItsRange(float drying, float expected)
        => Assert.Equal(expected, InkDispersionSettings.GetSurfaceEvaporation(drying), 7);
}
