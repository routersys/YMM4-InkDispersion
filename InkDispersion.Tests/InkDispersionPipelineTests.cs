using ComputeWeave;
using ComputeWeave.Interop;

namespace InkDispersion.Tests;

[Collection("Direct3D12")]
public sealed class InkDispersionPipelineTests
{
    const int Opaque = unchecked((int)0xFFC0C0C0);

    static InkDispersionPipeline CreatePipeline()
    {
        var pipeline = InkDispersionPipeline.TryCreate();
        if (pipeline is null)
            Assert.Skip("Direct3D 12 is unavailable.");
        return pipeline;
    }

    static InkDispersionPipeline.Parameters Parameters(
        InkDispersionQuality quality = InkDispersionQuality.Balanced,
        float spread = 1f,
        float water = 0.6f,
        float fiber = 0.5f,
        float sizing = 0.3f,
        float viscosity = 0.3f,
        float drying = 0.4f,
        float reachPixels = 40f,
        float density = 0.8f,
        float grain = 0.4f,
        int seed = 0)
        => new(quality, spread, water, fiber, sizing, viscosity, drying, reachPixels, density, grain, 0.12f, 0.1f, 0.09f, seed);

    static int[] Square(int width, int height, int left, int top, int squareWidth, int squareHeight)
    {
        var pixels = new int[width * height];
        for (var y = Math.Max(top, 0); y < Math.Min(top + squareHeight, height); y++)
        {
            for (var x = Math.Max(left, 0); x < Math.Min(left + squareWidth, width); x++)
                pixels[y * width + x] = Opaque;
        }

        return pixels;
    }

    static int Alpha(int pixel) => (pixel >> 24) & 255;

    static int LitPixels(int[] pixels) => pixels.Count(pixel => Alpha(pixel) > 8);

    static int LowestLitRow(int[] pixels, int width)
    {
        for (var index = pixels.Length - 1; index >= 0; index--)
        {
            if (Alpha(pixels[index]) >= 32)
                return index / width;
        }

        return 0;
    }

    static void Upload(ReadWriteTexture2D<Bgra32, Float4> texture, int[] pixels)
        => texture.CopyFrom(pixels.Select(pixel => new Bgra32 { PackedValue = unchecked((uint)pixel) }).ToArray());

    static int[] Render(InkDispersionPipeline pipeline, int[] source, int width, int height, InkDispersionPipeline.Parameters parameters)
    {
        var destination = new int[source.Length];
        pipeline.Process(source, destination, width, height, in parameters);
        return destination;
    }

    [Fact]
    public void ATransparentSourceProducesNoInk()
    {
        using var pipeline = CreatePipeline();
        var destination = Enumerable.Repeat(-1, 64 * 64).ToArray();
        var parameters = Parameters();

        pipeline.Process(new int[64 * 64], destination, 64, 64, in parameters);

        Assert.All(destination, pixel => Assert.Equal(0, pixel));
    }

    [Fact]
    public void AZeroSpreadProducesNoInk()
    {
        using var pipeline = CreatePipeline();
        var source = Square(96, 96, 32, 32, 32, 32);
        var destination = Enumerable.Repeat(-1, source.Length).ToArray();
        var parameters = Parameters(spread: 0f);

        pipeline.Process(source, destination, 96, 96, in parameters);

        Assert.All(destination, pixel => Assert.Equal(0, pixel));
    }

    [Fact]
    public void AFullSpreadProducesInk()
    {
        using var pipeline = CreatePipeline();

        var rendering = Render(pipeline, Square(128, 128, 48, 48, 32, 32), 128, 128, Parameters());

        Assert.True(LitPixels(rendering) > 0);
    }

    [Fact]
    public void TheSameSettingsAlwaysProduceTheSameInk()
    {
        using var pipeline = CreatePipeline();
        var source = Square(128, 128, 48, 48, 32, 32);

        var first = Render(pipeline, source, 128, 128, Parameters(fiber: 0.8f, seed: 42));
        var second = Render(pipeline, source, 128, 128, Parameters(fiber: 0.8f, seed: 42));

        Assert.Equal(first, second);
    }

