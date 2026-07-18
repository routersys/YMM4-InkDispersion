using ComputeSharp;

namespace InkDispersion;

[ThreadGroupSize(DefaultThreadGroupSizes.X)]
[GeneratedComputeShaderDescriptor]
internal readonly partial struct FillIntShader(
    ReadWriteBuffer<int> values,
    int length,
    int value) : IComputeShader
{
    private readonly ReadWriteBuffer<int> values = values;
    private readonly int length = length;
    private readonly int value = value;

    public void Execute()
    {
        var index = ThreadIds.X;
        if (index >= length)
            return;
        values[index] = value;
    }
}

[ThreadGroupSize(DefaultThreadGroupSizes.X)]
[GeneratedComputeShaderDescriptor]
internal readonly partial struct InitScratchShader(
    ReadWriteBuffer<int> scratch) : IComputeShader
{
    private readonly ReadWriteBuffer<int> scratch = scratch;

    public void Execute()
    {
        if (ThreadIds.X != 0)
            return;
        scratch[0] = 0;
        scratch[1] = -1;
        scratch[2] = 0;
        scratch[3] = 0;
        scratch[4] = 0;
        scratch[5] = 0;
    }
}

[ThreadGroupSize(DefaultThreadGroupSizes.XY)]
[GeneratedComputeShaderDescriptor]
internal readonly partial struct SilhouetteShader(
    ReadWriteTexture2D<Bgra32, Float4> source,
    ReadWriteBuffer<float> deposit,
    int sourceOffsetX,
    int sourceOffsetY,
    int sourceWidth,
    int sourceHeight,
    int gridWidth,
    int gridHeight,
    float cellSize) : IComputeShader
{
    private readonly ReadWriteTexture2D<Bgra32, Float4> source = source;
    private readonly ReadWriteBuffer<float> deposit = deposit;
    private readonly int sourceOffsetX = sourceOffsetX;
    private readonly int sourceOffsetY = sourceOffsetY;
    private readonly int sourceWidth = sourceWidth;
    private readonly int sourceHeight = sourceHeight;
    private readonly int gridWidth = gridWidth;
    private readonly int gridHeight = gridHeight;
    private readonly float cellSize = cellSize;

    public void Execute()
    {
        var gx = ThreadIds.X;
        var gy = ThreadIds.Y;
        if (gx >= gridWidth || gy >= gridHeight)
            return;

        var centerX = (gx + 0.5f) * cellSize;
        var centerY = (gy + 0.5f) * cellSize;
        var x0 = Hlsl.Max((int)(centerX - cellSize * 0.5f), sourceOffsetX);
        var x1 = Hlsl.Min((int)Hlsl.Ceil(centerX + cellSize * 0.5f), sourceOffsetX + sourceWidth);
        var y0 = Hlsl.Max((int)(centerY - cellSize * 0.5f), sourceOffsetY);
        var y1 = Hlsl.Min((int)Hlsl.Ceil(centerY + cellSize * 0.5f), sourceOffsetY + sourceHeight);

        var sum = 0f;
        var count = 0;
        for (var y = y0; y < y1; y++)
        {
            for (var x = x0; x < x1; x++)
            {
                sum += source[new Int2(x - sourceOffsetX, y - sourceOffsetY)].W;
                count++;
            }
        }
        deposit[gy * gridWidth + gx] = count > 0 ? sum / count : 0f;
    }
}

[ThreadGroupSize(DefaultThreadGroupSizes.X)]
[GeneratedComputeShaderDescriptor]
internal readonly partial struct MaskHashResetShader(
    ReadWriteBuffer<int> scratch) : IComputeShader
{
    private readonly ReadWriteBuffer<int> scratch = scratch;

    public void Execute()
    {
        if (ThreadIds.X != 0)
            return;
        scratch[6] = 0;
        scratch[7] = 0;
    }
}

[ThreadGroupSize(DefaultThreadGroupSizes.XY)]
[GeneratedComputeShaderDescriptor]
internal readonly partial struct MaskHashShader(
    ReadWriteBuffer<float> deposit,
    ReadWriteBuffer<int> scratch,
    int gridWidth,
    int gridHeight) : IComputeShader
{
    private readonly ReadWriteBuffer<float> deposit = deposit;
    private readonly ReadWriteBuffer<int> scratch = scratch;
    private readonly int gridWidth = gridWidth;
    private readonly int gridHeight = gridHeight;

    public void Execute()
    {
        var gx = ThreadIds.X;
        var gy = ThreadIds.Y;
        if (gx >= gridWidth || gy >= gridHeight)
            return;

        var index = gy * gridWidth + gx;
        var quantized = (int)(deposit[index] * 255f + 0.5f);
        if (quantized <= 0)
            return;

        var mixed = ((uint)index * 0x9E3779B9u) ^ ((uint)quantized * 0x85EBCA6Bu);
        mixed ^= mixed >> 16;
        mixed *= 0x85EBCA6Bu;
        mixed ^= mixed >> 13;
        Hlsl.InterlockedAdd(ref scratch[6], (int)mixed);
        Hlsl.InterlockedXor(ref scratch[7], (int)(mixed * 0xC2B2AE35u));
    }
}

