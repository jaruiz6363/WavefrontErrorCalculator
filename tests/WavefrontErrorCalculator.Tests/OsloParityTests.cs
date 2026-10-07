using System.Text.Json;
using WavefrontErrorCalculator.Core;
using Xunit;

namespace WavefrontErrorCalculator.Tests;

/// <summary>
/// OSLO EDU 6.6 against this program (method.md §10, docs/programs.md). The results in
/// TestData/oslo were gathered by a CCL macro run in OSLO by hand: for each lens, ray aiming
/// "enp" (the entrance pupil) and "crr" (the central reference ray, OSLO's default), and the
/// reference sphere at the exit pupil, at infinity and at the last surface, the OPD of single rays
/// at OSLO's own fractional pupil coordinates along both fans and over a grid, with the indices
/// OSLO used and the reference sphere radius it reported.
///
/// <para>The same (FY, FX) are traced here with:
/// <list type="bullet">
/// <item>the chief ray aimed at the centre of the real stop (<see cref="ChiefRayDefinition.RealStopCenter"/>);</item>
/// <item>each wavelength referred to its own reference ray (<see cref="ChromaticReference.OwnChief"/>);</item>
/// <item>OSLO's pupil coordinates (<see cref="RayAiming.Aplanatic"/>, <see cref="RayAiming.AplanaticReference"/>),
/// by direction sines for a finite object of NA 0.1 or more and by the pupil's plane otherwise;</item>
/// <item>the reference sphere at infinity (<see cref="ExitPupil.Infinite"/>), through the chief ray's
/// crossing of the last surface (<see cref="ExitPupil.LastSurface"/>), or, at the exit pupil, of the
/// radius OSLO reports (<see cref="ExitPupil.UserRadius"/>): the rule OSLO computes that radius by is
/// not yet known.</item>
/// </list></para>
/// </summary>
public class OsloParityTests(Xunit.Abstractions.ITestOutputHelper log)
{
    public static IEnumerable<object[]> Results() =>
        Directory.GetFiles(Path.Combine(AppContext.BaseDirectory, "TestData", "oslo"), "*.json")
                 .Select(p => new object[] { Path.GetFileNameWithoutExtension(p) }).OrderBy(o => (string)o[0]);

    [Theory]
    [MemberData(nameof(Results))]
    public void OslosOpdAtItsOwnPupilCoordinates(string name) => ComparesRayByRay("oslo", name);

    public static IEnumerable<object[]> OpdcResults() =>
        Directory.GetFiles(Path.Combine(AppContext.BaseDirectory, "TestData", "oslo-opdc"), "*.json")
                 .Select(p => new object[] { Path.GetFileNameWithoutExtension(p) }).OrderBy(o => (string)o[0]);

    /// <summary>
    /// The same at the 857 pupil points per field and wavelength of the OPDC comparison
    /// (docs/opd/compare): a grid of spacing 1/16 and 60 rim points, both aiming modes, the
    /// reference sphere at the exit pupil.
    /// </summary>
    [Theory]
    [MemberData(nameof(OpdcResults))]
    public void OslosOpdAtTheOpdcPoints(string name) => ComparesRayByRay("oslo-opdc", name);

