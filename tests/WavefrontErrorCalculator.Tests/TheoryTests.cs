using AberrationCalculator.Core.Aberrations;
using AberrationCalculator.Core.Enums;
using AberrationCalculator.Core.Glass;
using AberrationCalculator.Core.Models;
using AberrationCalculator.Core.RayTrace;
using WavefrontErrorCalculator.Core;
using Xunit;

namespace WavefrontErrorCalculator.Tests;

/// <summary>
/// The traced wavefront against aberration theory: the Seidel limit, the exact relation between
/// wave and ray aberration, the effect of moving the reference sphere's centre, and the two ways
/// of applying a change of focus (method.md §9.1 cases 5 and 6, §9.2).
/// </summary>
public class TheoryTests(Xunit.Abstractions.ITestOutputHelper log)
{
    private static readonly Lazy<LensModel> DoubleGauss =
        new(() => LensModel.Read(Path.Combine(AppContext.BaseDirectory, "TestData", "KingslakeDG.zmx")));

    /// <summary>
    /// A singlet at f/20 and half a degree, imaged on its paraxial plane: the traced wavefront's
    /// ρ⁴ term on axis must be W040 = S1/8 and its ρ³ cos φ term at the field W131 = S2/2
    /// (Welford's convention, which AberrationCalculator's Seidel sums follow), to the size of
    /// the higher orders the small aperture and field leave.
    /// </summary>
    [Fact]
    public void ASlowSingletHasItsSeidelWavefront()
    {
        var sys = new OpticalSystem { Aperture = new Aperture(ApertureType.EPD, 2.5) };
        sys.Wavelengths.Add(new Wavelength(0.5876, 1.0, true));
        sys.Fields.Add(new Field(0.0));
        sys.Fields.Add(new Field(0.5));
        sys.Surfaces.Add(new Surface { Index = 0, Thickness = double.PositiveInfinity });
        sys.Surfaces.Add(new Surface { Index = 1, Curvature = 1 / 50.0, Thickness = 5.0, Material = "N-BK7", IsStop = true });
        sys.Surfaces.Add(new Surface { Index = 2, Curvature = -1 / 50.0, Thickness = 50.0 });
        sys.Surfaces.Add(new Surface { Index = 3 });
        var catalog = CatalogLocator.LoadBundled();
        var n = IndexResolver.Build(sys, catalog, 0.5876);
        var p = ParaxialTrace.Trace(sys, n, 0.5);
        sys.Surfaces[2].Thickness = p.ParaxialFocusDistance;
        p = ParaxialTrace.Trace(sys, n, 0.5);
        var seidel = SeidelCoefficients.Compute(sys, n, n, n, p);
        double lambdaMm = 0.5876e-3;
        double w040 = seidel.TotalS1 / 8 / lambdaMm, w131 = seidel.TotalS2 / 2 / lambdaMm;

        var lens = new LensModel(sys, catalog);
        var options = WavefrontOptions.Reference with { RayAiming = RayAiming.Paraxial, ChiefRay = ChiefRayDefinition.StopCenter };

        var axis = WavefrontCalculator.Fan(lens, 0, 0, options, FanDirection.Tangential, 41);
        var even = Polynomial(axis.Samples.Where(s => s.Py > 0).Select(s => (s.Py, s.W)), new[] { 2, 4, 6 });
        Assert.True(Math.Abs(even[0]) < 0.01 * Math.Abs(w040), $"defocus {even[0]:E3} on the paraxial plane");
        log.WriteLine($"ρ⁴ {even[1]:F6} waves against W040 = S1/8 = {w040:F6}: {(even[1] - w040) / w040:P3}");
        Assert.True(Math.Abs(even[1] - w040) < 0.01 * Math.Abs(w040), $"ρ⁴ {even[1]:F6}, W040 {w040:F6}");

        var field = WavefrontCalculator.Fan(lens, 1, 0, options, FanDirection.Tangential, 41);
        var byPy = field.Samples.ToDictionary(s => Math.Round(s.Py, 9), s => s.W);
        var odd = Polynomial(field.Samples.Where(s => s.Py > 0).Select(s => (s.Py, 0.5 * (s.W - byPy[Math.Round(-s.Py, 9)]))), new[] { 1, 3, 5 });
        log.WriteLine($"ρ³ cos φ {odd[1]:F6} waves against W131 = S2/2 = {w131:F6}: {(odd[1] - w131) / w131:P3}");
        Assert.True(Math.Abs(odd[1] - w131) < 0.02 * Math.Abs(w131), $"ρ³ cos φ {odd[1]:F6}, W131 {w131:F6}");
    }

