using System.Globalization;
using System.Numerics;
using System.Windows.Media;
using ComputeWeave;
using Vortice.Direct2D1;
using Vortice.Direct2D1.Effects;
using YukkuriMovieMaker.Commons;
using YukkuriMovieMaker.Json;
using YukkuriMovieMaker.Player.Video;

namespace InkDispersion.Tests;

[Collection("Direct2D")]
public sealed class InkDispersionEffectProcessorTests
{
    const int Size = 64;
    const int Length = 30;

    static readonly Bgra Gray = Bgra.Opaque(192, 192, 192);

    static Bgra CenteredSquare(int x, int y) => x is >= 16 and < 48 && y is >= 16 and < 48 ? Gray : Bgra.Transparent;

    static void RequireInterop(IGraphicsDevicesAndContext devices)
    {
        using var scheduler = ComputeExternalQueueScheduler.Create();
        using var provider = InkDispersionInteropProvider.TryCreate(devices, scheduler, out var device);
        if (provider is null || device is null)
            Assert.Skip("Direct3D 11 and Direct3D 12 sharing is unavailable.");
    }

    static Animation Linear(double from, double to)
        => Json.LoadFromText<Animation>(string.Create(CultureInfo.InvariantCulture, $$"""{"AnimationType":"直線移動","Values":[{"Value":{{from}}},{"Value":{{to}}}]}"""))!;

    static Rendering RenderFrame(IGraphicsDevicesAndContext devices, IVideoEffectProcessor processor, int frame)
    {
        processor.Update(EffectDescriptions.At(frame, Length));
        return Rendering.Capture(devices, processor.Output);
    }

    static void AssertSameAsSource(Rendering rendering, SourceImage source)
    {
        Assert.Equal((0, 0, source.Width, source.Height), (rendering.Left, rendering.Top, rendering.Width, rendering.Height));
        Assert.All(rendering.Coordinates(), point => Assert.Equal(source[point.X, point.Y], rendering[point.X, point.Y]));
    }

    static bool HasInkOutside(Rendering rendering, SourceImage source)
        => rendering.Coordinates().Any(point => (!source.Contains(point.X, point.Y) || source[point.X, point.Y].Alpha == 0) && rendering[point.X, point.Y].Alpha > 0);

    static bool Tinted(byte channel, byte alpha, byte color) => Math.Abs(channel - color * alpha / 255d) <= 2d;

    static double DistanceFrom(SourceImage source, int x, int y)
    {
        var dx = Math.Max(Math.Max(-x, x - (source.Width - 1)), 0);
        var dy = Math.Max(Math.Max(-y, y - (source.Height - 1)), 0);
        return Math.Sqrt(dx * dx + dy * dy);
    }

    [Fact]
    public void TheProcessorHandsTheDrawDescriptionBackUnchanged()
    {
        using var devices = new GraphicsDevices();
        using var context = devices.CreateContext();
        using var source = new SourceImage(context, Size, Size, CenteredSquare);
        using var processor = new InkDispersionEffect().CreateVideoEffect(context);
        processor.SetInput(source.Bitmap);
        var description = EffectDescriptions.At(0, Length);

        var draw = processor.Update(description);

        Assert.Same(description.DrawDescription, draw);
    }

    [Fact]
    public void TheInkSpreadsOutOfTheSource()
    {
        using var devices = new GraphicsDevices();
        using var context = devices.CreateContext();
        RequireInterop(context);
        using var source = new SourceImage(context, Size, Size, CenteredSquare);
        using var processor = new InkDispersionEffect().CreateVideoEffect(context);
        processor.SetInput(source.Bitmap);

        var rendering = RenderFrame(context, processor, 0);

        Assert.True(HasInkOutside(rendering, source));
        Assert.All(rendering.Coordinates(), point =>
        {
            var pixel = rendering[point.X, point.Y];
            Assert.InRange(pixel.Blue, 0, pixel.Alpha);
            Assert.InRange(pixel.Green, 0, pixel.Alpha);
            Assert.InRange(pixel.Red, 0, pixel.Alpha);
        });
    }

