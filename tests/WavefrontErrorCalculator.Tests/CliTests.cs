using WavefrontErrorCalculator.Cli;
using Xunit;

namespace WavefrontErrorCalculator.Tests;

public class CliTests
{
    private static readonly string Lens = Path.Combine(AppContext.BaseDirectory, "TestData", "KingslakeDG.zmx");

    private static (int Code, string Text) Run(params string[] args)
    {
        var output = new StringWriter();
        int code = Program.Run(args, output);
        return (code, output.ToString());
    }

    [Fact]
    public void TheSummaryHasARowForEachField()
    {
        var (code, text) = Run(Lens, "--grid", "16");
        Assert.Equal(0, code);
        Assert.Contains("RMS(w)", text);
        Assert.Equal(3, text.Split('\n').Count(l => l.TrimStart().StartsWith("0 ") || l.TrimStart().StartsWith("1 ") || l.TrimStart().StartsWith("2 ")));
    }

    /// <summary>
    /// wfe parity reads a result file, finds its lens beside it or in the folder above, and
    /// reports how far the two wavefronts are apart.
    /// </summary>
    [Fact]
    public void ParityComparesAResultFileRayByRay()
    {
        string result = Path.Combine(AppContext.BaseDirectory, "TestData", "zemax", "KingslakeDG_ExitPupil_Off.json");
        var (code, text) = Run("parity", result, "--preset", "Zemax");
        Assert.Equal(0, code);
        var last = text.TrimEnd().Split('\n').Last();
        Assert.StartsWith("largest |W - program|", last);
        double largest = double.Parse(last.Split(' ')[4], System.Globalization.CultureInfo.InvariantCulture);
        Assert.True(largest < 1e-7, last);
        Assert.Contains("0 rays vignetted in one and not the other", last);

        var (_, reference) = Run("parity", result, "--quiet");
        double apart = double.Parse(reference.TrimEnd().Split('\n').Last().Split(' ')[4], System.Globalization.CultureInfo.InvariantCulture);
        Assert.True(apart > 0.01, "the Reference preset is not OpticStudio's");
    }

    /// <summary>
    /// wfe parity --rays writes the comparison ray by ray: every ray as CSV, and the primary
    /// wavelength's fans and map as Markdown tables, one row per pupil point.
    /// </summary>
    [Fact]
    public void ParityWritesTheComparisonRayByRay()
    {
        string result = Path.Combine(AppContext.BaseDirectory, "TestData", "zemax", "KingslakeDG_ExitPupil_Off.json");
        string csv = Path.Combine(Path.GetTempPath(), $"wfe-rays-{Guid.NewGuid():N}.csv");
        string md = Path.ChangeExtension(csv, ".md");
        try
        {
            Assert.Equal(0, Run("parity", result, "--preset", "Zemax", "--quiet", "--rays", csv).Code);
            var lines = File.ReadAllLines(csv);
            Assert.StartsWith("wave,lambda_um,field,field_value,set,px,py,program_opd,wfe_w,difference", lines[0]);
            Assert.Equal(3 * 3 * (41 + 41 + 197), lines.Length - 1);
            Assert.All(lines.Skip(1), l => Assert.True(Math.Abs(double.Parse(l.Split(',')[9], System.Globalization.CultureInfo.InvariantCulture)) < 1e-7, l));

            Assert.Equal(0, Run("parity", result, "--preset", "Zemax", "--quiet", "--rays", md).Code);
            var text = File.ReadAllText(md);
            Assert.Contains("## Tangential fan", text);
            Assert.Contains("## Map", text);
            Assert.Equal(41 + 41 + 197, text.Split('\n').Count(l => l.StartsWith("| ") && !l.StartsWith("| Px")));
        }
        finally
        {
            File.Delete(csv);
            File.Delete(md);
        }
    }

