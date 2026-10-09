using System.Globalization;
using System.Reflection;
using System.Text;
using WavefrontErrorCalculator.Core;

namespace WavefrontErrorCalculator.Cli;

public static class Program
{
    private const string Usage =
        "wfe <lens> [options]\n" +
        "\n" +
        "The wavefront error of a lens from real rays: W = the chief ray's optical path minus the ray's,\n" +
        "between the entrance and exit reference spheres (docs/method.md).\n" +
        "\n" +
        "  --field <i|all>        field index, from 0 (default all)\n" +
        "  --wave <i|all>         wavelength index, from 0 (default the primary)\n" +
        "  --preset <name>        Reference (default), Optiland, LensHHLT, Zemax or ZemaxZernike\n" +
        "  --set <Switch=Value>   override one convention, e.g. --set ExitPupil=ParaxialAxial;\n" +
        "                         repeatable. Switches: ReferenceCenter, ExitPupil, RayAiming, ChiefRay,\n" +
        "                         PupilCoordinates, Weighting, Sign, ChromaticReference, Rms,\n" +
        "                         Apertures, Defocus, FocusShift (mm), IncludeVignettedAsZero,\n" +
        "                         DivideByImageIndex\n" +
        "  --zernike <set:terms>  fit Zernike polynomials: standard:37 or fringe:37\n" +
        "  --grid <n>             square grid of n x n rays across the pupil, at cell centres (default 64)\n" +
        "  --nodes <n>            square grid of n x n nodes from -1 to 1, rim included\n" +
        "  --hexapolar <rings>    hexapolar rings instead of the grid\n" +
        "  --quadrature <rings> [arms]\n" +
        "                         Gauss quadrature over the pupil (Gauss-Legendre in rho^2, arms evenly\n" +
        "                         in angle; default arms 4 x rings): the integral the grid estimates -\n" +
        "                         over the exit pupil's area (ExitArea, by differential rays) or the\n" +
        "                         launch disc (PerRay becomes Quadrature) - to ~1e-11 wave by 8 rings\n" +
        "                         on a smooth wavefront over an unvignetted pupil\n" +
        "  --fan <T|S> [count]    an OPD fan instead of a map (default 101 rays)\n" +
        "  --csv <file>           write every ray to a CSV file\n" +
        "\n" +
        "wfe compare <lens> --presets A,B    what each switch between two presets does\n" +
        "wfe parity <result.json> ...        another program's wavefront against this one's\n" +
        "wfe opd-table <lens> name=result.json ... --out x.md\n" +
        "                                    programs' OPD side by side, point by point\n" +
        "wfe hopkins <lens>                  W by Hopkins's surface contributions and Tatian's focal shift\n";

    public static int Main(string[] args)
    {
        try { return Run(args, Console.Out); }
        catch (Exception e) when (e is ArgumentException or NotSupportedException or InvalidOperationException
                                      or FileNotFoundException or FormatException)
        {
            Console.Error.WriteLine($"wfe: {e.Message}");
            return 2;
        }
    }