    [Fact]
    public void TheInkTakesTheChosenColor()
    {
        using var devices = new GraphicsDevices();
        using var context = devices.CreateContext();
        RequireInterop(context);
        using var source = new SourceImage(context, Size, Size, CenteredSquare);
        using var processor = new InkDispersionEffect { InkColor = Color.FromRgb(32, 96, 160) }.CreateVideoEffect(context);
        processor.SetInput(source.Bitmap);

        var rendering = RenderFrame(context, processor, 0);

        Assert.True(HasInkOutside(rendering, source));
        Assert.All(rendering.Coordinates().Where(point => !source.Contains(point.X, point.Y) || source[point.X, point.Y].Alpha == 0), point =>
        {
            var pixel = rendering[point.X, point.Y];
            Assert.True(Tinted(pixel.Red, pixel.Alpha, 32) && Tinted(pixel.Green, pixel.Alpha, 96) && Tinted(pixel.Blue, pixel.Alpha, 160), $"({point.X}, {point.Y}) {pixel}");
        });
    }

    [Theory]
    [InlineData(100, 50)]
    [InlineData(-37, 21)]
    public void TheInkTravelsWithTheImage(int dx, int dy)
    {
        using var devices = new GraphicsDevices();
        using var context = devices.CreateContext();
        RequireInterop(context);
        using var source = new SourceImage(context, Size, Size, CenteredSquare);
        using var moved = new AffineTransform2D(context.DeviceContext)
        {
            InterPolationMode = AffineTransform2DInterpolationMode.NearestNeighbor,
            BorderMode = BorderMode.Hard,
            TransformMatrix = Matrix3x2.CreateTranslation(dx, dy),
        };
        moved.SetInput(0, source.Bitmap, true);
        using var movedOutput = moved.Output;
        using var inPlaceProcessor = new InkDispersionEffect().CreateVideoEffect(context);
        inPlaceProcessor.SetInput(source.Bitmap);
        var inPlace = RenderFrame(context, inPlaceProcessor, 0);
        using var travelledProcessor = new InkDispersionEffect().CreateVideoEffect(context);
        travelledProcessor.SetInput(movedOutput);

        var travelled = RenderFrame(context, travelledProcessor, 0);

        Assert.Equal((inPlace.Left + dx, inPlace.Top + dy, inPlace.Width, inPlace.Height), (travelled.Left, travelled.Top, travelled.Width, travelled.Height));
        Assert.All(inPlace.Coordinates(), point => Assert.True(inPlace[point.X, point.Y] == travelled[point.X + dx, point.Y + dy], $"({point.X}, {point.Y})"));
    }

    [Fact]
    public void TheReachIsMeasuredAgainstTheLongSideOfTheImage()
    {
        using var devices = new GraphicsDevices();
        using var context = devices.CreateContext();
        RequireInterop(context);
        using var source = SourceImage.Solid(context, 96, 24, Gray);
        var effect = new InkDispersionEffect();
        effect.Water.Values[0].Value = 100d;
        effect.Reach.Values[0].Value = 25d;
        using var processor = effect.CreateVideoEffect(context);
        processor.SetInput(source.Bitmap);

        var rendering = RenderFrame(context, processor, 0);

        var distances = rendering.Coordinates()
            .Where(point => !source.Contains(point.X, point.Y) && rendering[point.X, point.Y].Alpha > 0)
            .Select(point => DistanceFrom(source, point.X, point.Y))
            .ToArray();
        Assert.Contains(distances, distance => distance > 12d);
        Assert.All(distances, distance => Assert.True(distance <= 30d, $"{distance}"));
    }

    public static readonly TheoryData<string, Action<InkDispersionEffect>> PassThroughSettings = new()
    {
        { nameof(InkDispersionEffect.Amount), effect => effect.Amount.Values[0].Value = 0d },
        { nameof(InkDispersionEffect.Spread), effect => effect.Spread.Values[0].Value = 0d },
    };

