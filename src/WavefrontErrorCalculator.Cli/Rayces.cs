using System.Globalization;
using System.Text.Json;
using WavefrontErrorCalculator.Core;

namespace WavefrontErrorCalculator.Cli;

/// <summary>
/// <c>wfe rayces</c>: the wavefront by integrating Rayces's exact relation between wave and ray
/// aberration (<see cref="RaycesIntegration"/>), against this program's optical-path W at the same
/// pupil points - two independent ways to the same numbers.
/// </summary>
internal static class Rayces
{
    public const string Usage =
        "wfe rayces <lens> [--points result.json] [--preset name] [--set Switch=Value ...] [--steps n] [--radial] [--csv file]\n" +
        "\n" +
        "W at the primary wavelength by integrating Rayces's exact relation dW/dx = -X/(R - W) from the\n" +
        "chief ray across the pupil, using only where the rays go in image space, beside W from the\n" +
        "optical path. For each field, the largest |integrated - path| in waves and the largest |W|.\n" +
        "--points takes the pupil points of a result file (its first field's map and fans); by default\n" +
        "a grid of spacing 1/16 over the unit disk and 60 points on its rim. --steps is the number of\n" +
        "trapezoids per unit of pupil length to start with (a power of two, default 256); a point whose\n" +
        "error estimate exceeds 1e-10 wave is redone with four times as many, up to 16384. The paths\n" +
        "are shared: the meridional line, then rows of constant Py through the targets, each line\n" +
        "integrated once. --radial gives each point its own straight path from the chief ray instead,\n" +
        "with --steps trapezoids. --csv writes every point.\n";

    public static int Run(string[] args, TextWriter output)
    {
        if (args.Length == 0 || args.Contains("--help"))
        {
            output.Write(Usage);
            return args.Length == 0 ? 2 : 0;
        }
        string lensPath = args[0];
        var options = WavefrontOptions.Reference;
        string? pointsPath = null, csv = null;
        int steps = 256;
        var paths = RaycesPaths.Shared;
        for (int k = 1; k < args.Length; k++)
        {
            string Next() => k + 1 < args.Length ? args[++k] : throw new ArgumentException($"{args[k]} needs a value");
            switch (args[k])
            {
                case "--points": pointsPath = Next(); break;
                case "--preset": options = WavefrontOptions.Preset(Next()); break;
                case "--set": options = Program.Set(options, Next()); break;
                case "--steps": steps = int.Parse(Next(), CultureInfo.InvariantCulture); break;
                case "--radial": paths = RaycesPaths.Radial; break;
                case "--csv": csv = Next(); break;
                default: throw new ArgumentException($"unknown option {args[k]}");
            }
        }

        var lens = LensModel.Read(lensPath);
        var targets = pointsPath != null ? PointsOf(pointsPath) : DiskPoints();
        output.WriteLine($"{Path.GetFileName(lensPath)}: {targets.Count} pupil points, {paths.ToString().ToLowerInvariant()} paths, {steps} steps,{Program.Describe(options)}");
        using var writer = csv != null ? new StreamWriter(csv) { NewLine = "\n" } : null;
        writer?.WriteLine("field,px,py,integrated,path,difference,nijboer");
        double overall = 0.0;
        for (int f = 0; f < lens.System.Fields.Count; f++)
        {
            var result = RaycesIntegration.Compute(lens, f, options, targets, steps, paths: paths);
            double worst = 0.0, largest = 0.0;
            int compared = 0, missing = 0, finest = 0;
            foreach (var p in result)
            {
                if (double.IsNaN(p.Integrated) || double.IsNaN(p.ByPath)) { if (!double.IsNaN(p.ByPath)) missing++; }
                else
                {
                    compared++;
                    worst = Math.Max(worst, Math.Abs(p.Integrated - p.ByPath));
                    largest = Math.Max(largest, Math.Abs(p.ByPath));
                    finest = Math.Max(finest, p.Steps);
                }
                writer?.WriteLine(string.Join(",", new[] { f, p.Px, p.Py, p.Integrated, p.ByPath, p.Integrated - p.ByPath, p.Nijboer }
                    .Select(v => Convert.ToDouble(v).ToString("R", CultureInfo.InvariantCulture))));
            }
            overall = Math.Max(overall, worst);
            output.WriteLine($"  field {lens.System.Fields[f].Y.ToString("0.###", CultureInfo.InvariantCulture)}: {compared} points, " +
                             $"largest |integrated - path| {worst:E3} waves (|W| up to {largest:F4}; up to {finest} steps)" +
                             (missing > 0 ? $"; {missing} not reached along a straight path" : ""));
        }
        output.WriteLine($"largest |integrated - path| {overall:E3} waves");
        return 0;
    }

    /// <summary>The pupil points of a result file: its first field's map and fans, at its primary wavelength.</summary>
    internal static List<(double, double)> PointsOf(string path)
    {
        var data = JsonDocument.Parse(File.ReadAllText(path)).RootElement;
        var w = data.GetProperty("wavelengths")[0];
        var f = w.GetProperty("fields")[0];
        var sets = new List<JsonElement>();
        if (f.TryGetProperty("map", out var map)) sets.Add(map);
        if (f.TryGetProperty("fans", out var fans)) sets.AddRange(fans.EnumerateObject().Select(p => p.Value));
        var points = new List<(double, double)>();
        foreach (var s in sets)
        {
            var px = s.GetProperty("px").EnumerateArray().Select(v => v.GetDouble()).ToArray();
            var py = s.GetProperty("py").EnumerateArray().Select(v => v.GetDouble()).ToArray();
            for (int k = 0; k < px.Length; k++) points.Add((px[k], py[k]));
        }
        return points;
    }

    /// <summary>A grid of spacing 1/16 over the unit disk, and 60 points on its rim.</summary>
    internal static List<(double, double)> DiskPoints()
    {
        var points = new List<(double, double)>();
        for (int i = -16; i <= 16; i++)
            for (int j = -16; j <= 16; j++)
                if (i * i + j * j <= 256) points.Add((i / 16.0, j / 16.0));
        for (int k = 0; k < 60; k++)
            points.Add((Math.Cos(2 * Math.PI * k / 60), Math.Sin(2 * Math.PI * k / 60)));
        return points;
    }
}
