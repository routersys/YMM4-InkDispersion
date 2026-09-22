using System.Numerics;
using ComputeWeave;
using Vortice.Direct2D1;
using Vortice.Direct2D1.Effects;
using YukkuriMovieMaker.Commons;
using YukkuriMovieMaker.Player.Video;
using YukkuriMovieMaker.Player.Video.Effects;

namespace InkDispersion;

internal sealed class InkDispersionEffectProcessor : VideoEffectProcessorBase
{
    private readonly IGraphicsDevicesAndContext _devices;
    private readonly InkDispersionEffect _item;
    private ComputeExternalQueueScheduler? _scheduler;
    private InkDispersionInteropProvider? _interopProvider;
    private ComputeInteropDomain? _interopDomain;
    private InkDispersionResourceSet? _resourceSet;
    private ExternalTextureLease<ExternalDirect3D11TextureView>? _outputLease;
    private InkDispersionPipeline? _pipeline;
    private InkDispersionCustomEffect? _effect;
    private Crop? _outputCrop;
    private ID2D1Image? _outputCropOutput;
    private AffineTransform2D? _outputTransform;
    private ID2D1Image? _outputTransformOutput;
    private bool _isFirst = true;
    private bool _hasOutput;
    private bool _hasOutputOffset;
    private bool _hasCropRect;
    private bool _hasRenderState;
    private Vector2 _outputOffset;
    private Vector4 _cropRect;
    private Parameters _parameters;
    private RenderState _renderState;

    public InkDispersionEffectProcessor(IGraphicsDevicesAndContext devices, InkDispersionEffect item)
        : base(devices)
    {
        _devices = devices;
        _item = item;
    }

    public override DrawDescription Update(EffectDescription effectDescription)
    {
        try
        {
            return UpdateCore(effectDescription);
        }
        catch (Exception exception)
        {
            InkDispersionTelemetry.Report(exception);
            throw;
        }
    }

