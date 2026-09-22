using System.Runtime.InteropServices;
using ComputeWeave;
using ComputeWeave.Interop;
using Vortice;
using Vortice.Direct2D1;
using Vortice.DXGI;
using Vortice.Mathematics;
using YukkuriMovieMaker.Commons;
using PixelFormat = Vortice.DCommon.PixelFormat;

namespace InkDispersion.Tests;

public sealed class InkDispersionEffectTests
{
    private static double ValueAt(YukkuriMovieMaker.Commons.Animation animation) => animation.GetValue(0, 1, 30);

    private static InkDispersionPipeline.Parameters CreateParameters(
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

    [Theory]
    [InlineData(InkDispersionQuality.Balanced, 160, 720)]
    [InlineData(InkDispersionQuality.High, 224, 1080)]
    [InlineData(InkDispersionQuality.Ultra, 288, 1440)]
    public void QualitySettingsMatchSpecification(InkDispersionQuality quality, int resolution, int maxSteps)
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
    public void GridSizeCoversCanvas(int width, int height, int resolution)
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
    public void StepCountScalesWithReachAndRespectsCap(float reachCells, int maxSteps, int expected)
    {
        Assert.Equal(expected, InkDispersionSettings.GetStepCount(reachCells, maxSteps));
    }

    [Fact]
    public void ParameterMappingsAreMonotonicAndBounded()
    {
        Assert.Equal(InkDispersionSettings.MinimumDeposit, InkDispersionSettings.GetDeposit(0f), 5);
        Assert.Equal(InkDispersionSettings.MaximumDeposit, InkDispersionSettings.GetDeposit(1f), 5);
        Assert.Equal(InkDispersionSettings.MinimumDeposit, InkDispersionSettings.GetDeposit(-5f), 5);
        Assert.Equal(InkDispersionSettings.MaximumDeposit, InkDispersionSettings.GetDeposit(5f), 5);
        Assert.True(InkDispersionSettings.GetDeposit(0.75f) > InkDispersionSettings.GetDeposit(0.25f));

        Assert.Equal(InkDispersionSettings.MaximumOmega, InkDispersionSettings.GetOmega(0f), 5);
        Assert.Equal(InkDispersionSettings.MinimumOmega, InkDispersionSettings.GetOmega(1f), 5);
        Assert.True(InkDispersionSettings.GetOmega(0.25f) > InkDispersionSettings.GetOmega(0.75f));

        Assert.Equal(InkDispersionSettings.SigmaFloor, InkDispersionSettings.GetPinningSigma(0f), 6);
        Assert.Equal(InkDispersionSettings.SigmaFloor + InkDispersionSettings.MaximumSigma, InkDispersionSettings.GetPinningSigma(1f), 6);
        Assert.True(InkDispersionSettings.GetPinningSigma(0.75f) > InkDispersionSettings.GetPinningSigma(0.25f));

        Assert.Equal(0f, InkDispersionSettings.GetSurfaceEvaporation(0f), 7);
        Assert.Equal(InkDispersionSettings.MaximumSurfaceEvaporation, InkDispersionSettings.GetSurfaceEvaporation(1f), 7);
        Assert.Equal(0f, InkDispersionSettings.GetSurfaceEvaporation(-1f), 7);
        Assert.Equal(InkDispersionSettings.MaximumSurfaceEvaporation, InkDispersionSettings.GetSurfaceEvaporation(2f), 7);
    }

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
    public void LatticeOppositeDirectionsAreInvolutions(int direction, int expected)
    {
        Assert.Equal(expected, InkDispersionShaderMath.Opposite(direction));
        Assert.Equal(direction, InkDispersionShaderMath.Opposite(expected));
        Assert.Equal(-InkDispersionShaderMath.Ex(direction), InkDispersionShaderMath.Ex(expected));
        Assert.Equal(-InkDispersionShaderMath.Ey(direction), InkDispersionShaderMath.Ey(expected));
    }

    [Fact]
    public void LatticeWeightsSumToOne()
    {
        var sum = 0f;
        for (var i = 0; i < 9; i++)
            sum += InkDispersionShaderMath.Weight(i);

        Assert.Equal(1f, sum, 6);
    }

    [Fact]
    public void TransparentInputYieldsTransparentOutput()
    {
        using var pipeline = InkDispersionPipeline.TryCreate();
        if (pipeline is null)
        {
            Assert.Skip("Direct3D 12 is unavailable.");
            return;
        }

        const int width = 64;
        const int height = 64;
        var source = new int[width * height];
        var destination = new int[source.Length];
        Array.Fill(destination, -1);
        var parameters = CreateParameters();

        pipeline.Process(source, destination, width, height, in parameters);

        Assert.All(destination, pixel => Assert.Equal(0, pixel));
    }

    [Fact]
    public void SpreadZeroYieldsTransparentOutput()
    {
        using var pipeline = InkDispersionPipeline.TryCreate();
        if (pipeline is null)
        {
            Assert.Skip("Direct3D 12 is unavailable.");
            return;
        }

        const int width = 96;
        const int height = 96;
        var source = CreateSquareSource(width, height, 32, 32, 32, 32);
        var destination = new int[source.Length];
        Array.Fill(destination, -1);
        var parameters = CreateParameters(spread: 0f);

        pipeline.Process(source, destination, width, height, in parameters);

        Assert.All(destination, pixel => Assert.Equal(0, pixel));
    }

    [Fact]
    public void FullSpreadProducesInk()
    {
        using var pipeline = InkDispersionPipeline.TryCreate();
        if (pipeline is null)
        {
            Assert.Skip("Direct3D 12 is unavailable.");
            return;
        }

        const int width = 128;
        const int height = 128;
        var source = CreateSquareSource(width, height, 48, 48, 32, 32);
        var destination = new int[source.Length];
        var parameters = CreateParameters();

        pipeline.Process(source, destination, width, height, in parameters);

        Assert.True(CountLitPixels(destination) > 0);
    }

    [Fact]
    public void GpuPipelineIsDeterministic()
    {
        using var pipeline = InkDispersionPipeline.TryCreate();
        if (pipeline is null)
        {
            Assert.Skip("Direct3D 12 is unavailable.");
            return;
        }

        const int width = 128;
        const int height = 128;
        var source = CreateSquareSource(width, height, 48, 48, 32, 32);
        var first = new int[source.Length];
        var second = new int[source.Length];
        var parameters = CreateParameters(fiber: 0.8f, seed: 42);

        pipeline.Process(source, first, width, height, in parameters);
        pipeline.Process(source, second, width, height, in parameters);

        Assert.Equal(first, second);
    }

    [Fact]
    public void DifferentSeedsProduceDifferentInkMarks()
    {
        using var pipeline = InkDispersionPipeline.TryCreate();
        if (pipeline is null)
        {
            Assert.Skip("Direct3D 12 is unavailable.");
            return;
        }

        const int width = 128;
        const int height = 128;
        var source = CreateSquareSource(width, height, 48, 48, 32, 32);
        var first = new int[source.Length];
        var second = new int[source.Length];

        var parametersA = CreateParameters(fiber: 0.8f, seed: 1);
        var parametersB = CreateParameters(fiber: 0.8f, seed: 2);
        pipeline.Process(source, first, width, height, in parametersA);
        pipeline.Process(source, second, width, height, in parametersB);

        Assert.NotEqual(first, second);
    }

    [Fact]
    public void OutputAlphaStaysPremultipliedAndBounded()
    {
        using var pipeline = InkDispersionPipeline.TryCreate();
        if (pipeline is null)
        {
            Assert.Skip("Direct3D 12 is unavailable.");
            return;
        }

        const int width = 128;
        const int height = 128;
        var source = CreateSquareSource(width, height, 48, 48, 32, 32);
        var destination = new int[source.Length];
        var parameters = CreateParameters(water: 1f, density: 1f, grain: 1f);

        pipeline.Process(source, destination, width, height, in parameters);

        foreach (var pixel in destination)
        {
            var alpha = (pixel >> 24) & 255;
            Assert.InRange((pixel >> 16) & 255, 0, alpha);
            Assert.InRange((pixel >> 8) & 255, 0, alpha);
            Assert.InRange(pixel & 255, 0, alpha);
        }
    }

    [Fact]
    public void SpreadIncreasesLitArea()
    {
        using var pipeline = InkDispersionPipeline.TryCreate();
        if (pipeline is null)
        {
            Assert.Skip("Direct3D 12 is unavailable.");
            return;
        }

        const int width = 128;
        const int height = 128;
        var source = CreateSquareSource(width, height, 48, 48, 32, 32);
        var partial = new int[source.Length];
        var full = new int[source.Length];

        var partialParameters = CreateParameters(spread: 0.1f);
        var fullParameters = CreateParameters(spread: 1f);
        pipeline.Process(source, partial, width, height, in partialParameters);
        pipeline.Process(source, full, width, height, in fullParameters);

        Assert.True(CountLitPixels(full) > CountLitPixels(partial));
    }

    [Fact]
    public void ReachLimitsInkExtent()
    {
        using var pipeline = InkDispersionPipeline.TryCreate();
        if (pipeline is null)
        {
            Assert.Skip("Direct3D 12 is unavailable.");
            return;
        }

        const int width = 192;
        const int height = 192;
        var source = CreateSquareSource(width, height, 80, 80, 32, 32);
        var shortReach = new int[source.Length];
        var longReach = new int[source.Length];

        var shortParameters = CreateParameters(reachPixels: 12f, water: 1f);
        var longParameters = CreateParameters(reachPixels: 48f, water: 1f);
        pipeline.Process(source, shortReach, width, height, in shortParameters);
        pipeline.Process(source, longReach, width, height, in longParameters);

        var shortMaxY = MaxLitY(shortReach, width, height);
        var longMaxY = MaxLitY(longReach, width, height);
        Assert.True(shortMaxY > 0);
        Assert.True(longMaxY >= shortMaxY);
        Assert.True(shortMaxY <= 80 + 32 + 12 + 8);
    }

    [Fact]
    public void GpuPipelineDoesNotAllocateManagedMemoryAfterWarmup()
    {
        using var pipeline = InkDispersionPipeline.TryCreate();
        if (pipeline is null)
        {
            Assert.Skip("Direct3D 12 is unavailable.");
            return;
        }

        const int width = 64;
        const int height = 64;
        var source = CreateSquareSource(width, height, 24, 24, 16, 16);
        var destination = new int[source.Length];
        var parameters = CreateParameters();
        pipeline.Process(source, destination, width, height, in parameters);
        pipeline.Process(source, destination, width, height, in parameters);

        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
        var before = GC.GetAllocatedBytesForCurrentThread();
        pipeline.Process(source, destination, width, height, in parameters);
        var allocated = GC.GetAllocatedBytesForCurrentThread() - before;

        Assert.Equal(0, allocated);
    }

    [Fact]
    public void SharedTexturePipelineMatchesPackedBufferPipeline()
    {
        using var pipeline = InkDispersionPipeline.TryCreate();
        if (pipeline is null)
        {
            Assert.Skip("Direct3D 12 is unavailable.");
            return;
        }

        const int width = 96;
        const int height = 96;
        var source = CreateSquareSource(width, height, 32, 32, 32, 32);
        var expected = new int[source.Length];
        var parameters = CreateParameters(fiber: 0.7f, seed: 11);
        pipeline.Process(source, expected, width, height, in parameters);

        var device = GraphicsDevice.GetDefault();
        using var sourceTexture = InteropServices.AllocateSharedReadWriteTexture2D<Bgra32, Float4>(device, width, height);
        using var outputTexture = InteropServices.AllocateSharedReadWriteTexture2D<Bgra32, Float4>(device, width, height);
        var sourcePixels = new Bgra32[source.Length];
        for (var index = 0; index < source.Length; index++)
            sourcePixels[index].PackedValue = unchecked((uint)source[index]);
        sourceTexture.CopyFrom(sourcePixels);
        pipeline.ProcessSharedAndWait(sourceTexture, outputTexture, width, height, in parameters);
        var result = new Bgra32[source.Length];
        outputTexture.CopyTo(result);

        for (var index = 0; index < expected.Length; index++)
            Assert.Equal(unchecked((uint)expected[index]), result[index].PackedValue);
    }

    [Fact]
    public void SubmittedSharedTexturePipelineAllocationsAmortizeToZero()
    {
        using var pipeline = InkDispersionPipeline.TryCreate();
        if (pipeline is null)
        {
            Assert.Skip("Direct3D 12 is unavailable.");
            return;
        }

        const int width = 64;
        const int height = 64;
        var device = GraphicsDevice.GetDefault();
        using var source = InteropServices.AllocateSharedReadWriteTexture2D<Bgra32, Float4>(device, width, height);
        using var destination = InteropServices.AllocateSharedReadWriteTexture2D<Bgra32, Float4>(device, width, height);
        var parameters = CreateParameters();
        for (var iteration = 0; iteration < 4; iteration++)
            pipeline.Process(source, destination, width, height, in parameters);
        pipeline.WaitForCompletion();

        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
        var minimum = long.MaxValue;
        for (var iteration = 0; iteration < 16; iteration++)
        {
            var before = GC.GetAllocatedBytesForCurrentThread();
            pipeline.Process(source, destination, width, height, in parameters);
            minimum = Math.Min(minimum, GC.GetAllocatedBytesForCurrentThread() - before);
        }
        pipeline.WaitForCompletion();

        Assert.Equal(0, minimum);
    }

    [Theory]
    [InlineData(0.3f)]
    [InlineData(1f)]
    public void VisibleBoundsCoverAllLitPixelsAndMatchFullRender(float spread)
    {
        using var pipeline = InkDispersionPipeline.TryCreate();
        if (pipeline is null)
        {
            Assert.Skip("Direct3D 12 is unavailable.");
            return;
        }

        const int width = 192;
        const int height = 192;
        var source = CreateSquareSource(width, height, 80, 80, 32, 24);
        var full = new int[source.Length];
        var parameters = CreateParameters(spread: spread, reachPixels: 32f, seed: 5);
        pipeline.Process(source, full, width, height, in parameters);

        var device = GraphicsDevice.GetDefault();
        using var sourceTexture = device.AllocateReadWriteTexture2D<Bgra32, Float4>(width, height);
        var sourcePixels = new Bgra32[source.Length];
        for (var index = 0; index < source.Length; index++)
            sourcePixels[index].PackedValue = unchecked((uint)source[index]);
        sourceTexture.CopyFrom(sourcePixels);

        pipeline.Simulate(sourceTexture, width, height, 0, 0, width, height, in parameters);
        Assert.True(pipeline.TryGetVisibleBounds(width, height, in parameters, out var rect));
        Assert.True(rect.Width > 0 && rect.Height > 0);
        Assert.True(rect.X >= 0 && rect.Y >= 0);
        Assert.True(rect.X + rect.Width <= width && rect.Y + rect.Height <= height);

        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                if (full[y * width + x] == 0)
                    continue;
                Assert.InRange(x, rect.X, rect.X + rect.Width - 1);
                Assert.InRange(y, rect.Y, rect.Y + rect.Height - 1);
            }
        }

