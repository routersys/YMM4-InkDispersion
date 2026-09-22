using ComputeWeave;

namespace InkDispersion;

[ComputeResourceGroup]
internal sealed partial class InkDispersionGridResources
{
    [ComputePipelineResource(ComputeResourceAccess.ReadWrite)]
    internal ReadWriteBuffer<float> DistributionsA { get; }

    [ComputePipelineResource(ComputeResourceAccess.ReadWrite)]
    internal ReadWriteBuffer<float> DistributionsB { get; }

    [ComputePipelineResource(ComputeResourceAccess.ReadWrite)]
    internal ReadWriteBuffer<float> Deposit { get; }

    [ComputePipelineResource(ComputeResourceAccess.ReadWrite)]
    internal ReadWriteBuffer<float> Surface { get; }

    [ComputePipelineResource(ComputeResourceAccess.ReadWrite)]
    internal ReadWriteBuffer<float> FlowPigmentA { get; }

    [ComputePipelineResource(ComputeResourceAccess.ReadWrite)]
    internal ReadWriteBuffer<float> FlowPigmentB { get; }

    [ComputePipelineResource(ComputeResourceAccess.ReadWrite)]
    internal ReadWriteBuffer<float> FixedPigment { get; }

    [ComputePipelineResource(ComputeResourceAccess.ReadWrite)]
    internal ReadWriteBuffer<float> DensityA { get; }

    [ComputePipelineResource(ComputeResourceAccess.ReadWrite)]
    internal ReadWriteBuffer<float> DensityB { get; }

    [ComputePipelineResource(ComputeResourceAccess.ReadWrite)]
    internal ReadWriteBuffer<float> Kappa { get; }

    [ComputePipelineResource(ComputeResourceAccess.ReadWrite)]
    internal ReadWriteBuffer<float> Wetness { get; }

    [ComputePipelineResource(ComputeResourceAccess.ReadWrite)]
    internal ReadWriteBuffer<Float2> Velocity { get; }

    [ComputePipelineResource(ComputeResourceAccess.ReadWrite)]
    internal ReadWriteBuffer<int> ReachMask { get; }

    [ComputePipelineResource(ComputeResourceAccess.ReadWrite)]
    internal ReadWriteBuffer<int> JumpFloodA { get; }

    [ComputePipelineResource(ComputeResourceAccess.ReadWrite)]
    internal ReadWriteBuffer<int> JumpFloodB { get; }

    [ComputePipelineResource(ComputeResourceAccess.ReadWrite)]
    internal ReadWriteBuffer<float> FiberField { get; }

    [ComputePipelineResource(ComputeResourceAccess.ReadWrite)]
    internal ReadWriteBuffer<float> AlumField { get; }

    [ComputePipelineResource(ComputeResourceAccess.ReadWrite)]
    internal ReadWriteBuffer<float> PinField { get; }
}

[ComputePipelineHost("_device", 1)]
internal sealed partial class InkDispersionPipelineHost
{
    private readonly GraphicsDevice _device;

    [ComputePipelineResource(ComputeResourceAccess.ReadWrite, ComputeResourceRecovery.Recompute)]
    private readonly ComputeResourceGroupSlot<InkDispersionGridResources> _grid = new();

    [ComputePipeline]
    private void RecordFullPipeline(
        in ComputeContext context,
        [ComputeOwnedResource(nameof(_grid))] InkDispersionGridResources grid,
        [ComputeResource(ComputeResourceAccess.ReadWrite)] ReadWriteTexture2D<Bgra32, Float4> source,
        [ComputeResource(ComputeResourceAccess.ReadWrite)] ReadWriteTexture2D<Bgra32, Float4> output,
        [ComputeResource(ComputeResourceAccess.ReadWrite)] ReadWriteBuffer<int> scratch,
        [ComputeResource(ComputeResourceAccess.ReadWrite)] ReadWriteBuffer<int> inkStep,
        int width,
        int height,
        int gridWidth,
        int gridHeight,
        in InkDispersionPipeline.DerivedValues derived,
        in InkDispersionPipeline.Parameters parameters)
    {
        _ = _device;

        RecordSilhouetteStage(in context, grid, source, 0, 0, width, height, gridWidth, gridHeight, in derived);
        RecordFlowStage(in context, grid, scratch, inkStep, gridWidth, gridHeight, in derived, in parameters);
        RecordRenderStage(in context, grid, output, scratch, inkStep, new InkDispersionPipeline.PixelRect(0, 0, width, height), gridWidth, gridHeight, in derived, in parameters);
    }