    public static int Run(string[] args, TextWriter output)
    {
        if (args.Length > 0 && args[0] == "compare") return Compare(args.Skip(1).ToArray(), output);
        if (args.Length > 0 && args[0] == "parity") return Parity.Run(args.Skip(1).ToArray(), output);
        if (args.Length > 0 && args[0] == "opd-table") return OpdTable.Run(args.Skip(1).ToArray(), output);
        if (args.Length > 0 && args[0] == "rayces") return Rayces.Run(args.Skip(1).ToArray(), output);
        if (args.Length > 0 && args[0] == "hopkins") return Hopkins.Run(args.Skip(1).ToArray(), output);
        if (args.Length == 0 || args.Contains("--help") || args.Contains("-h"))
        {
            output.Write(Usage);
            return args.Length == 0 ? 2 : 0;
        }

        string lensPath = args[0];
        string fieldArg = "all", waveArg = "primary";
        var options = WavefrontOptions.Reference;
        Sampling sampling = new Sampling.SquareGrid(64);
        string? csv = null;
        bool fan = false;
        (ZernikeSet, int)? zernike = null;
        bool quadrature = false, weightingSet = false;

        for (int k = 1; k < args.Length; k++)
        {
            string Next() => k + 1 < args.Length ? args[++k] : throw new ArgumentException($"{args[k]} needs a value");
            switch (args[k])
            {
                case "--field": fieldArg = Next(); break;
                case "--wave": waveArg = Next(); break;
                case "--preset": options = WavefrontOptions.Preset(Next()); break;
                case "--set":
                    string assignment = Next();
                    weightingSet |= assignment.Split('=')[0].Trim().Equals("Weighting", StringComparison.OrdinalIgnoreCase);
                    options = Set(options, assignment);
                    break;
                case "--quadrature":
                    int rings = int.Parse(Next(), CultureInfo.InvariantCulture);
                    int arms = k + 1 < args.Length && int.TryParse(args[k + 1], out int a) ? (k++, a).a : 4 * rings;
                    sampling = new Sampling.GaussQuadrature(rings, arms);
                    quadrature = true;
                    break;
                case "--grid": sampling = new Sampling.SquareGrid(int.Parse(Next(), CultureInfo.InvariantCulture)); break;
                case "--nodes": sampling = new Sampling.NodeGrid(int.Parse(Next(), CultureInfo.InvariantCulture)); break;
                case "--hexapolar": sampling = new Sampling.Hexapolar(int.Parse(Next(), CultureInfo.InvariantCulture)); break;
                case "--fan":
                    var direction = Next().ToUpperInvariant() switch
                    {
                        "T" => FanDirection.Tangential,
                        "S" => FanDirection.Sagittal,
                        var d => throw new ArgumentException($"--fan takes T or S, not {d}"),
                    };
                    int count = k + 1 < args.Length && int.TryParse(args[k + 1], out int c) ? (k++, c).c : 101;
                    sampling = new Sampling.Fan(direction, count);
                    fan = true;
                    break;
                case "--csv": csv = Next(); break;
                case "--zernike":
                    var spec = Next().Split(':');
                    var zset = Enum.Parse<ZernikeSet>(spec[0], ignoreCase: true);
                    zernike = (zset, spec.Length > 1 ? int.Parse(spec[1], CultureInfo.InvariantCulture) : 37);
                    break;
                default: throw new ArgumentException($"unknown option {args[k]}");
            }
        }
        // On quadrature nodes, counting each ray once becomes the rule's own weights; exit-area weights stay.
        if (quadrature && !weightingSet && options.Weighting == Weighting.PerRay) options = options with { Weighting = Weighting.Quadrature };

        var lens = LensModel.Read(lensPath);
        var fields = fieldArg == "all" ? Enumerable.Range(0, lens.System.Fields.Count) : new[] { Index(fieldArg) };
        var waves = waveArg switch
        {
            "primary" => new[] { lens.PrimaryWavelength },
            "all" => Enumerable.Range(0, lens.System.Wavelengths.Count),
            _ => new[] { Index(waveArg) },
        };

        var results = (from f in fields from w in waves
                       select WavefrontCalculator.Compute(lens, f, w, options, sampling)).ToList();

        output.WriteLine($"{Path.GetFileName(lensPath)}: {Describe(options)}");
        output.WriteLine();
        if (fan)
        {
            foreach (var r in results) WriteFan(output, r);
        }
        else
        {
            output.WriteLine(" field     value   wave  lambda(um)   RMS(w)    P-V(w)   piston(w)  Strehl   rays  vign   R'(mm)   pupil (centre, x, y)");
            foreach (var r in results)
            {
                var s = r.Statistics;
                // Maréchal's formula holds only to about λ/14 RMS (Welford p. 246); past it the
                // number means nothing and is not printed.
                string strehl = s.StrehlIsValid ? s.StrehlMarechal.ToString("F4", CultureInfo.InvariantCulture) : "-";
                output.WriteLine(string.Format(CultureInfo.InvariantCulture,
                    "{0,6} {1,9:G6} {2,6} {3,11:F4} {4,9:F5} {5,9:F5} {6,10:F5} {7,7} {8,6} {9,5} {10,9:G6}   {11:F4}, {12:F4}, {13:F4}",
                    r.Field, r.FieldValue, r.Wavelength, r.Geometry.WavelengthUm, s.Rms, s.PeakToValley, s.Mean,
                    strehl, s.Count, s.Vignetted, r.Geometry.Radius, r.Pupil.CenterPy, r.Pupil.SemiX, r.Pupil.SemiY));
            }
            output.WriteLine("Strehl is Maréchal's 1 - (2πσ)², shown only where σ <= λ/14. The pupil is the part of it the");
            output.WriteLine("pencil fills, as a centre on the meridian and semi-axes, in pupil coordinates.");
        }

        if (zernike is (ZernikeSet set, int terms))
            foreach (var r in results) WriteZernike(output, r, Zernike.Fit(r, set, terms));

        foreach (var warning in results.SelectMany(r => r.Warnings).Distinct()) output.WriteLine($"note: {warning}");

        if (csv != null) WriteCsv(csv, results);
        return 0;
    }

    private static int Index(string s) => int.Parse(s, CultureInfo.InvariantCulture);

