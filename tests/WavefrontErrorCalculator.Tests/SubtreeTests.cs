using AberrationCalculator.Core.Glass;
using AberrationCalculator.Core.IO;
using AberrationCalculator.Core.RayTrace;
using WavefrontErrorCalculator.Core;
using Xunit;

namespace WavefrontErrorCalculator.Tests;

/// <summary>
/// The two things this program needs from AberrationCalculator and was the reason for carrying a
/// newer copy of it: the optical path along a real ray, and ray aiming at the stop. If either
/// goes missing in a subtree pull, these fail before anything subtler does.
/// </summary>
public class SubtreeTests
{
    private static string Lens(string name) => Path.Combine(AppContext.BaseDirectory, "TestData", name);

    [Fact]
    public void TheTraceReportsTheOpticalPathAlongAnAimedRay()
    {
        var catalog = CatalogLocator.LoadBundled();
        var system = LensFile.Read(Lens("KingslakeDG.zmx"), catalog);
        double lambda = system.Wavelengths[system.PrimaryWavelengthIndex].Value;
        var n = IndexResolver.Build(system, catalog, lambda);
        double field = system.Fields.Max(f => Math.Abs(f.Y));
        var p = ParaxialTrace.Trace(system, n, field);

        var launch = StopAimer.ForSystem(system, n, p).Launch(field, 0.0, 0.0);
        Assert.True(launch.HasValue);

        var path = new double[system.Surfaces.Count];
        var hits = RealRayTrace.TraceRecord(system, n, p, field, launch!.Value.Py, launch.Value.Pz,
                                            atParaxialFocus: false, null, null, path);
        int image = system.Surfaces.Count - 1;
        Assert.True(hits[image].Ok);
        Assert.True(path[image] > 0.0);
    }

    [Fact]
    public void ThePresetsAreDistinct()
    {
        Assert.NotEqual(WavefrontOptions.Reference, WavefrontOptions.Optiland);
        Assert.NotEqual(WavefrontOptions.Reference, WavefrontOptions.LensHHLT);
        Assert.Equal(WavefrontOptions.Optiland, WavefrontOptions.Preset("optiland"));
        Assert.Throws<ArgumentException>(() => WavefrontOptions.Preset("nonesuch"));
    }
}