    [Fact]
    public void ADifferentSeedSpreadsTheInkDifferently()
    {
        using var pipeline = CreatePipeline();
        var source = Square(128, 128, 48, 48, 32, 32);

        var first = Render(pipeline, source, 128, 128, Parameters(fiber: 0.8f, seed: 1));
        var second = Render(pipeline, source, 128, 128, Parameters(fiber: 0.8f, seed: 2));

        Assert.NotEqual(first, second);
    }

    [Fact]
    public void TheInkStaysPremultiplied()
    {
        using var pipeline = CreatePipeline();

        var rendering = Render(pipeline, Square(128, 128, 48, 48, 32, 32), 128, 128, Parameters(water: 1f, density: 1f, grain: 1f));

        Assert.All(rendering, pixel =>
        {
            Assert.InRange((pixel >> 16) & 255, 0, Alpha(pixel));
            Assert.InRange((pixel >> 8) & 255, 0, Alpha(pixel));
            Assert.InRange(pixel & 255, 0, Alpha(pixel));
        });
    }

    [Fact]
    public void AWiderSpreadCoversMorePixels()
    {
        using var pipeline = CreatePipeline();
        var source = Square(128, 128, 48, 48, 32, 32);

        var partial = Render(pipeline, source, 128, 128, Parameters(spread: 0.1f));
        var full = Render(pipeline, source, 128, 128, Parameters(spread: 1f));

        Assert.True(LitPixels(full) > LitPixels(partial));
    }

    [Fact]
    public void TheReachLimitsHowFarTheInkTravels()
    {
        using var pipeline = CreatePipeline();
        var source = Square(192, 192, 80, 80, 32, 32);

        var shortReach = LowestLitRow(Render(pipeline, source, 192, 192, Parameters(reachPixels: 12f, water: 1f)), 192);
        var longReach = LowestLitRow(Render(pipeline, source, 192, 192, Parameters(reachPixels: 48f, water: 1f)), 192);

        Assert.True(shortReach > 0);
        Assert.True(longReach >= shortReach);
        Assert.True(shortReach <= 80 + 32 + 12 + 8);
    }

    [Fact]
    public void AWarmPipelineAllocatesNoManagedMemory()
    {
        using var pipeline = CreatePipeline();
        var source = Square(64, 64, 24, 24, 16, 16);
        var destination = new int[source.Length];
        var parameters = Parameters();
        for (var iteration = 0; iteration < 4; iteration++)
            pipeline.Process(source, destination, 64, 64, in parameters);
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();

        var minimum = long.MaxValue;
        for (var iteration = 0; iteration < 16; iteration++)
        {
            var before = GC.GetAllocatedBytesForCurrentThread();
            pipeline.Process(source, destination, 64, 64, in parameters);
            minimum = Math.Min(minimum, GC.GetAllocatedBytesForCurrentThread() - before);
        }

        Assert.Equal(0, minimum);
    }

    [Fact]
    public void SharedTexturesProduceTheSameInkAsPackedBuffers()
    {
        using var pipeline = CreatePipeline();
        var source = Square(96, 96, 32, 32, 32, 32);
        var parameters = Parameters(fiber: 0.7f, seed: 11);
        var expected = Render(pipeline, source, 96, 96, parameters);
        var device = GraphicsDevice.GetDefault();
        using var sourceTexture = InteropServices.AllocateSharedReadWriteTexture2D<Bgra32, Float4>(device, 96, 96);
        using var outputTexture = InteropServices.AllocateSharedReadWriteTexture2D<Bgra32, Float4>(device, 96, 96);
        Upload(sourceTexture, source);

        pipeline.ProcessSharedAndWait(sourceTexture, outputTexture, 96, 96, in parameters);
        var result = new Bgra32[source.Length];
        outputTexture.CopyTo(result);

        Assert.Equal(expected.Select(pixel => unchecked((uint)pixel)), result.Select(pixel => pixel.PackedValue));
    }

