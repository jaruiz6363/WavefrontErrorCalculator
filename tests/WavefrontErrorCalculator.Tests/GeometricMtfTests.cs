using AberrationCalculator.Core.Enums;
using AberrationCalculator.Core.Glass;
using AberrationCalculator.Core.IO;
using AberrationCalculator.Core.Models;
using WavefrontErrorCalculator.Core;
using Xunit;

namespace WavefrontErrorCalculator.Tests;

/// <summary>
/// The geometric MTF by Kidger's summation, against closed forms and against the diffraction
/// reference as the wavelength goes to zero. Nothing here is taken from another program.
/// </summary>
public class GeometricMtfTests(Xunit.Abstractions.ITestOutputHelper log)
{
    private static string Path(string name) => System.IO.Path.Combine(AppContext.BaseDirectory, "TestData", name);

    /// <summary>
    /// A paraboloid of focal length 100 at f/25, collimated light on axis, the image plane
    /// <paramref name="defocus"/> beyond the focus. It has no aberration, so every ray passes
    /// through the focus and lands on the plane at defocus × its slope.
    /// </summary>
    private static LensModel Paraboloid(double defocus)
    {
        var sys = new OpticalSystem { Aperture = new Aperture(ApertureType.EPD, 4.0), FieldType = FieldType.ObjectAngle };
        sys.Wavelengths.Add(new Wavelength(0.55, 1.0, true));
        sys.Fields.Add(new Field(0.0));
        var surfaces = new[]
        {
            new Surface { Thickness = double.PositiveInfinity },
            new Surface { Curvature = -0.005, Conic = -1.0, Thickness = -100.0 - defocus, Material = "MIRROR", IsStop = true },
            new Surface(),
        };
        for (int i = 0; i < surfaces.Length; i++) surfaces[i].Index = i;
        sys.Surfaces.AddRange(surfaces);
        return new LensModel(sys, CatalogLocator.LoadBundled());
    }

    /// <summary>J₁ by its power series, ample for x up to 12.</summary>
    private static double BesselJ1(double x)
    {
        double term = x / 2.0, sum = term;
        for (int m = 1; m < 40; m++)
        {
            term *= -(x * x / 4.0) / (m * (m + 1.0));
            sum += term;
        }
        return sum;
    }

    // ── The pieces ──────────────────────────────────────────────────────────────────────

    /// <summary>Every ray stands for the same area: the mean of ρ² over the pattern is the disc's, ½, and it is symmetric.</summary>
    [Theory]
    [InlineData(1)]
    [InlineData(7)]
    [InlineData(30)]
    public void ThePupilPatternIsEvenAndSymmetric(int rings)
    {
        var pupil = GeometricMtf.Pupil(rings);
        Assert.Equal(0, pupil.Length % 4);
        Assert.Equal(0.5, pupil.Average(p => p.Px * p.Px + p.Py * p.Py), 12);
        Assert.Equal(0.0, pupil.Sum(p => p.Px), 9);
        Assert.Equal(0.0, pupil.Sum(p => p.Py), 9);
        Assert.Equal(pupil.Sum(p => p.Px * p.Px), pupil.Sum(p => p.Py * p.Py), 9);
        Assert.All(pupil, p => Assert.True(p.Px * p.Px + p.Py * p.Py < 1.0));
    }

    /// <summary>A point has an MTF of 1; two equal points d apart, |cos πfd|, along the line joining them only.</summary>
    [Fact]
    public void TheSumIsTheSpotsFourierTransform()
    {
        var point = new[] { new GeometricMtf.Spot(3.0, -2.0, 1.0) };
        Assert.Equal(new GeometricMtf.Modulation(1.0, 1.0), GeometricMtf.Geometric(point, 37.0));

        const double d = 0.01;
        var pair = new[] { new GeometricMtf.Spot(0.2, 5.0, 2.0), new GeometricMtf.Spot(0.2, 5.0 + d, 2.0) };
        foreach (double f in new[] { 10.0, 25.0, 50.0, 80.0 })
        {
            var m = GeometricMtf.Geometric(pair, f);
            Assert.Equal(Math.Abs(Math.Cos(Math.PI * f * d)), m.Tangential, 12);
            Assert.Equal(1.0, m.Sagittal, 12);
        }
    }

