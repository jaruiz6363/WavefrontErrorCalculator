using System.Text.Json;
using WavefrontErrorCalculator.Core;
using Xunit;

namespace WavefrontErrorCalculator.Tests;

/// <summary>
/// The Zemax presets against Zemax OpticStudio itself (method.md §10, docs/programs.md). The
/// results in TestData/zemax were gathered from OpticStudio outside this repository, in the result
/// format (docs/result-format.md): for each lens, each Reference OPD setting and ray aiming off and
/// real, the OPD of its batch ray trace along both fans and over a grid, and its Zernike Standard
/// Coefficients analysis with the RMS and P-V that analysis reports, with the indices OpticStudio
/// used. The same pupil points are traced here on those indices and compared.
/// </summary>
public class ZemaxParityTests(Xunit.Abstractions.ITestOutputHelper log)
{
    public static IEnumerable<object[]> Results() =>
        Directory.GetFiles(Path.Combine(AppContext.BaseDirectory, "TestData", "zemax"), "*.json")
                 .Select(p => new object[] { Path.GetFileNameWithoutExtension(p) }).OrderBy(o => (string)o[0]);

    /// <summary>
    /// The analyses' statistics are checked on a few results: each one traces 51,000 rays per field
    /// and wavelength. Between them they cover every Reference OPD setting, ray aiming, and a lens
    /// whose fixed apertures vignette.
    /// </summary>
    public static IEnumerable<object[]> StatisticsResults() => new[]
    {
        "KingslakeDG_ExitPupil_Off", "KingslakeDG_Infinity_Off", "KingslakeDG_Absolute_Off", "KingslakeDG_ExitPupil_Real",
        "Cooke_40deg_FC_ExitPupil_Off", "US8264785_Ex4_ExitPupil_Off", "Relay_1to1_ExitPupil_Off", "Objective_NA03_5x_ExitPupil_Off",
    }.Select(n => new object[] { n });

    /// <summary>The Zemax preset with the switches the result's own settings call for.</summary>
    private static WavefrontOptions OptionsFor(JsonElement data, WavefrontOptions preset)
    {
        var settings = data.GetProperty("settings");
        var exitPupil = settings.GetProperty("reference_opd").GetString() switch
        {
            "ExitPupil" => ExitPupil.ParaxialChiefIntersect,
            "Infinity" => ExitPupil.Infinite,
            "Absolute" or "Absolute2" => ExitPupil.ImageSurface,
            var s => throw new NotSupportedException($"Reference OPD {s}"),
        };
        var aiming = settings.GetProperty("ray_aiming").GetString() switch
        {
            "Off" => RayAiming.Paraxial,
            "Real" => RayAiming.RealStop,
            var s => throw new NotSupportedException($"ray aiming {s}"),
        };
        return preset with { ExitPupil = exitPupil, RayAiming = aiming };
    }