    [Fact]
    public void RepeatedSharedTextureSubmissionsAllocateNoManagedMemory()
    {
        using var pipeline = CreatePipeline();
        var device = GraphicsDevice.GetDefault();
        using var source = InteropServices.AllocateSharedReadWriteTexture2D<Bgra32, Float4>(device, 64, 64);
        using var destination = InteropServices.AllocateSharedReadWriteTexture2D<Bgra32, Float4>(device, 64, 64);
        var parameters = Parameters();
        for (var iteration = 0; iteration < 4; iteration++)
            pipeline.Process(source, destination, 64, 64, in parameters);
        pipeline.WaitForCompletion();
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();

        var minimum = long.MaxValue;
        for (var iteration = 0; iteration < 16; iteration++)
        {
            var before = GC.GetAllocatedBytesForCurrentThread();
            pipeline.Process(source, destination, 64, 64, in parameters);
            minimum = Math.Min(minimum, GC.GetAllocatedBytesForCurrentThread() - before);
        }
        pipeline.WaitForCompletion();

        Assert.Equal(0, minimum);
    }

    [Theory]
    [InlineData(0.3f)]
    [InlineData(1f)]
    public void TheVisibleBoundsHoldEveryInkedPixelAndRenderTheSameInk(float spread)
    {
        using var pipeline = CreatePipeline();
        var source = Square(192, 192, 80, 80, 32, 24);
        var parameters = Parameters(spread: spread, reachPixels: 32f, seed: 5);
        var full = Render(pipeline, source, 192, 192, parameters);
        var device = GraphicsDevice.GetDefault();
        using var sourceTexture = device.AllocateReadWriteTexture2D<Bgra32, Float4>(192, 192);
        Upload(sourceTexture, source);

        pipeline.Simulate(sourceTexture, 192, 192, 0, 0, 192, 192, in parameters);
        Assert.True(pipeline.TryGetVisibleBounds(192, 192, in parameters, out var rect));
        using var outputTexture = device.AllocateReadWriteTexture2D<Bgra32, Float4>(rect.Width, rect.Height);
        pipeline.RenderVisible(outputTexture, 192, 192, rect, in parameters);
        var visible = new Bgra32[rect.Width * rect.Height];
        outputTexture.CopyTo(visible);

        Assert.True(rect is { Width: > 0, Height: > 0, X: >= 0, Y: >= 0 });
        Assert.True(rect.X + rect.Width <= 192 && rect.Y + rect.Height <= 192);
        for (var y = 0; y < 192; y++)
        {
            for (var x = 0; x < 192; x++)
            {
                var inside = x >= rect.X && x < rect.X + rect.Width && y >= rect.Y && y < rect.Y + rect.Height;
                if (!inside)
                    Assert.Equal(0, full[y * 192 + x]);
                else
                    Assert.Equal(unchecked((uint)full[y * 192 + x]), visible[(y - rect.Y) * rect.Width + x - rect.X].PackedValue);
            }
        }
    }

    [Fact]
    public void TheFlowIsSimulatedAgainOnlyWhenTheShapeOrTheFlowSettingsChange()
    {
        using var pipeline = CreatePipeline();
        var device = GraphicsDevice.GetDefault();
        using var sourceTexture = device.AllocateReadWriteTexture2D<Bgra32, Float4>(128, 128);
        Upload(sourceTexture, Square(128, 128, 48, 48, 32, 32));
        var parameters = Parameters(seed: 3);

        Assert.True(pipeline.Simulate(sourceTexture, 128, 128, 0, 0, 128, 128, in parameters));
        Assert.False(pipeline.Simulate(sourceTexture, 128, 128, 0, 0, 128, 128, in parameters));

        var spreadChanged = parameters with { Spread = 0.5f };
        Assert.False(pipeline.Simulate(sourceTexture, 128, 128, 0, 0, 128, 128, in spreadChanged));

        var densityChanged = parameters with { Density = 0.5f };
        Assert.False(pipeline.Simulate(sourceTexture, 128, 128, 0, 0, 128, 128, in densityChanged));

        var seedChanged = parameters with { Seed = 4 };
        Assert.True(pipeline.Simulate(sourceTexture, 128, 128, 0, 0, 128, 128, in seedChanged));

        Upload(sourceTexture, Square(128, 128, 32, 32, 32, 32));
        Assert.True(pipeline.Simulate(sourceTexture, 128, 128, 0, 0, 128, 128, in seedChanged));
    }
}
