using System.Runtime.InteropServices;
using ComputeWeave;

namespace InkDispersion;

internal sealed class InkDispersionPipeline : IDisposable
{
    private readonly GraphicsDevice _device;
    private readonly InkDispersionPipelineHost _host;
    private readonly ReadWriteBuffer<int> _scratch;
    private readonly ReadBackBuffer<int> _scratchReadBack;
    private readonly int[] _boundsMinX;
    private readonly int[] _boundsMinY;
    private readonly int[] _boundsMaxX;
    private readonly int[] _boundsMaxY;
    private ReadWriteBuffer<int>? _inkStep;
    private ReadBackBuffer<int>? _inkStepReadBack;
    private int[]? _cachedInkStep;
    private int _cachedMaxInkStep;
    private int _cachedInkCount;
    private StructureKey? _structureKey;
    private int _gridWidth;
    private int _gridHeight;
    private ReadWriteTexture2D<Bgra32, Float4>? _packedSource;
    private ReadWriteTexture2D<Bgra32, Float4>? _packedOutput;
    private int _packedWidth;
    private int _packedHeight;

    private InkDispersionPipeline(GraphicsDevice device, InkDispersionPipelineHost host)
    {
        _device = device;
        _host = host;
        _scratch = device.AllocateReadWriteBuffer<int>(InkDispersionSettings.ScratchLength);
        _scratchReadBack = device.AllocateReadBackBuffer<int>(InkDispersionSettings.ScratchLength);
        _boundsMinX = new int[InkDispersionSettings.MaximumStepCount + 1];
        _boundsMinY = new int[InkDispersionSettings.MaximumStepCount + 1];
        _boundsMaxX = new int[InkDispersionSettings.MaximumStepCount + 1];
        _boundsMaxY = new int[InkDispersionSettings.MaximumStepCount + 1];
    }

    public static InkDispersionPipeline? TryCreate()
    {
        try
        {
            return TryCreate(GraphicsDevice.GetDefault());
        }
        catch
        {
            return null;
        }
    }

    public static InkDispersionPipeline? TryCreate(GraphicsDevice device)
    {
        InkDispersionPipelineHost? host = null;
        try
        {
            host = InkDispersionPipelineHost.Create(device, InkDispersionSettings.MaximumPendingSubmissions);
            return new InkDispersionPipeline(device, host);
        }
        catch
        {
            host?.Dispose();
            host?.WaitForDisposal();
            return null;
        }
    }

    internal void WaitForCompletion()
    {
        _device.For(1, new FillIntShader(_scratch, 0, 0));
    }

    public void Process(ReadOnlySpan<int> source, Span<int> destination, int width, int height, in Parameters parameters)
    {
        var pixelCount = checked(width * height);
        EnsureGridFor(width, height, parameters.Quality);
        EnsurePackedTextures(width, height);
        var sourceTexture = _packedSource!;
        var outputTexture = _packedOutput!;
        sourceTexture.CopyFrom(MemoryMarshal.Cast<int, Bgra32>(source[..pixelCount]));
        SubmitFullPipeline(sourceTexture, outputTexture, width, height, in parameters).Wait();
        outputTexture.CopyTo(MemoryMarshal.Cast<int, Bgra32>(destination[..pixelCount]));
    }

    public void Process(
        ReadWriteTexture2D<Bgra32, Float4> source,
        ReadWriteTexture2D<Bgra32, Float4> destination,
        int width,
        int height,
        in Parameters parameters)
    {
        EnsureGridFor(width, height, parameters.Quality);
        _ = SubmitFullPipeline(source, destination, width, height, in parameters);
    }

    internal void ProcessSharedAndWait(
        ReadWriteTexture2D<Bgra32, Float4> source,
        ReadWriteTexture2D<Bgra32, Float4> destination,
        int width,
        int height,
        in Parameters parameters)
    {
        EnsureGridFor(width, height, parameters.Quality);
        SubmitFullPipeline(source, destination, width, height, in parameters).Wait();
    }

