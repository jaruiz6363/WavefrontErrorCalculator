using System.Text.Json;
using WavefrontErrorCalculator.Core;
using Xunit;

namespace WavefrontErrorCalculator.Tests;

/// <summary>
/// The LensHHLT preset against LensHH-LT itself (method.md §10, docs/programs.md). The results in
/// TestData/lenshh-lt were gathered from LensHH-LT's engine outside this repository: for each lens,
/// with ray aiming off and real, its OPD fans and a 16×16 wavefront map ray by ray, the RMS and P-V
/// of its 64×64 map, and its Zernike Standard coefficients, with the indices it used. Those in
/// TestData/lenshh-lt-opdc are its wavefront map's OPD at the 857 OPDC points.
///
/// <para>LensHH-LT computes OpticStudio's OPDC: the reference sphere about the primary chief ray's
/// image point, through the chief ray's crossing of the paraxial exit-pupil plane, crossed exactly,
/// with every ray launched toward the lens and a finite object's rays aimed at the stop's real
/// radius. So the LensHHLT preset is the Zemax one.</para>
/// </summary>
public class LensHHLTParityTests(Xunit.Abstractions.ITestOutputHelper log)
{
    public static IEnumerable<object[]> Results() =>
        Directory.GetFiles(Path.Combine(AppContext.BaseDirectory, "TestData", "lenshh-lt"), "*.json")
                 .Select(p => new object[] { Path.GetFileNameWithoutExtension(p) }).OrderBy(o => (string)o[0]);

