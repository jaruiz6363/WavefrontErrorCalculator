using AberrationCalculator.Core.Enums;
using AberrationCalculator.Core.Glass;
using AberrationCalculator.Core.Models;
using WavefrontErrorCalculator.Core;
using Xunit;

namespace WavefrontErrorCalculator.Tests;

/// <summary>
/// Cases whose wavefront is known exactly (method.md §9.1), so nothing is taken from another
/// program. A perfect image has W ≡ 0 whatever the reference sphere's radius, which makes the
/// first, second and fourth cases tests of the optical path alone; the plate's W is a closed form
/// that exercises the entrance reference, the glass, a virtual image and the eikonal's reference.
/// </summary>
public class AnalyticTests
{
    private const double Tolerance = 1e-6;   // waves

    private static OpticalSystem System(double epd, FieldType fieldType, params Surface[] surfaces)
    {
        var sys = new OpticalSystem { Aperture = new Aperture(ApertureType.EPD, epd), FieldType = fieldType };
        sys.Wavelengths.Add(new Wavelength(0.5876, 1.0, true));
        sys.Fields.Add(new Field(0.0));
        for (int i = 0; i < surfaces.Length; i++) surfaces[i].Index = i;
        sys.Surfaces.AddRange(surfaces);
        return sys;
    }

    private static LensModel Model(OpticalSystem sys) => new(sys, CatalogLocator.LoadBundled());

    private static void AllZero(WavefrontResult r)
    {
        Assert.Empty(r.Samples.Where(s => s.Vignetted));
        double worst = r.Samples.Max(s => Math.Abs(s.W));
        Assert.True(worst < Tolerance, $"largest |W| {worst:E3} waves");
    }

    public static IEnumerable<object[]> Options() => new[]
    {
        new object[] { "Reference" }, new object[] { "Optiland" }, new object[] { "LensHHLT" },
    };

    /// <summary>A paraboloid focuses a collimated axial beam perfectly.</summary>
    [Theory]
    [MemberData(nameof(Options))]
    public void AParaboloidOnAxisHasNoWavefrontAberration(string preset)
    {
        var sys = System(40.0, FieldType.ObjectAngle,
            new Surface { Thickness = double.PositiveInfinity },
            new Surface { Curvature = -0.005, Conic = -1.0, Thickness = -100.0, Material = "MIRROR", IsStop = true },
            new Surface());
        AllZero(WavefrontCalculator.Compute(Model(sys), 0, 0, WavefrontOptions.Preset(preset), new Sampling.SquareGrid(16)));
    }

    /// <summary>A sphere images its centre of curvature onto itself perfectly, at any aperture.</summary>
    [Theory]
    [MemberData(nameof(Options))]
    public void ASphereImagesItsCentrePerfectly(string preset)
    {
        var sys = System(120.0, FieldType.ObjectHeight,
            new Surface { Thickness = 200.0 },
            new Surface { Curvature = -0.005, Thickness = -200.0, Material = "MIRROR", IsStop = true },
            new Surface());
        AllZero(WavefrontCalculator.Compute(Model(sys), 0, 0, WavefrontOptions.Preset(preset), new Sampling.SquareGrid(16)));
    }

    /// <summary>
    /// The aplanatic points of a refracting sphere: an object in glass at R/n from the centre of
    /// curvature, on the far side from the vertex, is imaged without aberration at any aperture,
    /// virtually, at nR from the centre. The object space is the glass.
    /// </summary>
    [Fact]
    public void ASphereImagesItsAplanaticPointPerfectly()
    {
        const double r = 50.0;
        var sys = System(60.0, FieldType.ObjectHeight,
            new Surface { Thickness = 1.0, Material = "N-BK7" },
            new Surface { Curvature = -1.0 / r, Thickness = 1.0, IsStop = true },
            new Surface());
        double n = IndexResolver.Build(sys, CatalogLocator.LoadBundled(), 0.5876)[0];
        Assert.True(n > 1.5, $"the object space should be glass, not n = {n}");
        sys.Surfaces[0].Thickness = r + r / n;
        sys.Surfaces[1].Thickness = -(r + r * n);

        AllZero(WavefrontCalculator.Compute(Model(sys), 0, 0, WavefrontOptions.Reference, new Sampling.SquareGrid(16)));
    }

    /// <summary>
    /// A plate of thickness T and index n in the diverging beam from an axial point at distance a.
    /// A ray at angle θ crosses the air in a / cos θ and the glass in nT / cos θ′, and leaves the
    /// plate at height h = a tan θ + T tan θ′. Referred to the eikonal's reference - the foot of
    /// the perpendicular from the paraxial image Q′, T(1 - 1/n) beyond the source, to the ray
    /// (Welford p. 101) - its optical path is that plus s = (z_q - a - T) cos θ - h sin θ, and W
    /// is the chief ray's minus it. Every term is exact.
    /// </summary>
    [Fact]
    public void APlateInADivergingBeamHasTheClosedFormWavefront()
    {
        const double a = 50.0, thickness = 10.0;
        var sys = System(36.0, FieldType.ObjectHeight,
            new Surface { Thickness = a },
            new Surface { Thickness = thickness, Material = "N-BK7", IsStop = true },
            new Surface { Thickness = 0.0 },
            new Surface());
        double n = IndexResolver.Build(sys, CatalogLocator.LoadBundled(), 0.5876)[1];
        double zq = thickness * (1.0 - 1.0 / n);
        sys.Surfaces[2].Thickness = zq - a - thickness;          // the virtual paraxial image

        var options = WavefrontOptions.Reference with { ExitPupil = ExitPupil.Infinite, RayAiming = RayAiming.Paraxial };
        var result = WavefrontCalculator.Compute(Model(sys), 0, 0, options, new Sampling.SquareGrid(12));

        double Path(double theta)
        {
            double inside = Math.Asin(Math.Sin(theta) / n);
            double h = a * Math.Tan(theta) + thickness * Math.Tan(inside);
            double s = (zq - a - thickness) * Math.Cos(theta) - h * Math.Sin(theta);
            return a / Math.Cos(theta) + n * thickness / Math.Cos(inside) + s;
        }

        double lambdaMm = 0.5876e-3;
        double largest = 0.0;
        foreach (var sample in result.Samples)
        {
            Assert.False(sample.Vignetted);
            var d = result.Geometry.ChiefDirection;   // on axis: (0, 0, 1)
            Assert.Equal(1.0, d.Z, 12);
            double theta = Math.Acos(Math.Abs(sample.Direction.Z));
            double expected = (Path(0.0) - Path(theta)) / lambdaMm;
            Assert.True(Math.Abs(sample.W - expected) < Tolerance,
                $"θ = {theta * 180 / Math.PI:F3}°: W {sample.W:F9}, closed form {expected:F9}");
            largest = Math.Max(largest, Math.Abs(expected));
        }
        Assert.True(largest > 5.0, $"the plate should have several waves to check, not {largest:F3}");
    }
}
