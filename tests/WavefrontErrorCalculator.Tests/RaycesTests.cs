using WavefrontErrorCalculator.Core;
using Xunit;

namespace WavefrontErrorCalculator.Tests;

/// <summary>
/// The wavefront two independent ways: by optical path (<see cref="WavefrontCalculator"/>) and by
/// integrating Rayces's exact relation between wave and ray aberration across the pupil
/// (<see cref="RaycesIntegration"/>), which uses only where the rays go in image space. On every
/// test lens, on axis and off, with ray aiming off and real, and on both WEC's reference sphere and
/// Zemax's, they agree to 10⁻¹⁰ wave (10⁻⁹ on the Cooke triplet, 7×10⁻⁹ on US8264785's aspheres)
/// at the 857 OPDC points (`wfe rayces`, docs/verification.md). Here, a ring of rim points and a
/// diagonal at the largest field, on shared paths and on one straight path per point. The three
/// singlets are stress cases (docs/verification.md): f/1.25 with up to 1,400 waves, the same
/// defocused by 1 mm, and one with its stop 6.7 mm before the image, up to 3,900 waves.
/// </summary>
public class RaycesTests(Xunit.Abstractions.ITestOutputHelper log)
{
    public static IEnumerable<object[]> Cases() =>
        from lens in new[] { "KingslakeDG", "Cooke_40deg_FC", "US8264785_Ex4", "Relay_1to1", "Objective_NA03_5x",
                             "FastSinglet", "FastSinglet_Defocused", "ShortPupilSinglet" }
        from aiming in new[] { RayAiming.Paraxial, RayAiming.RealStop }
        from sphere in new[] { ExitPupil.RealChief, ExitPupil.ParaxialChiefIntersect }
        from paths in new[] { RaycesPaths.Shared, RaycesPaths.Radial }
        select new object[] { lens, aiming, sphere, paths };

    private static readonly (double, double)[] Points =
        Enumerable.Range(0, 12).Select(k => (Math.Cos(Math.PI * k / 6), Math.Sin(Math.PI * k / 6)))
                  .Concat(new[] { (0.5, 0.5), (-0.3, 0.8), (0.0, -0.9), (0.7, 0.0) }).ToArray();

    [Theory]
    [MemberData(nameof(Cases))]
    public void IntegratingTheRayAberrationsGivesTheOpticalPathW(string lensName, RayAiming aiming, ExitPupil sphere, RaycesPaths paths)
    {
        var lens = LensModel.Read(Path.Combine(AppContext.BaseDirectory, "TestData", lensName + ".zmx"));
        var options = WavefrontOptions.Reference with
        {
            RayAiming = aiming, ExitPupil = sphere, ChiefRay = ChiefRayDefinition.StopCenter,
            PupilOrientation = PupilOrientation.EntrancePupilPlane,
        };
        int field = lens.System.Fields.Count - 1;
        var result = RaycesIntegration.Compute(lens, field, options, Points, paths: paths);
        double worst = 0.0;
        foreach (var p in result)
        {
            // A ray that does not pass the lens has no W either way (US8264785's top rim at
            // 17.5° with real aiming); every ray that does is reached, along a shared line or its own path.
            if (double.IsNaN(p.ByPath)) continue;
            Assert.False(double.IsNaN(p.Integrated), $"({p.Px}, {p.Py}) not reached");
            worst = Math.Max(worst, Math.Abs(p.Integrated - p.ByPath));
        }
        log.WriteLine($"{lensName} {aiming} {sphere} {paths}: largest |integrated - path| {worst:E3} waves");
        Assert.True(worst < 2e-8, $"{worst:E3} waves");
    }

    /// <summary>
    /// US8264785 at 17.5° has no aperture in its file, and with the defaults (real aiming, the
    /// chief ray at the vignetted pupil's centre) the top of its pupil is where the ray meets total
    /// internal reflection at surface 5: the ray leaves it at 89.97°, and W climbs from 0.27 to
    /// 17.9 waves over the last 6% of the pupil, with a slope that goes as 1/√(distance to the
    /// edge). Evenly spaced nodes stopped at 3×10⁻⁴ wave there however many; nodes closer together
    /// toward the edge reach the lens's floor.
    /// </summary>
    [Theory]
    [InlineData(RaycesPaths.Shared)]
    [InlineData(RaycesPaths.Radial)]
    public void APupilEndingAtTotalInternalReflectionIsReached(RaycesPaths paths)
    {
        var lens = LensModel.Read(Path.Combine(AppContext.BaseDirectory, "TestData", "US8264785_Ex4.zmx"));
        var result = RaycesIntegration.Compute(lens, 2, WavefrontOptions.Reference, new[] { (0.0, 1.0), (0.0, 0.99) }, paths: paths);
        foreach (var p in result)
        {
            log.WriteLine($"{paths} ({p.Px}, {p.Py}): W {p.ByPath:F4}, |integrated - path| {Math.Abs(p.Integrated - p.ByPath):E3} waves");
            Assert.True(Math.Abs(p.Integrated - p.ByPath) < 2e-8, $"({p.Px}, {p.Py}): {Math.Abs(p.Integrated - p.ByPath):E3} waves");
        }
        Assert.True(result[0].ByPath > 17.0, $"the rim's W is {result[0].ByPath:F4}, not the near-reflection value");
    }

    /// <summary>
    /// With the exit pupil close to the image the rays meet the reference sphere far from its
    /// radius, and Nijboer's W, along the radius, parts from the along-ray W the programs report:
    /// at the rim of ShortPupilSinglet at 3° (R = 18 mm, 32° between ray and radius) by 685 of
    /// 3,928 waves. The integration, converted to the along-ray W, still agrees with the optical path.
    /// </summary>
    [Fact]
    public void WithThePupilCloseToTheImageTheTwoDefinitionsOfWPartButTheMethodsAgree()
    {
        var lens = LensModel.Read(Path.Combine(AppContext.BaseDirectory, "TestData", "ShortPupilSinglet.zmx"));
        var options = WavefrontOptions.Reference with { RayAiming = RayAiming.Paraxial, ChiefRay = ChiefRayDefinition.StopCenter };
        var p = RaycesIntegration.Compute(lens, 1, options, new[] { (0.0, -1.0) })[0];
        log.WriteLine($"W {p.ByPath:F3}, integrated {p.Integrated:F3}, Nijboer {p.Nijboer:F3} waves");
        Assert.True(Math.Abs(p.Integrated - p.ByPath) < 2e-8, $"{Math.Abs(p.Integrated - p.ByPath):E3} waves");
        Assert.InRange(Math.Abs(p.ByPath), 3900.0, 3950.0);
        Assert.InRange(Math.Abs(p.Integrated - p.Nijboer), 680.0, 690.0);
    }
}
