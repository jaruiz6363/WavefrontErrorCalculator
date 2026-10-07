using AberrationCalculator.Core.Enums;
using AberrationCalculator.Core.Glass;
using AberrationCalculator.Core.Models;
using WavefrontErrorCalculator.Core;
using Xunit;

namespace WavefrontErrorCalculator.Tests;

/// <summary>
/// The switches of phase 3 (method.md §14): the best-fit reference sphere, Gauss quadrature,
/// aiming onto the exit sphere's canonical grid, Singh's refinement to the launch grid, and an
/// image at infinity.
/// </summary>
public class SwitchTests(Xunit.Abstractions.ITestOutputHelper log)
{
    private static readonly Lazy<LensModel> DoubleGauss =
        new(() => LensModel.Read(Path.Combine(AppContext.BaseDirectory, "TestData", "KingslakeDG.zmx")));

    private static int FullField(LensModel lens) => lens.System.Fields.Select((f, i) => (Math.Abs(f.Y), i)).Max().Item2;

    private static LensModel SphereAtCentre()
    {
        var sys = new OpticalSystem { Aperture = new Aperture(ApertureType.EPD, 120.0), FieldType = FieldType.ObjectHeight };
        sys.Wavelengths.Add(new Wavelength(0.5876, 1.0, true));
        sys.Fields.Add(new Field(0.0));
        sys.Surfaces.Add(new Surface { Index = 0, Thickness = 200.0 });
        sys.Surfaces.Add(new Surface { Index = 1, Curvature = -0.005, Thickness = -200.0, Material = "MIRROR", IsStop = true });
        sys.Surfaces.Add(new Surface { Index = 2 });
        return new LensModel(sys, CatalogLocator.LoadBundled());
    }

    /// <summary>
    /// A perfect image looked at out of focus: referred to the defocused plane it has waves of
    /// defocus, and the best-fit sphere finds the true focus again - its centre returns to where
    /// the image is, and the wavefront is a sphere about it.
    /// </summary>
    [Fact]
    public void TheBestFitSphereFindsTheTrueFocus()
    {
        var lens = SphereAtCentre();
        var o = WavefrontOptions.Reference with { FocusShift = 0.01 };
        var defocused = WavefrontCalculator.Compute(lens, 0, 0, o, new Sampling.SquareGrid(16));
        var best = WavefrontCalculator.Compute(lens, 0, 0, o with { ReferenceCenter = ReferenceCenter.BestFitSphere }, new Sampling.SquareGrid(16));
        Assert.True(defocused.Statistics.Rms > 0.1, $"defocused RMS {defocused.Statistics.Rms:F3}");
        Assert.True(best.Statistics.Rms < 1e-6, $"best-fit RMS {best.Statistics.Rms:E3}");
        Assert.True(best.Geometry.Center.Length < 1e-6, $"best-fit centre {best.Geometry.Center}");
    }

    /// <summary>The best-fit centre is a minimum: moving it any way raises the RMS.</summary>
    [Fact]
    public void TheBestFitSphereIsAMinimum()
    {
        var lens = DoubleGauss.Value;
        int field = FullField(lens);
        var o = WavefrontOptions.Reference with { ReferenceCenter = ReferenceCenter.BestFitSphere };
        var best = WavefrontCalculator.Compute(lens, field, lens.PrimaryWavelength, o, new Sampling.SquareGrid(24));
        var chief = WavefrontCalculator.Compute(lens, field, lens.PrimaryWavelength, WavefrontOptions.Reference, new Sampling.SquareGrid(24));
        log.WriteLine($"RMS about the chief ray {chief.Statistics.Rms:F5}, about the best-fit sphere {best.Statistics.Rms:F5} waves");
        Assert.True(best.Statistics.Rms < chief.Statistics.Rms);

        var c = best.Geometry.Center;
        foreach (var d in new[] { new Vec3(2e-4, 0, 0), new Vec3(0, 2e-4, 0), new Vec3(0, 0, 2e-3) })
            foreach (var s in new[] { 1.0, -1.0 })
            {
                var moved = c + s * d;
                // A centre off the image plane: the user centre takes (x, y) and the plane, so move
                // the plane with it and re-trace there.
                var r = WavefrontCalculator.Compute(lens, field, lens.PrimaryWavelength,
                    WavefrontOptions.Reference with { ReferenceCenter = ReferenceCenter.User, UserCenter = (moved.X, moved.Y), FocusShift = moved.Z },
                    new Sampling.SquareGrid(24));
                Assert.True(r.Statistics.Rms > best.Statistics.Rms - 1e-9,
                    $"moved by {s * d}: RMS {r.Statistics.Rms:F7} below the best {best.Statistics.Rms:F7}");
            }
    }