    /// <summary>
    /// The perfect lens in focus, as Haines and King tabulate it (Proc. SPIE 13, 105, 1968, Table I,
    /// the row δ = 0), to their four places - which are truncated, not rounded.
    /// </summary>
    [Theory]
    [InlineData(0.1, 0.8728)] [InlineData(0.2, 0.7470)] [InlineData(0.3, 0.6238)]
    [InlineData(0.4, 0.5046)] [InlineData(0.5, 0.3910)] [InlineData(0.6, 0.2847)]
    [InlineData(0.7, 0.1881)] [InlineData(0.8, 0.1040)] [InlineData(0.9, 0.0373)]
    public void ThePerfectLensMtfIsHainesAndKingsTable(double v, double mtf) =>
        Assert.Equal(mtf, Math.Floor(GeometricMtf.DiffractionLimit(v, 1.0) * 1e4) / 1e4, 9);

    /// <summary>The circle's MTF where it is known exactly, and zero from the cutoff on.</summary>
    [Fact]
    public void ThePerfectLensMtfOfACircularPupil()
    {
        Assert.Equal(1.0, GeometricMtf.DiffractionLimit(0.0, 100.0), 12);
        Assert.Equal(2.0 / Math.PI * (Math.PI / 3.0 - 0.5 * Math.Sqrt(0.75)), GeometricMtf.DiffractionLimit(50.0, 100.0), 12);
        Assert.Equal(0.0, GeometricMtf.DiffractionLimit(100.0, 100.0));
        Assert.Equal(0.0, GeometricMtf.DiffractionLimit(150.0, 100.0));
    }

    /// <summary>
    /// An annular pupil's MTF is the overlap of the annulus with itself shifted by 2v radii, over
    /// its area; counted on a fine grid, it is O'Neill's formula.
    /// </summary>
    [Theory]
    [InlineData(0.3)]
    [InlineData(0.6)]
    public void ThePerfectLensMtfOfAnAnnularPupilIsItsAutocorrelation(double e)
    {
        const int n = 1200;
        double h = 2.0 / n;
        var inside = new List<(double X, double Y)>();
        for (int i = 0; i < n; i++)
            for (int j = 0; j < n; j++)
            {
                double x = -1.0 + (i + 0.5) * h, y = -1.0 + (j + 0.5) * h, r2 = x * x + y * y;
                if (r2 <= 1.0 && r2 >= e * e) inside.Add((x, y));
            }
        foreach (double v in new[] { 0.05, 0.15, 0.25, 0.35, 0.5, 0.65, 0.8, 0.95 })
        {
            double s = 2.0 * v;
            int both = inside.Count(p => { double x = p.X - s, r2 = x * x + p.Y * p.Y; return r2 <= 1.0 && r2 >= e * e; });
            Assert.Equal((double)both / inside.Count, GeometricMtf.DiffractionLimit(v, 1.0, e), 2);
        }
    }