    [Theory]
    [MemberData(nameof(PassThroughSettings))]
    public void AZeroAmountOrSpreadPassesTheImageThrough(string setting, Action<InkDispersionEffect> configure)
    {
        using var devices = new GraphicsDevices();
        using var context = devices.CreateContext();
        using var source = new SourceImage(context, Size, Size, CenteredSquare);
        var effect = new InkDispersionEffect();
        configure(effect);
        using var processor = effect.CreateVideoEffect(context);
        processor.SetInput(source.Bitmap);

        var rendering = RenderFrame(context, processor, 0);

        Assert.Equal((0, 0, Size, Size), (rendering.Left, rendering.Top, rendering.Width, rendering.Height));
        Assert.All(rendering.Coordinates(), point => Assert.True(source[point.X, point.Y] == rendering[point.X, point.Y], $"{setting} ({point.X}, {point.Y})"));
    }

    [Fact]
    public void ReturningToAFrameReproducesItExactly()
    {
        using var devices = new GraphicsDevices();
        using var context = devices.CreateContext();
        RequireInterop(context);
        using var source = new SourceImage(context, Size, Size, CenteredSquare);
        using var processor = new InkDispersionEffect().CreateVideoEffect(context);
        processor.SetInput(source.Bitmap);

        var first = RenderFrame(context, processor, 4);
        RenderFrame(context, processor, 9);
        var again = RenderFrame(context, processor, 4);

        Assert.True(first.SamePixelsAs(again));
    }

    [Fact]
    public void ANewProcessorDrawsTheSameInk()
    {
        using var devices = new GraphicsDevices();
        using var context = devices.CreateContext();
        RequireInterop(context);
        using var source = new SourceImage(context, Size, Size, CenteredSquare);
        var effect = new InkDispersionEffect { Seed = 9 };
        using var firstProcessor = effect.CreateVideoEffect(context);
        firstProcessor.SetInput(source.Bitmap);
        var first = RenderFrame(context, firstProcessor, 0);
        using var secondProcessor = effect.CreateVideoEffect(context);
        secondProcessor.SetInput(source.Bitmap);

        var second = RenderFrame(context, secondProcessor, 0);

        Assert.True(first.SamePixelsAs(second));
    }

    [Fact]
    public void AnimatedSpreadIsReadAtEachFrame()
    {
        using var devices = new GraphicsDevices();
        using var context = devices.CreateContext();
        RequireInterop(context);
        using var source = new SourceImage(context, Size, Size, CenteredSquare);
        var effect = new InkDispersionEffect();
        effect.Spread.CopyFrom(Linear(0d, 100d));
        using var processor = effect.CreateVideoEffect(context);
        processor.SetInput(source.Bitmap);

        var start = RenderFrame(context, processor, 0);
        var end = RenderFrame(context, processor, Length - 1);

        AssertSameAsSource(start, source);
        Assert.True(HasInkOutside(end, source));
    }

    [Theory]
    [InlineData(100d, 30d)]
    [InlineData(30d, 100d)]
    public void AProcessorThatDrewAnotherSpreadDrawsLikeAFreshOne(double firstSpread, double secondSpread)
    {
        using var devices = new GraphicsDevices();
        using var context = devices.CreateContext();
        RequireInterop(context);
        using var source = new SourceImage(context, Size, Size, CenteredSquare);
        var effect = new InkDispersionEffect();
        effect.Spread.Values[0].Value = firstSpread;
        using var processor = effect.CreateVideoEffect(context);
        processor.SetInput(source.Bitmap);
        RenderFrame(context, processor, 0);
        effect.Spread.Values[0].Value = secondSpread;
        using var fresh = effect.CreateVideoEffect(context);
        fresh.SetInput(source.Bitmap);
        var expected = RenderFrame(context, fresh, 0);

        var reused = RenderFrame(context, processor, 0);

        Assert.True(reused.SamePixelsAs(expected));
    }