    /// <summary>Least squares for the coefficients of the given powers.</summary>
    private static double[] Polynomial(IEnumerable<(double X, double Y)> points, int[] powers)
    {
        var list = points.ToList();
        int m = powers.Length;
        var a = new double[m, m];
        var b = new double[m];
        foreach (var (x, y) in list)
            for (int i = 0; i < m; i++)
            {
                b[i] += Math.Pow(x, powers[i]) * y;
                for (int j = 0; j < m; j++) a[i, j] += Math.Pow(x, powers[i] + powers[j]);
            }
        // Gaussian elimination; the system is small and well scaled.
        for (int k = 0; k < m; k++)
            for (int i = k + 1; i < m; i++)
            {
                double f = a[i, k] / a[k, k];
                for (int j = k; j < m; j++) a[i, j] -= f * a[k, j];
                b[i] -= f * b[k];
            }
        var c = new double[m];
        for (int k = m - 1; k >= 0; k--)
        {
            double s = b[k];
            for (int j = k + 1; j < m; j++) s -= a[k, j] * c[j];
            c[k] = s / a[k, k];
        }
        return c;
    }

    /// <summary>
    /// Welford eq. 7.14: δη = -(Q₀P′ / n′) ∂W/∂y, exact for any field when y is the ray's
    /// coordinate on the reference sphere and Q₀P′ its distance from there to the image plane.
    /// Checked along the double Gauss's full-field tangential fan by central differences.
    /// </summary>
    [Fact]
    public void TheRayAberrationIsTheSlopeOfTheWavefront()
    {
        var lens = DoubleGauss.Value;
        int field = lens.System.Fields.Select((f, i) => (Math.Abs(f.Y), i)).Max().Item2;
        var r = WavefrontCalculator.Fan(lens, field, lens.PrimaryWavelength, WavefrontOptions.Reference, FanDirection.Tangential, 801);
        double lambdaMm = r.Geometry.WavelengthUm * 1e-3, n = r.Geometry.ImageIndex;
        var s = r.Samples;
        double worst = 0.0, scale = 0.0;
        for (int k = 1; k < s.Count - 1; k++)
        {
            double slope = (s[k + 1].W - s[k - 1].W) * lambdaMm / (s[k + 1].Sphere.Y - s[k - 1].Sphere.Y);
            double distance = (s[k].ImagePoint - s[k].Sphere).Length;
            double predicted = -distance / n * slope;
            double actual = s[k].ImagePoint.Y - r.Geometry.Center.Y;
            worst = Math.Max(worst, Math.Abs(predicted - actual));
            scale = Math.Max(scale, Math.Abs(actual));
        }
        Assert.True(scale > 1e-3, "the fan should have a ray aberration to check");
        log.WriteLine($"largest misfit {worst:E3} mm against a ray aberration of {scale:E3} mm");
        Assert.True(worst < 1e-4 * scale, $"largest misfit {worst:E3} mm against a ray aberration of {scale:E3} mm");
    }