[ThreadGroupSize(DefaultThreadGroupSizes.XY)]
[GeneratedComputeShaderDescriptor]
internal readonly partial struct InitFieldShader(
    ReadWriteBuffer<float> distributions,
    ReadWriteBuffer<float> deposit,
    ReadWriteBuffer<float> surface,
    ReadWriteBuffer<float> flowPigment,
    ReadWriteBuffer<float> fixedPigment,
    ReadWriteBuffer<float> density,
    ReadWriteBuffer<float> wetness,
    ReadWriteBuffer<Float2> velocity,
    ReadWriteBuffer<int> inkStep,
    int gridWidth,
    int gridHeight,
    float depositScale) : IComputeShader
{
    private readonly ReadWriteBuffer<float> distributions = distributions;
    private readonly ReadWriteBuffer<float> deposit = deposit;
    private readonly ReadWriteBuffer<float> surface = surface;
    private readonly ReadWriteBuffer<float> flowPigment = flowPigment;
    private readonly ReadWriteBuffer<float> fixedPigment = fixedPigment;
    private readonly ReadWriteBuffer<float> density = density;
    private readonly ReadWriteBuffer<float> wetness = wetness;
    private readonly ReadWriteBuffer<Float2> velocity = velocity;
    private readonly ReadWriteBuffer<int> inkStep = inkStep;
    private readonly int gridWidth = gridWidth;
    private readonly int gridHeight = gridHeight;
    private readonly float depositScale = depositScale;

    public void Execute()
    {
        var gx = ThreadIds.X;
        var gy = ThreadIds.Y;
        if (gx >= gridWidth || gy >= gridHeight)
            return;

        var index = gy * gridWidth + gx;
        var gridLength = gridWidth * gridHeight;
        for (var i = 0; i < 9; i++)
            distributions[i * gridLength + index] = 0f;
        surface[index] = deposit[index] * depositScale;
        flowPigment[index] = 0f;
        fixedPigment[index] = 0f;
        density[index] = 0f;
        wetness[index] = 0f;
        velocity[index] = new Float2(0f, 0f);
        inkStep[index] = InkDispersionSettings.BirthSentinel;
    }
}

[ThreadGroupSize(DefaultThreadGroupSizes.XY)]
[GeneratedComputeShaderDescriptor]
internal readonly partial struct JumpFloodSeedShader(
    ReadWriteBuffer<float> deposit,
    ReadWriteBuffer<int> jumpFlood,
    int gridWidth,
    int gridHeight) : IComputeShader
{
    private readonly ReadWriteBuffer<float> deposit = deposit;
    private readonly ReadWriteBuffer<int> jumpFlood = jumpFlood;
    private readonly int gridWidth = gridWidth;
    private readonly int gridHeight = gridHeight;

    public void Execute()
    {
        var gx = ThreadIds.X;
        var gy = ThreadIds.Y;
        if (gx >= gridWidth || gy >= gridHeight)
            return;

        var index = gy * gridWidth + gx;
        jumpFlood[index] = deposit[index] > InkDispersionSettings.DepositThreshold ? index : -1;
    }
}

[ThreadGroupSize(DefaultThreadGroupSizes.XY)]
[GeneratedComputeShaderDescriptor]
internal readonly partial struct JumpFloodPassShader(
    ReadWriteBuffer<int> input,
    ReadWriteBuffer<int> output,
    int gridWidth,
    int gridHeight,
    int stepSize) : IComputeShader
{
    private readonly ReadWriteBuffer<int> input = input;
    private readonly ReadWriteBuffer<int> output = output;
    private readonly int gridWidth = gridWidth;
    private readonly int gridHeight = gridHeight;
    private readonly int stepSize = stepSize;

    public void Execute()
    {
        var x = ThreadIds.X;
        var y = ThreadIds.Y;
        if (x >= gridWidth || y >= gridHeight)
            return;

        var best = -1;
        var bestDistance = 3.402823e+38f;
        for (var dy = -1; dy <= 1; dy++)
        {
            for (var dx = -1; dx <= 1; dx++)
            {
                var sx = x + dx * stepSize;
                var sy = y + dy * stepSize;
                if (sx < 0 || sx >= gridWidth || sy < 0 || sy >= gridHeight)
                    continue;
                var candidate = input[sy * gridWidth + sx];
                if (candidate < 0)
                    continue;
                var deltaX = (float)(x - candidate % gridWidth);
                var deltaY = (float)(y - candidate / gridWidth);
                var distance = deltaX * deltaX + deltaY * deltaY;
                if (distance < bestDistance)
                {
                    bestDistance = distance;
                    best = candidate;
                }
            }
        }
        output[y * gridWidth + x] = best;
    }
}