    /// <summary>A few rings of Gauss quadrature give the RMS a dense grid does.</summary>
    [Fact]
    public void GaussQuadratureMatchesADenseGrid()
    {
        var lens = DoubleGauss.Value;
        int field = FullField(lens);
        var dense = WavefrontCalculator.Compute(lens, field, lens.PrimaryWavelength,
            WavefrontOptions.Reference with { Weighting = Weighting.PerRay, ChiefRay = ChiefRayDefinition.StopCenter }, new Sampling.SquareGrid(256));
        var gauss = WavefrontCalculator.Compute(lens, field, lens.PrimaryWavelength,
            WavefrontOptions.Reference with { Weighting = Weighting.Quadrature, ChiefRay = ChiefRayDefinition.StopCenter }, new Sampling.GaussQuadrature(10, 24));
        log.WriteLine($"RMS: 256 x 256 grid {dense.Statistics.Rms:F6}, {gauss.Samples.Count} Gauss points {gauss.Statistics.Rms:F6}");
        Assert.True(Math.Abs(gauss.Statistics.Rms - dense.Statistics.Rms) < 2e-3 * dense.Statistics.Rms);
        Assert.Equal(Math.PI, new Sampling.GaussQuadrature(10, 24).Points().Sum(p => p.Area), 12);
    }

    /// <summary>
    /// Exit-area weights on Gauss quadrature, from differential rays, give the integral over the
    /// exit pupil's area: converged by 8 rings, and what the square grid's exit-area weights
    /// approach as the grid is refined (to 2×10⁻⁴ of the RMS at 256 × 256, the grid's own error).
    /// </summary>
    [Fact]
    public void GaussQuadratureIntegratesOverTheExitArea()
    {
        var lens = DoubleGauss.Value;
        int field = FullField(lens);
        double Rms(Sampling s) => WavefrontCalculator.Compute(lens, field, lens.PrimaryWavelength, WavefrontOptions.Reference, s).Statistics.Rms;
        double coarse = Rms(new Sampling.GaussQuadrature(8, 32)), fine = Rms(new Sampling.GaussQuadrature(24, 96));
        double grid = Rms(new Sampling.SquareGrid(256));
        log.WriteLine($"exit-area RMS: Gauss 8 rings {coarse:F10}, 24 rings {fine:F10}; 256 x 256 grid {grid:F6}");
        Assert.True(Math.Abs(coarse - fine) < 1e-9, $"{coarse - fine:E2}");
        Assert.True(Math.Abs(grid - fine) < 5e-4 * fine, $"{grid - fine:E2}");
    }

    /// <summary>Aimed onto the exit sphere's canonical grid, every ray lands on its grid point.</summary>
    [Fact]
    public void RaysAimedOntoTheExitGridLandOnIt()
    {
        var lens = DoubleGauss.Value;
        var aimed = WavefrontCalculator.Compute(lens, FullField(lens), lens.PrimaryWavelength,
            WavefrontOptions.Reference with { RayAiming = RayAiming.ExitSphereGrid, Weighting = Weighting.Quadrature },
            new Sampling.GaussQuadrature(10, 24));
        foreach (var s in aimed.Samples)
        {
            Assert.False(s.Vignetted);
            Assert.True(Math.Abs(s.CanonicalX - s.U) < 1e-9 && Math.Abs(s.CanonicalY - s.V) < 1e-9,
                $"target ({s.U:F4}, {s.V:F4}), landed ({s.CanonicalX:F9}, {s.CanonicalY:F9})");
        }
    }

    /// <summary>
    /// Integrated over the exit pupil, the RMS does not depend on where the samples were placed
    /// (method.md §9.2): a launch grid weighted by the exit area each ray stands for gives what a
    /// grid placed uniformly on the exit sphere gives, over the same pupil.
    ///
    /// <para>The same pupil matters. The canonical unit circle is the pupil's image only to first
    /// order: the real exit pupil, the image of the stop's rim, differs from it by the pupil's own
    /// aberration - by about 1% of its radius on this lens at full field - and W is steepest at
    /// the rim. So the exit grid here reaches past the canonical circle and keeps the rays that
    /// pass inside the real stop, which is the launch grid's domain.</para>
    /// </summary>
    [Fact]
    public void TheRmsDoesNotDependOnWhereTheSamplesWerePlaced()
    {
        var lens = DoubleGauss.Value;
        int field = FullField(lens);
        var o = WavefrontOptions.Reference with { ChiefRay = ChiefRayDefinition.StopCenter };
        var launch = WavefrontCalculator.Compute(lens, field, lens.PrimaryWavelength, o, new Sampling.SquareGrid(160));

        var exit = WavefrontCalculator.Compute(lens, field, lens.PrimaryWavelength,
            o with { RayAiming = RayAiming.ExitSphereGrid, Weighting = Weighting.PerRay }, new Widened(160, 1.05));
        var inside = exit.Samples.Where(s => !s.Vignetted && s.Px * s.Px + s.Py * s.Py <= 1.0).ToList();
        double mean = inside.Average(s => s.W);
        double rms = Math.Sqrt(inside.Average(s => (s.W - mean) * (s.W - mean)));

        log.WriteLine($"RMS over the exit pupil: launch grid, exit-area weights {launch.Statistics.Rms:F6}; exit grid, uniform {rms:F6}");
        Assert.True(Math.Abs(launch.Statistics.Rms - rms) < 2e-3 * rms);
    }