    // ── Lenses ──────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// The geometric MTF of a defocused perfect lens, 2 J₁(x)/x with x = 2π f r, r = defocus × NA
    /// the blur radius (Haines and King, Proc. SPIE 13, 105, 1968, Fig. 2, after Steel 1956, written
    /// there as x = 8πδ ν/ν₀ with δ = Δ sin²U/2λ and ν₀ = 2 sin U/λ). The defocused paraboloid at
    /// f/25 is that lens to 1e-4. End to end: the trace, Kidger's pattern and the sum together.
    /// </summary>
    [Fact]
    public void ADefocusedParaboloidHasTheUniformDiscsMtf()
    {
        const double defocus = 1.0;
        var lens = Paraboloid(defocus);
        double r = defocus * 2.0 / 100.0;
        var frequencies = Enumerable.Range(1, 16).Select(k => 5.0 * k).ToArray();   // x up to 10
        var result = GeometricMtf.Compute(lens, 0, frequencies, WavefrontOptions.Reference, new MtfOptions { Rings = 60 });
        Assert.Equal(result.RaysTraced, result.RaysArrived);
        for (int k = 0; k < frequencies.Length; k++)
        {
            double x = 2.0 * Math.PI * frequencies[k] * r;
            double disc = Math.Abs(2.0 * BesselJ1(x) / x);
            Assert.InRange(result.Mtf[k].Tangential - disc, -0.003, 0.003);
            Assert.InRange(result.Mtf[k].Sagittal - disc, -0.003, 0.003);
        }
    }

    /// <summary>
    /// In focus the paraboloid's spot is a point: the geometric MTF is 1 at every frequency, and the
    /// perfect lens's cutoff is 2 NA/λ, NA the sine of the real marginal ray's angle; Kidger's
    /// correction, asked for, is that perfect lens's MTF.
    /// </summary>
    [Fact]
    public void InFocusTheMtfIsOneAndTheCutoffIsTwiceTheNaOverTheWavelength()
    {
        var lens = Paraboloid(0.0);
        var r = GeometricMtf.Compute(lens, 0, new[] { 10.0, 100.0 }, WavefrontOptions.Reference);
        Assert.All(r.Mtf, m => { Assert.Equal(1.0, m.Tangential, 9); Assert.Equal(1.0, m.Sagittal, 9); });
        Assert.Equal(r.Geometric[0], r.Mtf[0]);                 // by default, the geometric MTF alone

        double h = 2.0, sag = h * h / 400.0;                    // the marginal ray meets the mirror at z = h²/4f
        double na = h / Math.Sqrt(h * h + (100.0 - sag) * (100.0 - sag));
        Assert.Equal(2.0 * na / 0.55e-3, r.CutoffTangential, 6);
        Assert.Equal(r.CutoffTangential, r.CutoffSagittal, 9);
        Assert.Equal(0.0, r.Obscuration);

        var limited = GeometricMtf.Compute(lens, 0, new[] { 10.0 }, WavefrontOptions.Reference, new MtfOptions { DiffractionLimited = true });
        Assert.Equal(GeometricMtf.DiffractionLimit(10.0, r.CutoffTangential), limited.Mtf[0].Tangential, 9);
    }

    /// <summary>
    /// The default 30 rings of Kidger's pattern are within 0.006 of a converged one (240 rings) on
    /// the Cooke triplet at every field, up to 80 cycles/mm: 0.005 at worst, its 20° field at 80.
    /// </summary>
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public void TheDefaultRingsAreEnough(int field)
    {
        var lens = LensModel.Read(Path("Cooke_40deg_FC.zmx"));
        var frequencies = new[] { 10.0, 20.0, 40.0, 80.0 };
        var standard = GeometricMtf.Compute(lens, field, frequencies, WavefrontOptions.Reference);
        var fine = GeometricMtf.Compute(lens, field, frequencies, WavefrontOptions.Reference, new MtfOptions { Rings = 240 });
        for (int k = 0; k < frequencies.Length; k++)
        {
            Assert.InRange(standard.Mtf[k].Tangential - fine.Mtf[k].Tangential, -0.006, 0.006);
            Assert.InRange(standard.Mtf[k].Sagittal - fine.Mtf[k].Sagittal, -0.006, 0.006);
        }
    }