[ThreadGroupSize(DefaultThreadGroupSizes.XY)]
[GeneratedComputeShaderDescriptor]
internal readonly partial struct ReachMaskShader(
    ReadWriteBuffer<int> jumpFlood,
    ReadWriteBuffer<int> reachMask,
    int gridWidth,
    int gridHeight,
    float cellSize,
    float reachPixels) : IComputeShader
{
    private readonly ReadWriteBuffer<int> jumpFlood = jumpFlood;
    private readonly ReadWriteBuffer<int> reachMask = reachMask;
    private readonly int gridWidth = gridWidth;
    private readonly int gridHeight = gridHeight;
    private readonly float cellSize = cellSize;
    private readonly float reachPixels = reachPixels;

    public void Execute()
    {
        var x = ThreadIds.X;
        var y = ThreadIds.Y;
        if (x >= gridWidth || y >= gridHeight)
            return;

        var index = y * gridWidth + x;
        var seed = jumpFlood[index];
        if (seed < 0)
        {
            reachMask[index] = 0;
            return;
        }
        var deltaX = (x - seed % gridWidth) * cellSize;
        var deltaY = (y - seed / gridWidth) * cellSize;
        reachMask[index] = deltaX * deltaX + deltaY * deltaY <= reachPixels * reachPixels ? 1 : 0;
    }
}

[ThreadGroupSize(DefaultThreadGroupSizes.XY)]
[GeneratedComputeShaderDescriptor]
internal readonly partial struct SupplyShader(
    ReadWriteBuffer<float> distributions,
    ReadWriteBuffer<float> surface,
    ReadWriteBuffer<float> flowPigment,
    ReadWriteBuffer<float> density,
    int gridWidth,
    int gridHeight) : IComputeShader
{
    private readonly ReadWriteBuffer<float> distributions = distributions;
    private readonly ReadWriteBuffer<float> surface = surface;
    private readonly ReadWriteBuffer<float> flowPigment = flowPigment;
    private readonly ReadWriteBuffer<float> density = density;
    private readonly int gridWidth = gridWidth;
    private readonly int gridHeight = gridHeight;

    public void Execute()
    {
        var gx = ThreadIds.X;
        var gy = ThreadIds.Y;
        if (gx >= gridWidth || gy >= gridHeight)
            return;

        var index = gy * gridWidth + gx;
        var gridLength = gridWidth * gridHeight;
        var rho = 0f;
        for (var i = 0; i < 9; i++)
            rho += distributions[i * gridLength + index];

        var reservoir = surface[index];
        var supply = Hlsl.Clamp(reservoir, 0f, InkDispersionSettings.PaperCapacity - rho);
        if (supply > 0f)
        {
            for (var i = 0; i < 9; i++)
                distributions[i * gridLength + index] += InkDispersionShaderMath.Weight(i) * supply;
            flowPigment[index] = (flowPigment[index] * rho + supply) / (rho + supply);
            surface[index] = reservoir - supply;
            rho += supply;
        }
        density[index] = rho;
    }
}

