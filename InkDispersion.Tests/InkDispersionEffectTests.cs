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

    [Fact]
    public void Direct2DInteropProducesInkAfterGrowingFullHdOutput()
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
        const int fullHdWidth = 1920;
        const int fullHdHeight = 1080;
        using var inputBitmap = CreateInputBitmap(graphicsContext.DeviceContext, CreateSquareSource(width, height, 32, 32, 32, 32), width, height);

        Assert.True(resourceSet.TryEnsureSource(width, height, out _));
        var parameters = CreateParameters();
        InkDispersionPipeline.PixelRect visible = default;
        for (var iteration = 0; iteration < 2; iteration++)
        {
            DrawSource(resourceSet, provider.RenderContext, inputBitmap);
            pipeline!.Simulate(
                resourceSet.GetSourceComputeBinding(), width, height, 0, 0, width, height, in parameters);
            Assert.True(pipeline.TryGetVisibleBounds(width, height, in parameters, out visible));
            Assert.True(resourceSet.TryEnsureOutput(
                iteration == 0 ? visible.Width : fullHdWidth,
                iteration == 0 ? visible.Height : fullHdHeight,
                out _));
            pipeline.RenderVisible(
                resourceSet.GetOutputComputeBinding(), width, height, visible, in parameters);

            if (iteration == 0)
            {
                using var retiredLease = resourceSet.AcquireOutputExternalViewLease();
                Assert.Equal(visible.Width, retiredLease.Width);
                Assert.Equal(visible.Height, retiredLease.Height);
            }
        }

        using var outputLease = resourceSet.AcquireOutputExternalViewLease();
        Assert.Equal(fullHdWidth, outputLease.Width);
        Assert.Equal(fullHdHeight, outputLease.Height);
        Assert.True(CountLitOutput(graphicsContext.DeviceContext, outputLease, visible) > 0);
    }

    [Fact]
    public void Direct2DInteropProducesInkAfterReplacingSource()
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

        var parameters = CreateParameters();
        var previousVisible = default(InkDispersionPipeline.PixelRect);
        foreach (var (size, square) in new[] { (96, 32), (128, 64) })
        {
            using var inputBitmap = CreateInputBitmap(graphicsContext.DeviceContext, CreateSquareSource(size, size, (size - square) / 2, (size - square) / 2, square, square), size, size);
            Assert.True(resourceSet.TryEnsureSource(size, size, out var sourceChanged));
            Assert.True(sourceChanged);
            DrawSource(resourceSet, provider.RenderContext, inputBitmap);

            Assert.True(pipeline!.Simulate(
                resourceSet.GetSourceComputeBinding(), size, size, 0, 0, size, size, in parameters));
            Assert.True(pipeline.TryGetVisibleBounds(size, size, in parameters, out var visible));
            Assert.True(visible.Width > previousVisible.Width && visible.Height > previousVisible.Height);
            Assert.True(resourceSet.TryEnsureOutput(visible.Width, visible.Height, out _));
            pipeline.RenderVisible(
                resourceSet.GetOutputComputeBinding(), size, size, visible, in parameters);

            using var outputLease = resourceSet.AcquireOutputExternalViewLease();
            Assert.Equal(visible.Width, outputLease.Width);
            Assert.Equal(visible.Height, outputLease.Height);
            Assert.True(CountLitOutput(graphicsContext.DeviceContext, outputLease, visible) > square * square);
            previousVisible = visible;
        }
    }

    private static ID2D1Bitmap1 CreateInputBitmap(ID2D1DeviceContext6 deviceContext, int[] pixels, int width, int height)
    {
        var handle = GCHandle.Alloc(pixels, GCHandleType.Pinned);
        var inputBitmap = deviceContext.CreateBitmap(
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
        return inputBitmap;
    }

    private static void DrawSource(InkDispersionResourceSet resourceSet, ID2D1DeviceContext6 renderContext, ID2D1Bitmap1 inputBitmap)
    {
        using var borrow = resourceSet.BeginSourceExternalOperation();
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

    private static int CountLitOutput(ID2D1DeviceContext6 deviceContext, ExternalTextureLease<ExternalDirect3D11TextureView> outputLease, InkDispersionPipeline.PixelRect visible)
    {
        using var staging = deviceContext.CreateBitmap(
            new SizeI(visible.Width, visible.Height),
            new BitmapProperties1(
                new PixelFormat(Format.B8G8R8A8_UNorm, Vortice.DCommon.AlphaMode.Premultiplied),
                96f,
                96f,
                BitmapOptions.CpuRead | BitmapOptions.CannotDraw));
        using var outputBitmap = new ID2D1Bitmap1(outputLease.DangerousGetView().AddRefBitmap());
        staging.CopyFromBitmap(Vortice.Mathematics.Int2.Zero, outputBitmap, new RectI(0, 0, visible.Width, visible.Height));
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
            return lit;
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
}
