using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Reflection;
using System.Windows.Media;
using YukkuriMovieMaker.Commons;
using YukkuriMovieMaker.Controls;
using YukkuriMovieMaker.Exo;
using YukkuriMovieMaker.Json;
using YukkuriMovieMaker.Plugin.Effects;
using YukkuriMovieMaker.Project;

namespace InkDispersion.Tests;

public sealed class InkDispersionEffectTests
{
    static readonly Color DefaultInkColor = Color.FromArgb(255, 30, 26, 24);

    static PropertyInfo Property(string name) => typeof(InkDispersionEffect).GetProperty(name)!;

    static T Attribute<T>(string property) where T : Attribute => Property(property).GetCustomAttribute<T>()!;

    static Animation[] Animations(InkDispersionEffect effect)
        => [effect.Amount, effect.Spread, effect.Water, effect.Reach, effect.Fiber, effect.Sizing, effect.Viscosity, effect.Drying, effect.Density, effect.Grain];

    [Theory]
    [InlineData(nameof(InkDispersionEffect.Amount), 100d, 0d, 100d)]
    [InlineData(nameof(InkDispersionEffect.Spread), 100d, 0d, 100d)]
    [InlineData(nameof(InkDispersionEffect.Water), 60d, 0d, 100d)]
    [InlineData(nameof(InkDispersionEffect.Reach), 25d, 1d, 400d)]
    [InlineData(nameof(InkDispersionEffect.Fiber), 50d, 0d, 100d)]
    [InlineData(nameof(InkDispersionEffect.Sizing), 30d, 0d, 100d)]
    [InlineData(nameof(InkDispersionEffect.Viscosity), 30d, 0d, 100d)]
    [InlineData(nameof(InkDispersionEffect.Drying), 40d, 0d, 100d)]
    [InlineData(nameof(InkDispersionEffect.Density), 80d, 0d, 100d)]
    [InlineData(nameof(InkDispersionEffect.Grain), 40d, 0d, 100d)]
    public void AnimatedParametersStartFromTheirDefaultsWithinTheirRange(string name, double defaultValue, double minimum, double maximum)
    {
        var effect = new InkDispersionEffect();

        var animation = (Animation)Property(name).GetValue(effect)!;

        Assert.Equal(defaultValue, animation.DefaultValue);
        Assert.Equal(minimum, animation.MinValue);
        Assert.Equal(maximum, animation.MaxValue);
        Assert.Equal(defaultValue, animation.GetValue(0, 1, EffectDescriptions.Fps));
    }

    [Fact]
    public void QualitySeedAndInkColorStartFromTheirDefaults()
    {
        var effect = new InkDispersionEffect();

        Assert.Equal(InkDispersionQuality.High, effect.Quality);
        Assert.Equal(0, effect.Seed);
        Assert.Equal(DefaultInkColor, effect.InkColor);
    }

    [Theory]
    [InlineData(int.MinValue, 0)]
    [InlineData(-1, 0)]
    [InlineData(0, 0)]
    [InlineData(1234, 1234)]
    [InlineData(10000, 10000)]
    [InlineData(int.MaxValue, int.MaxValue)]
    public void SeedNeverDropsBelowZero(int value, int expected)
    {
        var effect = new InkDispersionEffect { Seed = 5 };

        effect.Seed = value;

        Assert.Equal(expected, effect.Seed);
        Assert.False(effect.HasErrors);
    }

    [Fact]
    public void ChangingQualitySeedOrInkColorNotifiesTheEditor()
    {
        var effect = new InkDispersionEffect();
        var changed = new List<string?>();
        effect.PropertyChanged += (_, e) => changed.Add(e.PropertyName);

        effect.Quality = InkDispersionQuality.Ultra;
        effect.Seed = 7;
        effect.InkColor = Colors.Crimson;

        Assert.Equal([nameof(InkDispersionEffect.Quality), nameof(InkDispersionEffect.Seed), nameof(InkDispersionEffect.InkColor)], changed);
    }

    [Fact]
    public void AssigningAnUnchangedOrClampedValueDoesNotNotify()
    {
        var effect = new InkDispersionEffect();
        var changed = new List<string?>();
        effect.PropertyChanged += (_, e) => changed.Add(e.PropertyName);

        effect.Quality = InkDispersionQuality.High;
        effect.Seed = 0;
        effect.Seed = -1;
        effect.InkColor = DefaultInkColor;

        Assert.Empty(changed);
    }