    private const string CompareUsage =
        "wfe compare <lens> --presets A,B [--grid n] [--wave i]\n" +
        "\n" +
        "Each field's RMS and P-V under two presets, and what each switch on which they differ does\n" +
        "on its own: the first preset with that one switch taken from the second.\n";

    /// <summary>
    /// Two presets side by side, and the switches between them one at a time (method.md §8.4):
    /// which convention a difference between two programs comes from.
    /// </summary>
    private static int Compare(string[] args, TextWriter output)
    {
        if (args.Length == 0 || args.Contains("--help"))
        {
            output.Write(CompareUsage);
            return args.Length == 0 ? 2 : 0;
        }
        string lensPath = args[0];
        string[] presets = { "Reference", "Optiland" };
        Sampling sampling = new Sampling.SquareGrid(64);
        string waveArg = "primary";
        for (int k = 1; k < args.Length; k++)
        {
            string Next() => k + 1 < args.Length ? args[++k] : throw new ArgumentException($"{args[k]} needs a value");
            switch (args[k])
            {
                case "--presets": presets = Next().Split(','); break;
                case "--grid": sampling = new Sampling.SquareGrid(Index(Next())); break;
                case "--wave": waveArg = Next(); break;
                default: throw new ArgumentException($"unknown option {args[k]}");
            }
        }
        if (presets.Length != 2) throw new ArgumentException("--presets takes two names, A,B");

        var lens = LensModel.Read(lensPath);
        int wave = waveArg == "primary" ? lens.PrimaryWavelength : Index(waveArg);
        var a = WavefrontOptions.Preset(presets[0]);
        var b = WavefrontOptions.Preset(presets[1]);
        var fields = Enumerable.Range(0, lens.System.Fields.Count).ToList();

        double[] Rms(WavefrontOptions o) =>
            fields.Select(f => WavefrontCalculator.Compute(lens, f, wave, o, sampling).Statistics.Rms).ToArray();
        double[] Pv(WavefrontOptions o) =>
            fields.Select(f => WavefrontCalculator.Compute(lens, f, wave, o, sampling).Statistics.PeakToValley).ToArray();

        var rmsA = Rms(a);
        var rmsB = Rms(b);
        var pvA = Pv(a);
        var pvB = Pv(b);

        output.WriteLine($"{Path.GetFileName(lensPath)}, wavelength {wave} ({lens.WavelengthUm(wave):F4} um): {presets[0]} against {presets[1]}");
        output.WriteLine();
        output.WriteLine(string.Format(CultureInfo.InvariantCulture, " field     value  {0,12} {1,12}  {2,10}  {3,12} {4,12}  {5,10}",
            "RMS " + presets[0], "RMS " + presets[1], "difference", "P-V " + presets[0], "P-V " + presets[1], "difference"));
        foreach (int f in fields)
            output.WriteLine(string.Format(CultureInfo.InvariantCulture, "{0,6} {1,9:G6}  {2,12:F5} {3,12:F5}  {4,10:F5}  {5,12:F5} {6,12:F5}  {7,10:F5}",
                f, lens.System.Fields[f].Y, rmsA[f], rmsB[f], rmsB[f] - rmsA[f], pvA[f], pvB[f], pvB[f] - pvA[f]));

        var differing = typeof(WavefrontOptions).GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(p => p.CanWrite && !Equals(p.GetValue(a), p.GetValue(b))).ToList();
        output.WriteLine();
        if (differing.Count == 0)
        {
            output.WriteLine("The presets do not differ.");
            return 0;
        }
        output.WriteLine($"Each switch on its own: {presets[0]} with the one switch from {presets[1]}, change in RMS (waves).");
        output.WriteLine(string.Format(CultureInfo.InvariantCulture, "  {0,-34} {1}", "switch",
            string.Join(" ", fields.Select(f => $"{"field " + f,12}"))));
        foreach (var p in differing)
        {
            var one = a with { };
            p.SetValue(one, p.GetValue(b));
            double[] rms;
            try { rms = Rms(one); }
            catch (Exception e) when (e is NotSupportedException or ArgumentException or InvalidOperationException)
            {
                output.WriteLine($"  {p.Name} = {p.GetValue(b)}: cannot be applied alone ({e.Message})");
                continue;
            }
            output.WriteLine(string.Format(CultureInfo.InvariantCulture, "  {0,-34} {1}", $"{p.Name} = {p.GetValue(b)}",
                string.Join(" ", fields.Select(f => (rms[f] - rmsA[f]).ToString("+0.00000;-0.00000; 0.00000", CultureInfo.InvariantCulture).PadLeft(12)))));
        }
        return 0;
    }