    /// <summary>A fixed semi-diameter that cuts into the beam stops rays and narrows the cutoff; left to follow the beam, it stops none.</summary>
    [Fact]
    public void AFixedSemiDiameterVignettes()
    {
        var catalog = CatalogLocator.LoadBundled();
        LensModel With(SemiDiameterMode mode)
        {
            var sys = LensFile.Read(Path("Cooke_40deg_FC.zmx"), catalog);
            sys.Surfaces[1].SemiDiameter = 0.8 * 5.0;           // the entrance pupil's radius is 5
            sys.Surfaces[1].SemiDiameterMode = mode;
            return new LensModel(sys, catalog);
        }
        var f = new[] { 20.0 };
        var open = GeometricMtf.Compute(LensModel.Read(Path("Cooke_40deg_FC.zmx")), 0, f, WavefrontOptions.Reference);
        var clipped = GeometricMtf.Compute(With(SemiDiameterMode.Fixed), 0, f, WavefrontOptions.Reference);
        var automatic = GeometricMtf.Compute(With(SemiDiameterMode.Auto), 0, f, WavefrontOptions.Reference);

        Assert.Equal(open.RaysTraced, open.RaysArrived);
        Assert.True(clipped.RaysArrived < clipped.RaysTraced);
        Assert.Equal(open.RaysArrived, automatic.RaysArrived);
        Assert.InRange(clipped.CutoffTangential / open.CutoffTangential, 0.75, 0.85);
    }

    [Fact]
    public void AnAfocalSystemIsRefused()
    {
        var lens = LensModel.Read(Path("Galilean_4x.zmx"));
        Assert.Throws<NotSupportedException>(() => GeometricMtf.Compute(lens, 0, new[] { 1.0 }, WavefrontOptions.Reference));
    }

    // ── The limit of diffraction ─────────────────────────────────────────────────────────

    /// <summary>
    /// The geometric MTF is the diffraction MTF's limit as the wavelength goes to zero - the check
    /// Kidger made of his own programs (Proc. SPIE 46, 49, 1974; Optica Acta 25, 665, 1978, §2.8):
    /// the diffraction OTF computed with the wavelength reduced 100 times, the glass kept at the
    /// real one, must agree with the geometric. Kidger found 0.02. The two routes share only the
    /// traced rays: one sums where they land, the other autocorrelates the pupil function built
    /// from their optical paths (<see cref="Diffraction.Otf"/>). Under both ray-aiming conventions,
    /// on axis and at 14° on the Cooke triplet.
    /// </summary>
    [Theory]
    [InlineData("zemax", 0)]
    [InlineData("zemax", 1)]
    [InlineData("reference", 1)]
    public void TheGeometricMtfIsTheDiffractionMtfAsTheWavelengthGoesToZero(string preset, int field)
    {
        var options = WavefrontOptions.Preset(preset) with { ChiefRay = ChiefRayDefinition.StopCenter };
        var lens = LensModel.Read(Path("Cooke_40deg_FC.zmx"));
        int w = lens.PrimaryWavelength;

        const double reduction = 100.0;
        var catalog = CatalogLocator.LoadBundled();
        var shortSystem = LensFile.Read(Path("Cooke_40deg_FC.zmx"), catalog);
        foreach (var wave in shortSystem.Wavelengths) wave.Value /= reduction;
        var shortLens = new LensModel(shortSystem, catalog, i => lens.Indices(i));

        var frequencies = new[] { 10.0, 20.0, 40.0 };
        var geometric = GeometricMtf.Compute(lens, field, frequencies, options, new MtfOptions { Rings = 60, Wavelength = w });
        for (int k = 0; k < frequencies.Length; k++)
        {
            double f = frequencies[k];
            double s = Diffraction.Otf(shortLens, field, w, options, 128, f, 0.0).Modulus;
            double t = Diffraction.Otf(shortLens, field, w, options, 128, 0.0, f).Modulus;
            log.WriteLine($"{preset} field {field}, {f} c/mm: S geometric {geometric.Geometric[k].Sagittal:F4} diffraction {s:F4}   T geometric {geometric.Geometric[k].Tangential:F4} diffraction {t:F4}");
            Assert.InRange(s - geometric.Geometric[k].Sagittal, -0.005, 0.005);
            Assert.InRange(t - geometric.Geometric[k].Tangential, -0.005, 0.005);
        }
    }
}