    private static (JsonElement Data, LensModel Lens) Load(string name, string folder = "zemax")
    {
        var data = JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "TestData", folder, name + ".json"))).RootElement;
        var indices = data.GetProperty("wavelengths").EnumerateArray().ToDictionary(
            w => w.GetProperty("index").GetInt32(),
            w => w.GetProperty("indices").EnumerateArray().Select(e => e.GetDouble()).ToArray());
        var lens = LensModel.Read(Path.Combine(AppContext.BaseDirectory, "TestData", data.GetProperty("lens").GetString()!),
                                  w => indices.TryGetValue(w, out var n) ? n : null);
        return (data, lens);
    }

    private static bool Aimed(JsonElement data) => data.GetProperty("settings").GetProperty("ray_aiming").GetString() != "Off";

    [Theory]
    [MemberData(nameof(Results))]
    public void TheZemaxPresetIsOpticStudiosOpd(string name)
    {
        // With ray aiming on, the two programs' aiming iterations stop at slightly different
        // places: what is left grows with the slope of W, to 2.5e-6 wave where W spans over 160
        // waves across the pupil (Absolute; docs/programs.md).
        var (data, _) = Load(name);
        ComparesRayByRay(name, "zemax", Aimed(data) ? 5e-6 : 1e-7);
    }

    public static IEnumerable<object[]> OpdcResults() =>
        Directory.GetFiles(Path.Combine(AppContext.BaseDirectory, "TestData", "zemax-opdc"), "*.json")
                 .Select(p => new object[] { Path.GetFileNameWithoutExtension(p) }).OrderBy(o => (string)o[0]);

    /// <summary>
    /// OpticStudio's OPDC operand - OPD with respect to the chief ray, Reference OPD "Exit Pupil" -
    /// read from the Merit Function Editor at 857 pupil points per field and wavelength: a grid of
    /// spacing 1/16 inside the pupil and 60 points on its rim. OpticStudio stops evaluating a merit
    /// function at the first ray that fails, so the rows after a failure were evaluated again in
    /// further passes (gathered outside this repository). The vignetted rays are the same here.
    /// With ray aiming on, 3 of the 23,000 rays differ by more than 1e-6 wave (6.1e-6 at most):
    /// all on US8264785 at 17.5 deg near the top rim, where W climbs to grazing rays, and where
    /// OpticStudio's own values at mirror-image points differ by 7e-7.
    /// </summary>
    [Theory]
    [MemberData(nameof(OpdcResults))]
    public void TheZemaxPresetIsOpticStudiosOpdcOperand(string name)
    {
        var (data, _) = Load(name, "zemax-opdc");
        ComparesRayByRay(name, "zemax-opdc", Aimed(data) ? 1e-5 : 1e-7);
    }

    public static IEnumerable<object[]> InfinityResults() =>
        Results().Where(r => ((string)r[0]).Contains("_Infinity_"));

    /// <summary>
    /// Hopkins's surface contributions with Tatian's focal shift (<see cref="HopkinsTatian"/>)
    /// against OpticStudio's OPD with Reference OPD "Infinity", directly: OpticStudio's own pupil
    /// points, indices, fields and wavelengths, ray aiming as each result was gathered. Tatian's
    /// focal shift refers W to the foot of the perpendicular from the image point; this is the
    /// test that OpticStudio's "Infinity" setting computes that same W.
    /// </summary>
    [Theory]
    [MemberData(nameof(InfinityResults))]
    public void HopkinsAndTatiansWIsOpticStudiosInfinityOpd(string name)
    {
        var (data, lens) = Load(name);
        var options = OptionsFor(data, WavefrontOptions.Zemax);
        double tolerance = Aimed(data) ? 5e-6 : 1e-7;
        double overall = 0.0;
        int compared = 0;
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
                    var ht = HopkinsTatian.Compute(lens, field, wave, options, new Sampling.Given(points));
                    double worst = 0.0;
                    for (int k = 0; k < points.Count; k++)
                    {
                        if (vignetted[k] || double.IsNaN(ht[k].Total)) continue;
                        worst = Math.Max(worst, Math.Abs(ht[k].Total - opd[k]));
                        compared++;
                    }
                    overall = Math.Max(overall, worst);
                    if (worst >= tolerance) failures.Add($"wave {wave} field {field} {set}: {worst:E3} waves");
                }
            }
        }
        log.WriteLine($"{name}: {compared} rays, largest |Hopkins-Tatian - OpticStudio| {overall:E3} waves");
        Assert.True(compared > 0);
        Assert.True(failures.Count == 0, string.Join("; ", failures.Take(10)));
    }

    private void ComparesRayByRay(string name, string folder, double tolerance)
    {
        var (data, lens) = Load(name, folder);
        var options = OptionsFor(data, WavefrontOptions.Zemax);

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

                    double worst = 0.0;
                    for (int k = 0; k < points.Count; k++)
                    {
                        if (ours.Samples[k].Vignetted != vignetted[k])
                            failures.Add($"wave {wave} field {field} {set} ({px[k]:F3}, {py[k]:F3}): vignetted {ours.Samples[k].Vignetted} here, {vignetted[k]} in OpticStudio");
                        else if (!vignetted[k])
                            worst = Math.Max(worst, Math.Abs(ours.Samples[k].W - opd[k]));
                    }
                    overall = Math.Max(overall, worst);
                    if (worst >= tolerance) failures.Add($"wave {wave} field {field} {set}: {worst:E3} waves");
                }
            }
        }
        log.WriteLine($"{name}: largest |W - OpticStudio| {overall:E3} waves");
        Assert.True(failures.Count == 0, string.Join("; ", failures.Take(10)));
    }

    /// <summary>
    /// The Zernike Standard Coefficients analysis: its RMS and P-V "to chief" and its 37
    /// coefficients, against the ZemaxZernike preset on a grid of 255 nodes a side - what
    /// OpticStudio's "256 x 256" sampling is - fitted unweighted in launch coordinates.
    /// OpticStudio prints them to 1e-8.
    /// </summary>
    [Theory]
    [MemberData(nameof(StatisticsResults))]
    public void TheZemaxZernikePresetIsOpticStudiosZernikeAnalysis(string name)
    {
        var (data, lens) = Load(name);
        var options = OptionsFor(data, WavefrontOptions.ZemaxZernike);
        // Ray aiming leaves its 1e-8 to 1e-7 wave in the coefficients (see above). Without it, the
        // results agree to the 1e-8 OpticStudio prints, but for the NA 0.3 objective, whose rays
        // already differ by up to 6e-8 wave: its blue P-V is 5e-8 apart.
        double tolerance = Aimed(data) ? 2e-6 : 1e-7;
        var sampling = new Sampling.NodeGrid(255);

        var failures = new List<string>();
        foreach (var w in data.GetProperty("wavelengths").EnumerateArray())
        {
            int wave = w.GetProperty("index").GetInt32();
            foreach (var f in w.GetProperty("fields").EnumerateArray())
            {
                int field = f.GetProperty("index").GetInt32();
                var ours = WavefrontCalculator.Compute(lens, field, wave, options, sampling);
                var stats = f.GetProperty("statistics");
                double rms = stats.GetProperty("rms").GetDouble(), pv = stats.GetProperty("pv").GetDouble();
                var theirs = f.GetProperty("zernike").GetProperty("coefficients").EnumerateArray().Select(e => e.GetDouble()).ToArray();
                var fit = Zernike.Fit(ours, ZernikeSet.Standard, theirs.Length);
                double worstZ = Enumerable.Range(0, theirs.Length).Max(k => Math.Abs(fit.Coefficients[k] - theirs[k]));

                log.WriteLine($"{name} wave {wave} field {field}: RMS {ours.Statistics.Rms:F8} / {rms:F8}, P-V {ours.Statistics.PeakToValley:F8} / {pv:F8}, " +
                              $"largest Zernike difference {worstZ:E2}");
                // Relative to the size: the fast lens's blue full-field P-V is 44 waves, from a ray
                // grazing the rim of its asphere, where the two programs part by 1e-8 of it.
                bool Near(double a, double b) => Math.Abs(a - b) <= tolerance * Math.Max(1.0, Math.Abs(b));
                if (!Near(ours.Statistics.Rms, rms)) failures.Add($"wave {wave} field {field} RMS {ours.Statistics.Rms:F8} against {rms:F8}");
                if (!Near(ours.Statistics.PeakToValley, pv)) failures.Add($"wave {wave} field {field} P-V {ours.Statistics.PeakToValley:F8} against {pv:F8}");
                if (worstZ > tolerance) failures.Add($"wave {wave} field {field} Zernike {worstZ:E2}");
            }
        }
        Assert.True(failures.Count == 0, string.Join("; ", failures));
    }
}
