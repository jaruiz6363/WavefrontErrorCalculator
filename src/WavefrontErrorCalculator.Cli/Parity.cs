using System.Globalization;
using System.Text.Json;
using WavefrontErrorCalculator.Core;

namespace WavefrontErrorCalculator.Cli;

/// <summary>
/// <c>wfe parity</c>: another program's wavefront, read from a file in the result format
/// (docs/result-format.md), against this one's under a preset and switches (method.md §10). The
/// program's own pupil points are traced, on its own refractive indices where the file gives them,
/// and the two wavefronts compared ray by ray.
/// </summary>
internal static class Parity
{
    public const string Usage =
        "wfe parity <result.json> [--lens file] [--preset name] [--set Switch=Value ...] [--quiet]\n" +
        "           [--rays file.csv|file.md]\n" +
        "\n" +
        "Another program's wavefront, from a file in the result format (docs/result-format.md), against\n" +
        "this one's: the same pupil points, on the program's own refractive indices where the file gives\n" +
        "them. For each wavelength and field, the largest |W - program| on each fan and on the map, in\n" +
        "waves, and the rays vignetted in one and not the other. The lens is the file's \"lens\", looked\n" +
        "for beside the result file and in its parent folder, unless --lens names it.\n" +
        "\n" +
        "--rays writes the comparison ray by ray: every ray of every wavelength and field as CSV, or,\n" +
        "for a .md file, tables at the primary wavelength - each fan and the map, one row per pupil\n" +
        "point, with the program's OPD and the difference W - program for every field.\n";

    private sealed record Rays(double[] Px, double[] Py, double[] Opd, bool[] Vignetted);

    /// <summary>One ray of the comparison: the program's OPD and this one's W at one pupil point.</summary>
    private sealed record Row(int Wave, double LambdaUm, int Field, double FieldValue, string Set,
                              double Px, double Py, double Theirs, double Ours, bool TheirsVignetted, bool OursVignetted);

