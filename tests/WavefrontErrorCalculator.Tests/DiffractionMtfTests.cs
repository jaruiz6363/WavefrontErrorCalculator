using AberrationCalculator.Core.Enums;
using AberrationCalculator.Core.Glass;
using AberrationCalculator.Core.Models;
using WavefrontErrorCalculator.Core;
using Xunit;

namespace WavefrontErrorCalculator.Tests;

/// <summary>
/// The diffraction MTF over a lens's wavelengths (method.md §11.2) against what is known exactly: a perfect image
/// at several wavelengths, through a central obscuration (O'Neill 1956), a pure defocus (Hopkins 1955), and the
/// Fresnel number.
/// </summary>
public class DiffractionMtfTests(Xunit.Abstractions.ITestOutputHelper log)
{
    private const double F = 100.0, R = 5.0;

    /// <summary>A paraboloid, f = 100 mm at F/10, on axis: W ≡ 0 at every wavelength.</summary>
    private static LensModel Paraboloid(double epd = 2 * R, params (double Um, double Weight)[] waves) =>
        Paraboloid(epd, 0.0, waves);

    /// <summary>
    /// The paraboloid behind a central obscuration of <paramref name="obscuration"/> times the pupil's radius, on a
    /// plane at its vertex, where the beam is still the entrance pupil's size.
    /// </summary>
    internal static LensModel Paraboloid(double epd, double obscuration, params (double Um, double Weight)[] waves)
    {
        var sys = new OpticalSystem { Aperture = new Aperture(ApertureType.EPD, epd), FieldType = FieldType.ObjectAngle };
        if (waves.Length == 0) waves = new[] { (0.5876, 1.0) };
        for (int i = 0; i < waves.Length; i++) sys.Wavelengths.Add(new Wavelength(waves[i].Um, waves[i].Weight, i == 0));
        sys.Fields.Add(new Field(0.0));
        var surfaces = new List<Surface> { new Surface { Thickness = double.PositiveInfinity } };
        if (obscuration > 0.0) surfaces.Add(new Surface { ObscurationRadius = obscuration * epd / 2 });
        surfaces.Add(new Surface { Curvature = -1.0 / (2 * F), Conic = -1.0, Thickness = -F, Material = "MIRROR", IsStop = true });
        surfaces.Add(new Surface());
        for (int i = 0; i < surfaces.Count; i++) surfaces[i].Index = i;
        sys.Surfaces.AddRange(surfaces);
        return new LensModel(sys, CatalogLocator.LoadBundled());
    }

    /// <summary>The marginal ray's sine at the focus: it meets z = r²/4f at a distance f + z from the focus.</summary>
    private static double Sine(double r = R) => r / (F + r * r / (4 * F));

    private static double Circle(double v) => v >= 1 ? 0 : 2 / Math.PI * (Math.Acos(v) - v * Math.Sqrt(1 - v * v));

    /// <summary>
    /// A perfect image at three wavelengths, weighted 1, 2 and 1: each has the circles' overlap with its own cutoff
    /// 2 sin θ/λ, so the MTF is their weighted mean.
    /// </summary>
    [Theory]
    [InlineData(20.0)]
    [InlineData(60.0)]
    [InlineData(110.0)]
    [InlineData(150.0)]
    public void A_perfect_image_at_several_wavelengths(double nu)
    {
        var waves = new[] { (0.5876, 2.0), (0.4861, 1.0), (0.6563, 1.0) };
        var lens = Paraboloid(2 * R, waves);
        var r = DiffractionMtf.Compute(lens, 0, new[] { nu }, WavefrontOptions.Reference);
        double expected = waves.Sum(w => w.Item2 * Circle(nu / (2 * Sine() / (w.Item1 * 1e-3)))) / waves.Sum(w => w.Item2);
        log.WriteLine($"ν = {nu}: MTF S {r.Mtf[0].Sagittal:F5} T {r.Mtf[0].Tangential:F5} against {expected:F5}; limit {r.DiffractionLimit[0].Sagittal:F5}");
        Assert.InRange(r.Mtf[0].Sagittal - expected, -3e-3, 3e-3);
        Assert.InRange(r.Mtf[0].Tangential - expected, -3e-3, 3e-3);
        Assert.InRange(r.DiffractionLimit[0].Sagittal - expected, -1e-3, 1e-3);
        Assert.Equal(0, r.Unsolved);
    }

    /// <summary>
    /// A perfect image through a central obscuration of a third of the pupil's radius: the annulus's MTF (O'Neill
    /// 1956). The chief ray, down the axis, is stopped by the obscuration, and is still the reference.
    /// </summary>
    [Theory]
    [InlineData(20.0)]
    [InlineData(60.0)]
    [InlineData(110.0)]
    [InlineData(150.0)]
    public void A_perfect_image_through_a_central_obscuration(double nu)
    {
        const double lambda = 0.5876, e = 1.0 / 3.0;
        var lens = Paraboloid(2 * R, e, (lambda, 1.0));
        var r = DiffractionMtf.Compute(lens, 0, new[] { nu }, WavefrontOptions.Reference, new DiffractionMtfOptions { Grid = 128 });
        double expected = GeometricMtf.DiffractionLimit(nu, 2 * Sine() / (lambda * 1e-3), e);
        log.WriteLine($"ν = {nu}: MTF S {r.Mtf[0].Sagittal:F5} T {r.Mtf[0].Tangential:F5} against {expected:F5}");
        Assert.InRange(r.Mtf[0].Sagittal - expected, -3e-3, 3e-3);
        Assert.InRange(r.Mtf[0].Tangential - expected, -3e-3, 3e-3);
    }