    internal bool Simulate(
        ReadWriteTexture2D<Bgra32, Float4> source,
        int canvasWidth,
        int canvasHeight,
        int sourceOffsetX,
        int sourceOffsetY,
        int sourceWidth,
        int sourceHeight,
        in Parameters parameters)
    {
        var derived = BeginSimulate(canvasWidth, canvasHeight, in parameters);
        _host.RecordSilhouetteAndMaskHash(
            source, _scratch, sourceOffsetX, sourceOffsetY, sourceWidth, sourceHeight, _gridWidth, _gridHeight, in derived).Wait();
        return CompleteSimulate(canvasWidth, canvasHeight, in parameters, in derived);
    }

    internal bool Simulate(
        ComputeResourceBinding<ReadWriteTexture2D<Bgra32, Float4>> source,
        int canvasWidth,
        int canvasHeight,
        int sourceOffsetX,
        int sourceOffsetY,
        int sourceWidth,
        int sourceHeight,
        in Parameters parameters)
    {
        var derived = BeginSimulate(canvasWidth, canvasHeight, in parameters);
        _host.RecordSharedSilhouetteAndMaskHash(
            source, _scratch, sourceOffsetX, sourceOffsetY, sourceWidth, sourceHeight, _gridWidth, _gridHeight, in derived).Wait();
        return CompleteSimulate(canvasWidth, canvasHeight, in parameters, in derived);
    }

    private DerivedValues BeginSimulate(int canvasWidth, int canvasHeight, in Parameters parameters)
    {
        EnsureGridFor(canvasWidth, canvasHeight, parameters.Quality);
        return Derive(canvasWidth, canvasHeight, in parameters);
    }

    private bool CompleteSimulate(int canvasWidth, int canvasHeight, in Parameters parameters, in DerivedValues derived)
    {
        _scratchReadBack.CopyFrom(_scratch);
        var hashed = _scratchReadBack.Span;
        var key = new StructureKey(
            hashed[6],
            hashed[7],
            canvasWidth,
            canvasHeight,
            parameters.Quality,
            parameters.Seed,
            parameters.ReachPixels,
            parameters.Water,
            parameters.Fiber,
            parameters.Sizing,
            parameters.Viscosity,
            parameters.Drying);
        if (_structureKey == key)
            return false;

        _host.RecordFlow(_scratch, _inkStep!, _gridWidth, _gridHeight, in derived, in parameters).Wait();
        _scratchReadBack.CopyFrom(_scratch);
        var wetStepReadBack = _inkStepReadBack!;
        wetStepReadBack.CopyFrom(_inkStep!);
        var scratch = _scratchReadBack.Span;
        _cachedInkCount = scratch[0];
        _cachedMaxInkStep = scratch[1];
        wetStepReadBack.Span.CopyTo(_cachedInkStep!);
        BuildBoundsPrefix();
        _structureKey = key;
        return true;
    }

    internal bool TryGetVisibleBounds(int canvasWidth, int canvasHeight, in Parameters parameters, out PixelRect rect)
    {
        rect = default;
        if (_cachedInkCount <= 0 || _cachedMaxInkStep < 0)
            return false;
        var visible = parameters.Spread * (_cachedMaxInkStep + 1);
        if (visible <= 0f)
            return false;

        var derived = Derive(canvasWidth, canvasHeight, in parameters);
        var lastStep = Math.Min((int)MathF.Ceiling(visible) - 1, _cachedMaxInkStep);
        if (lastStep < 0 || _boundsMinX[lastStep] == int.MaxValue)
            return false;

        var cellSize = derived.CellSize;
        var padding = (int)MathF.Ceiling(cellSize) + 4;
        var left = Math.Clamp(((int)(_boundsMinX[lastStep] * cellSize) - padding) & ~3, 0, canvasWidth);
        var top = Math.Clamp(((int)(_boundsMinY[lastStep] * cellSize) - padding) & ~3, 0, canvasHeight);
        var right = Math.Clamp((int)MathF.Ceiling((_boundsMaxX[lastStep] + 1) * cellSize) + padding, 0, canvasWidth);
        var bottom = Math.Clamp((int)MathF.Ceiling((_boundsMaxY[lastStep] + 1) * cellSize) + padding, 0, canvasHeight);
        var width = Math.Min((right - left + 3) & ~3, canvasWidth - left);
        var height = Math.Min((bottom - top + 3) & ~3, canvasHeight - top);
        if (width <= 0 || height <= 0)
            return false;

        rect = new PixelRect(left, top, width, height);
        return true;
    }

