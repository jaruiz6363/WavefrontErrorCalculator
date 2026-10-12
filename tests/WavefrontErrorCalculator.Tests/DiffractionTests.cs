using AberrationCalculator.Core.Enums;
using AberrationCalculator.Core.Glass;
using AberrationCalculator.Core.Models;
using WavefrontErrorCalculator.Core;
using Xunit;

namespace WavefrontErrorCalculator.Tests;

/// <summary>
/// The diffraction reference (method.md §11) against what is known exactly: a perfect image's MTF,
/// and, on an aberrated field off axis, its two routes - the OTF as the pupil's autocorrelation
/// with traced partners, and the Fourier transform of the PSF summed directly - against each other.
/// </summary>
public class DiffractionTests(Xunit.Abstractions.ITestOutputHelper log)
{
    /// <summary>A paraboloid, f = 100 mm at F/10, on axis: W ≡ 0.</summary>
    private static LensModel Paraboloid()
    {
        var sys = new OpticalSystem { Aperture = new Aperture(ApertureType.EPD, 10.0), FieldType = FieldType.ObjectAngle };
        sys.Wavelengths.Add(new Wavelength(0.5876, 1.0, true));
        sys.Fields.Add(new Field(0.0));
        var surfaces = new[]
        {
            new Surface { Thickness = double.PositiveInfinity },
            new Surface { Curvature = -0.005, Conic = -1.0, Thickness = -100.0, Material = "MIRROR", IsStop = true },
            new Surface(),
        };
        for (int i = 0; i < surfaces.Length; i++) surfaces[i].Index = i;
        sys.Surfaces.AddRange(surfaces);
        return new LensModel(sys, CatalogLocator.LoadBundled());
    }

    /// <summary>
    /// The perfect image's MTF is the overlap of two circles of radius sin θ / λ, θ the marginal
    /// ray's angle at the focus: (2/π)(acos v - v√(1 - v²)), v = ν / (2 sin θ / λ). The marginal
    /// ray meets the paraboloid z = r²/4f at a distance f + z from its focus.
    /// </summary>
    [Theory]
    [InlineData(0.1)]
    [InlineData(0.3)]
    [InlineData(0.5)]
    [InlineData(0.7)]
    [InlineData(0.9)]
    public void APerfectImagesMtfIsTheCirclesOverlap(double v)
    {
        var lens = Paraboloid();
        double r = 5.0, f = 100.0, z = r * r / (4 * f);
        double sine = r / (f + z);
        double cutoff = 2 * sine / (0.5876e-3);
        double expected = 2 / Math.PI * (Math.Acos(v) - v * Math.Sqrt(1 - v * v));
        foreach (var (nx, ny) in new[] { (v * cutoff, 0.0), (0.0, v * cutoff) })
        {
            var otf = Diffraction.Otf(lens, 0, 0, WavefrontOptions.Zemax, 256, nx, ny);
            log.WriteLine($"v = {v}: MTF {otf.Modulus:F6} against {expected:F6}, {otf.Pairs} of {otf.Rays} pairs, {otf.Unsolved} unsolved");
            Assert.Equal(0, otf.Unsolved);
            Assert.True(Math.Abs(otf.Modulus - expected) < 2e-3, $"{otf.Modulus} against {expected}");
        }
    }

    /// <summary>
    /// Off axis on the Cooke triplet, where W spans waves: the OTF from traced partners equals the
    /// Fourier transform of the directly summed PSF, two routes that share only the traced rays.
    /// </summary>
    [Theory]
    [InlineData(40.0, 0.0)]
    [InlineData(0.0, 40.0)]
    [InlineData(0.0, 80.0)]
    public void TheOtfIsThePsfsFourierTransform(double nuX, double nuY)
    {
        var lens = LensModel.Read(Path.Combine(AppContext.BaseDirectory, "TestData", "Cooke_40deg_FC.zmx"));
        var options = WavefrontOptions.Zemax;
        int field = 1, wave = lens.PrimaryWavelength;

        var otf = Diffraction.Otf(lens, field, wave, options, 256, nuX, nuY);

        // The PSF over ±160 µm, which holds 99.8% of its energy, at 1 µm: finer than the
        // Nyquist spacing of its cutoff, so the sum is the transform but for the tails left out.
        const int n = 321;
        const double pixel = 0.001;
        var axis = Enumerable.Range(0, n).Select(i => (i - n / 2) * pixel).ToArray();
        var psf = Diffraction.Psf(lens, field, wave, options, 128, axis, axis);
        double total = 0.0, re = 0.0, im = 0.0;
        for (int iy = 0; iy < n; iy++)
            for (int ix = 0; ix < n; ix++)
            {
                double value = psf.Intensity[iy, ix] * pixel * pixel;
                double phase = -2 * Math.PI * (nuX * axis[ix] + nuY * axis[iy]);
                total += value;
                re += value * Math.Cos(phase);
                im += value * Math.Sin(phase);
            }
        double fromPsf = Math.Sqrt(re * re + im * im) / total;
        log.WriteLine($"ν = ({nuX}, {nuY}): MTF {otf.Modulus:F5} by traced partners, {fromPsf:F5} from the PSF; energy in the window {total:F5}");
        Assert.InRange(total, 0.99, 1.01);
        Assert.True(Math.Abs(otf.Modulus - fromPsf) < 3e-3, $"{otf.Modulus} against {fromPsf}");
    }
}