    /// <summary>A square grid over a disk of radius <see cref="Radius"/>.</summary>
    private sealed record Widened(int N, double Radius) : Sampling
    {
        public override IReadOnlyList<PupilPoint> Points() =>
            new Sampling.SquareGrid(N).Points().Select(p => p with { Px = Radius * p.Px, Py = Radius * p.Py }).ToList();
    }

    /// <summary>
    /// Singh's refinement: Zernike coefficients fitted to launch-grid samples carried to their grid
    /// points along the wavefront's slope agree with a fit to rays aimed exactly at those points.
    /// Fitted without it, as if each ray had landed on its grid point, they do not.
    /// </summary>
    [Fact]
    public void RefiningToTheLaunchGridMatchesAimingOntoIt()
    {
        var lens = DoubleGauss.Value;
        int field = FullField(lens);
        var grid = new Sampling.SquareGrid(48);
        var launch = WavefrontCalculator.Compute(lens, field, lens.PrimaryWavelength, WavefrontOptions.Reference with { Weighting = Weighting.PerRay }, grid);
        var aimed = WavefrontCalculator.Compute(lens, field, lens.PrimaryWavelength,
            WavefrontOptions.Reference with { Weighting = Weighting.PerRay, RayAiming = RayAiming.ExitSphereGrid }, grid);

        var exact = Zernike.Fit(aimed, ZernikeSet.Standard, 15, PupilCoordinates.Canonical).Coefficients;
        var refined = Zernike.Fit(launch, ZernikeSet.Standard, 15, PupilCoordinates.LaunchRefined).Coefficients;
        var naive = Zernike.Fit(launch with { Samples = launch.Samples.Select(s => s with { CanonicalX = s.U, CanonicalY = s.V }).ToList() },
                                ZernikeSet.Standard, 15, PupilCoordinates.Canonical).Coefficients;

        double refinedError = exact.Zip(refined).Max(p => Math.Abs(p.First - p.Second));
        double naiveError = exact.Zip(naive).Max(p => Math.Abs(p.First - p.Second));
        log.WriteLine($"largest coefficient difference from aiming exactly: refined {refinedError:E3}, unrefined {naiveError:E3} waves");
        Assert.True(refinedError < 0.1 * naiveError, $"refined {refinedError:E3}, unrefined {naiveError:E3}");
    }

    /// <summary>
    /// Two confocal paraboloids, the second convex, make a beam compressor with no aberration on
    /// axis (Mersenne): the image is at infinity, and referred to the plane across the chief ray
    /// the emerging wavefront is flat.
    /// </summary>
    [Fact]
    public void ConfocalParaboloidsMakeAFlatWavefront()
    {
        var sys = new OpticalSystem { Aperture = new Aperture(ApertureType.EPD, 40.0), IsAfocal = true };
        sys.Wavelengths.Add(new Wavelength(0.55, 1.0, true));
        sys.Fields.Add(new Field(0.0));
        sys.Surfaces.Add(new Surface { Index = 0, Thickness = double.PositiveInfinity });
        sys.Surfaces.Add(new Surface { Index = 1, Curvature = -1 / 200.0, Conic = -1, Thickness = -50.0, Material = "MIRROR", IsStop = true });
        sys.Surfaces.Add(new Surface { Index = 2, Curvature = -1 / 100.0, Conic = -1, Thickness = 100.0, Material = "MIRROR" });
        sys.Surfaces.Add(new Surface { Index = 3 });
        var lens = new LensModel(sys, CatalogLocator.LoadBundled());
        Assert.True(WavefrontCalculator.IsAfocal(lens));

        var r = WavefrontCalculator.Compute(lens, 0, 0, WavefrontOptions.Reference, new Sampling.SquareGrid(16));
        Assert.True(r.Geometry.IsAfocal);
        Assert.Empty(r.Samples.Where(s => s.Vignetted));
        double worst = r.Samples.Max(s => Math.Abs(s.W));
        Assert.True(worst < 1e-6, $"largest |W| {worst:E3}");
        // The beam leaves at half its width, travelling back the way it came in.
        Assert.True(r.Samples.All(s => Math.Abs(s.Direction.Z - 1.0) < 1e-9));
    }
}