    [ComputePipeline]
    private void RecordSilhouetteAndMaskHash(
        in ComputeContext context,
        [ComputeOwnedResource(nameof(_grid))] InkDispersionGridResources grid,
        [ComputeResource(ComputeResourceAccess.ReadWrite)] ReadWriteTexture2D<Bgra32, Float4> source,
        [ComputeResource(ComputeResourceAccess.ReadWrite)] ReadWriteBuffer<int> scratch,
        int sourceOffsetX,
        int sourceOffsetY,
        int sourceWidth,
        int sourceHeight,
        int gridWidth,
        int gridHeight,
        in InkDispersionPipeline.DerivedValues derived)
    {
        _ = _device;

        RecordSilhouetteStage(in context, grid, source, sourceOffsetX, sourceOffsetY, sourceWidth, sourceHeight, gridWidth, gridHeight, in derived);
        RecordMaskHashStage(in context, grid, scratch, gridWidth, gridHeight);
    }

    [ComputePipeline]
    [ComputeInterop]
    private void RecordSharedSilhouetteAndMaskHash(
        in ComputeContext context,
        [ComputeOwnedResource(nameof(_grid))] InkDispersionGridResources grid,
        [ComputeResource(ComputeResourceAccess.ReadWrite, Sharing = ComputeResourceSharing.External)] ReadWriteTexture2D<Bgra32, Float4> source,
        [ComputeResource(ComputeResourceAccess.ReadWrite)] ReadWriteBuffer<int> scratch,
        int sourceOffsetX,
        int sourceOffsetY,
        int sourceWidth,
        int sourceHeight,
        int gridWidth,
        int gridHeight,
        in InkDispersionPipeline.DerivedValues derived)
    {
        _ = _device;

        RecordSilhouetteStage(in context, grid, source, sourceOffsetX, sourceOffsetY, sourceWidth, sourceHeight, gridWidth, gridHeight, in derived);
        RecordMaskHashStage(in context, grid, scratch, gridWidth, gridHeight);
    }

    [ComputePipeline]
    [ComputeInterop]
    private void RecordSharedRender(
        in ComputeContext context,
        [ComputeOwnedResource(nameof(_grid))] InkDispersionGridResources grid,
        [ComputeResource(ComputeResourceAccess.ReadWrite, Sharing = ComputeResourceSharing.External)] ReadWriteTexture2D<Bgra32, Float4> output,
        [ComputeResource(ComputeResourceAccess.ReadWrite)] ReadWriteBuffer<int> scratch,
        [ComputeResource(ComputeResourceAccess.ReadWrite)] ReadWriteBuffer<int> inkStep,
        in InkDispersionPipeline.PixelRect rect,
        int gridWidth,
        int gridHeight,
        in InkDispersionPipeline.DerivedValues derived,
        in InkDispersionPipeline.Parameters parameters)
    {
        _ = _device;

        RecordRenderStage(in context, grid, output, scratch, inkStep, rect, gridWidth, gridHeight, in derived, in parameters);
    }

    [ComputePipeline]
    private void RecordFlow(
        in ComputeContext context,
        [ComputeOwnedResource(nameof(_grid))] InkDispersionGridResources grid,
        [ComputeResource(ComputeResourceAccess.ReadWrite)] ReadWriteBuffer<int> scratch,
        [ComputeResource(ComputeResourceAccess.ReadWrite)] ReadWriteBuffer<int> inkStep,
        int gridWidth,
        int gridHeight,
        in InkDispersionPipeline.DerivedValues derived,
        in InkDispersionPipeline.Parameters parameters)
    {
        _ = _device;

        RecordFlowStage(in context, grid, scratch, inkStep, gridWidth, gridHeight, in derived, in parameters);
    }