[ThreadGroupSize(DefaultThreadGroupSizes.XY)]
[GeneratedComputeShaderDescriptor]
internal readonly partial struct KappaShader(
    ReadWriteBuffer<float> density,
    ReadWriteBuffer<float> fixedPigment,
    ReadWriteBuffer<int> reachMask,
    ReadWriteBuffer<float> kappa,
    int gridWidth,
    int gridHeight,
    int seed,
    float fiber,
    float sizing,
    float viscosity,
    float sigma) : IComputeShader
{
    private readonly ReadWriteBuffer<float> density = density;
    private readonly ReadWriteBuffer<float> fixedPigment = fixedPigment;
    private readonly ReadWriteBuffer<int> reachMask = reachMask;
    private readonly ReadWriteBuffer<float> kappa = kappa;
    private readonly int gridWidth = gridWidth;
    private readonly int gridHeight = gridHeight;
    private readonly int seed = seed;
    private readonly float fiber = fiber;
    private readonly float sizing = sizing;
    private readonly float viscosity = viscosity;
    private readonly float sigma = sigma;

    public void Execute()
    {
        var gx = ThreadIds.X;
        var gy = ThreadIds.Y;
        if (gx >= gridWidth || gy >= gridHeight)
            return;

        var index = gy * gridWidth + gx;
        if (reachMask[index] == 0)
        {
            kappa[index] = 1f;
            return;
        }

        if (density[index] <= InkDispersionSettings.WetEpsilon)
        {
            var pin = InkDispersionShaderMath.PinField(gx, gy, seed);
            var accumulated = Hlsl.Min(fixedPigment[index], 1f);
            var threshold = sigma * (0.4f + 0.6f * pin) * (1f + accumulated);
            var pinned = true;
            for (var i = 1; i <= 8 && pinned; i++)
            {
                var nx = gx + InkDispersionShaderMath.Ex(i);
                var ny = gy + InkDispersionShaderMath.Ey(i);
                var neighborDensity = 0f;
                if (nx >= 0 && nx < gridWidth && ny >= 0 && ny < gridHeight)
                    neighborDensity = density[ny * gridWidth + nx];
                var limit = i <= 4 ? threshold : threshold * InkDispersionSettings.DiagonalSigmaFactor;
                if (neighborDensity >= limit)
                    pinned = false;
            }
            if (pinned)
            {
                kappa[index] = InkDispersionSettings.PinnedKappaMarker;
                return;
            }
        }

        var grain = InkDispersionShaderMath.FiberField(gx, gy, seed);
        var alum = InkDispersionShaderMath.AlumField(gx, gy, seed);
        var blocking = InkDispersionSettings.KappaBase
            + InkDispersionSettings.KappaFiber * fiber * grain
            + InkDispersionSettings.KappaAlum * sizing * alum
            + InkDispersionSettings.KappaGlue * viscosity
            + InkDispersionSettings.KappaFixture * Hlsl.Min(fixedPigment[index], 1f);
        kappa[index] = Hlsl.Clamp(blocking, 0f, InkDispersionSettings.KappaMax);
    }
}