    internal void RenderVisible(
        ReadWriteTexture2D<Bgra32, Float4> output,
        int canvasWidth,
        int canvasHeight,
        PixelRect rect,
        in Parameters parameters)
    {
        var derived = Derive(canvasWidth, canvasHeight, in parameters);
        _host.RecordRender(output, _scratch, _inkStep!, in rect, _gridWidth, _gridHeight, in derived, in parameters).Wait();
    }

    internal void RenderVisible(
        ComputeResourceBinding<ReadWriteTexture2D<Bgra32, Float4>> output,
        int canvasWidth,
        int canvasHeight,
        PixelRect rect,
        in Parameters parameters)
    {
        var derived = Derive(canvasWidth, canvasHeight, in parameters);
        _host.RecordSharedRender(output, _scratch, _inkStep!, in rect, _gridWidth, _gridHeight, in derived, in parameters).Wait();
    }

    private ComputeSubmission SubmitFullPipeline(
        ReadWriteTexture2D<Bgra32, Float4> source,
        ReadWriteTexture2D<Bgra32, Float4> output,
        int width,
        int height,
        in Parameters parameters)
    {
        _structureKey = null;
        var derived = Derive(width, height, in parameters);
        return _host.RecordFullPipeline(source, output, _scratch, _inkStep!, width, height, _gridWidth, _gridHeight, in derived, in parameters);
    }

    private void BuildBoundsPrefix()
    {
        var maxWetStep = Math.Clamp(_cachedMaxInkStep, -1, InkDispersionSettings.MaximumStepCount);
        _cachedMaxInkStep = maxWetStep;
        if (maxWetStep < 0)
            return;
        for (var step = 0; step <= maxWetStep; step++)
        {
            _boundsMinX[step] = int.MaxValue;
            _boundsMinY[step] = int.MaxValue;
            _boundsMaxX[step] = int.MinValue;
            _boundsMaxY[step] = int.MinValue;
        }

        var wetStep = _cachedInkStep!;
        var gridWidth = _gridWidth;
        for (var index = 0; index < wetStep.Length; index++)
        {
            var step = wetStep[index];
            if (step < 0 || step > maxWetStep)
                continue;
            var x = index % gridWidth;
            var y = index / gridWidth;
            if (x < _boundsMinX[step])
                _boundsMinX[step] = x;
            if (x > _boundsMaxX[step])
                _boundsMaxX[step] = x;
            if (y < _boundsMinY[step])
                _boundsMinY[step] = y;
            if (y > _boundsMaxY[step])
                _boundsMaxY[step] = y;
        }

        for (var step = 1; step <= maxWetStep; step++)
        {
            _boundsMinX[step] = Math.Min(_boundsMinX[step], _boundsMinX[step - 1]);
            _boundsMinY[step] = Math.Min(_boundsMinY[step], _boundsMinY[step - 1]);
            _boundsMaxX[step] = Math.Max(_boundsMaxX[step], _boundsMaxX[step - 1]);
            _boundsMaxY[step] = Math.Max(_boundsMaxY[step], _boundsMaxY[step - 1]);
        }
    }

    private DerivedValues Derive(int width, int height, in Parameters parameters)
    {
        var settings = InkDispersionSettings.GetQuality(parameters.Quality);
        var (_, _, cellSize) = InkDispersionSettings.GetGridSize(width, height, settings.GridResolution);
        var reachPixels = Math.Max(parameters.ReachPixels, cellSize);
        var reachCells = reachPixels / cellSize;
        return new DerivedValues(
            cellSize,
            InkDispersionSettings.GetStepCount(reachCells, settings.MaxSteps),
            InkDispersionSettings.GetDeposit(parameters.Water),
            Math.Clamp(parameters.Fiber, 0f, 1f),
            Math.Clamp(parameters.Sizing, 0f, 1f),
            Math.Clamp(parameters.Viscosity, 0f, 1f),
            InkDispersionSettings.GetOmega(parameters.Viscosity),
            InkDispersionSettings.GetPinningSigma(parameters.Sizing),
            InkDispersionSettings.GetSurfaceEvaporation(parameters.Drying),
            reachPixels);
    }