    [ComputePipeline]
    private void RecordRender(
        in ComputeContext context,
        [ComputeOwnedResource(nameof(_grid))] InkDispersionGridResources grid,
        [ComputeResource(ComputeResourceAccess.ReadWrite)] ReadWriteTexture2D<Bgra32, Float4> output,
        [ComputeResource(ComputeResourceAccess.ReadWrite)] ReadWriteBuffer<int> scratch,
        [ComputeResource(ComputeResourceAccess.ReadWrite)] ReadWriteBuffer<int> inkStep,
        in InkDispersionPipeline.PixelRect rect,
        int gridWidth,
        int gridHeight,
        in InkDispersionPipeline.DerivedValues derived,
        in InkDispersionPipeline.Parameters parameters)
    {
        _ = _device;

        RecordRenderStage(in context, grid, output, scratch, inkStep, rect, gridWidth, gridHeight, in derived, in parameters);
    }

    private static void RecordSilhouetteStage(
        in ComputeContext context,
        InkDispersionGridResources grid,
        ReadWriteTexture2D<Bgra32, Float4> source,
        int sourceOffsetX,
        int sourceOffsetY,
        int sourceWidth,
        int sourceHeight,
        int gridWidth,
        int gridHeight,
        in InkDispersionPipeline.DerivedValues derived)
    {
        context.For(gridWidth, gridHeight, new SilhouetteShader(
            source, grid.Deposit, sourceOffsetX, sourceOffsetY, sourceWidth, sourceHeight, gridWidth, gridHeight, derived.CellSize));
        context.Barrier(grid.Deposit);
    }

    private static void RecordMaskHashStage(
        in ComputeContext context,
        InkDispersionGridResources grid,
        ReadWriteBuffer<int> scratch,
        int gridWidth,
        int gridHeight)
    {
        context.For(1, new MaskHashResetShader(scratch));
        context.Barrier(scratch);
        context.For(gridWidth, gridHeight, new MaskHashShader(grid.Deposit, scratch, gridWidth, gridHeight));
        context.Barrier(scratch);
    }