    public static int Run(string[] args, TextWriter output)
    {
        if (args.Length == 0 || args.Contains("--help"))
        {
            output.Write(Usage);
            return args.Length == 0 ? 2 : 0;
        }
        string resultPath = args[0];
        string? lensPath = null;
        var options = WavefrontOptions.Reference;
        bool quiet = false;
        string? raysPath = null;
        for (int k = 1; k < args.Length; k++)
        {
            string Next() => k + 1 < args.Length ? args[++k] : throw new ArgumentException($"{args[k]} needs a value");
            switch (args[k])
            {
                case "--lens": lensPath = Next(); break;
                case "--preset": options = WavefrontOptions.Preset(Next()); break;
                case "--set": options = Program.Set(options, Next()); break;
                case "--quiet": quiet = true; break;
                case "--rays": raysPath = Next(); break;
                default: throw new ArgumentException($"unknown option {args[k]}");
            }
        }

        var data = JsonDocument.Parse(File.ReadAllText(resultPath)).RootElement;
        if (data.GetProperty("format").GetString() != "wavefront-result/1")
            throw new FormatException($"{resultPath} is not in the wavefront-result/1 format");
        lensPath ??= FindLens(resultPath, data.GetProperty("lens").GetString()!);

        var indices = new Dictionary<int, double[]>();
        foreach (var w in data.GetProperty("wavelengths").EnumerateArray())
            if (w.TryGetProperty("indices", out var n))
                indices[w.GetProperty("index").GetInt32()] = n.EnumerateArray().Select(e => e.GetDouble()).ToArray();
        var lens = LensModel.Read(lensPath, w => indices.TryGetValue(w, out var n) ? n : null);

        output.WriteLine($"{data.GetProperty("program").GetString()} {data.GetProperty("version").GetString()}, {Path.GetFileName(lensPath)}, against {Program.Describe(options)}");
        if (data.TryGetProperty("settings", out var settings))
            output.WriteLine("  program settings: " + string.Join(", ", settings.EnumerateObject().Select(p => $"{p.Name}={p.Value}")));
        output.WriteLine();
        if (!quiet) output.WriteLine(" wave  field   set          rays   largest |W - program|   size     vignetting differs");

        double overall = 0.0;
        int mismatched = 0;
        var rows = new List<Row>();
        foreach (var w in data.GetProperty("wavelengths").EnumerateArray())
        {
            int wave = w.GetProperty("index").GetInt32();
            double lambda = w.TryGetProperty("wavelength_um", out var l) ? l.GetDouble() : double.NaN;
            foreach (var f in w.GetProperty("fields").EnumerateArray())
            {
                int field = f.GetProperty("index").GetInt32();
                double value = f.TryGetProperty("value", out var fv) ? fv.GetDouble() : double.NaN;
                var sets = new List<(string Name, JsonElement Rays)>();
                if (f.TryGetProperty("fans", out var fans))
                    sets.AddRange(fans.EnumerateObject().Select(p => (p.Name, p.Value)));
                if (f.TryGetProperty("map", out var map)) sets.Add(("map", map));
                foreach (var (name, element) in sets)
                {
                    var r = Read(element);
                    var points = Enumerable.Range(0, r.Px.Length).Select(k => new PupilPoint(r.Px[k], r.Py[k], 0.0)).ToList();
                    var ours = WavefrontCalculator.Compute(lens, field, wave, options, new Sampling.Given(points));
                    double worst = 0.0, size = 0.0;
                    int compared = 0, differ = 0;
                    for (int k = 0; k < points.Count; k++)
                    {
                        bool theirs = r.Vignetted[k], mine = ours.Samples[k].Vignetted;
                        rows.Add(new Row(wave, lambda, field, value, name, r.Px[k], r.Py[k], r.Opd[k], ours.Samples[k].W, theirs, mine));
                        if (theirs != mine) { differ++; continue; }
                        if (theirs) continue;
                        compared++;
                        worst = Math.Max(worst, Math.Abs(ours.Samples[k].W - r.Opd[k]));
                        size = Math.Max(size, Math.Abs(r.Opd[k]));
                    }
                    overall = Math.Max(overall, worst);
                    mismatched += differ;
                    if (!quiet)
                        output.WriteLine(string.Format(CultureInfo.InvariantCulture, "{0,5} {1,6}   {2,-11} {3,5}   {4,21:E3}   {5,8:F4}   {6,6}",
                            wave, field, name, compared, worst, size, differ));
                }
            }
        }
        output.WriteLine(string.Format(CultureInfo.InvariantCulture,
            "largest |W - program| {0:E3} waves; {1} rays vignetted in one and not the other", overall, mismatched));

        if (raysPath != null)
        {
            string program = data.GetProperty("program").GetString()!;
            int primary = data.TryGetProperty("settings", out var s) && s.TryGetProperty("primary_wavelength", out var pw)
                ? pw.GetInt32() : lens.PrimaryWavelength;
            using (var writer = new StreamWriter(raysPath))
            {
                if (Path.GetExtension(raysPath).Equals(".md", StringComparison.OrdinalIgnoreCase))
                    WriteTables(writer, rows.Where(r => r.Wave == primary).ToList(), program, Path.GetFileName(resultPath),
                                Path.GetFileName(lensPath), Program.Describe(options));
                else
                    WriteCsv(writer, rows);
            }
            output.WriteLine($"rays written to {raysPath}");
        }
        return 0;
    }

    private static string Number(double x, string format) =>
        double.IsNaN(x) ? "" : x.ToString(format, CultureInfo.InvariantCulture);

    private static void WriteCsv(TextWriter writer, List<Row> rows)
    {
        writer.NewLine = "\n";
        writer.WriteLine("wave,lambda_um,field,field_value,set,px,py,program_opd,wfe_w,difference,program_vignetted,wfe_vignetted");
        foreach (var r in rows)
        {
            bool both = !r.TheirsVignetted && !r.OursVignetted;
            writer.WriteLine(string.Join(",", r.Wave, Number(r.LambdaUm, "R"), r.Field, Number(r.FieldValue, "R"), r.Set,
                Number(r.Px, "R"), Number(r.Py, "R"),
                r.TheirsVignetted ? "" : Number(r.Theirs, "R"), r.OursVignetted ? "" : Number(r.Ours, "R"),
                both ? Number(r.Ours - r.Theirs, "E3") : "",
                r.TheirsVignetted ? "1" : "0", r.OursVignetted ? "1" : "0"));
        }
    }