    /// <summary>One switch of <see cref="WavefrontOptions"/>, by name, on a copy.</summary>
    internal static WavefrontOptions Set(WavefrontOptions options, string assignment)
    {
        int eq = assignment.IndexOf('=');
        if (eq <= 0) throw new ArgumentException($"--set takes Switch=Value, not {assignment}");
        string name = assignment[..eq], value = assignment[(eq + 1)..];
        var property = typeof(WavefrontOptions).GetProperty(name, BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase)
                       ?? throw new ArgumentException($"no switch {name}");
        object parsed = property.PropertyType.IsEnum
            ? Enum.Parse(property.PropertyType, value, ignoreCase: true)
            : property.PropertyType == typeof(bool) ? bool.Parse(value)
            : property.PropertyType == typeof(double?) || property.PropertyType == typeof(double)
                ? double.Parse(value, CultureInfo.InvariantCulture)
            : throw new ArgumentException($"{name} cannot be set from the command line");
        var copy = options with { };
        property.SetValue(copy, parsed);
        return copy;
    }

    internal static string Describe(WavefrontOptions o) =>
        $"Q′ {o.ReferenceCenter}, E′ {o.ExitPupil}, aiming {Aiming(o.RayAiming)}, chief {o.ChiefRay}, " +
        $"weights {o.Weighting}, RMS {o.Rms}, sign {o.Sign}, chromatic {o.ChromaticReference}";

    // WEC's Paraxial launches unaimed at the paraxial entrance pupil: OpticStudio's ray aiming
    // Off. Said so, because OpticStudio also has a ray-aiming mode it calls Paraxial, which is
    // something else and has not been compared (docs/programs.md).
    private static string Aiming(RayAiming a) => a switch
    {
        RayAiming.Paraxial => "Paraxial (unaimed, at the paraxial entrance pupil: OpticStudio's Off)",
        RayAiming.RealStop => "RealStop (aimed at the real stop: OpticStudio's Real)",
        _ => a.ToString(),
    };

    private static void WriteFan(TextWriter output, WavefrontResult r)
    {
        output.WriteLine(string.Format(CultureInfo.InvariantCulture, "field {0} ({1:G6}), wavelength {2} ({3:F4} um)",
                                       r.Field, r.FieldValue, r.Wavelength, r.Geometry.WavelengthUm));
        output.WriteLine("      Px        Py      W(waves)");
        foreach (var s in r.Samples)
            output.WriteLine(string.Format(CultureInfo.InvariantCulture, "{0,8:F4} {1,8:F4} {2,13}",
                                           s.Px, s.Py, s.Vignetted ? "vignetted" : s.W.ToString("F6", CultureInfo.InvariantCulture)));
        output.WriteLine();
    }

    private static void WriteZernike(TextWriter output, WavefrontResult r, ZernikeFit fit)
    {
        output.WriteLine();
        output.WriteLine(string.Format(CultureInfo.InvariantCulture,
            "Zernike {0}, field {1}, wavelength {2}, on {3} coordinates: residual RMS {4:F6} w{5}",
            fit.Set, r.Field, r.Wavelength, fit.Coordinates, fit.ResidualRms,
            double.IsNaN(fit.RmsFromCoefficients) ? "" : string.Format(CultureInfo.InvariantCulture, ", RMS from the coefficients {0:F6} w", fit.RmsFromCoefficients)));
        for (int j = 0; j < fit.Coefficients.Length; j++)
        {
            var (n, m) = fit.Set == ZernikeSet.Standard ? Zernike.Noll(j + 1) : Zernike.Fringe(j + 1);
            output.WriteLine(string.Format(CultureInfo.InvariantCulture, "  Z{0,-3} n={1,-2} m={2,-3} {3,12:F6}", j + 1, n, m, fit.Coefficients[j]));
        }
        foreach (var w in fit.Warnings) output.WriteLine($"  note: {w}");
    }

    private static void WriteCsv(string path, IEnumerable<WavefrontResult> results)
    {
        var sb = new StringBuilder("field,wavelength,Px,Py,launchPx,launchPy,x',y',z',xS',yT',imageX,imageY,L,M,N,W,weight,vignetted,stoppedAt\n");
        foreach (var r in results)
            foreach (var s in r.Samples)
                sb.AppendLine(string.Join(",", new object[]
                {
                    r.Field, r.Wavelength, s.Px, s.Py, s.LaunchPx, s.LaunchPy, s.ExitX, s.ExitY, s.ExitZ, s.CanonicalX, s.CanonicalY,
                    s.ImagePoint.X, s.ImagePoint.Y, s.Direction.X, s.Direction.Y, s.Direction.Z, s.W, s.Weight,
                    s.Vignetted ? 1 : 0, s.StoppedAt,
                }.Select(v => Convert.ToString(v, CultureInfo.InvariantCulture))));
        File.WriteAllText(path, sb.ToString());
    }
}