[ThreadGroupSize(DefaultThreadGroupSizes.XY)]
[GeneratedComputeShaderDescriptor]
internal readonly partial struct StreamCollideShader(
    ReadWriteBuffer<float> distributionsIn,
    ReadWriteBuffer<float> distributionsOut,
    ReadWriteBuffer<float> kappa,
    ReadWriteBuffer<int> reachMask,
    ReadWriteBuffer<float> densityOut,
    ReadWriteBuffer<Float2> velocity,
    int gridWidth,
    int gridHeight,
    float omega,
    float surfaceEvaporation) : IComputeShader
{
    private readonly ReadWriteBuffer<float> distributionsIn = distributionsIn;
    private readonly ReadWriteBuffer<float> distributionsOut = distributionsOut;
    private readonly ReadWriteBuffer<float> kappa = kappa;
    private readonly ReadWriteBuffer<int> reachMask = reachMask;
    private readonly ReadWriteBuffer<float> densityOut = densityOut;
    private readonly ReadWriteBuffer<Float2> velocity = velocity;
    private readonly int gridWidth = gridWidth;
    private readonly int gridHeight = gridHeight;
    private readonly float omega = omega;
    private readonly float surfaceEvaporation = surfaceEvaporation;

    public void Execute()
    {
        var gx = ThreadIds.X;
        var gy = ThreadIds.Y;
        if (gx >= gridWidth || gy >= gridHeight)
            return;

        var index = gy * gridWidth + gx;
        var gridLength = gridWidth * gridHeight;
        if (reachMask[index] == 0)
        {
            for (var i = 0; i < 9; i++)
                distributionsOut[i * gridLength + index] = 0f;
            densityOut[index] = 0f;
            velocity[index] = new Float2(0f, 0f);
            return;
        }

        var selfKappa = kappa[index];
        var selfPinned = selfKappa >= InkDispersionSettings.PinnedKappaMarker;
        var selfBlocking = selfPinned ? 1f : selfKappa;

        var f0 = distributionsIn[index];
        var f1 = 0f;
        var f2 = 0f;
        var f3 = 0f;
        var f4 = 0f;
        var f5 = 0f;
        var f6 = 0f;
        var f7 = 0f;
        var f8 = 0f;
        for (var i = 1; i <= 8; i++)
        {
            var sx = gx - InkDispersionShaderMath.Ex(i);
            var sy = gy - InkDispersionShaderMath.Ey(i);
            var opposite = InkDispersionShaderMath.Opposite(i);
            var bounced = distributionsIn[opposite * gridLength + index];
            var streamed = 0f;
            var linkBlocking = 1f;
            var linkPinned = false;
            if (sx >= 0 && sx < gridWidth && sy >= 0 && sy < gridHeight)
            {
                var sourceIndex = sy * gridWidth + sx;
                streamed = distributionsIn[i * gridLength + sourceIndex];
                var sourceKappa = kappa[sourceIndex];
                var sourcePinned = sourceKappa >= InkDispersionSettings.PinnedKappaMarker;
                var sourceBlocking = sourcePinned ? 1f : sourceKappa;
                linkBlocking = Hlsl.Min((selfBlocking + sourceBlocking) * 0.5f, 1f);
                linkPinned = selfPinned || sourcePinned;
            }
            var bounceScale = linkPinned ? 1f - InkDispersionSettings.BoundaryEvaporation : 1f;
            var value = (1f - linkBlocking) * streamed + linkBlocking * bounced * bounceScale;
            if (i == 1)
                f1 = value;
            else if (i == 2)
                f2 = value;
            else if (i == 3)
                f3 = value;
            else if (i == 4)
                f4 = value;
            else if (i == 5)
                f5 = value;
            else if (i == 6)
                f6 = value;
            else if (i == 7)
                f7 = value;
            else
                f8 = value;
        }

        var rho = f0 + f1 + f2 + f3 + f4 + f5 + f6 + f7 + f8;
        var ux = f1 - f3 + f5 - f6 - f7 + f8;
        var uy = f2 - f4 + f5 + f6 - f7 - f8;
        var speedSquared = ux * ux + uy * uy;
        var speedLimit = InkDispersionSettings.MaximumLatticeSpeed;
        if (speedSquared > speedLimit * speedLimit)
        {
            var scale = speedLimit / Hlsl.Sqrt(speedSquared);
            ux *= scale;
            uy *= scale;
        }
        var psi = Hlsl.SmoothStep(0f, InkDispersionSettings.AdvectionAlpha, rho);
        var uSquared = 1.5f * (ux * ux + uy * uy);
        var evaporationScale = 1f - surfaceEvaporation;

        for (var i = 0; i < 9; i++)
        {
            var eDotU = InkDispersionShaderMath.Ex(i) * ux + InkDispersionShaderMath.Ey(i) * uy;
            var equilibrium = InkDispersionShaderMath.Weight(i) * (rho + psi * (3f * eDotU + 4.5f * eDotU * eDotU - uSquared));
            var current = i == 0 ? f0 : i == 1 ? f1 : i == 2 ? f2 : i == 3 ? f3 : i == 4 ? f4 : i == 5 ? f5 : i == 6 ? f6 : i == 7 ? f7 : f8;
            distributionsOut[i * gridLength + index] = ((1f - omega) * current + omega * equilibrium) * evaporationScale;
        }
        densityOut[index] = rho * evaporationScale;
        velocity[index] = new Float2(ux, uy);
    }
}