    /// <summary>
    /// Markdown tables at one wavelength: for each fan and the map, one row per pupil point, and
    /// for each field the program's OPD and the difference W - program, in waves.
    /// </summary>
    private static void WriteTables(TextWriter writer, List<Row> rows, string program, string result, string lens, string switches)
    {
        writer.NewLine = "\n";
        if (rows.Count == 0) return;
        var fields = rows.Select(r => (r.Field, r.FieldValue)).Distinct().OrderBy(f => f.Field).ToList();
        writer.WriteLine($"# {Path.GetFileNameWithoutExtension(result)}");
        writer.WriteLine();
        writer.WriteLine($"{program}'s OPD against WavefrontErrorCalculator's W, ray by ray, from `{result}`. Generated by `wfe parity --rays`.");
        writer.WriteLine();
        writer.WriteLine($"- **Lens:** `{lens}`, at the primary wavelength, {Number(rows[0].LambdaUm, "0.#####")} µm.");
        writer.WriteLine($"- **WavefrontErrorCalculator switches:** {switches}.");
        writer.WriteLine("- **Units:** OPD and W are in waves; the difference is W − program. Px and Py are the program's own normalised pupil coordinates, traced here as they are there.");
        writer.WriteLine("- **Vignetting:** a dash marks a ray the program vignettes. \"WFE only\" or \"program only\" marks a ray vignetted in just one of the two.");
        foreach (var set in rows.Select(r => r.Set).Distinct())
        {
            var inSet = rows.Where(r => r.Set == set).ToList();
            var points = inSet.Select(r => (r.Px, r.Py)).Distinct().ToList();
            writer.WriteLine();
            writer.WriteLine($"## {char.ToUpperInvariant(set[0]) + set[1..]}{(set is "tangential" or "sagittal" ? " fan" : "")}");
            writer.WriteLine();
            writer.WriteLine("| Px | Py | " + string.Join(" | ", fields.Select(f => $"field {Number(f.FieldValue, "0.###")} | W − program")) + " |");
            writer.WriteLine("|---:|---:|" + string.Concat(fields.Select(_ => "---:|---:|")));
            var at = new Dictionary<(double, double, int), Row>();
            foreach (var r in inSet) at.TryAdd((r.Px, r.Py, r.Field), r);
            foreach (var (px, py) in points)
            {
                var cells = fields.Select(f =>
                {
                    if (!at.TryGetValue((px, py, f.Field), out var r)) return " | ";
                    if (r.TheirsVignetted && r.OursVignetted) return "– | ";
                    if (r.TheirsVignetted) return "– | program only";
                    if (r.OursVignetted) return $"{Number(r.Theirs, "F6")} | WFE only";
                    return $"{Number(r.Theirs, "F6")} | {Number(r.Ours - r.Theirs, "+0.0E+0;-0.0E+0;0")}";
                });
                writer.WriteLine($"| {Number(px, "0.####")} | {Number(py, "0.####")} | " + string.Join(" | ", cells) + " |");
            }
            var compared = inSet.Where(r => !r.TheirsVignetted && !r.OursVignetted).ToList();
            if (compared.Count > 0)
            {
                writer.WriteLine();
                writer.WriteLine($"Largest |W − program| on this set: {Number(compared.Max(r => Math.Abs(r.Ours - r.Theirs)), "0.0E+0")} wave over {compared.Count} rays.");
            }
        }
    }

    private static string FindLens(string resultPath, string lens)
    {
        string dir = Path.GetDirectoryName(Path.GetFullPath(resultPath))!;
        foreach (var candidate in new[] { Path.Combine(dir, lens), Path.Combine(Path.GetDirectoryName(dir) ?? dir, lens) })
            if (File.Exists(candidate)) return candidate;
        throw new FileNotFoundException($"cannot find {lens} beside {resultPath}; name it with --lens");
    }

    private static Rays Read(JsonElement e)
    {
        double[] A(string key) => e.GetProperty(key).EnumerateArray()
            .Select(v => v.ValueKind == JsonValueKind.Number ? v.GetDouble() : double.NaN).ToArray();
        var opd = A("opd");
        bool[] vignetted = e.TryGetProperty("vignetted", out var v)
            ? v.EnumerateArray().Select(b => b.GetBoolean()).ToArray()
            : opd.Select(double.IsNaN).ToArray();
        return new Rays(A("px"), A("py"), opd, vignetted);
    }
}
