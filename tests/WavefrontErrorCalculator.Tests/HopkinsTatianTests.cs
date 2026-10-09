using WavefrontErrorCalculator.Core;
using Xunit;

namespace WavefrontErrorCalculator.Tests;

/// <summary>
/// The wavefront by Hopkins's surface contributions (1952, eq. 7) and Tatian's focal shift (1972,
/// eq. 1) (<see cref="HopkinsTatian"/>), against the optical-path W with the reference of infinite
/// radius, which is what Tatian's focal shift refers to: each ray measured to the foot of the
/// perpendicular from the image point. On every test lens, aiming off and real, they agree to
/// 10⁻⁸ wave at the 857 OPDC points (`wfe hopkins`, docs/verification.md); here a ring of rim points
/// and a diagonal at the largest field. The three singlets are the stress cases of
/// <see cref="RaycesTests"/>, with thousands of waves.
/// </summary>
public class HopkinsTatianTests(Xunit.Abstractions.ITestOutputHelper log)
{
    public static IEnumerable<object[]> Cases() =>
        from lens in new[] { "KingslakeDG", "Cooke_40deg_FC", "US8264785_Ex4", "Relay_1to1", "Objective_NA03_5x",
                             "FastSinglet", "FastSinglet_Defocused", "ShortPupilSinglet" }
        from aiming in new[] { RayAiming.Paraxial, RayAiming.RealStop }
        select new object[] { lens, aiming };

    private static readonly Sampling Points = new Sampling.Given(
        Enumerable.Range(0, 12).Select(k => new PupilPoint(Math.Cos(Math.PI * k / 6), Math.Sin(Math.PI * k / 6), 0.0))
                  .Concat(new[] { new PupilPoint(0.5, 0.5, 0.0), new PupilPoint(-0.3, 0.8, 0.0), new PupilPoint(0.0, -0.9, 0.0),
                                  new PupilPoint(0.7, 0.0, 0.0), new PupilPoint(0.0, 0.0, 0.0) }).ToList());

    private static WavefrontOptions Options(RayAiming aiming) => WavefrontOptions.Zemax with { RayAiming = aiming };

    [Theory]
    [MemberData(nameof(Cases))]
    public void SurfaceContributionsAndTatiansFocalShiftGiveTheInfiniteReferenceW(string lensName, RayAiming aiming)
    {
        var lens = LensModel.Read(Path.Combine(AppContext.BaseDirectory, "TestData", lensName + ".zmx"));
        int field = lens.System.Fields.Count - 1;
        var points = HopkinsTatian.Compute(lens, field, lens.PrimaryWavelength, Options(aiming), Points);
        double worst = 0.0, worstJoin = 0.0, largest = 0.0;
        foreach (var p in points)
        {
            if (double.IsNaN(p.ByPath)) continue;          // a ray that does not pass the lens
            worst = Math.Max(worst, Math.Abs(p.Total - p.ByPath));
            largest = Math.Max(largest, Math.Abs(p.Total));
            if (!double.IsNaN(p.Join)) worstJoin = Math.Max(worstJoin, Math.Abs(p.Join - p.FocalShift));
        }
        log.WriteLine($"{lensName} {aiming}: |Hopkins-Tatian - path| {worst:E3}, |closed form - join| {worstJoin:E3}, |W| up to {largest:F2} waves");
        Assert.True(worst < 1e-8, $"{worst:E3} waves");
        // Tatian's eq. 1 in closed form is the shortest-join geometry it was derived from.
        Assert.True(worstJoin < 1e-8, $"{worstJoin:E3} waves");
    }

    [Fact]
    public void TheChiefRayHasNoAberration()
    {
        var lens = LensModel.Read(Path.Combine(AppContext.BaseDirectory, "TestData", "KingslakeDG.zmx"));
        var chief = HopkinsTatian.Compute(lens, 2, lens.PrimaryWavelength, Options(RayAiming.Paraxial),
                                          new Sampling.Given(new[] { new PupilPoint(0.0, 0.0, 0.0) })).Single();
        Assert.Equal(0.0, chief.Hopkins, 12);
        Assert.Equal(0.0, chief.Total, 12);
    }

    /// <summary>
    /// Why a program that reports Hopkins and Tatian's W and one that reports W on a reference
    /// sphere through the exit pupil part company: the two definitions differ by terms that grow
    /// with the aberration, and more so as the exit pupil comes nearer the image (Tatian p. 79).
    /// On the Kingslake double Gauss at 14° (W to 5.9 waves) Zemax's default OPDC, on the sphere
    /// through the chief ray's crossing of the paraxial exit pupil, is half a wave from it.
    /// </summary>
    [Fact]
    public void TheExitPupilSphereDiffersByMoreAsTheAberrationGrows()
    {
        var lens = LensModel.Read(Path.Combine(AppContext.BaseDirectory, "TestData", "KingslakeDG.zmx"));
        var grid = new Sampling.Given(Enumerable.Range(-8, 17).SelectMany(i => Enumerable.Range(-8, 17).Select(j => (i / 8.0, j / 8.0)))
                                                .Where(t => t.Item1 * t.Item1 + t.Item2 * t.Item2 <= 1.0)
                                                .Select(t => new PupilPoint(t.Item1, t.Item2, 0.0)).ToList());
        double Apart(int field)
        {
            var ht = HopkinsTatian.Compute(lens, field, lens.PrimaryWavelength, Options(RayAiming.Paraxial), grid);
            var opdc = WavefrontCalculator.Compute(lens, field, lens.PrimaryWavelength, Options(RayAiming.Paraxial), grid).Samples;
            return ht.Select((p, k) => opdc[k].Vignetted ? 0.0 : Math.Abs(opdc[k].W - p.Total)).Max();
        }
        double onAxis = Apart(0), edge = Apart(2);
        log.WriteLine($"KingslakeDG: OPDC - Hopkins-Tatian up to {onAxis:E3} on axis, {edge:E3} waves at 14°");
        Assert.True(onAxis < 1e-3);
        Assert.True(edge > 0.3);
    }
}