[ThreadGroupSize(DefaultThreadGroupSizes.XY)]
[GeneratedComputeShaderDescriptor]
internal readonly partial struct PigmentShader(
    ReadWriteBuffer<float> distributions,
    ReadWriteBuffer<float> densityPrevious,
    ReadWriteBuffer<float> densityCurrent,
    ReadWriteBuffer<Float2> velocity,
    ReadWriteBuffer<float> flowPigmentIn,
    ReadWriteBuffer<float> flowPigmentOut,
    ReadWriteBuffer<float> fixedPigment,
    ReadWriteBuffer<float> wetness,
    ReadWriteBuffer<int> inkStep,
    ReadWriteBuffer<int> reachMask,
    ReadWriteBuffer<int> scratch,
    int gridWidth,
    int gridHeight,
    int step,
    int seed,
    float fiber,
    float viscosity) : IComputeShader
{
    private readonly ReadWriteBuffer<float> distributions = distributions;
    private readonly ReadWriteBuffer<float> densityPrevious = densityPrevious;
    private readonly ReadWriteBuffer<float> densityCurrent = densityCurrent;
    private readonly ReadWriteBuffer<Float2> velocity = velocity;
    private readonly ReadWriteBuffer<float> flowPigmentIn = flowPigmentIn;
    private readonly ReadWriteBuffer<float> flowPigmentOut = flowPigmentOut;
    private readonly ReadWriteBuffer<float> fixedPigment = fixedPigment;
    private readonly ReadWriteBuffer<float> wetness = wetness;
    private readonly ReadWriteBuffer<int> inkStep = inkStep;
    private readonly ReadWriteBuffer<int> reachMask = reachMask;
    private readonly ReadWriteBuffer<int> scratch = scratch;
    private readonly int gridWidth = gridWidth;
    private readonly int gridHeight = gridHeight;
    private readonly int step = step;
    private readonly int seed = seed;
    private readonly float fiber = fiber;
    private readonly float viscosity = viscosity;

    public void Execute()
    {
        var gx = ThreadIds.X;
        var gy = ThreadIds.Y;
        if (gx >= gridWidth || gy >= gridHeight)
            return;

        var index = gy * gridWidth + gx;
        if (reachMask[index] == 0)
        {
            flowPigmentOut[index] = 0f;
            return;
        }

        var gridLength = gridWidth * gridHeight;
        var previousDensity = densityPrevious[index];
        var currentDensity = densityCurrent[index];
        var wasWet = previousDensity > InkDispersionSettings.WetEpsilon;
        var isWet = currentDensity > InkDispersionSettings.WetEpsilon;
        var pigment = flowPigmentIn[index];
        var advected = pigment;

        if (isWet && !wasWet)
        {
            var sum = 0f;
            for (var i = 1; i <= 8; i++)
            {
                var sx = gx - InkDispersionShaderMath.Ex(i);
                var sy = gy - InkDispersionShaderMath.Ey(i);
                if (sx < 0 || sx >= gridWidth || sy < 0 || sy >= gridHeight)
                    continue;
                sum += distributions[i * gridLength + index] * flowPigmentIn[sy * gridWidth + sx];
            }
            advected = sum / currentDensity;
        }
        else if (isWet)
        {
            var u = velocity[index];
            var posX = gx - u.X;
            var posY = gy - u.Y;
            var ix = (int)Hlsl.Floor(posX);
            var iy = (int)Hlsl.Floor(posY);
            var fx = posX - ix;
            var fy = posY - iy;
            var valid = ix >= 0 && ix + 1 < gridWidth && iy >= 0 && iy + 1 < gridHeight;
            if (valid)
            {
                var i00 = iy * gridWidth + ix;
                var i10 = i00 + 1;
                var i01 = i00 + gridWidth;
                var i11 = i01 + 1;
                if (densityPrevious[i00] <= InkDispersionSettings.WetEpsilon ||
                    densityPrevious[i10] <= InkDispersionSettings.WetEpsilon ||
                    densityPrevious[i01] <= InkDispersionSettings.WetEpsilon ||
                    densityPrevious[i11] <= InkDispersionSettings.WetEpsilon)
                {
                    advected = pigment;
                }
                else
                {
                    var top = Hlsl.Lerp(flowPigmentIn[i00], flowPigmentIn[i10], fx);
                    var bottom = Hlsl.Lerp(flowPigmentIn[i01], flowPigmentIn[i11], fx);
                    advected = Hlsl.Lerp(top, bottom, fy);
                }
            }
            var speed = Hlsl.Sqrt(u.X * u.X + u.Y * u.Y);
            var grain = InkDispersionShaderMath.FiberField(gx, gy, seed);
            var hindrance = Hlsl.Saturate(InkDispersionSettings.HindranceBase + InkDispersionSettings.HindranceFiber * fiber * grain);
            var mix = Hlsl.Lerp(1f, hindrance, Hlsl.SmoothStep(0f, InkDispersionSettings.HindranceSpeed, speed));
            advected = Hlsl.Lerp(advected, pigment, mix);
        }

        var waterLoss = Hlsl.Max(previousDensity - currentDensity, 0f);
        var fixFactor = wasWet && waterLoss > 0f ? waterLoss / previousDensity : 0f;
        var glueRate = Hlsl.Clamp(InkDispersionSettings.FixtureDryness + InkDispersionSettings.FixtureGlue * viscosity, 0f, 1f);
        fixFactor = Hlsl.Clamp(Hlsl.Max(fixFactor * (1f - Hlsl.SmoothStep(0f, glueRate, currentDensity)), InkDispersionSettings.FixtureBase), 0f, 1f);
        var settled = fixFactor * advected;
        var accumulated = fixedPigment[index] + settled;
        fixedPigment[index] = accumulated;
        flowPigmentOut[index] = advected - settled;

        if (isWet)
            wetness[index] = Hlsl.Max(wetness[index], currentDensity);
        if (accumulated > InkDispersionSettings.InkEpsilon && inkStep[index] == InkDispersionSettings.BirthSentinel)
        {
            inkStep[index] = step;
            Hlsl.InterlockedAdd(ref scratch[0], 1);
            Hlsl.InterlockedMax(ref scratch[1], step);
        }
    }
}