    private static void RecordFlowStage(
        in ComputeContext context,
        InkDispersionGridResources grid,
        ReadWriteBuffer<int> scratch,
        ReadWriteBuffer<int> inkStep,
        int gridWidth,
        int gridHeight,
        in InkDispersionPipeline.DerivedValues derived,
        in InkDispersionPipeline.Parameters parameters)
    {
        context.For(1, new InitScratchShader(scratch));
        context.Barrier(scratch);
        context.For(gridWidth, gridHeight, new InitFieldShader(
            grid.DistributionsA, grid.DistributionsB, grid.Deposit, grid.Surface, grid.FlowPigmentA, grid.FlowPigmentB, grid.FixedPigment,
            grid.DensityA, grid.DensityB, grid.Wetness, grid.Velocity, inkStep, grid.FiberField, grid.AlumField, grid.PinField,
            gridWidth, gridHeight, derived.Deposit, parameters.Seed));
        context.Barrier(grid.DistributionsA);
        context.Barrier(grid.DistributionsB);
        context.Barrier(grid.Surface);
        context.Barrier(grid.FlowPigmentA);
        context.Barrier(grid.FlowPigmentB);
        context.Barrier(grid.FixedPigment);
        context.Barrier(grid.DensityA);
        context.Barrier(grid.DensityB);
        context.Barrier(grid.Wetness);
        context.Barrier(grid.Velocity);
        context.Barrier(inkStep);
        context.Barrier(grid.FiberField);
        context.Barrier(grid.AlumField);
        context.Barrier(grid.PinField);

        context.For(gridWidth, gridHeight, new JumpFloodSeedShader(grid.Deposit, grid.JumpFloodA, gridWidth, gridHeight));
        context.Barrier(grid.JumpFloodA);
        var reading = grid.JumpFloodA;
        var writing = grid.JumpFloodB;
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
        context.For(gridWidth, gridHeight, new ReachMaskShader(reading, grid.ReachMask, grid.Kappa, gridWidth, gridHeight, derived.CellSize, derived.ReachPixels));
        context.Barrier(grid.ReachMask);
        context.Barrier(grid.Kappa);

        var distributionsIn = grid.DistributionsA;
        var distributionsOut = grid.DistributionsB;
        var pigmentIn = grid.FlowPigmentA;
        var pigmentOut = grid.FlowPigmentB;
        for (var step = 0; step < derived.Steps; step++)
        {
            context.For(gridWidth, gridHeight, new SupplyShader(
                distributionsIn, grid.Surface, pigmentIn, grid.DensityA, grid.ReachMask, grid.Deposit, gridWidth, gridHeight));
            context.Barrier(distributionsIn);
            context.Barrier(grid.Surface);
            context.Barrier(pigmentIn);
            context.Barrier(grid.DensityA);
            context.For(gridWidth, gridHeight, new KappaShader(
                grid.DensityA, grid.FixedPigment, grid.ReachMask, grid.FiberField, grid.AlumField, grid.PinField, grid.Kappa, gridWidth, gridHeight,
                derived.Fiber, derived.Sizing, derived.Viscosity, derived.Sigma));
            context.Barrier(grid.Kappa);
            context.For(gridWidth, gridHeight, new StreamCollideShader(
                distributionsIn, distributionsOut, grid.Kappa, grid.ReachMask, grid.Deposit, grid.DensityB, grid.Velocity,
                gridWidth, gridHeight, derived.Omega, derived.SurfaceEvaporation));
            context.Barrier(distributionsOut);
            context.Barrier(grid.DensityB);
            context.Barrier(grid.Velocity);
            context.For(gridWidth, gridHeight, new PigmentShader(
                distributionsOut, grid.DensityA, grid.DensityB, grid.Velocity, pigmentIn, pigmentOut,
                grid.FixedPigment, grid.Wetness, inkStep, grid.ReachMask, grid.Deposit, grid.FiberField, scratch,
                gridWidth, gridHeight, step, derived.Fiber, derived.Viscosity));
            context.Barrier(pigmentOut);
            context.Barrier(grid.FixedPigment);
            context.Barrier(grid.Wetness);
            context.Barrier(inkStep);
            (distributionsIn, distributionsOut) = (distributionsOut, distributionsIn);
            (pigmentIn, pigmentOut) = (pigmentOut, pigmentIn);
        }
        context.For(gridWidth, gridHeight, new FinalFixShader(pigmentIn, grid.FixedPigment, gridWidth, gridHeight));
        context.Barrier(grid.FixedPigment);
        context.Barrier(scratch);
    }

    private static void RecordRenderStage(
        in ComputeContext context,
        InkDispersionGridResources grid,
        ReadWriteTexture2D<Bgra32, Float4> output,
        ReadWriteBuffer<int> scratch,
        ReadWriteBuffer<int> inkStep,
        in InkDispersionPipeline.PixelRect rect,
        int gridWidth,
        int gridHeight,
        in InkDispersionPipeline.DerivedValues derived,
        in InkDispersionPipeline.Parameters parameters)
    {
        context.For(rect.Width, rect.Height, new RenderShader(
            inkStep, grid.FixedPigment, grid.Wetness, scratch, output,
            rect.X, rect.Y, rect.Width, rect.Height, gridWidth, gridHeight,
            derived.CellSize, Math.Clamp(parameters.Spread, 0f, 1f),
            Math.Clamp(parameters.Density, 0f, 1f), Math.Clamp(parameters.Grain, 0f, 1f),
            parameters.ColorR, parameters.ColorG, parameters.ColorB, parameters.Seed));
    }
}
