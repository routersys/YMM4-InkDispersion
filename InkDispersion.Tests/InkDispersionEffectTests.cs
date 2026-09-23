namespace InkDispersion.Tests;

public sealed class InkDispersionEffectTests
{
    private static double ValueAt(YukkuriMovieMaker.Commons.Animation animation) => animation.GetValue(0, 1, 30);

    [Fact]
    public void DefaultParameterValuesMatchSpecification()
    {
        var effect = new InkDispersionEffect();

        Assert.Equal(100d, ValueAt(effect.Amount), 6);
        Assert.Equal(100d, ValueAt(effect.Spread), 6);
        Assert.Equal(60d, ValueAt(effect.Water), 6);
        Assert.Equal(25d, ValueAt(effect.Reach), 6);
        Assert.Equal(50d, ValueAt(effect.Fiber), 6);
        Assert.Equal(30d, ValueAt(effect.Sizing), 6);
        Assert.Equal(30d, ValueAt(effect.Viscosity), 6);
        Assert.Equal(40d, ValueAt(effect.Drying), 6);
        Assert.Equal(80d, ValueAt(effect.Density), 6);
        Assert.Equal(40d, ValueAt(effect.Grain), 6);
        Assert.Equal(InkDispersionQuality.High, effect.Quality);
        Assert.Equal(0, effect.Seed);
        Assert.Equal(System.Windows.Media.Color.FromArgb(255, 30, 26, 24), effect.InkColor);
    }

    [Theory]
    [InlineData(int.MinValue, 0)]
    [InlineData(-1, 0)]
    [InlineData(0, 0)]
    [InlineData(1234, 1234)]
    public void SeedClampsNegativeInputToZero(int input, int expected)
    {
        var effect = new InkDispersionEffect { Seed = input };

        Assert.Equal(expected, effect.Seed);
    }

    [Fact]
    public void CreateExoVideoFiltersReturnsEmpty()
    {
        var effect = new InkDispersionEffect();

        Assert.Empty(effect.CreateExoVideoFilters(0, null!));
    }
}