    private void ComparesRayByRay(string folder, string name)
    {
        var data = JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "TestData", folder, name + ".json"))).RootElement;
        var indices = data.GetProperty("wavelengths").EnumerateArray().ToDictionary(
            w => w.GetProperty("index").GetInt32(),
            w => w.GetProperty("indices").EnumerateArray().Select(e => e.GetDouble()).ToArray());
        string lensName = data.GetProperty("lens").GetString()!;
        var lens = LensModel.Read(Path.Combine(AppContext.BaseDirectory, "TestData", lensName),
                                  w => indices.TryGetValue(w, out var n) ? n : null);
        var settings = data.GetProperty("settings");
        bool central = settings.GetProperty("raim").GetString() == "crr";
        // The aplanatic modes choose OSLO's sine or plane mapping themselves, by the object NA.
        var aiming = central ? RayAiming.AplanaticReference : RayAiming.Aplanatic;
        var exitPupil = settings.GetProperty("wrsp").GetString() switch
        {
            "xpu" => ExitPupil.UserRadius,
            "inf" => ExitPupil.Infinite,
            "lsf" => ExitPupil.LastSurface,
            var s => throw new NotSupportedException(s),
        };

        double overall = 0.0;
        var failures = new List<string>();
        foreach (var w in data.GetProperty("wavelengths").EnumerateArray())
        {
            int wave = w.GetProperty("index").GetInt32();
            foreach (var f in w.GetProperty("fields").EnumerateArray())
            {
                int field = f.GetProperty("index").GetInt32();
                var options = WavefrontOptions.Zemax with
                {
                    RayAiming = aiming,
                    // OSLO names (FY, FX) on the entrance pupil's plane in entrance pupil mode, and
                    // upright about the reference ray in central reference ray mode.
                    PupilOrientation = central ? PupilOrientation.MarginalRay : PupilOrientation.EntrancePupilPlane,
                    ChiefRay = ChiefRayDefinition.RealStopCenter,
                    ChromaticReference = ChromaticReference.OwnChief,
                    ExitPupil = exitPupil,
                    UserReferenceRadius = f.GetProperty("reference").GetProperty("radius").GetDouble(),
                };
                var sets = f.GetProperty("fans").EnumerateObject().Select(p => (p.Name, p.Value)).Append(("map", f.GetProperty("map")));
                foreach (var (set, rays) in sets)
                {
                    double[] A(string key) => rays.GetProperty(key).EnumerateArray()
                        .Select(v => v.ValueKind == JsonValueKind.Number ? v.GetDouble() : double.NaN).ToArray();
                    var px = A("px"); var py = A("py"); var opd = A("opd");
                    var points = Enumerable.Range(0, px.Length).Select(k => new PupilPoint(px[k], py[k], 0.0)).ToList();
                    var ours = WavefrontCalculator.Compute(lens, field, wave, options, new Sampling.Given(points));
                    double worst = 0.0;
                    for (int k = 0; k < points.Count; k++)
                    {
                        bool theirs = double.IsNaN(opd[k]);
                        if (ours.Samples[k].Vignetted != theirs)
                            failures.Add($"wave {wave} field {field} {set} ({px[k]:F3}, {py[k]:F3}): vignetted {ours.Samples[k].Vignetted} here, {theirs} in OSLO");
                        else if (!theirs)
                            worst = Math.Max(worst, Math.Abs(ours.Samples[k].W - opd[k]));
                    }
                    overall = Math.Max(overall, worst);
                    // OSLO prints 15 digits; what is left is the two programs' ray aiming iterations.
                    if (worst >= 3e-8) failures.Add($"wave {wave} field {field} {set}: {worst:E3} waves");
                }
            }
        }
        log.WriteLine($"{name}: largest |W - OSLO| {overall:E3} waves");
        Assert.True(failures.Count == 0, string.Join("; ", failures.Take(10)));
    }

    /// <summary>
    /// OSLO's wavefront statistics and Zernike fit, from its spot diagram: a grid of cell centres
    /// spaced 2/17.03 of the pupil (its default 17.03 aperture divisions) across the unit circle,
    /// 232 rays, each counted once, the grid scaled by 0.999724 (found so on the double Gauss on
    /// axis; it then holds for all 30 results).
    /// <list type="bullet">
    /// <item><c>wavefront(ref)</c>: the standard deviation and max - min of W.</item>
    /// <item><c>wavefront()</c>, OSLO's default: the same after the lateral shift of the reference
    /// point that minimises the RMS, which removes tilt linear in where the rays cross the
    /// reference sphere (Welford eq. 7.18), or in their directions when it is at infinity.</item>
    /// <item><c>zernike_fit()</c> on axis: Fringe, 36 terms, unweighted, in exit-sphere
    /// coordinates with OSLO's angle from the y axis, the rim ray (FY = 1) at radius 1. Off axis
    /// its unit radius differs from the rim ray's by 0.2 to 0.5 %, by a rule not yet known.</item>
    /// </list>
    /// </summary>
    [Theory]
    [MemberData(nameof(Results))]
    public void OslosStatisticsAndZernikeFitOnAxis(string name)
    {
        var data = JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "TestData", "oslo", name + ".json"))).RootElement;
        var indices = data.GetProperty("wavelengths").EnumerateArray().ToDictionary(
            w => w.GetProperty("index").GetInt32(),
            w => w.GetProperty("indices").EnumerateArray().Select(e => e.GetDouble()).ToArray());
        var lens = LensModel.Read(Path.Combine(AppContext.BaseDirectory, "TestData", data.GetProperty("lens").GetString()!),
                                  w => indices.TryGetValue(w, out var n) ? n : null);
        var settings = data.GetProperty("settings");
        bool central = settings.GetProperty("raim").GetString() == "crr";
        var exitPupil = settings.GetProperty("wrsp").GetString() switch
        {
            "xpu" => ExitPupil.UserRadius,
            "inf" => ExitPupil.Infinite,
            _ => ExitPupil.LastSurface,
        };

        const double scale = 0.999724074;
        double step = 2.0 / 17.03 * scale;
        var grid = new List<PupilPoint>();
        for (int i = -10; i <= 10; i++)
            for (int j = -10; j <= 10; j++)
            {
                double fx = (i + 0.5) * step, fy = (j + 0.5) * step;
                if (fx * fx + fy * fy <= scale * scale * (1.0 + 1e-12)) grid.Add(new PupilPoint(fx, fy, 0.0));
            }
        Assert.Equal(232, grid.Count);

        double worstStats = 0.0, worstZernike = 0.0;
        foreach (var w in data.GetProperty("wavelengths").EnumerateArray())
        {
            int wave = w.GetProperty("index").GetInt32();
            foreach (var f in w.GetProperty("fields").EnumerateArray())
            {
                int field = f.GetProperty("index").GetInt32();
                var options = WavefrontOptions.Zemax with
                {
                    RayAiming = central ? RayAiming.AplanaticReference : RayAiming.Aplanatic,
                    PupilOrientation = central ? PupilOrientation.MarginalRay : PupilOrientation.EntrancePupilPlane,
                    ChiefRay = ChiefRayDefinition.RealStopCenter,
                    ChromaticReference = ChromaticReference.OwnChief,
                    ExitPupil = exitPupil,
                    UserReferenceRadius = f.GetProperty("reference").GetProperty("radius").GetDouble(),
                };
                var ours = WavefrontCalculator.Compute(lens, field, wave, options, new Sampling.Given(grid));
                var used = ours.Samples.Where(s => !s.Vignetted).ToList();
                var stats = f.GetProperty("statistics");

                double mean = used.Average(s => s.W);
                double rms = Math.Sqrt(used.Average(s => (s.W - mean) * (s.W - mean)));
                double pv = used.Max(s => s.W) - used.Min(s => s.W);
                worstStats = Math.Max(worstStats, Math.Max(Math.Abs(rms - stats.GetProperty("rms").GetDouble()),
                                                           Math.Abs(pv - stats.GetProperty("pv").GetDouble())));

                // The lateral shift of the reference point: tilt in the reference sphere's
                // coordinates, or in the ray directions for a reference at infinity.
                bool atInfinity = double.IsInfinity(ours.Geometry.Radius);
                var tilt = used.Select(s => atInfinity ? (s.Direction.X, s.Direction.Y) : (s.Sphere.X, s.Sphere.Y)).ToList();
                var residual = PlaneResidual(used.Select(s => s.W).ToList(), tilt);
                double rmsT = Math.Sqrt(residual.Average(v => v * v));
                double pvT = residual.Max() - residual.Min();
                worstStats = Math.Max(worstStats, Math.Max(Math.Abs(rmsT - stats.GetProperty("rms_default").GetDouble()),
                                                           Math.Abs(pvT - stats.GetProperty("pv_default").GetDouble())));

                if (field == 0 && !atInfinity)
                {
                    // The rim ray's exit-sphere radius is the unit of OSLO's fit on axis.
                    var rim = WavefrontCalculator.Compute(lens, field, wave, options,
                                                          new Sampling.Given(new List<PupilPoint> { new(0.0, 1.0, 0.0) }));
                    double unit = Math.Abs(rim.Samples[0].ExitY);
                    var swapped = ours with
                    {
                        Samples = ours.Samples.Select(s => s with { ExitX = s.ExitY / unit, ExitY = s.ExitX / unit }).ToList(),
                    };
                    var fit = Zernike.Fit(swapped, ZernikeSet.Fringe, 36, PupilCoordinates.ExitSphere);
                    var theirs = f.GetProperty("zernike").GetProperty("coefficients").EnumerateArray().Select(e => e.GetDouble()).ToArray();
                    worstZernike = Math.Max(worstZernike, theirs.Select((c, k) => Math.Abs(fit.Coefficients[k] - c)).Max());
                }
            }
        }
        log.WriteLine($"{name}: statistics {worstStats:E2} waves, Zernike on axis {worstZernike:E2} waves");
        Assert.True(worstStats < 5e-8, $"statistics {worstStats:E2}");
        // On axis the fit is exact to 2e-9 on the double Gauss at its primary wavelength; the unit
        // radius then drifts from the rim ray's by up to 2e-4 (the fast aspheric lens), and the
        // high-order lenses keep up to 1e-5 of misfit whatever the unit (docs/programs.md).
        Assert.True(worstZernike < 2e-4, $"Zernike on axis {worstZernike:E2}");
    }

    /// <summary>W less its least-squares plane a + b·x + c·y.</summary>
    private static List<double> PlaneResidual(List<double> w, List<(double X, double Y)> at)
    {
        double n = w.Count, sx = 0, sy = 0, sxx = 0, syy = 0, sxy = 0, sw = 0, swx = 0, swy = 0;
        for (int k = 0; k < w.Count; k++)
        {
            var (x, y) = at[k];
            sx += x; sy += y; sxx += x * x; syy += y * y; sxy += x * y; sw += w[k]; swx += w[k] * x; swy += w[k] * y;
        }
        var m = new double[3, 4] { { n, sx, sy, sw }, { sx, sxx, sxy, swx }, { sy, sxy, syy, swy } };
        for (int c = 0; c < 3; c++)
        {
            int p = c;
            for (int r = c + 1; r < 3; r++) if (Math.Abs(m[r, c]) > Math.Abs(m[p, c])) p = r;
            for (int k = 0; k < 4; k++) (m[c, k], m[p, k]) = (m[p, k], m[c, k]);
            for (int r = 0; r < 3; r++)
            {
                if (r == c) continue;
                double factor = m[r, c] / m[c, c];
                for (int k = c; k < 4; k++) m[r, k] -= factor * m[c, k];
            }
        }
        double a = m[0, 3] / m[0, 0], b = m[1, 3] / m[1, 1], c2 = m[2, 3] / m[2, 2];
        return w.Select((v, k) => v - a - b * at[k].X - c2 * at[k].Y).ToList();
    }
}
