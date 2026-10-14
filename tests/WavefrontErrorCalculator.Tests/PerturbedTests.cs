using WavefrontErrorCalculator.Core;
using Xunit;

namespace WavefrontErrorCalculator.Tests;

/// <summary>
/// A lens with an element decentred and tilted, as a tolerance analysis builds it: its on-axis
/// chief ray leaves the axis by a few micrometres and crosses it again at a slant of a few
/// hundred-thousandths of a radian. Where it crosses is then all but arbitrary, so under
/// <see cref="ExitPupil.RealChief"/> the on-axis field takes the paraxial exit pupil, as on a
/// centred lens (method.md §5.2), at the point the chief ray passes through its plane.
/// </summary>
public class PerturbedTests
{
    private static readonly Sampling Grid = new Sampling.GaussQuadrature(8, 24);

    private static LensModel Cooke() => LensModel.Read(Path.Combine(AppContext.BaseDirectory, "TestData", "Cooke_40deg_FC.zmx"));

    private static int Primary => Cooke().PrimaryWavelength;

    /// <summary>The Cooke triplet with its last element, behind the stop, decentred 50 µm and 20 µm and tilted a minute of arc about each axis.</summary>
    private static LensModel Perturbed()
    {
        var lens = Cooke();
        const double minute = Math.PI / 180.0 / 60.0;
        foreach (int s in new[] { 5, 6 })
        {
            lens.System.Surfaces[s].DecenterX = 0.05;
            lens.System.Surfaces[s].DecenterY = -0.02;
            lens.System.Surfaces[s].TiltX = minute;
            lens.System.Surfaces[s].TiltY = -minute;
        }
        return lens;
    }

    [Fact]
    public void OnAxisTheSphereStillPassesThroughTheExitPupil()
    {
        var nominal = WavefrontCalculator.Compute(Cooke(), 0, Primary, WavefrontOptions.Reference, Grid).Geometry;
        var moved = WavefrontCalculator.Compute(Perturbed(), 0, Primary, WavefrontOptions.Reference, Grid).Geometry;

        Assert.Equal(nominal.PupilPoint.Z, moved.PupilPoint.Z, 9);
        Assert.Equal(nominal.Radius, moved.Radius, 0.01 * nominal.Radius);
        // E′ is on the chief ray, a few micrometres off the axis.
        Assert.InRange(Math.Abs(moved.PupilPoint.X) + Math.Abs(moved.PupilPoint.Y), 1e-7, 0.05);
    }

    /// <summary>
    /// The case that broke a tolerance analysis: the image where the on-axis chief ray crosses the
    /// axis. Taking that crossing for E′ put it on the image, the sphere shrank to nothing, most
    /// rays missed it and were counted vignetted, and W jumped by tens of waves from one focus to
    /// the next. The last element is tilted a minute of arc, which puts the crossing behind the lens.
    /// </summary>
    [Fact]
    public void OnAxisAnImageAtTheChiefRaysCrossingStillHasItsSphere()
    {
        var lens = Cooke();
        foreach (int s in new[] { 5, 6 }) lens.System.Surfaces[s].TiltX = Math.PI / 180.0 / 60.0;
        var g = WavefrontCalculator.Compute(lens, 0, Primary, WavefrontOptions.Reference, Grid).Geometry;
        var (r, d) = (g.ChiefImagePoint, g.ChiefDirection);
        double crossing = r.Z - (r.X * d.X + r.Y * d.Y) / (d.X * d.X + d.Y * d.Y) * d.Z;
        Assert.InRange(crossing, -40.0, -5.0);                  // behind the last surface, in air

        lens.System.Surfaces[6].Thickness += crossing + 0.01;   // the image 10 µm past the crossing
        var w = WavefrontCalculator.Compute(lens, 0, Primary, WavefrontOptions.Reference, Grid);

        Assert.Equal(0, w.Statistics.Vignetted);
        Assert.True(w.Geometry.Radius > 10.0, $"the reference sphere's radius is {w.Geometry.Radius} mm");
    }

    [Fact]
    public void OffAxisTheRealChiefRayStillSetsThePupil()
    {
        var lens = Perturbed();
        int edge = lens.System.Fields.Count - 1;
        var g = WavefrontCalculator.Compute(lens, edge, lens.PrimaryWavelength, WavefrontOptions.Reference, Grid).Geometry;
        var r = new Vec3(g.ChiefImagePoint.X, g.ChiefImagePoint.Y, g.ChiefImagePoint.Z);
        // Where that chief ray comes closest to the axis, as eq. 4.31 has it.
        var d = g.ChiefDirection;
        double s = -(r.X * d.X + r.Y * d.Y) / (d.X * d.X + d.Y * d.Y);
        Assert.Equal(r.Z + s * d.Z, g.PupilPoint.Z, 9);
    }
}

/// <summary>
/// The stop's radius on a lens built with errors (<see cref="LensModel.StopHeight"/>). Measured on
/// the lens itself, it is where the axial marginal ray crosses the stop, which a decenter ahead of
/// the stop moves to first order in y and not in x: the stop would grow or shrink with the
/// direction of an error. Given the design's radius, a decenter in x and one in y are alike.
/// </summary>
public class StopHeightTests
{
    private static readonly Sampling Grid = new Sampling.GaussQuadrature(8, 24);

    private static LensModel Cooke() => LensModel.Read(Path.Combine(AppContext.BaseDirectory, "TestData", "Cooke_40deg_FC.zmx"));

    /// <summary>The middle element, which carries the stop, decentered 70 µm in x or in y.</summary>
    private static LensModel Decentered(bool inY, double? stopHeight)
    {
        var nominal = Cooke();
        foreach (int s in new[] { 3, 4 })
        {
            if (inY) nominal.System.Surfaces[s].DecenterY = 0.07; else nominal.System.Surfaces[s].DecenterX = 0.07;
        }
        return new LensModel(nominal.System, AberrationCalculator.Core.Glass.CatalogLocator.LoadBundled(), null, stopHeight);
    }

    private static double Rms(LensModel lens) =>
        WavefrontCalculator.Compute(lens, 0, lens.PrimaryWavelength, WavefrontOptions.Reference, Grid).Statistics.RmsStandardDeviation;

    [Fact]
    public void MeasuredOnTheLensADecenterInYResizesTheStop()
    {
        double nominal = Cooke().StopHeight!.Value;
        Assert.True(Math.Abs(Decentered(true, null).StopHeight!.Value - nominal) > 1e-3 * nominal);
        Assert.Equal(nominal, Decentered(false, null).StopHeight!.Value, 4);
    }

    [Fact]
    public void GivenTheDesignsStopADecenterInXAndInYAreAlike()
    {
        double nominal = Cooke().StopHeight!.Value;
        var y = Decentered(true, nominal);
        var x = Decentered(false, nominal);
        Assert.Equal(nominal, y.StopHeight!.Value, 12);
        Assert.Equal(Rms(y), Rms(x), 9);
        Assert.NotEqual(Rms(Decentered(true, null)), Rms(Decentered(false, null)), 4);
    }
}
