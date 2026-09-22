using System.Runtime.InteropServices;
using ComputeWeave;

namespace InkDispersion;

internal sealed class InkDispersionPipeline : IDisposable
{
    private readonly GraphicsDevice _device;
    private readonly ReadWriteBuffer<int> _scratch;
    private readonly ReadBackBuffer<int> _scratchReadBack;
    private readonly int[] _boundsMinX;
    private readonly int[] _boundsMinY;
    private readonly int[] _boundsMaxX;
    private readonly int[] _boundsMaxY;
    private ReadWriteBuffer<float>? _distributionsA;
    private ReadWriteBuffer<float>? _distributionsB;
    private ReadWriteBuffer<float>? _deposit;
    private ReadWriteBuffer<float>? _surface;
    private ReadWriteBuffer<float>? _flowPigmentA;
    private ReadWriteBuffer<float>? _flowPigmentB;
    private ReadWriteBuffer<float>? _fixedPigment;
    private ReadWriteBuffer<float>? _densityA;
    private ReadWriteBuffer<float>? _densityB;
    private ReadWriteBuffer<float>? _kappa;
    private ReadWriteBuffer<float>? _wetness;
    private ReadWriteBuffer<Float2>? _velocity;
    private ReadWriteBuffer<int>? _inkStep;
    private ReadWriteBuffer<int>? _reachMask;
    private ReadWriteBuffer<int>? _jumpFloodA;
    private ReadWriteBuffer<int>? _jumpFloodB;
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