    private static (JsonElement Data, LensModel Lens, WavefrontOptions Options) Load(string name)
    {
        var data = JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "TestData", "lenshh-lt", name + ".json"))).RootElement;
        var indices = data.GetProperty("wavelengths").EnumerateArray().ToDictionary(
            w => w.GetProperty("index").GetInt32(),
            w => w.GetProperty("indices").EnumerateArray().Select(e => e.GetDouble()).ToArray());
        var lens = LensModel.Read(Path.Combine(AppContext.BaseDirectory, "TestData", data.GetProperty("lens").GetString()!),
                                  w => indices.TryGetValue(w, out var n) ? n : null);
        var options = data.GetProperty("settings").GetProperty("ray_aiming").GetString() == "Off"
            ? WavefrontOptions.LensHHLT
            : WavefrontOptions.LensHHLT with { RayAiming = RayAiming.RealStop };
        return (data, lens, options);
    }

    [Theory]
    [MemberData(nameof(Results))]
    public void TheLensHHLTPresetIsLensHHLTsOpd(string name)
    {
        var (data, lens, options) = Load(name);
        double overall = 0.0;
        var failures = new List<string>();
        foreach (var w in data.GetProperty("wavelengths").EnumerateArray())
        {
            int wave = w.GetProperty("index").GetInt32();
            foreach (var f in w.GetProperty("fields").EnumerateArray())
            {
                int field = f.GetProperty("index").GetInt32();
                var sets = f.GetProperty("fans").EnumerateObject().Select(p => (p.Name, p.Value)).Append(("map", f.GetProperty("map")));
                foreach (var (set, rays) in sets)
                {
                    double[] A(string key) => rays.GetProperty(key).EnumerateArray()
                        .Select(v => v.ValueKind == JsonValueKind.Number ? v.GetDouble() : double.NaN).ToArray();
                    var px = A("px"); var py = A("py"); var opd = A("opd");
                    var vignetted = rays.GetProperty("vignetted").EnumerateArray().Select(v => v.GetBoolean()).ToArray();
                    var points = Enumerable.Range(0, px.Length).Select(k => new PupilPoint(px[k], py[k], 0.0)).ToList();
                    var ours = WavefrontCalculator.Compute(lens, field, wave, options, new Sampling.Given(points));
                    for (int k = 0; k < points.Count; k++)
                    {
                        if (ours.Samples[k].Vignetted != vignetted[k])
                            failures.Add($"wave {wave} field {field} {set} ({px[k]:F3}, {py[k]:F3}): vignetted {ours.Samples[k].Vignetted} here, {vignetted[k]} in LensHH-LT");
                        else if (!vignetted[k])
                            overall = Math.Max(overall, Math.Abs(ours.Samples[k].W - opd[k]));
                    }
                }
            }
        }
        log.WriteLine($"{name}: largest |W - LensHH-LT| {overall:E3} waves");
        Assert.True(failures.Count == 0, string.Join("; ", failures.Take(10)));
        bool aimed = data.GetProperty("settings").GetProperty("ray_aiming").GetString() != "Off";
        // 2.1e-8 wave with aiming off, 2.3e-8 with aiming real, and 6.7e-7 on US8264785 at full field.
        Assert.True(overall < (aimed ? 1e-6 : 1e-7), $"{overall:E3} waves");
    }

    /// <summary>
    /// The RMS and P-V of LensHH-LT's 64×64 map, at the nodes it traces, px = (j - n/2)/(n/2 - 1/2)
    /// and py = (i - (n/2 - 1))/(n/2 - 1/2): the standard deviation, and max - min, to 10⁻⁶ of their size.
    /// </summary>
    [Theory]
    [InlineData("KingslakeDG_Off")]
    [InlineData("Cooke_40deg_FC_Off")]
    public void TheStatisticsAreLensHHLTsOnItsGrid(string name)
    {
        var (data, lens, options) = Load(name);
        var traced = Grid(64);
        foreach (var w in data.GetProperty("wavelengths").EnumerateArray())
            foreach (var f in w.GetProperty("fields").EnumerateArray())
            {
                var ours = WavefrontCalculator.Compute(lens, f.GetProperty("index").GetInt32(), w.GetProperty("index").GetInt32(), options, new Sampling.Given(traced));
                var stats = f.GetProperty("statistics");
                double rms = stats.GetProperty("rms").GetDouble(), pv = stats.GetProperty("pv").GetDouble();
                log.WriteLine($"{name} wave {w.GetProperty("index")} field {f.GetProperty("index")}: RMS {ours.Statistics.Rms:F6} / {rms:F6}, P-V {ours.Statistics.PeakToValley:F6} / {pv:F6}");
                Assert.True(Math.Abs(ours.Statistics.Rms - rms) <= 1e-6 * rms, $"RMS {ours.Statistics.Rms} against {rms}");
                Assert.True(Math.Abs(ours.Statistics.PeakToValley - pv) <= 1e-6 * pv, $"P-V {ours.Statistics.PeakToValley} against {pv}");
            }
    }

    /// <summary>
    /// LensHH-LT's Zernike coefficients (its Zernike Coefficients analysis, from 1.0.162): fitted on
    /// its n×n grid's n - 1 nodes a side from -1 to 1, rim included, with each wavelength referred to
    /// its own chief ray, as OpticStudio's Zernike analysis is. So the ZemaxZernike preset on
    /// <see cref="Sampling.NodeGrid"/>(63) gives its Standard and Fringe coefficients for grid 64.
    /// </summary>
    [Theory]
    [MemberData(nameof(Results))]
    public void TheZemaxZernikePresetIsLensHHLTsZernike(string name)
    {
        var (data, lens, options) = Load(name);
        var zernikeOptions = options with { ChromaticReference = ChromaticReference.OwnChief };
        var sampling = new Sampling.NodeGrid(63);
        double worst = 0.0;
        foreach (var w in data.GetProperty("wavelengths").EnumerateArray())
            foreach (var f in w.GetProperty("fields").EnumerateArray())
            {
                var ours = WavefrontCalculator.Compute(lens, f.GetProperty("index").GetInt32(), w.GetProperty("index").GetInt32(), zernikeOptions, sampling);
                foreach (var (key, set) in new[] { ("zernike", ZernikeSet.Standard), ("zernike_fringe", ZernikeSet.Fringe) })
                {
                    var theirs = f.GetProperty(key).GetProperty("coefficients").EnumerateArray().Select(e => e.GetDouble()).ToArray();
                    var fit = Zernike.Fit(ours, set, theirs.Length);
                    double d = theirs.Select((c, k) => Math.Abs(fit.Coefficients[k] - c)).Max();
                    log.WriteLine($"{name} wave {w.GetProperty("index")} field {f.GetProperty("index")} {set}: {d:E2}");
                    worst = Math.Max(worst, d);
                }
            }
        log.WriteLine($"{name}: largest difference {worst:E2} wave");
        Assert.True(worst < 1e-6, $"largest Zernike difference {worst:E2} wave");
    }

    /// <summary>LensHH-LT's n×n map: the nodes it traces inside the unit circle.</summary>
    private static List<PupilPoint> Grid(int n)
    {
        var traced = new List<PupilPoint>();
        for (int i = 0; i < n; i++)
            for (int j = 0; j < n; j++)
            {
                double px = (j - n / 2) / (n / 2.0 - 0.5), py = (i - (n / 2 - 1)) / (n / 2.0 - 0.5);
                if (px * px + py * py > 1.0) continue;
                traced.Add(new PupilPoint(px, py, 0.0));
            }
        return traced;
    }
    public static IEnumerable<object[]> OpdcResults() =>
        Directory.GetFiles(Path.Combine(AppContext.BaseDirectory, "TestData", "lenshh-lt-opdc"), "*.json")
                 .Select(p => new object[] { Path.GetFileNameWithoutExtension(p) }).OrderBy(o => (string)o[0]);

    /// <summary>
    /// LensHH-LT's per-ray OPD at the 857 OPDC points (TestData/lenshh-lt-opdc; its wavefront map's
    /// own computation, gathered outside this repository). Compared at LensHH-LT's own refractive
    /// indices; the double Gauss also on Schott's F4 (`*_Schott`). Largest differences: 2.2e-8 wave
    /// with aiming off, 3.7e-7 with aiming real (on US8264785 at full field).
    /// </summary>
    [Theory]
    [MemberData(nameof(OpdcResults))]
    public void TheLensHHLTPresetIsLensHHLTsOpdAtTheOpdcPoints(string name)
    {
        var data = JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "TestData", "lenshh-lt-opdc", name + ".json"))).RootElement;
        var indices = data.GetProperty("wavelengths").EnumerateArray().ToDictionary(
            w => w.GetProperty("index").GetInt32(),
            w => w.GetProperty("indices").EnumerateArray().Select(e => e.GetDouble()).ToArray());
        var lens = LensModel.Read(Path.Combine(AppContext.BaseDirectory, "TestData", data.GetProperty("lens").GetString()!),
                                  w => indices.TryGetValue(w, out var n) ? n : null);
        bool aimed = data.GetProperty("settings").GetProperty("ray_aiming").GetString() != "Off";
        var options = WavefrontOptions.LensHHLT with { RayAiming = aimed ? RayAiming.RealStop : RayAiming.Paraxial };

        double overall = 0.0;
        var failures = new List<string>();
        foreach (var w in data.GetProperty("wavelengths").EnumerateArray())
        {
            int wave = w.GetProperty("index").GetInt32();
            foreach (var f in w.GetProperty("fields").EnumerateArray())
            {
                int field = f.GetProperty("index").GetInt32();
                var sets = f.GetProperty("fans").EnumerateObject().Select(p => (p.Name, p.Value)).Append(("map", f.GetProperty("map")));
                foreach (var (set, rays) in sets)
                {
                    double[] A(string key) => rays.GetProperty(key).EnumerateArray()
                        .Select(v => v.ValueKind == JsonValueKind.Number ? v.GetDouble() : double.NaN).ToArray();
                    var px = A("px"); var py = A("py"); var opd = A("opd");
                    var vignetted = rays.GetProperty("vignetted").EnumerateArray().Select(v => v.GetBoolean()).ToArray();
                    var points = Enumerable.Range(0, px.Length).Select(k => new PupilPoint(px[k], py[k], 0.0)).ToList();
                    var ours = WavefrontCalculator.Compute(lens, field, wave, options, new Sampling.Given(points));
                    for (int k = 0; k < points.Count; k++)
                    {
                        if (ours.Samples[k].Vignetted != vignetted[k])
                            failures.Add($"wave {wave} field {field} {set} ({px[k]:F3}, {py[k]:F3}): vignetted {ours.Samples[k].Vignetted} here, {vignetted[k]} in LensHH-LT");
                        else if (!vignetted[k])
                            overall = Math.Max(overall, Math.Abs(ours.Samples[k].W - opd[k]));
                    }
                }
            }
        }
        log.WriteLine($"{name}: largest |W - LensHH-LT| {overall:E3} waves");
        Assert.True(failures.Count == 0, string.Join("; ", failures.Take(10)));
        Assert.True(overall < (aimed ? 1e-6 : 1e-7), $"{overall:E3} waves");
    }
}