        using var outputTexture = device.AllocateReadWriteTexture2D<Bgra32, Float4>(rect.Width, rect.Height);
        pipeline.RenderVisible(outputTexture, width, height, rect, in parameters);
        var result = new Bgra32[rect.Width * rect.Height];
        outputTexture.CopyTo(result);

        for (var y = 0; y < rect.Height; y++)
        {
            for (var x = 0; x < rect.Width; x++)
            {
                var expected = unchecked((uint)full[(rect.Y + y) * width + rect.X + x]);
                Assert.Equal(expected, result[y * rect.Width + x].PackedValue);
            }
        }
    }

    [Fact]
    public void SimulateCachesStructureUntilInputsChange()
    {
        using var pipeline = InkDispersionPipeline.TryCreate();
        if (pipeline is null)
        {
            Assert.Skip("Direct3D 12 is unavailable.");
            return;
        }

        const int width = 128;
        const int height = 128;
        var source = CreateSquareSource(width, height, 48, 48, 32, 32);
        var device = GraphicsDevice.GetDefault();
        using var sourceTexture = device.AllocateReadWriteTexture2D<Bgra32, Float4>(width, height);
        var sourcePixels = new Bgra32[source.Length];
        for (var index = 0; index < source.Length; index++)
            sourcePixels[index].PackedValue = unchecked((uint)source[index]);
        sourceTexture.CopyFrom(sourcePixels);

        var parameters = CreateParameters(seed: 3);
        Assert.True(pipeline.Simulate(sourceTexture, width, height, 0, 0, width, height, in parameters));
        Assert.False(pipeline.Simulate(sourceTexture, width, height, 0, 0, width, height, in parameters));

        var spreadChanged = parameters with { Spread = 0.5f };
        Assert.False(pipeline.Simulate(sourceTexture, width, height, 0, 0, width, height, in spreadChanged));

        var densityChanged = parameters with { Density = 0.5f };
        Assert.False(pipeline.Simulate(sourceTexture, width, height, 0, 0, width, height, in densityChanged));

        var seedChanged = parameters with { Seed = 4 };
        Assert.True(pipeline.Simulate(sourceTexture, width, height, 0, 0, width, height, in seedChanged));

        var movedSource = CreateSquareSource(width, height, 32, 32, 32, 32);
        for (var index = 0; index < movedSource.Length; index++)
            sourcePixels[index].PackedValue = unchecked((uint)movedSource[index]);
        sourceTexture.CopyFrom(sourcePixels);
        Assert.True(pipeline.Simulate(sourceTexture, width, height, 0, 0, width, height, in seedChanged));
    }

    [Fact]
    public void Direct2DInteropProducesInkFromOpaqueCore()
    {
        using var devices = new GraphicsDevices();
        using var graphicsContext = devices.CreateContext();
        using var scheduler = ComputeExternalQueueScheduler.Create();
        using var provider = InkDispersionInteropProvider.TryCreate(graphicsContext, scheduler, out var interopDevice);
        if (provider is null || interopDevice is null)
        {
            Assert.Skip("Direct3D 11 and Direct3D 12 sharing is unavailable.");
            return;
        }

        using var domain = interopDevice.RegisterExternalDomain(provider);
        using var resourceSet = InkDispersionResourceSet.Create(interopDevice, domain);
        using var pipeline = InkDispersionPipeline.TryCreate(interopDevice);
        Assert.NotNull(pipeline);

        const int width = 96;
        const int height = 96;
        var pixels = CreateSquareSource(width, height, 32, 32, 32, 32);
        var handle = GCHandle.Alloc(pixels, GCHandleType.Pinned);
        using var inputBitmap = graphicsContext.DeviceContext.CreateBitmap(
            new SizeI(width, height),
            new BitmapProperties1(
                new PixelFormat(Format.B8G8R8A8_UNorm, Vortice.DCommon.AlphaMode.Premultiplied),
                96f,
                96f,
                BitmapOptions.None));
        try
        {
            inputBitmap.CopyFromMemory(handle.AddrOfPinnedObject(), width * sizeof(int));
        }
        finally
        {
            handle.Free();
        }

        Assert.True(resourceSet.TryEnsureSource(width, height, out _));
        var parameters = CreateParameters();
        var renderContext = provider.RenderContext;
        InkDispersionPipeline.PixelRect visible = default;
        for (var iteration = 0; iteration < 2; iteration++)
        {
            using (var borrow = resourceSet.BeginSourceExternalOperation())
            {
                var previousTarget = renderContext.Target;
                using var sourceBitmap = new ID2D1Bitmap1(borrow.DangerousGetView().AddRefBitmap());
                renderContext.Target = sourceBitmap;
                renderContext.BeginDraw();
                renderContext.Clear(null);
                renderContext.DrawImage(
                    inputBitmap,
                    new System.Numerics.Vector2(0f, 0f),
                    null,
                    InterpolationMode.NearestNeighbor,
                    CompositeMode.SourceCopy);
                renderContext.EndDraw();
                renderContext.Target = previousTarget;
            }

            pipeline!.Simulate(
                resourceSet.GetSourceComputeBinding(), width, height, 0, 0, width, height, in parameters);
            Assert.True(pipeline.TryGetVisibleBounds(width, height, in parameters, out visible));
            Assert.True(resourceSet.TryEnsureOutput(visible.Width, visible.Height, out _));
            pipeline.RenderVisible(
                resourceSet.GetOutputComputeBinding(), width, height, visible, in parameters);
        }

        using var outputLease = resourceSet.AcquireOutputExternalViewLease();
        Assert.Equal(visible.Width, outputLease.Width);
        Assert.Equal(visible.Height, outputLease.Height);
        using var staging = graphicsContext.DeviceContext.CreateBitmap(
            new SizeI(visible.Width, visible.Height),
            new BitmapProperties1(
                new PixelFormat(Format.B8G8R8A8_UNorm, Vortice.DCommon.AlphaMode.Premultiplied),
                96f,
                96f,
                BitmapOptions.CpuRead | BitmapOptions.CannotDraw));
        using var outputBitmap = new ID2D1Bitmap1(outputLease.DangerousGetView().AddRefBitmap());
        staging.CopyFromBitmap(outputBitmap);
        var mapped = staging.Map(MapOptions.Read);
        try
        {
            var lit = 0;
            for (var y = 0; y < visible.Height; y++)
            {
                for (var x = 0; x < visible.Width; x++)
                {
                    var actual = Marshal.ReadInt32(mapped.Bits + (nint)(y * mapped.Pitch + x * sizeof(int)));
                    var alpha = (actual >> 24) & 255;
                    Assert.InRange((actual >> 16) & 255, 0, alpha);
                    Assert.InRange((actual >> 8) & 255, 0, alpha);
                    Assert.InRange(actual & 255, 0, alpha);
                    if (alpha > 0)
                        lit++;
                }
            }
            Assert.True(lit > 0);
        }
        finally
        {
            staging.Unmap();
        }
    }

    private static int[] CreateSquareSource(int width, int height, int left, int top, int squareWidth, int squareHeight)
    {
        var source = new int[width * height];
        for (var y = top; y < top + squareHeight; y++)
        {
            for (var x = left; x < left + squareWidth; x++)
            {
                if (x < 0 || x >= width || y < 0 || y >= height)
                    continue;
                source[y * width + x] = unchecked((int)0xFFC0C0C0);
            }
        }
        return source;
    }

    private static int CountLitPixels(int[] pixels)
    {
        var count = 0;
        foreach (var pixel in pixels)
        {
            if (((pixel >> 24) & 255) > 8)
                count++;
        }
        return count;
    }

    private static int MaxLitY(int[] pixels, int width, int height)
    {
        for (var y = height - 1; y >= 0; y--)
        {
            for (var x = 0; x < width; x++)
            {
                if (((pixels[y * width + x] >> 24) & 255) >= 32)
                    return y;
            }
        }
        return 0;
    }
}
