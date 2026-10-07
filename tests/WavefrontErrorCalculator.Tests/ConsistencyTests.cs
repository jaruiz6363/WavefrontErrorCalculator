using WavefrontErrorCalculator.Core;
using Xunit;

namespace WavefrontErrorCalculator.Tests;

/// <summary>
/// Properties every wavefront must have whatever the lens (method.md §9.2), checked on a lens
/// with real aberrations: the double Gauss at full field.
/// </summary>
public class ConsistencyTests
{
    private static readonly Lazy<LensModel> DoubleGauss =
        new(() => LensModel.Read(Path.Combine(AppContext.BaseDirectory, "TestData", "KingslakeDG.zmx")));

    private static int FullField(LensModel lens) =>
        lens.System.Fields.Select((f, i) => (Math.Abs(f.Y), i)).Max().Item2;

    private static WavefrontResult Full(WavefrontOptions options, Sampling? sampling = null)
    {
        var lens = DoubleGauss.Value;
        return WavefrontCalculator.Compute(lens, FullField(lens), lens.PrimaryWavelength, options,
                                           sampling ?? new Sampling.SquareGrid(24));
    }

    /// <summary>
    /// The exit pupil measured on real rays of a vanishing field is the paraxial exit pupil
    /// AberrationCalculator's paraxial trace reports - same plane, independent routes.
    /// </summary>
    [Fact]
    public void TheParaxialExitPupilMatchesTheParaxialTrace()
    {
        var lens = DoubleGauss.Value;
        double z = WavefrontCalculator.ParaxialExitPupilZ(lens);
        double expected = lens.Paraxial.ExitPupilPosition;
        Assert.True(Math.Abs(z - expected) < 1e-6 * Math.Abs(expected), $"{z:G12} against {expected:G12}");
    }

    /// <summary>The chief ray is its own reference, so its W is zero by construction.</summary>
    [Fact]
    public void TheChiefRayHasNoAberration()
    {
        var r = Full(WavefrontOptions.Reference, new Sampling.Fan(FanDirection.Tangential, 3));
        var middle = r.Samples[1];
        Assert.Equal(0.0, middle.Py);
        Assert.True(Math.Abs(middle.W) < 1e-9, $"{middle.W:E3}");
    }

    [Fact]
    public void WolfsSignIsHopkinsTurnedOver()
    {
        var hopkins = Full(WavefrontOptions.Reference);
        var wolf = Full(WavefrontOptions.Reference with { Sign = WavefrontSign.Wolf });
        for (int k = 0; k < hopkins.Samples.Count; k++)
            Assert.Equal(-hopkins.Samples[k].W, wolf.Samples[k].W, 12);
    }

    /// <summary>⟨W²⟩ is the variance plus the square of the mean, whatever the weights.</summary>
    [Theory]
    [InlineData(Weighting.PerRay)]
    [InlineData(Weighting.ExitArea)]
    public void TheRmsAboutZeroIsTheStandardDeviationAndThePistonTogether(Weighting weighting)
    {
        var s = Full(WavefrontOptions.Reference with { Weighting = weighting }).Statistics;
        Assert.Equal(s.RmsAboutZero * s.RmsAboutZero, s.RmsStandardDeviation * s.RmsStandardDeviation + s.Mean * s.Mean, 10);
        Assert.True(s.RmsStandardDeviation > 0.01, "the double Gauss at full field is not perfect");
    }

    /// <summary>
    /// Moving E′ along the chief ray changes the reference sphere's radius, and so W, by no more
    /// than about N′θ²δR/2 with θ the angular aberration (Welford §7.4): small, and much smaller
    /// than W itself. The real exit pupil and the paraxial one differ by such a move.
    /// </summary>
    [Fact]
    public void TheExitPupilsChoiceIsASmallEffect()
    {
        var real = Full(WavefrontOptions.Reference with { ExitPupil = ExitPupil.RealChief });
        var paraxial = Full(WavefrontOptions.Reference with { ExitPupil = ExitPupil.ParaxialChiefIntersect });
        double largest = real.Samples.Where(x => !x.Vignetted).Max(x => Math.Abs(x.W));
        double change = real.Samples.Zip(paraxial.Samples).Where(p => !p.First.Vignetted)
                                    .Max(p => Math.Abs(p.First.W - p.Second.W));
        Assert.True(change > 0.0);
        Assert.True(change < 0.05 * largest, $"changed by {change:E3} of {largest:E3} waves");
    }
}