    private void EnsureGridFor(int width, int height, InkDispersionQuality quality)
    {
        var settings = InkDispersionSettings.GetQuality(quality);
        var (gridWidth, gridHeight, _) = InkDispersionSettings.GetGridSize(width, height, settings.GridResolution);
        EnsureGrid(gridWidth, gridHeight);
    }

    private void EnsureGrid(int gridWidth, int gridHeight)
    {
        if (_gridWidth == gridWidth && _gridHeight == gridHeight)
            return;

        DisposeGridBuffers();
        var gridLength = gridWidth * gridHeight;
        var distributionLength = gridLength * 9;
        if (!_host.TryEnsureGrid(
                new InkDispersionGridResources.Plan(
                    alumFieldLength: gridLength,
                    densityALength: gridLength,
                    densityBLength: gridLength,
                    depositLength: gridLength,
                    distributionsALength: distributionLength,
                    distributionsBLength: distributionLength,
                    fiberFieldLength: gridLength,
                    fixedPigmentLength: gridLength,
                    flowPigmentALength: gridLength,
                    flowPigmentBLength: gridLength,
                    jumpFloodALength: gridLength,
                    jumpFloodBLength: gridLength,
                    kappaLength: gridLength,
                    pinFieldLength: gridLength,
                    reachMaskLength: gridLength,
                    surfaceLength: gridLength,
                    velocityLength: gridLength,
                    wetnessLength: gridLength),
                out _))
            throw new InvalidOperationException();
        _inkStep = _device.AllocateReadWriteBuffer<int>(gridLength);
        _inkStepReadBack = _device.AllocateReadBackBuffer<int>(gridLength);
        _cachedInkStep = new int[gridLength];
        _cachedMaxInkStep = -1;
        _cachedInkCount = 0;
        _gridWidth = gridWidth;
        _gridHeight = gridHeight;
    }

    private void EnsurePackedTextures(int width, int height)
    {
        if (_packedWidth == width && _packedHeight == height)
            return;

        _packedSource?.Dispose();
        _packedOutput?.Dispose();
        _packedSource = _device.AllocateReadWriteTexture2D<Bgra32, Float4>(width, height);
        _packedOutput = _device.AllocateReadWriteTexture2D<Bgra32, Float4>(width, height);
        _packedWidth = width;
        _packedHeight = height;
    }

    private void DisposeGridBuffers()
    {
        _inkStep?.Dispose();
        _inkStepReadBack?.Dispose();
        _inkStep = null;
        _inkStepReadBack = null;
        _cachedInkStep = null;
        _cachedMaxInkStep = -1;
        _cachedInkCount = 0;
        _structureKey = null;
        _gridWidth = 0;
        _gridHeight = 0;
    }

    public void Dispose()
    {
        _host.Dispose();
        _host.WaitForDisposal();
        DisposeGridBuffers();
        _packedSource?.Dispose();
        _packedOutput?.Dispose();
        _packedSource = null;
        _packedOutput = null;
        _packedWidth = 0;
        _packedHeight = 0;
        _scratchReadBack.Dispose();
        _scratch.Dispose();
    }

    internal readonly record struct PixelRect(int X, int Y, int Width, int Height);

    private readonly record struct StructureKey(
        int MaskHashSum,
        int MaskHashMix,
        int CanvasWidth,
        int CanvasHeight,
        InkDispersionQuality Quality,
        int Seed,
        float ReachPixels,
        float Water,
        float Fiber,
        float Sizing,
        float Viscosity,
        float Drying);

    internal readonly record struct DerivedValues(
        float CellSize,
        int Steps,
        float Deposit,
        float Fiber,
        float Sizing,
        float Viscosity,
        float Omega,
        float Sigma,
        float SurfaceEvaporation,
        float ReachPixels);

    internal readonly record struct Parameters(
        InkDispersionQuality Quality,
        float Spread,
        float Water,
        float Fiber,
        float Sizing,
        float Viscosity,
        float Drying,
        float ReachPixels,
        float Density,
        float Grain,
        float ColorR,
        float ColorG,
        float ColorB,
        int Seed);
}
