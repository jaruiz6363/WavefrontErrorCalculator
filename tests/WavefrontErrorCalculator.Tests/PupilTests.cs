using AberrationCalculator.Core.Enums;
using WavefrontErrorCalculator.Core;
using Xunit;

namespace WavefrontErrorCalculator.Tests;

/// <summary>
/// Vignetting, the pupil it leaves, and the coordinates that make that pupil a circle
/// (method.md §5.4, §3.2). The double Gauss is given fixed apertures at its first and last
/// surfaces, set just large enough for the axial beam, so its oblique pencils are cut top and
/// bottom by different surfaces - the usual way a pencil is vignetted.
/// </summary>
public class PupilTests
{
    private static readonly Lazy<LensModel> Vignetted = new(() =>
    {
        var lens = LensModel.Read(Path.Combine(AppContext.BaseDirectory, "TestData", "KingslakeDG.zmx"));
        int last = lens.System.LastOpticalSurface();
        foreach (int i in new[] { 1, last })
        {
            // The axial beam's height there, from its rim rays.
            double height = new[] { 1.0, -1.0 }.Max(py =>
            {
                var hits = AberrationCalculator.Core.RayTrace.RealRayTrace.TraceRecord(
                    lens.System, lens.Indices(lens.PrimaryWavelength), lens.Paraxial, 0.0, py, 0.0, false);
                return Math.Abs(hits[i].Y);
            });
            lens.System.Surfaces[i].SemiDiameter = 1.01 * height;
            lens.System.Surfaces[i].SemiDiameterMode = SemiDiameterMode.Fixed;
        }
        return lens;
    });

    private static int FullField(LensModel lens) => lens.System.Fields.Select((f, i) => (Math.Abs(f.Y), i)).Max().Item2;

    [Fact]
    public void TheAxialPencilFillsThePupil()
    {
        var pupil = WavefrontCalculator.Explore(Vignetted.Value, 0.0, WavefrontOptions.Reference);
        Assert.False(pupil.IsVignetted);
        Assert.Equal(0.0, pupil.CenterPy, 9);
    }

    /// <summary>The explored rim is the rim: a ray on it passes and one just outside it does not.</summary>
    [Fact]
    public void AnObliquePencilIsCutAndItsRimIsWhereTheRaysStop()
    {
        var lens = Vignetted.Value;
        var o = WavefrontOptions.Reference;
        double f = lens.System.Fields[FullField(lens)].Y;
        var pupil = WavefrontCalculator.Explore(lens, f, o);
        Assert.True(pupil.IsVignetted);
        Assert.True(pupil.Upper < 1.0 || pupil.Lower > -1.0);
        Assert.NotEqual(0.0, pupil.CenterPy);

        var aimer = lens.Aimer(lens.PrimaryWavelength)!;
        bool Passes(double px, double py) =>
            aimer.Launch(f, py, px) is (double ly, double lx) && lens.Trace(f, lens.PrimaryWavelength, ly, lx, o.Apertures).Ok;
        Assert.True(Passes(0.0, pupil.Upper));
        if (pupil.Upper < 1.0) Assert.False(Passes(0.0, pupil.Upper + 1e-5));
        Assert.True(Passes(0.0, pupil.Lower));
        if (pupil.Lower > -1.0) Assert.False(Passes(0.0, pupil.Lower - 1e-5));
    }