    [Fact]
    public void TheLabelIsTheLocalizedEffectName()
    {
        var effect = new InkDispersionEffect();

        Assert.Equal(Texts.InkDispersion, effect.Label);
    }

    [Fact]
    public void TheTenNumericParametersReceiveTheAnimationParameters()
    {
        var effect = new InkDispersionEffect();

        effect.SetAnimationParameters(120, EffectDescriptions.Fps);

        Assert.All(Animations(effect), animation => Assert.Equal(120, animation.Length));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(int.MaxValue)]
    public void NoExoFilterIsWrittenForAviUtl(int keyFrameIndex)
    {
        var effect = new InkDispersionEffect();

        var description = new ExoOutputDescription(new VideoInfo(), string.Empty, new AviUtlDirectories(string.Empty, string.Empty));

        Assert.Empty(effect.CreateExoVideoFilters(keyFrameIndex, description));
    }

    [Fact]
    public void TheEffectIsRegisteredForDecorationAndAnimationWithoutAviUtlSupport()
    {
        var attribute = typeof(InkDispersionEffect).GetCustomAttribute<VideoEffectAttribute>()!;

        Assert.Equal(nameof(Texts.InkDispersion), attribute.Name);
        Assert.Equal([VideoEffectCategories.Decoration, VideoEffectCategories.Animation], attribute.Categories);
        Assert.Equal([nameof(Texts.TagInk), nameof(Texts.TagBlot), nameof(Texts.TagJapanese)], attribute.Keywords);
        Assert.False(attribute.IsAviUtlSupported);
        Assert.True(attribute.IsEffectItemSupported);
        Assert.Equal(typeof(Texts), attribute.ResourceType);
        Assert.Equal(Texts.InkDispersion, attribute.GetName());
    }

    [Theory]
    [InlineData(nameof(InkDispersionEffect.Amount), nameof(Texts.BasicGroup), nameof(Texts.Amount), nameof(Texts.AmountDescription), 0)]
    [InlineData(nameof(InkDispersionEffect.Spread), nameof(Texts.BasicGroup), nameof(Texts.Spread), nameof(Texts.SpreadDescription), 1)]
    [InlineData(nameof(InkDispersionEffect.Quality), nameof(Texts.BasicGroup), nameof(Texts.Quality), nameof(Texts.QualityDescription), 2)]
    [InlineData(nameof(InkDispersionEffect.Water), nameof(Texts.FlowGroup), nameof(Texts.Water), nameof(Texts.WaterDescription), 10)]
    [InlineData(nameof(InkDispersionEffect.Reach), nameof(Texts.FlowGroup), nameof(Texts.Reach), nameof(Texts.ReachDescription), 11)]
    [InlineData(nameof(InkDispersionEffect.Fiber), nameof(Texts.FlowGroup), nameof(Texts.Fiber), nameof(Texts.FiberDescription), 12)]
    [InlineData(nameof(InkDispersionEffect.Sizing), nameof(Texts.FlowGroup), nameof(Texts.Sizing), nameof(Texts.SizingDescription), 13)]
    [InlineData(nameof(InkDispersionEffect.Viscosity), nameof(Texts.FlowGroup), nameof(Texts.Viscosity), nameof(Texts.ViscosityDescription), 14)]
    [InlineData(nameof(InkDispersionEffect.Drying), nameof(Texts.FlowGroup), nameof(Texts.Drying), nameof(Texts.DryingDescription), 15)]
    [InlineData(nameof(InkDispersionEffect.Seed), nameof(Texts.FlowGroup), nameof(Texts.Seed), nameof(Texts.SeedDescription), 16)]
    [InlineData(nameof(InkDispersionEffect.Density), nameof(Texts.AppearanceGroup), nameof(Texts.Density), nameof(Texts.DensityDescription), 20)]
    [InlineData(nameof(InkDispersionEffect.Grain), nameof(Texts.AppearanceGroup), nameof(Texts.Grain), nameof(Texts.GrainDescription), 21)]
    [InlineData(nameof(InkDispersionEffect.InkColor), nameof(Texts.AppearanceGroup), nameof(Texts.InkColor), nameof(Texts.InkColorDescription), 22)]
    public void EveryParameterIsDisplayedInItsGroupInOrder(string property, string group, string name, string description, int order)
    {
        var display = Attribute<DisplayAttribute>(property);

        Assert.Equal(group, display.GroupName);
        Assert.Equal(name, display.Name);
        Assert.Equal(description, display.Description);
        Assert.Equal(order, display.Order);
        Assert.Equal(typeof(Texts), display.ResourceType);
    }