[ThreadGroupSize(DefaultThreadGroupSizes.XY)]
[GeneratedComputeShaderDescriptor]
internal readonly partial struct FinalFixShader(
    ReadWriteBuffer<float> flowPigment,
    ReadWriteBuffer<float> fixedPigment,
    int gridWidth,
    int gridHeight) : IComputeShader
{
    private readonly ReadWriteBuffer<float> flowPigment = flowPigment;
    private readonly ReadWriteBuffer<float> fixedPigment = fixedPigment;
    private readonly int gridWidth = gridWidth;
    private readonly int gridHeight = gridHeight;

    public void Execute()
    {
        var gx = ThreadIds.X;
        var gy = ThreadIds.Y;
        if (gx >= gridWidth || gy >= gridHeight)
            return;

        var index = gy * gridWidth + gx;
        fixedPigment[index] += flowPigment[index];
        flowPigment[index] = 0f;
    }
}

[ThreadGroupSize(DefaultThreadGroupSizes.XY)]
[GeneratedComputeShaderDescriptor]
internal readonly partial struct RenderShader(
    ReadWriteBuffer<int> inkStep,
    ReadWriteBuffer<float> fixedPigment,
    ReadWriteBuffer<float> wetness,
    ReadWriteBuffer<int> scratch,
    ReadWriteTexture2D<Bgra32, Float4> output,
    int rectOffsetX,
    int rectOffsetY,
    int rectWidth,
    int rectHeight,
    int gridWidth,
    int gridHeight,
    float cellSize,
    float spread,
    float density,
    float grain,
    float colorR,
    float colorG,
    float colorB,
    int seed) : IComputeShader
{
    private readonly ReadWriteBuffer<int> inkStep = inkStep;
    private readonly ReadWriteBuffer<float> fixedPigment = fixedPigment;
    private readonly ReadWriteBuffer<float> wetness = wetness;
    private readonly ReadWriteBuffer<int> scratch = scratch;
    private readonly ReadWriteTexture2D<Bgra32, Float4> output = output;
    private readonly int rectOffsetX = rectOffsetX;
    private readonly int rectOffsetY = rectOffsetY;
    private readonly int rectWidth = rectWidth;
    private readonly int rectHeight = rectHeight;
    private readonly int gridWidth = gridWidth;
    private readonly int gridHeight = gridHeight;
    private readonly float cellSize = cellSize;
    private readonly float spread = spread;
    private readonly float density = density;
    private readonly float grain = grain;
    private readonly float colorR = colorR;
    private readonly float colorG = colorG;
    private readonly float colorB = colorB;
    private readonly int seed = seed;

    public void Execute()
    {
        if (ThreadIds.X >= rectWidth || ThreadIds.Y >= rectHeight)
            return;
        var px = ThreadIds.X + rectOffsetX + 0.5f;
        var py = ThreadIds.Y + rectOffsetY + 0.5f;
        var visible = spread * (scratch[1] + 1);

        var cellX = px / cellSize - 0.5f;
        var cellY = py / cellSize - 0.5f;
        var ix = (int)Hlsl.Floor(cellX);
        var iy = (int)Hlsl.Floor(cellY);
        var fx = cellX - ix;
        var fy = cellY - iy;

        var ink = 0f;
        var tau = 0f;
        for (var corner = 0; corner < 4; corner++)
        {
            var cx = ix + (corner & 1);
            var cy = iy + (corner >> 1);
            if (cx < 0 || cx >= gridWidth || cy < 0 || cy >= gridHeight)
                continue;
            var weight = ((corner & 1) == 0 ? 1f - fx : fx) * ((corner >> 1) == 0 ? 1f - fy : fy);
            var index = cy * gridWidth + cx;
            var cellStep = inkStep[index];
            if (cellStep >= InkDispersionSettings.BirthSentinel)
                continue;
            var fade = Hlsl.Saturate(visible - cellStep);
            ink += weight * fade * Hlsl.Max(fixedPigment[index] - InkDispersionSettings.InkEpsilon, 0f);
            tau += weight * fade * wetness[index];
        }

        if (tau <= 0f)
        {
            output[ThreadIds.XY] = new Float4(0f, 0f, 0f, 0f);
            return;
        }

        var rougher = InkDispersionShaderMath.ValueNoise(px * (0.5f / cellSize), py * (0.5f / cellSize), (uint)seed, 0x51ED270Bu);
        var phi = tau * InkDispersionSettings.CoverageGain - InkDispersionSettings.RougherBias - grain * InkDispersionSettings.RougherScale * rougher;
        var coverage = Hlsl.SmoothStep(0f, InkDispersionSettings.CoverageSoftness, phi);
        if (coverage <= 0f)
        {
            output[ThreadIds.XY] = new Float4(0f, 0f, 0f, 0f);
            return;
        }

        var hairField = InkDispersionShaderMath.FiberField((int)(px * 2f), (int)(py * 2f), seed);
        var hair = 1f - grain * InkDispersionSettings.HairStrength * hairField * (1f - Hlsl.Saturate(tau * 2f));
        var absorbed = 1f - Hlsl.Exp(-ink * InkDispersionSettings.InkAbsorbance * density);
        var alpha = Hlsl.Saturate(coverage * absorbed * hair);
        var r = Hlsl.Min(colorR * alpha, alpha);
        var g = Hlsl.Min(colorG * alpha, alpha);
        var b = Hlsl.Min(colorB * alpha, alpha);
        output[ThreadIds.XY] = new Float4(r, g, b, alpha);
    }
}