    private DrawDescription UpdateCore(EffectDescription effectDescription)
    {
        if (IsPassThroughEffect || _effect is null || _outputCrop is null || _outputTransform is null || _outputTransformOutput is null || _resourceSet is null || _interopProvider is null || _pipeline is null || input is null)
            return effectDescription.DrawDescription;

        var frame = effectDescription.ItemPosition.Frame;
        var length = effectDescription.ItemDuration.Frame;
        var fps = effectDescription.FPS;
        var parameters = new Parameters(
            (float)(_item.Amount.GetValue(frame, length, fps) / 100.0),
            (float)(_item.Spread.GetValue(frame, length, fps) / 100.0),
            _item.Quality,
            (float)(_item.Water.GetValue(frame, length, fps) / 100.0),
            (float)(_item.Reach.GetValue(frame, length, fps) / 100.0),
            (float)(_item.Fiber.GetValue(frame, length, fps) / 100.0),
            (float)(_item.Sizing.GetValue(frame, length, fps) / 100.0),
            (float)(_item.Viscosity.GetValue(frame, length, fps) / 100.0),
            (float)(_item.Drying.GetValue(frame, length, fps) / 100.0),
            (float)(_item.Density.GetValue(frame, length, fps) / 100.0),
            (float)(_item.Grain.GetValue(frame, length, fps) / 100.0),
            _item.InkColor,
            _item.Seed);

        if (_isFirst || _parameters.Amount != parameters.Amount)
            _effect.Amount = parameters.Amount;

        if (parameters.Amount <= 0f || parameters.Spread <= 0f)
        {
            _effect.Amount = 0f;
            _parameters = parameters;
            _isFirst = true;
            return effectDescription.DrawDescription;
        }

        var bounds = _devices.DeviceContext.GetImageLocalBounds(input);
        var widthValue = Math.Ceiling((double)bounds.Right - bounds.Left);
        var heightValue = Math.Ceiling((double)bounds.Bottom - bounds.Top);
        if (!double.IsFinite(widthValue) || !double.IsFinite(heightValue) ||
            !float.IsFinite(bounds.Left) || !float.IsFinite(bounds.Top) ||
            widthValue <= 0d || heightValue <= 0d)
        {
            _effect.Amount = 0f;
            _isFirst = true;
            return effectDescription.DrawDescription;
        }

        var reach = Math.Clamp(parameters.Reach, 0.01f, 4f);
        var longSide = Math.Max(widthValue, heightValue);
        var marginLimit = (InkDispersionSettings.MaximumCanvasSize - longSide) / 2d;
        if (marginLimit < 16d)
        {
            _effect.Amount = 0f;
            _isFirst = true;
            return effectDescription.DrawDescription;
        }
        var padding = (double)InkDispersionSettings.MarginPadding;
        var reachPixels = Math.Min(reach * longSide, Math.Max(marginLimit - padding, 1d));
        var margin = (int)Math.Ceiling(reachPixels + padding);
        var canvasWidthValue = widthValue + margin * 2d;
        var canvasHeightValue = heightValue + margin * 2d;
        if (canvasWidthValue * canvasHeightValue > int.MaxValue)
        {
            _effect.Amount = 0f;
            _isFirst = true;
            return effectDescription.DrawDescription;
        }
        var canvasWidth = (int)canvasWidthValue;
        var canvasHeight = (int)canvasHeightValue;
        var itemWidth = (int)widthValue;
        var itemHeight = (int)heightValue;

        if (!EnsureSource(itemWidth, itemHeight))
        {
            _effect.Amount = 0f;
            _isFirst = true;
            return effectDescription.DrawDescription;
        }

        RenderInput(new Vortice.RawRectF(bounds.Left, bounds.Top, bounds.Left + itemWidth, bounds.Top + itemHeight));

        var pipelineParameters = new InkDispersionPipeline.Parameters(
            parameters.Quality,
            Math.Clamp(parameters.Spread, 0f, 1f),
            Math.Clamp(parameters.Water, 0f, 1f),
            Math.Clamp(parameters.Fiber, 0f, 1f),
            Math.Clamp(parameters.Sizing, 0f, 1f),
            Math.Clamp(parameters.Viscosity, 0f, 1f),
            Math.Clamp(parameters.Drying, 0f, 1f),
            (float)reachPixels,
            Math.Clamp(parameters.Density, 0f, 1f),
            Math.Clamp(parameters.Grain, 0f, 1f),
            parameters.Color.R / 255f,
            parameters.Color.G / 255f,
            parameters.Color.B / 255f,
            Math.Max(parameters.Seed, 0));

        var structureChanged = _pipeline.Simulate(
            _resourceSet.GetSourceComputeBinding(),
            canvasWidth,
            canvasHeight,
            margin,
            margin,
            itemWidth,
            itemHeight,
            in pipelineParameters);

        if (!_pipeline.TryGetVisibleBounds(canvasWidth, canvasHeight, in pipelineParameters, out var rect))
        {
            _effect.Amount = 0f;
            _parameters = parameters;
            _isFirst = true;
            _hasRenderState = false;
            return effectDescription.DrawDescription;
        }

        if (!OutputCovers(rect.Width, rect.Height))
            _outputCrop.SetInput(0, null, true);
        if (!EnsureOutput(rect.Width, rect.Height, out var outputChanged))
        {
            _effect.Amount = 0f;
            _parameters = parameters;
            _isFirst = true;
            _hasRenderState = false;
            return effectDescription.DrawDescription;
        }
        var renderState = new RenderState(
            pipelineParameters.Spread,
            pipelineParameters.Density,
            pipelineParameters.Grain,
            pipelineParameters.ColorR,
            pipelineParameters.ColorG,
            pipelineParameters.ColorB,
            rect);
        if (structureChanged || outputChanged || !_hasOutput || !_hasRenderState || _renderState != renderState)
        {
            _pipeline.RenderVisible(
                _resourceSet.GetOutputComputeBinding(),
                canvasWidth,
                canvasHeight,
                rect,
                in pipelineParameters);
            _renderState = renderState;
            _hasRenderState = true;
        }

        _outputLease ??= _resourceSet.AcquireOutputExternalViewLease();

        if (outputChanged || !_hasOutput)
        {
            using var outputBitmap = new ID2D1Bitmap1(_outputLease.DangerousGetView().AddRefBitmap());
            _outputCrop.SetInput(0, outputBitmap, true);
            _effect.SetInput(1, _outputTransformOutput, true);
        }
        var cropRect = new Vector4(0f, 0f, rect.Width, rect.Height);
        if (!_hasCropRect || _cropRect != cropRect)
        {
            _outputCrop.Rectangle = cropRect;
            _cropRect = cropRect;
            _hasCropRect = true;
        }
        var outputOffset = new Vector2(bounds.Left - margin + rect.X, bounds.Top - margin + rect.Y);
        if (!_hasOutputOffset || _outputOffset != outputOffset)
        {
            _outputTransform.TransformMatrix = Matrix3x2.CreateTranslation(outputOffset);
            _outputOffset = outputOffset;
            _hasOutputOffset = true;
        }
        _hasOutput = true;
        _parameters = parameters;
        _isFirst = false;
        return effectDescription.DrawDescription;
    }

    private bool EnsureSource(int width, int height)
    {
        return _resourceSet!.TryEnsureSource(width, height, out _);
    }

    private bool OutputCovers(int width, int height)
        => _outputLease is { IsDisposed: false } lease &&
           lease.Width >= width &&
           lease.Height >= height;

    private bool EnsureOutput(int width, int height, out bool changed)
    {
        changed = false;
        if (OutputCovers(width, height))
            return true;

        _outputLease?.Dispose();
        _outputLease = null;
        return _resourceSet!.TryEnsureOutput(width, height, out changed);
    }

