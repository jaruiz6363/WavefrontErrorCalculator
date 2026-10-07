using System.Globalization;
using System.Text.Json;
using WavefrontErrorCalculator.Core;

namespace WavefrontErrorCalculator.Cli;

/// <summary>
/// <c>wfe opd-table</c>: several programs' OPD with respect to the chief ray at the same pupil
/// points, side by side with this program's W, one row per point. Each program's file is in the
/// result format (docs/result-format.md) and holds the same points; this program's W is computed
/// under the options given, on the refractive indices of the first file, and again on each
/// program's own indices for the difference, so that a difference in glass data does not show as
/// one in the OPD.
/// </summary>
internal static class OpdTable
{
    public const string Usage =
        "wfe opd-table <lens> <name>=<result.json> ... [--preset name] [--set Switch=Value ...] [--values] --out file.md\n" +
        "\n" +
        "Several programs' OPD at the same pupil points, with this program's W: at the primary\n" +
        "wavelength, one table per field and one row per point, each program's OPD and its difference\n" +
        "from W computed on that program's own refractive indices. The W column is on the first\n" +
        "file's indices.\n" +
        "\n" +
        "--values writes the OPD values alone, side by side: W and each program's OPD at every point,\n" +
        "with no differences and no summary.\n";

    private sealed record Program_(string Name, string Path, JsonElement Data, LensModel Lens);