    /// <summary>
    /// A pure defocus, at F/50 so that the pupil's apodization and the ρ⁴ part of the defocus, both of order sin²θ,
    /// are 10⁻⁴. The image plane moves by Δz toward the paraboloid, and the reference sphere's centre with it, so
    /// the pupil of radius a is seen at a distance R_Q = f − Δz: W = W₂₀ρ² with W₂₀ = Δz·(a/f)(a/R_Q)/(2λ), ρ the
    /// pupil radius in spatial frequencies, a/(λR_Q) at the rim. The OTF at a shift s of the unit pupil, a = s/2, is
    /// (4/π) ∫₀^(1−a) √(1 − (x + a)²) cos(4π W₂₀ s x) dx (Hopkins 1955), by quadrature. Δz is set for W₂₀ near
    /// 1 wave, past the MTF's first zero. On a 256 grid the agreement is 2×10⁻⁴.
    /// </summary>
    [Theory]
    [InlineData(0.1)]
    [InlineData(0.3)]
    [InlineData(0.5)]
    [InlineData(0.8)]
    [InlineData(1.2)]
    public void A_pure_defocus_against_Hopkins(double s)
    {
        const double lambda = 0.5876e-3;
        const double r0 = 1.0;
        double naF = r0 / F, dz = 2 * lambda / (naF * naF);
        double naQ = r0 / (F - dz), w20 = dz * naF * naQ / (2 * lambda);
        var lens = Paraboloid(2 * r0);
        double nu = s * naQ / lambda;
        var r = DiffractionMtf.Compute(lens, 0, new[] { nu }, WavefrontOptions.Reference with { FocusShift = dz });

        double a = s / 2, upper = 1 - a, sum = 0;
        const int n = 200_000;
        for (int k = 0; k < n; k++)
        {
            double x = (k + 0.5) * upper / n;
            sum += Math.Sqrt(Math.Max(0, 1 - (x + a) * (x + a))) * Math.Cos(4 * Math.PI * w20 * s * x);
        }
        double expected = Math.Abs(4 / Math.PI * sum * upper / n);
        log.WriteLine($"s = {s}: W20 {w20:F4}: MTF S {r.Mtf[0].Sagittal:F5} T {r.Mtf[0].Tangential:F5} against {expected:F5}");
        Assert.InRange(r.Mtf[0].Sagittal - expected, -1e-3, 1e-3);
        Assert.InRange(r.Mtf[0].Tangential - expected, -1e-3, 1e-3);
    }

    /// <summary>
    /// The Fresnel number a²/(λR′): at F/10 and f = 100 mm, R′ sin²θ/λ with R′ the mirror's distance from its focus;
    /// a pupil a hundred times smaller has N_F under 1, and is flagged.
    /// </summary>
    [Fact]
    public void The_Fresnel_number()
    {
        const double lambda = 0.5876e-3;
        var r = DiffractionMtf.Compute(Paraboloid(), 0, new[] { 10.0 }, WavefrontOptions.Reference);
        double expected = (F + R * R / (4 * F)) * Sine() * Sine() / lambda;
        log.WriteLine($"N_F {r.FresnelNumber:F2} against {expected:F2}");
        Assert.InRange(r.FresnelNumber / expected - 1.0, -0.01, 0.01);
        Assert.DoesNotContain(r.Warnings, w => w.Contains("Fresnel"));

        var small = DiffractionMtf.Compute(Paraboloid(0.1), 0, new[] { 10.0 }, WavefrontOptions.Reference);
        Assert.True(small.FresnelNumber < 1.0);
        Assert.Contains(small.Warnings, w => w.Contains("Fresnel"));
    }

    /// <summary>
    /// On an aberrated field off axis, the MTF of one wavelength is <see cref="Diffraction.Otf"/>'s modulus, the pupil
    /// traced once for every frequency; and the vignetted pupil's centre is replaced by the stop's, with a note.
    /// </summary>
    [Fact]
    public void One_wavelength_is_the_OTF_of_that_wavelength()
    {
        var lens = LensModel.Read(Path.Combine(AppContext.BaseDirectory, "TestData", "Cooke_40deg_FC.zmx"));
        int wave = lens.PrimaryWavelength;
        var r = DiffractionMtf.Compute(lens, 1, new[] { 20.0, 40.0 }, WavefrontOptions.Reference,
            new DiffractionMtfOptions { Grid = 96, Wavelength = wave });
        var stop = WavefrontOptions.Reference with { ChiefRay = ChiefRayDefinition.StopCenter };
        for (int i = 0; i < 2; i++)
        {
            double nu = new[] { 20.0, 40.0 }[i];
            Assert.Equal(Diffraction.Otf(lens, 1, wave, stop, 96, nu, 0).Modulus, r.Mtf[i].Sagittal, 12);
            Assert.Equal(Diffraction.Otf(lens, 1, wave, stop, 96, 0, nu).Modulus, r.Mtf[i].Tangential, 12);
        }
        Assert.Contains(r.Warnings, w => w.Contains("stop's centre"));
    }
}
