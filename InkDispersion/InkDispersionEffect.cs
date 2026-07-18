using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Windows.Media;
using YukkuriMovieMaker.Commons;
using YukkuriMovieMaker.Controls;
using YukkuriMovieMaker.Exo;
using YukkuriMovieMaker.Player.Video;
using YukkuriMovieMaker.Plugin.Effects;

namespace InkDispersion;

[VideoEffect(nameof(Texts.InkDispersion), [VideoEffectCategories.Decoration, VideoEffectCategories.Animation], [nameof(Texts.TagInk), nameof(Texts.TagBlot), nameof(Texts.TagJapanese)], IsAviUtlSupported = false, ResourceType = typeof(Texts))]
public sealed class InkDispersionEffect : VideoEffectBase
{
    public override string Label => Texts.InkDispersion;

    public InkDispersionEffect()
    {
        InkDispersionUpdateNotifier.EnsureCheckedOnce();
    }

    [Display(GroupName = nameof(Texts.BasicGroup), Name = nameof(Texts.Amount), Description = nameof(Texts.AmountDescription), Order = 0, ResourceType = typeof(Texts))]
    [AnimationSlider("F1", "%", 0, 100)]
    public Animation Amount { get; } = new Animation(100, 0, 100);

    [Display(GroupName = nameof(Texts.BasicGroup), Name = nameof(Texts.Spread), Description = nameof(Texts.SpreadDescription), Order = 1, ResourceType = typeof(Texts))]
    [AnimationSlider("F1", "%", 0, 100)]
    public Animation Spread { get; } = new Animation(100, 0, 100);

    [Display(GroupName = nameof(Texts.BasicGroup), Name = nameof(Texts.Quality), Description = nameof(Texts.QualityDescription), Order = 2, ResourceType = typeof(Texts))]
    [EnumComboBox]
    public InkDispersionQuality Quality { get => _quality; set => Set(ref _quality, value); }
    private InkDispersionQuality _quality = InkDispersionQuality.High;

    [Display(GroupName = nameof(Texts.FlowGroup), Name = nameof(Texts.Water), Description = nameof(Texts.WaterDescription), Order = 10, ResourceType = typeof(Texts))]
    [AnimationSlider("F1", "%", 0, 100)]
    public Animation Water { get; } = new Animation(60, 0, 100);

    [Display(GroupName = nameof(Texts.FlowGroup), Name = nameof(Texts.Reach), Description = nameof(Texts.ReachDescription), Order = 11, ResourceType = typeof(Texts))]
    [AnimationSlider("F1", "%", 5, 100)]
    public Animation Reach { get; } = new Animation(25, 1, 400);

    [Display(GroupName = nameof(Texts.FlowGroup), Name = nameof(Texts.Fiber), Description = nameof(Texts.FiberDescription), Order = 12, ResourceType = typeof(Texts))]
    [AnimationSlider("F1", "%", 0, 100)]
    public Animation Fiber { get; } = new Animation(50, 0, 100);

    [Display(GroupName = nameof(Texts.FlowGroup), Name = nameof(Texts.Sizing), Description = nameof(Texts.SizingDescription), Order = 13, ResourceType = typeof(Texts))]
    [AnimationSlider("F1", "%", 0, 100)]
    public Animation Sizing { get; } = new Animation(30, 0, 100);

    [Display(GroupName = nameof(Texts.FlowGroup), Name = nameof(Texts.Viscosity), Description = nameof(Texts.ViscosityDescription), Order = 14, ResourceType = typeof(Texts))]
    [AnimationSlider("F1", "%", 0, 100)]
    public Animation Viscosity { get; } = new Animation(30, 0, 100);

    [Display(GroupName = nameof(Texts.FlowGroup), Name = nameof(Texts.Drying), Description = nameof(Texts.DryingDescription), Order = 15, ResourceType = typeof(Texts))]
    [AnimationSlider("F1", "%", 0, 100)]
    public Animation Drying { get; } = new Animation(40, 0, 100);

    [Display(GroupName = nameof(Texts.FlowGroup), Name = nameof(Texts.Seed), Description = nameof(Texts.SeedDescription), Order = 16, ResourceType = typeof(Texts))]
    [Range(0, int.MaxValue)]
    [DefaultValue(0)]
    [TextBoxSlider("F0", "", 0, 10000)]
    public int Seed
    {
        get => _seed;
        set => Set(ref _seed, Math.Max(value, 0));
    }
    private int _seed;

    [Display(GroupName = nameof(Texts.AppearanceGroup), Name = nameof(Texts.Density), Description = nameof(Texts.DensityDescription), Order = 20, ResourceType = typeof(Texts))]
    [AnimationSlider("F1", "%", 0, 100)]
    public Animation Density { get; } = new Animation(80, 0, 100);

    [Display(GroupName = nameof(Texts.AppearanceGroup), Name = nameof(Texts.Grain), Description = nameof(Texts.GrainDescription), Order = 21, ResourceType = typeof(Texts))]
    [AnimationSlider("F1", "%", 0, 100)]
    public Animation Grain { get; } = new Animation(40, 0, 100);

    [Display(GroupName = nameof(Texts.AppearanceGroup), Name = nameof(Texts.InkColor), Description = nameof(Texts.InkColorDescription), Order = 22, ResourceType = typeof(Texts))]
    [ColorPicker]
    public Color InkColor
    {
        get => _inkColor;
        set => Set(ref _inkColor, value);
    }
    private Color _inkColor = Color.FromArgb(255, 30, 26, 24);

    private IAnimatable[]? _animatables;

    public override IEnumerable<string> CreateExoVideoFilters(int keyFrameIndex, ExoOutputDescription exoOutputDescription) => [];

    public override IVideoEffectProcessor CreateVideoEffect(IGraphicsDevicesAndContext devices)
        => new InkDispersionEffectProcessor(devices, this);

    protected override IEnumerable<IAnimatable> GetAnimatables()
        => _animatables ??= [Amount, Spread, Water, Reach, Fiber, Sizing, Viscosity, Drying, Density, Grain];
}
