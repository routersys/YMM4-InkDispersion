namespace InkDispersion;

internal static class InkDispersionSettings
{
    public const float PaperCapacity = 1f;
    public const float MinimumDeposit = 0.4f;
    public const float MaximumDeposit = 3.0f;
    public const float MinimumOmega = 0.5f;
    public const float MaximumOmega = 1.5f;
    public const float AdvectionAlpha = 0.35f;
    public const float MaximumLatticeSpeed = 0.3f;
    public const float WetEpsilon = 0.0001f;
    public const float DepositThreshold = 0.02f;
    public const float KappaBase = 0.02f;
    public const float KappaFiber = 0.55f;
    public const float KappaAlum = 0.5f;
    public const float KappaGlue = 0.35f;
    public const float KappaFixture = 0.4f;
    public const float KappaMax = 0.95f;
    public const float PinnedKappaMarker = 2f;
    public const float SigmaFloor = 0.0015f;
    public const float MaximumSigma = 0.05f;
    public const float DiagonalSigmaFactor = 1.4142135f;
    public const float MaximumSurfaceEvaporation = 0.005f;
    public const float BoundaryEvaporation = 0.00005f;
    public const float HindranceBase = 0.25f;
    public const float HindranceFiber = 0.45f;
    public const float HindranceSpeed = 0.004f;
    public const float FixtureBase = 0.0005f;
    public const float FixtureDryness = 0.35f;
    public const float FixtureGlue = 0.5f;
    public const float InkAbsorbance = 2.5f;
    public const float InkEpsilon = 0.008f;
    public const float RougherScale = 0.06f;
    public const float RougherBias = 0.01f;
    public const float CoverageGain = 12f;
    public const float CoverageSoftness = 0.05f;
    public const float HairStrength = 0.7f;
    public const float StepsPerCell = 10f;
    public const int MinimumStepCount = 192;
    public const int BirthSentinel = 268435456;
    public const int MinimumGridSize = 4;
    public const int MaximumCanvasSize = 8192;
    public const int MaximumStepCount = 1440;
    public const int MarginPadding = 64;
    public const int ScratchLength = 8;
    public const int ScratchWetCount = 0;
    public const int ScratchMaxWetStep = 1;
    public const int ScratchBoundsMinX = 2;
    public const int ScratchBoundsMinY = 3;
    public const int ScratchBoundsMaxX = 4;
    public const int ScratchBoundsMaxY = 5;
    public const int ScratchMaskHashSum = 6;
    public const int ScratchMaskHashMix = 7;
    public const int MaximumPendingSubmissions = 32;
    public const int FlowStepsPerSubmission = 128;

    public static QualitySettings GetQuality(InkDispersionQuality quality)
        => quality switch
        {
            InkDispersionQuality.Balanced => new QualitySettings(160, 720),
            InkDispersionQuality.Ultra => new QualitySettings(288, 1440),
            _ => new QualitySettings(224, 1080),
        };

    public static (int Width, int Height, float CellSize) GetGridSize(int width, int height, int resolution)
    {
        var longSide = Math.Max(Math.Max(width, height), 1);
        var cellSize = longSide / (float)Math.Max(Math.Min(resolution, longSide), MinimumGridSize);
        var gridWidth = Math.Max((int)Math.Ceiling(width / cellSize) + 1, MinimumGridSize);
        var gridHeight = Math.Max((int)Math.Ceiling(height / cellSize) + 1, MinimumGridSize);
        return (gridWidth, gridHeight, cellSize);
    }

    public static int GetStepCount(float reachCells, int maxSteps)
        => Math.Clamp((int)(reachCells * StepsPerCell) + 128, MinimumStepCount, maxSteps);

    public static float GetDeposit(float water)
        => MinimumDeposit + Math.Clamp(water, 0f, 1f) * (MaximumDeposit - MinimumDeposit);

    public static float GetOmega(float viscosity)
        => MaximumOmega - Math.Clamp(viscosity, 0f, 1f) * (MaximumOmega - MinimumOmega);

    public static float GetPinningSigma(float sizing)
        => SigmaFloor + Math.Clamp(sizing, 0f, 1f) * MaximumSigma;

    public static float GetSurfaceEvaporation(float drying)
        => Math.Clamp(drying, 0f, 1f) * MaximumSurfaceEvaporation;

    internal readonly record struct QualitySettings(int GridResolution, int MaxSteps);
}
