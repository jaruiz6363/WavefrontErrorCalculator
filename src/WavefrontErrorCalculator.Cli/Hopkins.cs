using System.Globalization;
using WavefrontErrorCalculator.Core;

namespace WavefrontErrorCalculator.Cli;

/// <summary>
/// <c>wfe hopkins</c>: the wavefront by Hopkins's surface contributions and Tatian's focal shift
/// (<see cref="HopkinsTatian"/>), against this program's optical-path W with the same infinite
/// reference, and against the W of the preset's own reference sphere.
/// </summary>
internal static class Hopkins
{
    public const string Usage =
        "wfe hopkins <lens> [--points result.json] [--preset name] [--set Switch=Value ...] [--csv file]\n" +
        "\n" +
        "W at the primary wavelength by Hopkins's surface-contribution formula (1952, eq. 7) and Tatian's\n" +
        "focal shift to the image point (1972, eq. 1): referred to the image point with no exit pupil, as\n" +
        "the reference of infinite radius (ExitPupil=Infinite; Zemax's Reference OPD \"Infinity\"). For each\n" +
        "field: the largest |Hopkins-Tatian - path| against the optical path with that reference, the\n" +
        "largest |W|, and the largest |preset - Hopkins-Tatian|, where the preset's own reference sphere\n" +
        "(through its exit pupil) is used - the difference a program using that sphere shows. --points\n" +
        "as for wfe rayces (default a grid of spacing 1/16 and 60 rim points). --csv writes every point.\n";

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
        for (int k = 1; k < args.Length; k++)
        {
            string Next() => k + 1 < args.Length ? args[++k] : throw new ArgumentException($"{args[k]} needs a value");
            switch (args[k])
            {
                case "--points": pointsPath = Next(); break;
                case "--preset": options = WavefrontOptions.Preset(Next()); break;
                case "--set": options = Program.Set(options, Next()); break;
                case "--csv": csv = Next(); break;
                default: throw new ArgumentException($"unknown option {args[k]}");
            }
        }

        var lens = LensModel.Read(lensPath);
        var targets = pointsPath != null ? Rayces.PointsOf(pointsPath) : Rayces.DiskPoints();
        var sampling = new Sampling.Given(targets.Select(t => new PupilPoint(t.Item1, t.Item2, 0.0)).ToList());
        int w = lens.PrimaryWavelength;
        output.WriteLine($"{Path.GetFileName(lensPath)}: {targets.Count} pupil points,{Program.Describe(options)}; preset exit pupil {options.ExitPupil}");
        using var writer = csv != null ? new StreamWriter(csv) { NewLine = "\n" } : null;
        writer?.WriteLine("field,px,py,hopkins,focal_shift,hopkins_tatian,path_infinite,join_shift,preset_sphere");
        double overall = 0.0;
        for (int f = 0; f < lens.System.Fields.Count; f++)
        {
            var points = HopkinsTatian.Compute(lens, f, w, options, sampling);
            var sphere = WavefrontCalculator.Compute(lens, f, w, options, sampling).Samples;
            double worst = 0.0, worstJoin = 0.0, largest = 0.0, apart = 0.0;
            int compared = 0;
            for (int k = 0; k < points.Count; k++)
            {
                var p = points[k];
                double preset = sphere[k].Vignetted ? double.NaN : sphere[k].W;
                if (!double.IsNaN(p.Total) && !double.IsNaN(p.ByPath))
                {
                    compared++;
                    worst = Math.Max(worst, Math.Abs(p.Total - p.ByPath));
                    largest = Math.Max(largest, Math.Abs(p.Total));
                    if (!double.IsNaN(p.Join)) worstJoin = Math.Max(worstJoin, Math.Abs(p.Join - p.FocalShift));
                    if (!double.IsNaN(preset)) apart = Math.Max(apart, Math.Abs(preset - p.Total));
                }
                writer?.WriteLine(string.Join(",", new double[] { f, p.Px, p.Py, p.Hopkins, p.FocalShift, p.Total, p.ByPath, p.Join, preset }
                    .Select(v => v.ToString("R", CultureInfo.InvariantCulture))));
            }
            overall = Math.Max(overall, worst);
            output.WriteLine($"  field {lens.System.Fields[f].Y.ToString("0.###", CultureInfo.InvariantCulture)}: {compared} points, " +
                             $"|Hopkins-Tatian - path| {worst:E3}, |closed form - join| {worstJoin:E3}, |W| up to {largest:F4}, " +
                             $"|preset sphere - Hopkins-Tatian| up to {apart:E3} waves");
        }
        output.WriteLine($"largest |Hopkins-Tatian - path| {overall:E3} waves");
        return 0;
    }
}