    public static int Run(string[] args, TextWriter output)
    {
        if (args.Length == 0 || args.Contains("--help"))
        {
            output.Write(Usage);
            return args.Length == 0 ? 2 : 0;
        }
        string lensPath = args[0];
        var options = WavefrontOptions.Reference;
        string? outPath = null;
        bool valuesOnly = false;
        var programs = new List<(string Name, string Path)>();
        for (int k = 1; k < args.Length; k++)
        {
            string Next() => k + 1 < args.Length ? args[++k] : throw new ArgumentException($"{args[k]} needs a value");
            switch (args[k])
            {
                case "--preset": options = WavefrontOptions.Preset(Next()); break;
                case "--set": options = Program.Set(options, Next()); break;
                case "--out": outPath = Next(); break;
                case "--values": valuesOnly = true; break;
                default:
                    int eq = args[k].IndexOf('=');
                    if (eq <= 0 || args[k].StartsWith("--")) throw new ArgumentException($"unknown option {args[k]}");
                    programs.Add((args[k][..eq], args[k][(eq + 1)..]));
                    break;
            }
        }
        if (programs.Count == 0 || outPath == null) throw new ArgumentException("name=result.json and --out are needed");

        var loaded = programs.Select(p =>
        {
            var data = JsonDocument.Parse(File.ReadAllText(p.Path)).RootElement;
            var indices = new Dictionary<int, double[]>();
            foreach (var w in data.GetProperty("wavelengths").EnumerateArray())
                if (w.TryGetProperty("indices", out var n))
                    indices[w.GetProperty("index").GetInt32()] = n.EnumerateArray().Select(e => e.GetDouble()).ToArray();
            return new Program_(p.Name, p.Path, data, LensModel.Read(lensPath, w => indices.TryGetValue(w, out var n) ? n : null));
        }).ToList();

        var reference = loaded[0].Lens;
        int wave = reference.PrimaryWavelength;
        double lambda = reference.WavelengthUm(wave);

        // Each program's rays at the primary wavelength, by field and set, keyed by point.
        static Dictionary<(int Field, string Set, double Px, double Py), double?> Rays(JsonElement data, int primary)
        {
            var at = new Dictionary<(int, string, double, double), double?>();
            int fileWave = data.TryGetProperty("settings", out var s) && s.TryGetProperty("primary_wavelength", out var pw) ? pw.GetInt32() : primary;
            var w = data.GetProperty("wavelengths").EnumerateArray().First(x => x.GetProperty("index").GetInt32() == fileWave);
            foreach (var f in w.GetProperty("fields").EnumerateArray())
            {
                int field = f.GetProperty("index").GetInt32();
                var sets = new List<(string, JsonElement)>();
                if (f.TryGetProperty("fans", out var fans)) sets.AddRange(fans.EnumerateObject().Select(p => (p.Name, p.Value)));
                if (f.TryGetProperty("map", out var map)) sets.Add(("map", map));
                foreach (var (name, e) in sets)
                {
                    var px = e.GetProperty("px").EnumerateArray().Select(v => v.GetDouble()).ToArray();
                    var py = e.GetProperty("py").EnumerateArray().Select(v => v.GetDouble()).ToArray();
                    var opd = e.GetProperty("opd").EnumerateArray().Select(v => v.ValueKind == JsonValueKind.Number ? v.GetDouble() : (double?)null).ToArray();
                    var vig = e.TryGetProperty("vignetted", out var vv) ? vv.EnumerateArray().Select(b => b.GetBoolean()).ToArray() : opd.Select(o => o == null).ToArray();
                    for (int k = 0; k < px.Length; k++)
                        at[(field, name, Math.Round(px[k], 12), Math.Round(py[k], 12))] = vig[k] ? null : opd[k];
                }
            }
            return at;
        }
        var rays = loaded.Select(p => Rays(p.Data, wave)).ToList();

        // The points, in the first file's order.
        var points = rays[0].Keys.ToList();
        var fields = points.Select(p => p.Field).Distinct().OrderBy(f => f).ToList();

        // W on the first file's indices, and on each program's own.
        Dictionary<(int, string, double, double), double?> Ours(LensModel lens)
        {
            var at = new Dictionary<(int, string, double, double), double?>();
            foreach (int field in fields)
            {
                var here = points.Where(p => p.Field == field).ToList();
                var r = WavefrontCalculator.Compute(lens, field, wave, options,
                    new Sampling.Given(here.Select(p => new PupilPoint(p.Px, p.Py, 0.0)).ToList()));
                for (int k = 0; k < here.Count; k++)
                    at[here[k]] = r.Samples[k].Vignetted ? null : r.Samples[k].W;
            }
            return at;
        }
        var ours = Ours(reference);
        var oursOn = loaded.Select(p => ReferenceEquals(p.Lens, reference) ? ours : Ours(p.Lens)).ToList();

        string N(double? x, string f) => x is double v ? v.ToString(f, CultureInfo.InvariantCulture) : "–";
        string D(double? a, double? b) => a is double x && b is double y ? (x - y).ToString("+0.0E+0;-0.0E+0;0", CultureInfo.InvariantCulture)
                                        : a.HasValue != b.HasValue ? (a.HasValue ? "WFE vignettes" : "program vignettes") : "";

        using var w = new StreamWriter(outPath) { NewLine = "\n" };
        if (valuesOnly)
        {
            WriteValues(w, lensPath, lambda, options, loaded, rays, ours, points, fields, reference);
            output.WriteLine($"wrote {outPath}");
            return 0;
        }
        w.WriteLine($"# {Path.GetFileNameWithoutExtension(lensPath)}: OPD with respect to the chief ray, point by point");
        w.WriteLine();
        w.WriteLine($"Generated by `wfe opd-table`. Primary wavelength, {lambda.ToString("0.#####", CultureInfo.InvariantCulture)} µm. Values in waves.");
        w.WriteLine();
        w.WriteLine($"- **W:** WavefrontErrorCalculator, {Program.Describe(options)}, on {loaded[0].Name}'s refractive indices.");
        w.WriteLine("- **Each program:** its own OPD, and Δ = program − W, with W recomputed on that program's own refractive indices, so a difference in glass data does not show as one here.");
        foreach (var p in loaded)
        {
            string settings = p.Data.TryGetProperty("settings", out var s)
                ? string.Join(", ", s.EnumerateObject().Where(o => o.Name != "rays").Select(o => $"{o.Name} {o.Value}")) : "";
            w.WriteLine($"- **{p.Name}:** {p.Data.GetProperty("program").GetString()} {p.Data.GetProperty("version").GetString()}, `{Path.GetFileName(p.Path)}`; {settings}.");
        }

        w.WriteLine();
        w.WriteLine("## Largest |Δ| by field");
        w.WriteLine();
        w.WriteLine("| Field | " + string.Join(" | ", loaded.Select(p => p.Name)) + " |");
        w.WriteLine("|---|" + string.Concat(loaded.Select(_ => "---|")));
        foreach (int field in fields)
        {
            var cells = loaded.Select((p, i) =>
            {
                double worst = 0; int compared = 0, differ = 0;
                foreach (var key in points.Where(q => q.Field == field))
                {
                    rays[i].TryGetValue(key, out var theirs);
                    oursOn[i].TryGetValue(key, out var mine);
                    if (theirs.HasValue != mine.HasValue) { differ++; continue; }
                    if (theirs is double t && mine is double m) { worst = Math.Max(worst, Math.Abs(t - m)); compared++; }
                }
                return worst.ToString("0.0E+0", CultureInfo.InvariantCulture) + (differ > 0 ? $" ({differ} vignetted in one only)" : "");
            });
            w.WriteLine($"| {FieldValue(reference, field)} | " + string.Join(" | ", cells) + " |");
        }

        foreach (int field in fields)
        {
            w.WriteLine();
            w.WriteLine($"## Field {FieldValue(reference, field)}");
            w.WriteLine();
            w.WriteLine("| Set | Px | Py | W | " + string.Join(" | ", loaded.Select(p => $"{p.Name} | Δ")) + " |");
            w.WriteLine("|---|---:|---:|---:|" + string.Concat(loaded.Select(_ => "---:|---:|")));
            foreach (var key in points.Where(p => p.Field == field))
            {
                var cells = loaded.Select((p, i) =>
                {
                    rays[i].TryGetValue(key, out var theirs);
                    oursOn[i].TryGetValue(key, out var mine);
                    return $"{N(theirs, "F6")} | {D(theirs, mine)}";
                });
                w.WriteLine($"| {key.Set} | {key.Px.ToString("0.####", CultureInfo.InvariantCulture)} | {key.Py.ToString("0.####", CultureInfo.InvariantCulture)} | {N(ours[key], "F6")} | " + string.Join(" | ", cells) + " |");
            }
        }
        output.WriteLine($"wrote {outPath}");
        return 0;
    }

    /// <summary>
    /// The --values form: every program's OPD beside W at each point, one table per field, nothing
    /// derived. W is on the first file's refractive indices; a program on other glass shows it.
    /// </summary>
    private static void WriteValues(StreamWriter w, string lensPath, double lambda, WavefrontOptions options,
        List<Program_> loaded, List<Dictionary<(int Field, string Set, double Px, double Py), double?>> rays,
        Dictionary<(int, string, double, double), double?> ours,
        List<(int Field, string Set, double Px, double Py)> points, List<int> fields, LensModel reference)
    {
        string N(double? x) => x is double v ? v.ToString("F6", CultureInfo.InvariantCulture) : "–";
        w.WriteLine($"# {Path.GetFileNameWithoutExtension(lensPath)}: OPD with respect to the chief ray, point by point");
        w.WriteLine();
        w.WriteLine($"Generated by `wfe opd-table --values`. Primary wavelength, {lambda.ToString("0.#####", CultureInfo.InvariantCulture)} µm. Values in waves; – where the ray is vignetted.");
        w.WriteLine();
        w.WriteLine($"- **WEC:** WavefrontErrorCalculator's W, {Program.Describe(options)}, on {loaded[0].Name}'s refractive indices.");
        foreach (var p in loaded)
        {
            string settings = p.Data.TryGetProperty("settings", out var s)
                ? string.Join(", ", s.EnumerateObject().Where(o => o.Name != "rays").Select(o => $"{o.Name} {o.Value}")) : "";
            w.WriteLine($"- **{p.Name}:** {p.Data.GetProperty("program").GetString()} {p.Data.GetProperty("version").GetString()}, `{Path.GetFileName(p.Path)}`; {settings}.");
        }
        foreach (int field in fields)
        {
            w.WriteLine();
            w.WriteLine($"## Field {FieldValue(reference, field)}");
            w.WriteLine();
            w.WriteLine("| Px | Py | WEC | " + string.Join(" | ", loaded.Select(p => p.Name)) + " |");
            w.WriteLine("|---:|---:|---:|" + string.Concat(loaded.Select(_ => "---:|")));
            foreach (var key in points.Where(p => p.Field == field))
            {
                var cells = loaded.Select((p, i) => { rays[i].TryGetValue(key, out var theirs); return N(theirs); });
                w.WriteLine($"| {key.Px.ToString("0.####", CultureInfo.InvariantCulture)} | {key.Py.ToString("0.####", CultureInfo.InvariantCulture)} | {N(ours[key])} | " + string.Join(" | ", cells) + " |");
            }
        }
    }

    private static string FieldValue(LensModel lens, int field) =>
        lens.System.Fields[field].Y.ToString("0.###", CultureInfo.InvariantCulture);
}