    [Theory]
    [InlineData(nameof(InkDispersionEffect.Amount), 0d, 100d)]
    [InlineData(nameof(InkDispersionEffect.Spread), 0d, 100d)]
    [InlineData(nameof(InkDispersionEffect.Water), 0d, 100d)]
    [InlineData(nameof(InkDispersionEffect.Reach), 5d, 100d)]
    [InlineData(nameof(InkDispersionEffect.Fiber), 0d, 100d)]
    [InlineData(nameof(InkDispersionEffect.Sizing), 0d, 100d)]
    [InlineData(nameof(InkDispersionEffect.Viscosity), 0d, 100d)]
    [InlineData(nameof(InkDispersionEffect.Drying), 0d, 100d)]
    [InlineData(nameof(InkDispersionEffect.Density), 0d, 100d)]
    [InlineData(nameof(InkDispersionEffect.Grain), 0d, 100d)]
    public void AnimatedParametersAreEditedAsPercentagesWithAnimationSliders(string property, double minimum, double maximum)
    {
        var slider = Attribute<AnimationSliderAttribute>(property);

        Assert.Equal("F1", slider.StringFormat);
        Assert.Equal("%", slider.UnitText);
        Assert.Equal(minimum, slider.DefaultMin);
        Assert.Equal(maximum, slider.DefaultMax);
    }

    [Fact]
    public void TheQualityIsChosenFromACombo()
    {
        Assert.NotNull(Attribute<EnumComboBoxAttribute>(nameof(InkDispersionEffect.Quality)));
        Assert.Equal([InkDispersionQuality.Balanced, InkDispersionQuality.High, InkDispersionQuality.Ultra], Enum.GetValues<InkDispersionQuality>());
    }

    [Theory]
    [InlineData(InkDispersionQuality.Balanced, nameof(Texts.QualityBalanced), nameof(Texts.QualityBalancedDescription))]
    [InlineData(InkDispersionQuality.High, nameof(Texts.QualityHigh), nameof(Texts.QualityHighDescription))]
    [InlineData(InkDispersionQuality.Ultra, nameof(Texts.QualityUltra), nameof(Texts.QualityUltraDescription))]
    public void EveryQualityIsDisplayedWithItsLocalizedName(InkDispersionQuality quality, string name, string description)
    {
        var display = typeof(InkDispersionQuality).GetField(quality.ToString())!.GetCustomAttribute<DisplayAttribute>()!;

        Assert.Equal(name, display.Name);
        Assert.Equal(description, display.Description);
        Assert.Equal(typeof(Texts), display.ResourceType);
    }

    [Fact]
    public void TheSeedIsEditedWithoutAUnitFromZero()
    {
        var slider = Attribute<TextBoxSliderAttribute>(nameof(InkDispersionEffect.Seed));
        var range = Attribute<RangeAttribute>(nameof(InkDispersionEffect.Seed));

        Assert.Equal("F0", slider.StringFormat);
        Assert.Equal(string.Empty, slider.UnitText);
        Assert.Equal(0d, slider.DefaultMin);
        Assert.Equal(10000d, slider.DefaultMax);
        Assert.Equal(0, range.Minimum);
        Assert.Equal(int.MaxValue, range.Maximum);
        Assert.Equal(0, Attribute<DefaultValueAttribute>(nameof(InkDispersionEffect.Seed)).Value);
    }

    [Fact]
    public void TheInkColorIsPickedWithAColorPicker()
    {
        Assert.NotNull(Attribute<ColorPickerAttribute>(nameof(InkDispersionEffect.InkColor)));
    }

    [Fact]
    public void EverySettingSurvivesAProjectRoundTrip()
    {
        var effect = new InkDispersionEffect { Quality = InkDispersionQuality.Ultra, Seed = 42, InkColor = Colors.Crimson };
        var values = new[] { 55d, 45d, 70d, 120d, 20d, 65d, 15d, 85d, 35d, 90d };
        foreach (var (animation, value) in Animations(effect).Zip(values))
            animation.Values[0].Value = value;

        var clone = Json.GetClone(effect)!;

        Assert.NotSame(effect, clone);
        Assert.Equal(InkDispersionQuality.Ultra, clone.Quality);
        Assert.Equal(42, clone.Seed);
        Assert.Equal(Colors.Crimson, clone.InkColor);
        Assert.Equal(values, Animations(clone).Select(animation => animation.GetValue(0, 1, EffectDescriptions.Fps)));
    }
}
