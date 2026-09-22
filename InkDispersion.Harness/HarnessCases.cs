using System.Windows.Media;

namespace InkDispersion.Harness;

internal static class HarnessCases
{
    public static IEnumerable<(string Name, InkDispersionEffect Effect, IReadOnlyList<int> Frames)> All()
    {
        yield return ("default", Create(), [0]);
        yield return ("default-frames-0-8", Create(), Enumerable.Range(0, 9).ToArray());
        yield return ("amount-0", Create(effect => effect.Amount.Values[0].Value = 0), [0]);
        yield return ("spread-35", Create(effect => effect.Spread.Values[0].Value = 35), [0]);
        yield return ("quality-balanced", Create(effect => effect.Quality = InkDispersionQuality.Balanced), [0]);
        yield return ("quality-ultra", Create(effect => effect.Quality = InkDispersionQuality.Ultra), [0]);
        yield return ("water-0", Create(effect => effect.Water.Values[0].Value = 0), [0]);
        yield return ("water-100", Create(effect => effect.Water.Values[0].Value = 100), [0]);
        yield return ("reach-5", Create(effect => effect.Reach.Values[0].Value = 5), [0]);
        yield return ("reach-100", Create(effect => effect.Reach.Values[0].Value = 100), [0]);
        yield return ("fiber-0", Create(effect => effect.Fiber.Values[0].Value = 0), [0]);
        yield return ("fiber-100", Create(effect => effect.Fiber.Values[0].Value = 100), [0]);
        yield return ("sizing-0", Create(effect => effect.Sizing.Values[0].Value = 0), [0]);
        yield return ("sizing-100", Create(effect => effect.Sizing.Values[0].Value = 100), [0]);
        yield return ("viscosity-0", Create(effect => effect.Viscosity.Values[0].Value = 0), [0]);
        yield return ("viscosity-100", Create(effect => effect.Viscosity.Values[0].Value = 100), [0]);
        yield return ("drying-0", Create(effect => effect.Drying.Values[0].Value = 0), [0]);
        yield return ("drying-100", Create(effect => effect.Drying.Values[0].Value = 100), [0]);
        yield return ("density-20", Create(effect => effect.Density.Values[0].Value = 20), [0]);
        yield return ("density-100", Create(effect => effect.Density.Values[0].Value = 100), [0]);
        yield return ("grain-0", Create(effect => effect.Grain.Values[0].Value = 0), [0]);
        yield return ("grain-100", Create(effect => effect.Grain.Values[0].Value = 100), [0]);
        yield return ("seed-42", Create(effect => effect.Seed = 42), [0]);
        yield return ("ink-color", Create(effect => effect.InkColor = Color.FromArgb(255, 200, 20, 20)), [0]);
    }

    public static IEnumerable<(string Name, Func<InkDispersionEffect> Create, Action<InkDispersionEffect> Change, int Frame)> Transitions()
    {
        yield return ("spread-100-to-35", () => Create(), effect => effect.Spread.Values[0].Value = 35, 0);
        yield return ("quality-high-to-ultra", () => Create(), effect => effect.Quality = InkDispersionQuality.Ultra, 0);
        yield return ("water-60-to-100", () => Create(), effect => effect.Water.Values[0].Value = 100, 0);
        yield return ("reach-25-to-100", () => Create(), effect => effect.Reach.Values[0].Value = 100, 0);
        yield return ("fiber-50-to-100", () => Create(), effect => effect.Fiber.Values[0].Value = 100, 0);
        yield return ("sizing-30-to-100", () => Create(), effect => effect.Sizing.Values[0].Value = 100, 0);
        yield return ("viscosity-30-to-100", () => Create(), effect => effect.Viscosity.Values[0].Value = 100, 0);
        yield return ("drying-40-to-100", () => Create(), effect => effect.Drying.Values[0].Value = 100, 0);
        yield return ("density-80-to-20", () => Create(), effect => effect.Density.Values[0].Value = 20, 0);
        yield return ("grain-40-to-100", () => Create(), effect => effect.Grain.Values[0].Value = 100, 0);
        yield return ("seed-0-to-42", () => Create(), effect => effect.Seed = 42, 0);
        yield return ("ink-color-change", () => Create(), effect => effect.InkColor = Color.FromArgb(255, 200, 20, 20), 0);
        yield return ("amount-100-to-0", () => Create(), effect => effect.Amount.Values[0].Value = 0, 0);
    }

    public static IEnumerable<(string Name, InkDispersionEffect Effect)> Benchmarks()
    {
        yield return ("quality-balanced", Create(effect => effect.Quality = InkDispersionQuality.Balanced));
        yield return ("default", Create());
        yield return ("quality-ultra", Create(effect => effect.Quality = InkDispersionQuality.Ultra));
        yield return ("reach-100", Create(effect => effect.Reach.Values[0].Value = 100));
        yield return ("amount-0", Create(effect => effect.Amount.Values[0].Value = 0));
    }

    public static InkDispersionEffect Create(Action<InkDispersionEffect>? configure = null)
    {
        var effect = new InkDispersionEffect();
        configure?.Invoke(effect);
        return effect;
    }
}