    /// <summary>
    /// Sampled on the explored pupil, the Reference wavefront loses far fewer rays to the
    /// apertures than when the whole pupil is sampled, as a program whose chief ray is the stop's
    /// centre does. Not none: the explored pupil is an ellipse through the rim's extreme points
    /// (King 1968), and two apertures each just large enough for the axial beam cut the pencil
    /// into a cat's eye, pointed top and bottom, which that ellipse overshoots there. Rays it
    /// overshoots are stopped and left out like any other. The pencil's own chief ray has W = 0.
    /// </summary>
    [Fact]
    public void TheReferenceSamplesThePencilTheLensAccepts()
    {
        var lens = Vignetted.Value;
        int field = FullField(lens);
        var reference = WavefrontCalculator.Compute(lens, field, lens.PrimaryWavelength, WavefrontOptions.Reference, new Sampling.SquareGrid(32));
        var whole = WavefrontCalculator.Compute(lens, field, lens.PrimaryWavelength, WavefrontOptions.LensHHLT, new Sampling.SquareGrid(32));

        double lostInside = (double)reference.Statistics.Vignetted / reference.Samples.Count;
        double lostWhole = (double)whole.Statistics.Vignetted / whole.Samples.Count;
        Assert.True(lostWhole > 0.2, $"only {lostWhole:P1} of the whole pupil vignetted");
        Assert.True(lostInside < 0.5 * lostWhole, $"{lostInside:P1} lost inside the explored pupil, {lostWhole:P1} across the whole");
        Assert.Contains(whole.Samples, s => s.Vignetted && s.StoppedAt > 0);

        var chief = WavefrontCalculator.Compute(lens, field, lens.PrimaryWavelength, WavefrontOptions.Reference,
                                                new Sampling.Fan(FanDirection.Tangential, 3)).Samples[1];
        Assert.True(Math.Abs(chief.W) < 1e-9);
        Assert.Equal(reference.Pupil.CenterPy, chief.Py, 12);
    }

    /// <summary>
    /// In canonical coordinates the pupil is close to a unit circle (Hopkins 1964): the rim of
    /// the sampled ellipse lands within a few per cent of radius 1 on the exit sphere. Exactly 1
    /// only for an isoplanatic image; the remainder is the pupil's own aberration.
    /// </summary>
    [Fact]
    public void CanonicalCoordinatesMakeTheVignettedPupilNearlyACircle()
    {
        var lens = Vignetted.Value;
        int field = FullField(lens);
        var rim = Enumerable.Range(0, 24).Select(k => 2 * Math.PI * k / 24).Select(a => new PupilPoint(Math.Sin(a), Math.Cos(a), 0)).ToList();
        var r = WavefrontCalculator.Compute(lens, field, lens.PrimaryWavelength, WavefrontOptions.Reference with { Apertures = ApertureClipping.None },
                                            new Sampling.Fan(FanDirection.Tangential, 2));   // geometry only
        foreach (var p in rim)
        {
            var one = WavefrontCalculator.Compute(lens, field, lens.PrimaryWavelength, WavefrontOptions.Reference with { Apertures = ApertureClipping.None },
                                                  new Single(p)).Samples[0];
            double radius = Math.Sqrt(one.CanonicalX * one.CanonicalX + one.CanonicalY * one.CanonicalY);
            Assert.True(Math.Abs(radius - 1.0) < 0.1, $"rim point ({p.Px:F2}, {p.Py:F2}) at canonical radius {radius:F4}");
        }
        Assert.True(r.Geometry.HPrimeS > 0 && r.Geometry.HPrimeT != 0);
    }

    private sealed record Single(PupilPoint Point) : Sampling
    {
        public override IReadOnlyList<PupilPoint> Points() => new[] { Point };
    }

    [Fact]
    public void ZernikeFitsOfTheVignettedPencilWarnUnlessCanonical()
    {
        var lens = Vignetted.Value;
        var r = WavefrontCalculator.Compute(lens, FullField(lens), lens.PrimaryWavelength, WavefrontOptions.Reference, new Sampling.SquareGrid(32));
        var canonical = Zernike.Fit(r, ZernikeSet.Standard, 37);
        var launch = Zernike.Fit(r, ZernikeSet.Standard, 37, PupilCoordinates.Launch);
        Assert.DoesNotContain(canonical.Warnings, w => w.Contains("vignetted"));
        Assert.Contains(launch.Warnings, w => w.Contains("vignetted"));
        Assert.True(canonical.ResidualRms < 0.05 * r.Statistics.Rms, $"residual {canonical.ResidualRms:E3} of RMS {r.Statistics.Rms:E3}");
    }
}