internal static class InkDispersionShaderMath
{
    public static int Ex(int direction)
    {
        if (direction == 1 || direction == 5 || direction == 8)
            return 1;
        if (direction == 3 || direction == 6 || direction == 7)
            return -1;
        return 0;
    }

    public static int Ey(int direction)
    {
        if (direction == 2 || direction == 5 || direction == 6)
            return 1;
        if (direction == 4 || direction == 7 || direction == 8)
            return -1;
        return 0;
    }

    public static int Opposite(int direction)
    {
        if (direction == 0)
            return 0;
        if (direction <= 4)
            return ((direction + 1) & 3) + 1;
        return ((direction - 3) & 3) + 5;
    }

    public static float Weight(int direction)
    {
        if (direction == 0)
            return 4f / 9f;
        return direction <= 4 ? 1f / 9f : 1f / 36f;
    }

    public static float Hash01(uint value)
    {
        value ^= value >> 16;
        value *= 0x7FEB352Du;
        value ^= value >> 15;
        value *= 0x846CA68Bu;
        value ^= value >> 16;
        return value * 2.3283064e-10f;
    }

    public static float LatticeHash(int x, int y, uint seed, uint salt)
        => Hash01(((uint)x * 0x9E3779B9u) ^ ((uint)y * 0x85EBCA6Bu) ^ (seed * 0xC2B2AE35u) ^ salt);

    public static float ValueNoise(float x, float y, uint seed, uint salt)
    {
        var ix = (int)Hlsl.Floor(x);
        var iy = (int)Hlsl.Floor(y);
        var fx = x - ix;
        var fy = y - iy;
        var sx = fx * fx * (3f - 2f * fx);
        var sy = fy * fy * (3f - 2f * fy);
        var v00 = LatticeHash(ix, iy, seed, salt);
        var v10 = LatticeHash(ix + 1, iy, seed, salt);
        var v01 = LatticeHash(ix, iy + 1, seed, salt);
        var v11 = LatticeHash(ix + 1, iy + 1, seed, salt);
        return Hlsl.Lerp(Hlsl.Lerp(v00, v10, sx), Hlsl.Lerp(v01, v11, sx), sy);
    }

    public static float FiberField(int gx, int gy, int seed)
    {
        var s = (uint)seed;
        var value = 0.5f * ValueNoise(gx * 0.11f, gy * 0.31f, s, 0x1B873593u)
            + 0.3f * ValueNoise(gx * 0.37f, gy * 0.13f, s, 0xCC9E2D51u)
            + 0.2f * ValueNoise(gx * 0.53f, gy * 0.53f, s, 0xE6546B64u);
        return Hlsl.Saturate((value - 0.3f) * 1.8f);
    }

    public static float PinField(int gx, int gy, int seed)
    {
        var value = ValueNoise(gx * 0.23f, gy * 0.23f, (uint)seed, 0x27D4EB2Fu);
        return 1f - Hlsl.Abs(2f * value - 1f);
    }

    public static float AlumField(int gx, int gy, int seed)
    {
        var value = ValueNoise(gx * 0.17f, gy * 0.19f, (uint)seed, 0x165667B1u);
        return Hlsl.SmoothStep(0.75f, 0.95f, value);
    }
}