    private void RenderInput(Vortice.RawRectF bounds)
    {
        var renderContext = _interopProvider!.RenderContext;
        using var borrow = _resourceSet!.BeginSourceExternalOperation();
        var previousTarget = renderContext.Target;
        using var sourceBitmap = new ID2D1Bitmap1(borrow.DangerousGetView().AddRefBitmap());
        renderContext.Target = sourceBitmap;
        renderContext.BeginDraw();
        renderContext.Clear(null);
        renderContext.DrawImage(
            input,
            new Vector2(-bounds.Left, -bounds.Top),
            null,
            InterpolationMode.NearestNeighbor,
            CompositeMode.SourceCopy);
        renderContext.EndDraw();
        renderContext.Target = previousTarget;
    }

    private void ReleaseInterop()
    {
        _outputLease?.Dispose();
        _outputLease = null;
        _pipeline?.Dispose();
        _pipeline = null;
        _resourceSet?.Dispose();
        _resourceSet?.WaitForDisposal();
        _resourceSet = null;
        _interopDomain?.Dispose();
        _interopDomain?.WaitForDisposal();
        _interopDomain = null;
        _interopProvider?.Dispose();
        _interopProvider = null;
        _scheduler?.Dispose();
        _scheduler = null;
    }

    protected override ID2D1Image? CreateEffect(IGraphicsDevicesAndContext devices)
    {
        var scheduler = ComputeExternalQueueScheduler.Create();
        var interopProvider = InkDispersionInteropProvider.TryCreate(devices, scheduler, out var interopDevice);
        if (interopProvider is null || interopDevice is null)
        {
            scheduler.Dispose();
            return null;
        }

        _scheduler = scheduler;

        try
        {
            _interopProvider = interopProvider;
            _interopDomain = interopDevice.RegisterExternalDomain(interopProvider);
            _resourceSet = InkDispersionResourceSet.Create(interopDevice, _interopDomain);
            _pipeline = InkDispersionPipeline.TryCreate(interopDevice);
        }
        catch (Exception exception)
        {
            InkDispersionTelemetry.Report(exception);
            ReleaseInterop();
            throw;
        }

        if (_pipeline is null)
        {
            ReleaseInterop();
            return null;
        }

        InkDispersionCustomEffect? effect = null;
        Crop? outputCrop = null;
        ID2D1Image? outputCropOutput = null;
        AffineTransform2D? outputTransform = null;
        ID2D1Image? outputTransformOutput = null;
        ID2D1Image? output = null;
        try
        {
            effect = new InkDispersionCustomEffect(devices);
            if (!effect.IsEnabled)
            {
                effect.Dispose();
                ReleaseInterop();
                return null;
            }
            outputCrop = new Crop(devices.DeviceContext);
            outputCropOutput = outputCrop.Output;
            outputTransform = new AffineTransform2D(devices.DeviceContext)
            {
                BorderMode = BorderMode.Hard,
            };
            outputTransform.SetInput(0, outputCropOutput, true);
            outputTransformOutput = outputTransform.Output;
            output = effect.Output;
            _effect = effect;
            _outputCrop = outputCrop;
            _outputCropOutput = outputCropOutput;
            _outputTransform = outputTransform;
            _outputTransformOutput = outputTransformOutput;
            disposer.Collect(effect);
            disposer.Collect(outputCrop);
            disposer.Collect(outputCropOutput);
            disposer.Collect(outputTransform);
            disposer.Collect(outputTransformOutput);
            disposer.Collect(output);
            return output;
        }
        catch (Exception exception)
        {
            InkDispersionTelemetry.Report(exception);
            output?.Dispose();
            outputTransformOutput?.Dispose();
            outputTransform?.Dispose();
            outputCropOutput?.Dispose();
            outputCrop?.Dispose();
            effect?.Dispose();
            ReleaseInterop();
            throw;
        }
    }

    protected override void setInput(ID2D1Image? inputImage)
    {
        _effect?.SetInput(0, inputImage, true);
        if (!_hasOutput)
            _effect?.SetInput(1, inputImage, true);
    }

    protected override void ClearEffectChain()
    {
        try
        {
            ClearEffectChainCore();
        }
        catch (Exception exception)
        {
            InkDispersionTelemetry.Report(exception);
            throw;
        }
    }

    private void ClearEffectChainCore()
    {
        _effect?.SetInput(0, null, true);
        _effect?.SetInput(1, null, true);
        _outputCrop?.SetInput(0, null, true);
        _isFirst = true;
        _hasOutput = false;
        _hasOutputOffset = false;
        _hasCropRect = false;
        _hasRenderState = false;
    }

    protected override void Dispose(bool disposing)
    {
        try
        {
            if (disposing)
            {
                ClearEffectChain();
                ReleaseInterop();
            }
        }
        finally
        {
            base.Dispose(disposing);
        }
    }

    private readonly record struct RenderState(
        float Spread,
        float Density,
        float Grain,
        float ColorR,
        float ColorG,
        float ColorB,
        InkDispersionPipeline.PixelRect Rect);

    private readonly record struct Parameters(
        float Amount,
        float Spread,
        InkDispersionQuality Quality,
        float Water,
        float Reach,
        float Fiber,
        float Sizing,
        float Viscosity,
        float Drying,
        float Density,
        float Grain,
        System.Windows.Media.Color Color,
        int Seed);
}