    /// <summary>
    /// Welford eq. 7.18: moving the reference sphere's centre by δ, the sphere still through E′,
    /// changes W by (n′/R′)(B′ - E′)·δ, B′ the ray's point on the sphere.
    /// </summary>
    [Fact]
    public void MovingTheCentreTiltsTheWavefront()
    {
        var lens = DoubleGauss.Value;
        int field = lens.System.Fields.Select((f, i) => (Math.Abs(f.Y), i)).Max().Item2;
        var basis = WavefrontCalculator.Compute(lens, field, lens.PrimaryWavelength, WavefrontOptions.Reference, new Sampling.SquareGrid(12));
        const double dx = 2e-4, dy = -3e-4;
        var c = basis.Geometry.Center;
        var moved = WavefrontCalculator.Compute(lens, field, lens.PrimaryWavelength,
            WavefrontOptions.Reference with { ReferenceCenter = ReferenceCenter.User, UserCenter = (c.X + dx, c.Y + dy) },
            new Sampling.SquareGrid(12));

        double lambdaMm = basis.Geometry.WavelengthUm * 1e-3, n = basis.Geometry.ImageIndex, radius = basis.Geometry.Radius;
        for (int k = 0; k < basis.Samples.Count; k++)
        {
            var b = basis.Samples[k];
            var e = b.Sphere - basis.Geometry.PupilPoint;
            double expected = n / radius * (e.X * dx + e.Y * dy) / lambdaMm;
            Assert.True(Math.Abs(moved.Samples[k].W - b.W - expected) < 1e-4,
                $"({b.U:F2}, {b.V:F2}): changed by {moved.Samples[k].W - b.W:F6}, predicted {expected:F6}");
        }
    }

    /// <summary>
    /// A sphere imaging its centre of curvature at NA 0.8, defocused. Re-tracing to the moved
    /// plane is exact; Hopkins's term N′δ₀W₂₀(x′² + y′² + z′²) agrees with it to the size of the
    /// terms it neglects, while dropping z′² - the shortcut of adding a ρ² term - is in error by
    /// terms in sin²α′ (Hopkins &amp; Yzuel 1970 eq. 39) that at this aperture are large.
    /// </summary>
    [Fact]
    public void AChangeOfFocusIsExactWithTheAxialTermAndNotWithout()
    {
        double na = 0.8, alpha = Math.Asin(na);
        var sys = new OpticalSystem { Aperture = new Aperture(ApertureType.EPD, 2 * 200.0 * Math.Tan(alpha)), FieldType = FieldType.ObjectHeight };
        sys.Wavelengths.Add(new Wavelength(0.5876, 1.0, true));
        sys.Fields.Add(new Field(0.0));
        sys.Surfaces.Add(new Surface { Index = 0, Thickness = 200.0 });
        sys.Surfaces.Add(new Surface { Index = 1, Curvature = -0.005, Thickness = -200.0, Material = "MIRROR", IsStop = true });
        sys.Surfaces.Add(new Surface { Index = 2 });
        var lens = new LensModel(sys, CatalogLocator.LoadBundled());

        var o = WavefrontOptions.Reference with { FocusShift = 0.005 };
        var grid = new Sampling.SquareGrid(16);
        var retrace = WavefrontCalculator.Compute(lens, 0, 0, o, grid);
        var exact = WavefrontCalculator.Compute(lens, 0, 0, o with { Defocus = DefocusMethod.ExactTerm }, grid);
        var paraxial = WavefrontCalculator.Compute(lens, 0, 0, o with { Defocus = DefocusMethod.ParaxialTerm }, grid);

        double size = retrace.Samples.Max(s => Math.Abs(s.W));
        double exactError = retrace.Samples.Zip(exact.Samples).Max(p => Math.Abs(p.First.W - p.Second.W));
        double paraxialError = retrace.Samples.Zip(paraxial.Samples).Max(p => Math.Abs(p.First.W - p.Second.W));
        Assert.True(size > 1.0, $"the defocus should be several waves, not {size:F3}");
        log.WriteLine($"defocus {size:F4} waves at the rim; exact term off by {exactError:E3}, paraxial term by {paraxialError:F4}");
        Assert.True(exactError < 1e-3, $"exact term off by {exactError:E3} waves of {size:F3}");
        Assert.True(paraxialError > 0.1, $"the paraxial term should be visibly wrong at NA {na}, but is off by only {paraxialError:E3}");
    }
}