    private InkDispersionPipeline(GraphicsDevice device)
    {
        _device = device;
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
            return new InkDispersionPipeline(GraphicsDevice.GetDefault());
        }
        catch
        {
            return null;
        }
    }

    public static InkDispersionPipeline? TryCreate(GraphicsDevice device)
    {
        try
        {
            return new InkDispersionPipeline(device);
        }
        catch
        {
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
        using (ComputeContext context = _device.CreateComputeContext())
            RecordFullPipeline(in context, sourceTexture, outputTexture, width, height, in parameters);
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
        using ComputeContext context = _device.CreateComputeContext();
        RecordFullPipeline(in context, source, destination, width, height, in parameters);
        context.Submit();
    }

    internal void ProcessSharedAndWait(
        ReadWriteTexture2D<Bgra32, Float4> source,
        ReadWriteTexture2D<Bgra32, Float4> destination,
        int width,
        int height,
        in Parameters parameters)
    {
        EnsureGridFor(width, height, parameters.Quality);
        using ComputeContext context = _device.CreateComputeContext();
        RecordFullPipeline(in context, source, destination, width, height, in parameters);
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
        EnsureGridFor(canvasWidth, canvasHeight, parameters.Quality);
        var derived = Derive(canvasWidth, canvasHeight, in parameters);
        using (ComputeContext context = _device.CreateComputeContext())
        {
            RecordSilhouetteStage(in context, source, sourceOffsetX, sourceOffsetY, sourceWidth, sourceHeight, in derived);
            RecordMaskHashStage(in context);
        }
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

        using (ComputeContext context = _device.CreateComputeContext())
            RecordFlowStage(in context, in derived, in parameters);
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
        using ComputeContext context = _device.CreateComputeContext();
        RecordRenderStage(in context, output, rect, in derived, in parameters);
    }

    private void RecordFullPipeline(
        in ComputeContext context,
        ReadWriteTexture2D<Bgra32, Float4> source,
        ReadWriteTexture2D<Bgra32, Float4> output,
        int width,
        int height,
        in Parameters parameters)
    {
        _structureKey = null;
        var derived = Derive(width, height, in parameters);
        RecordSilhouetteStage(in context, source, 0, 0, width, height, in derived);
        RecordFlowStage(in context, in derived, in parameters);
        RecordRenderStage(in context, output, new PixelRect(0, 0, width, height), in derived, in parameters);
    }

    private void RecordSilhouetteStage(
        in ComputeContext context,
        ReadWriteTexture2D<Bgra32, Float4> source,
        int sourceOffsetX,
        int sourceOffsetY,
        int sourceWidth,
        int sourceHeight,
        in DerivedValues derived)
    {
        context.For(_gridWidth, _gridHeight, new SilhouetteShader(
            source, _deposit!, sourceOffsetX, sourceOffsetY, sourceWidth, sourceHeight, _gridWidth, _gridHeight, derived.CellSize));
        context.Barrier(_deposit!);
    }

    private void RecordMaskHashStage(in ComputeContext context)
    {
        context.For(1, new MaskHashResetShader(_scratch));
        context.Barrier(_scratch);
        context.For(_gridWidth, _gridHeight, new MaskHashShader(_deposit!, _scratch, _gridWidth, _gridHeight));
        context.Barrier(_scratch);
    }

    private void RecordFlowStage(
        in ComputeContext context,
        in DerivedValues derived,
        in Parameters parameters)
    {
        var gridWidth = _gridWidth;
        var gridHeight = _gridHeight;

        context.For(1, new InitScratchShader(_scratch));
        context.Barrier(_scratch);
        context.For(gridWidth, gridHeight, new InitFieldShader(
            _distributionsA!, _distributionsB!, _deposit!, _surface!, _flowPigmentA!, _flowPigmentB!, _fixedPigment!,
            _densityA!, _densityB!, _wetness!, _velocity!, _inkStep!,
            gridWidth, gridHeight, derived.Deposit));
        context.Barrier(_distributionsA!);
        context.Barrier(_distributionsB!);
        context.Barrier(_surface!);
        context.Barrier(_flowPigmentA!);
        context.Barrier(_flowPigmentB!);
        context.Barrier(_fixedPigment!);
        context.Barrier(_densityA!);
        context.Barrier(_densityB!);
        context.Barrier(_wetness!);
        context.Barrier(_velocity!);
        context.Barrier(_inkStep!);

        context.For(gridWidth, gridHeight, new JumpFloodSeedShader(_deposit!, _jumpFloodA!, gridWidth, gridHeight));
        context.Barrier(_jumpFloodA!);
        var reading = _jumpFloodA!;
        var writing = _jumpFloodB!;
        var stepSize = 1;
        var maxSide = Math.Max(gridWidth, gridHeight);
        while (stepSize < maxSide)
            stepSize <<= 1;
        stepSize >>= 1;
        while (stepSize >= 1)
        {
            context.For(gridWidth, gridHeight, new JumpFloodPassShader(reading, writing, gridWidth, gridHeight, stepSize));
            context.Barrier(writing);
            (reading, writing) = (writing, reading);
            stepSize >>= 1;
        }
        context.For(gridWidth, gridHeight, new ReachMaskShader(reading, _reachMask!, _kappa!, gridWidth, gridHeight, derived.CellSize, derived.ReachPixels));
        context.Barrier(_reachMask!);
        context.Barrier(_kappa!);

        var distributionsIn = _distributionsA!;
        var distributionsOut = _distributionsB!;
        var pigmentIn = _flowPigmentA!;
        var pigmentOut = _flowPigmentB!;
        for (var step = 0; step < derived.Steps; step++)
        {
            context.For(gridWidth, gridHeight, new SupplyShader(
                distributionsIn, _surface!, pigmentIn, _densityA!, _reachMask!, _deposit!, gridWidth, gridHeight));
            context.Barrier(distributionsIn);
            context.Barrier(_surface!);
            context.Barrier(pigmentIn);
            context.Barrier(_densityA!);
            context.For(gridWidth, gridHeight, new KappaShader(
                _densityA!, _fixedPigment!, _reachMask!, _kappa!, gridWidth, gridHeight,
                parameters.Seed, derived.Fiber, derived.Sizing, derived.Viscosity, derived.Sigma));
            context.Barrier(_kappa!);
            context.For(gridWidth, gridHeight, new StreamCollideShader(
                distributionsIn, distributionsOut, _kappa!, _reachMask!, _deposit!, _densityB!, _velocity!,
                gridWidth, gridHeight, derived.Omega, derived.SurfaceEvaporation));
            context.Barrier(distributionsOut);
            context.Barrier(_densityB!);
            context.Barrier(_velocity!);
            context.For(gridWidth, gridHeight, new PigmentShader(
                distributionsOut, _densityA!, _densityB!, _velocity!, pigmentIn, pigmentOut,
                _fixedPigment!, _wetness!, _inkStep!, _reachMask!, _deposit!, _scratch,
                gridWidth, gridHeight, step, parameters.Seed, derived.Fiber, derived.Viscosity));
            context.Barrier(pigmentOut);
            context.Barrier(_fixedPigment!);
            context.Barrier(_wetness!);
            context.Barrier(_inkStep!);
            (distributionsIn, distributionsOut) = (distributionsOut, distributionsIn);
            (pigmentIn, pigmentOut) = (pigmentOut, pigmentIn);
        }
        context.For(gridWidth, gridHeight, new FinalFixShader(pigmentIn, _fixedPigment!, gridWidth, gridHeight));
        context.Barrier(_fixedPigment!);
        context.Barrier(_scratch);
    }

    private void RecordRenderStage(
        in ComputeContext context,
        ReadWriteTexture2D<Bgra32, Float4> output,
        PixelRect rect,
        in DerivedValues derived,
        in Parameters parameters)
    {
        context.For(rect.Width, rect.Height, new RenderShader(
            _inkStep!, _fixedPigment!, _wetness!, _scratch, output,
            rect.X, rect.Y, rect.Width, rect.Height, _gridWidth, _gridHeight,
            derived.CellSize, Math.Clamp(parameters.Spread, 0f, 1f),
            Math.Clamp(parameters.Density, 0f, 1f), Math.Clamp(parameters.Grain, 0f, 1f),
            parameters.ColorR, parameters.ColorG, parameters.ColorB, parameters.Seed));
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
        _distributionsA = _device.AllocateReadWriteBuffer<float>(distributionLength);
        _distributionsB = _device.AllocateReadWriteBuffer<float>(distributionLength);
        _deposit = _device.AllocateReadWriteBuffer<float>(gridLength);
        _surface = _device.AllocateReadWriteBuffer<float>(gridLength);
        _flowPigmentA = _device.AllocateReadWriteBuffer<float>(gridLength);
        _flowPigmentB = _device.AllocateReadWriteBuffer<float>(gridLength);
        _fixedPigment = _device.AllocateReadWriteBuffer<float>(gridLength);
        _densityA = _device.AllocateReadWriteBuffer<float>(gridLength);
        _densityB = _device.AllocateReadWriteBuffer<float>(gridLength);
        _kappa = _device.AllocateReadWriteBuffer<float>(gridLength);
        _wetness = _device.AllocateReadWriteBuffer<float>(gridLength);
        _velocity = _device.AllocateReadWriteBuffer<Float2>(gridLength);
        _inkStep = _device.AllocateReadWriteBuffer<int>(gridLength);
        _reachMask = _device.AllocateReadWriteBuffer<int>(gridLength);
        _jumpFloodA = _device.AllocateReadWriteBuffer<int>(gridLength);
        _jumpFloodB = _device.AllocateReadWriteBuffer<int>(gridLength);
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
        _distributionsA?.Dispose();
        _distributionsB?.Dispose();
        _deposit?.Dispose();
        _surface?.Dispose();
        _flowPigmentA?.Dispose();
        _flowPigmentB?.Dispose();
        _fixedPigment?.Dispose();
        _densityA?.Dispose();
        _densityB?.Dispose();
        _kappa?.Dispose();
        _wetness?.Dispose();
        _velocity?.Dispose();
        _inkStep?.Dispose();
        _reachMask?.Dispose();
        _jumpFloodA?.Dispose();
        _jumpFloodB?.Dispose();
        _inkStepReadBack?.Dispose();
        _distributionsA = null;
        _distributionsB = null;
        _deposit = null;
        _surface = null;
        _flowPigmentA = null;
        _flowPigmentB = null;
        _fixedPigment = null;
        _densityA = null;
        _densityB = null;
        _kappa = null;
        _wetness = null;
        _velocity = null;
        _inkStep = null;
        _reachMask = null;
        _jumpFloodA = null;
        _jumpFloodB = null;
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

    private readonly record struct DerivedValues(
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