    public static readonly TheoryData<string, Action<InkDispersionEffect>> LaterChanges = new()
    {
        { nameof(InkDispersionEffect.Amount), effect => effect.Amount.Values[0].Value = 50d },
        { nameof(InkDispersionEffect.Spread), effect => effect.Spread.Values[0].Value = 30d },
        { nameof(InkDispersionEffect.Quality), effect => effect.Quality = InkDispersionQuality.Balanced },
        { nameof(InkDispersionEffect.Water), effect => effect.Water.Values[0].Value = 100d },
        { nameof(InkDispersionEffect.Reach), effect => effect.Reach.Values[0].Value = 10d },
        { nameof(InkDispersionEffect.Fiber), effect => effect.Fiber.Values[0].Value = 100d },
        { nameof(InkDispersionEffect.Sizing), effect => effect.Sizing.Values[0].Value = 100d },
        { nameof(InkDispersionEffect.Viscosity), effect => effect.Viscosity.Values[0].Value = 100d },
        { nameof(InkDispersionEffect.Drying), effect => effect.Drying.Values[0].Value = 100d },
        { nameof(InkDispersionEffect.Seed), effect => effect.Seed = 1 },
        { nameof(InkDispersionEffect.Density), effect => effect.Density.Values[0].Value = 20d },
        { nameof(InkDispersionEffect.Grain), effect => effect.Grain.Values[0].Value = 100d },
        { nameof(InkDispersionEffect.InkColor), effect => effect.InkColor = Colors.Crimson },
    };

    [Theory]
    [MemberData(nameof(LaterChanges))]
    public void EverySettingChangedAfterTheFirstFrameReachesTheEffect(string setting, Action<InkDispersionEffect> change)
    {
        using var devices = new GraphicsDevices();
        using var context = devices.CreateContext();
        RequireInterop(context);
        using var source = new SourceImage(context, Size, Size, CenteredSquare);
        var effect = new InkDispersionEffect();
        using var processor = effect.CreateVideoEffect(context);
        processor.SetInput(source.Bitmap);

        var before = RenderFrame(context, processor, 0);
        change(effect);
        var after = RenderFrame(context, processor, 0);

        Assert.False(before.SamePixelsAs(after), setting);
    }

    [Theory]
    [InlineData(32, 128)]
    [InlineData(128, 32)]
    public void AResizedSourceKeepsProducingInk(int firstSize, int secondSize)
    {
        using var devices = new GraphicsDevices();
        using var context = devices.CreateContext();
        RequireInterop(context);
        using var first = new SourceImage(context, firstSize, firstSize, (x, y) => Gray);
        using var second = new SourceImage(context, secondSize, secondSize, (x, y) => Gray);
        using var processor = new InkDispersionEffect().CreateVideoEffect(context);
        processor.SetInput(first.Bitmap);
        var before = RenderFrame(context, processor, 0);

        processor.SetInput(second.Bitmap);
        var after = RenderFrame(context, processor, 0);

        Assert.True(HasInkOutside(before, first));
        Assert.True(HasInkOutside(after, second));
        Assert.Equal(secondSize > firstSize, after.Width > before.Width);
    }

    [Fact]
    public void InkAppearsOnceATransparentSourceTakesShape()
    {
        using var devices = new GraphicsDevices();
        using var context = devices.CreateContext();
        RequireInterop(context);
        using var empty = SourceImage.Solid(context, Size, Size, Bgra.Transparent);
        using var source = new SourceImage(context, Size, Size, CenteredSquare);
        using var processor = new InkDispersionEffect().CreateVideoEffect(context);
        processor.SetInput(empty.Bitmap);
        var before = RenderFrame(context, processor, 0);

        processor.SetInput(source.Bitmap);
        var after = RenderFrame(context, processor, 0);

        Assert.False(HasInkOutside(before, empty));
        Assert.True(HasInkOutside(after, source));
    }

    [Fact]
    public void AFailureWhileUpdatingIsNotSwallowed()
    {
        using var devices = new GraphicsDevices();
        using var context = devices.CreateContext();
        using var processor = new InkDispersionEffect().CreateVideoEffect(context);

        Assert.ThrowsAny<Exception>(() => processor.Update(null!));
    }
}