    /// <summary>
    /// wfe opd-table puts programs' OPD beside W, one row per pupil point and field; under the
    /// Zemax preset W is OpticStudio's OPDC itself, so every Δ is tiny.
    /// </summary>
    [Fact]
    public void OpdTableListsEveryPointBesideW()
    {
        string data = Path.Combine(AppContext.BaseDirectory, "TestData");
        string md = Path.Combine(Path.GetTempPath(), $"wfe-opd-table-{Guid.NewGuid():N}.md");
        try
        {
            var (code, _) = Run("opd-table", Lens, "OpticStudio=" + Path.Combine(data, "zemax-opdc", "KingslakeDG_OPDC_Off.json"),
                                "--preset", "Zemax", "--out", md);
            Assert.Equal(0, code);
            var rows = File.ReadAllLines(md).Where(l => l.StartsWith("| map ") || l.StartsWith("| rim ")).ToList();
            Assert.Equal(3 * 857, rows.Count);
            foreach (var row in rows)
            {
                string delta = row.Split('|', StringSplitOptions.TrimEntries)[6];
                Assert.True(Math.Abs(double.Parse(delta, System.Globalization.CultureInfo.InvariantCulture)) < 1e-8, row);
            }
        }
        finally
        {
            File.Delete(md);
        }
    }

    /// <summary>
    /// --quadrature samples on Gauss rings and integrates: the Reference preset's exit-area
    /// weights are kept, and 8 and 24 rings agree to the printed digits on the unvignetted double
    /// Gauss; a preset that counts each ray once takes the rule's own weights instead.
    /// </summary>
    [Fact]
    public void QuadratureIntegratesAndConverges()
    {
        string Rms(string text) => text.Split('\n').Single(l => l.TrimStart().StartsWith("2 ")).Split(' ', StringSplitOptions.RemoveEmptyEntries)[4];
        var (code, coarse) = Run(Lens, "--field", "2", "--quadrature", "8");
        Assert.Equal(0, code);
        Assert.Contains("weights ExitArea", coarse);
        var (_, fine) = Run(Lens, "--field", "2", "--quadrature", "24", "48");
        Assert.Equal(Rms(coarse), Rms(fine));
        Assert.Contains(" 1152 ", fine);

        var (_, perRay) = Run(Lens, "--field", "2", "--quadrature", "8", "--preset", "LensHHLT");
        Assert.Contains("weights Quadrature", perRay);
        var (_, kept) = Run(Lens, "--field", "2", "--quadrature", "8", "--set", "Weighting=PerRay");
        Assert.Contains("weights PerRay", kept);
    }

    [Fact]
    public void ASwitchCanBeSetByName()
    {
        var (code, text) = Run(Lens, "--field", "0", "--grid", "8", "--set", "exitpupil=paraxialaxial", "--set", "Rms=AboutZero");
        Assert.Equal(0, code);
        Assert.Contains("E′ ParaxialAxial", text);
        Assert.Contains("RMS AboutZero", text);
    }

    [Fact]
    public void AFanListsOneLinePerRay()
    {
        var (code, text) = Run(Lens, "--field", "2", "--fan", "S", "5");
        Assert.Equal(0, code);
        Assert.Equal(5, text.Split('\n').Count(l => l.Contains("  0.0000 ") && !l.Contains("Px")));
    }

    [Fact]
    public void TheCsvHasOneRowPerRay()
    {
        string csv = Path.Combine(Path.GetTempPath(), $"wfe-{Guid.NewGuid():N}.csv");
        try
        {
            Assert.Equal(0, Run(Lens, "--field", "1", "--grid", "8", "--csv", csv).Code);
            var lines = File.ReadAllLines(csv);
            Assert.StartsWith("field,wavelength,Px,Py", lines[0]);
            Assert.Equal(1 + 52, lines.Length);   // the cells of an 8 x 8 grid whose centres are inside the pupil
        }
        finally { File.Delete(csv); }
    }

    [Fact]
    public void CompareListsTheSwitchesThatDiffer()
    {
        var (code, text) = Run("compare", Lens, "--presets", "Reference,LensHHLT", "--grid", "8");
        Assert.Equal(0, code);
        Assert.Contains("ExitPupil = ParaxialChiefIntersect", text);
        Assert.Contains("Weighting = PerRay", text);
        Assert.Contains("RayAiming = Paraxial", text);
        Assert.DoesNotContain("Sign =", text);   // both chief - ray
    }

    [Fact]
    public void AnUnknownSwitchIsRefused()
    {
        Assert.Throws<ArgumentException>(() => Run(Lens, "--set", "Nonesuch=1"));
    }
}
