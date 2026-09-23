namespace InkDispersion.Tests;

public sealed class InkDispersionShaderMathTests
{
    [Theory]
    [InlineData(0, 0)]
    [InlineData(1, 3)]
    [InlineData(2, 4)]
    [InlineData(3, 1)]
    [InlineData(4, 2)]
    [InlineData(5, 7)]
    [InlineData(6, 8)]
    [InlineData(7, 5)]
    [InlineData(8, 6)]
    public void EveryLatticeDirectionHasAnOppositeThatPointsBack(int direction, int expected)
    {
        Assert.Equal(expected, InkDispersionShaderMath.Opposite(direction));
        Assert.Equal(direction, InkDispersionShaderMath.Opposite(expected));
        Assert.Equal(-InkDispersionShaderMath.Ex(direction), InkDispersionShaderMath.Ex(expected));
        Assert.Equal(-InkDispersionShaderMath.Ey(direction), InkDispersionShaderMath.Ey(expected));
    }

    [Fact]
    public void TheLatticeWeightsSumToOne()
        => Assert.Equal(1f, Enumerable.Range(0, 9).Sum(InkDispersionShaderMath.Weight), 6);
}
