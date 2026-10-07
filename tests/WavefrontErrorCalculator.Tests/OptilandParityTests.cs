using System.Text.Json;
using AberrationCalculator.Core.Glass;
using AberrationCalculator.Core.IO;
using WavefrontErrorCalculator.Core;
using Xunit;

namespace WavefrontErrorCalculator.Tests;

/// <summary>
/// The Optiland preset against Optiland itself (method.md §10). The export in TestData/optiland
/// was written by verification/optiland/export.py: Optiland's own wavefront, strategy
/// "chief_ray", ray by ray, with the indices it used. The same pupil points are traced here under
/// the Optiland preset, on those indices, and the two wavefronts compared ray by ray.
/// </summary>
public class OptilandParityTests(Xunit.Abstractions.ITestOutputHelper log)
{
    private sealed record Rays(double[] Px, double[] Py, double[] Opd, double[] Intensity, double Radius);

    private sealed record Given(IReadOnlyList<PupilPoint> List) : Sampling
    {
        public override IReadOnlyList<PupilPoint> Points() => List;
    }

    // Not the objective: Optiland traces it backwards (see the test at the end).
    public static IEnumerable<object[]> Lenses() => new[] { "KingslakeDG", "Cooke_40deg_FC", "US8264785_Ex4", "Relay_1to1" }.Select(n => new object[] { n });

    /// <summary>
    /// The NA 0.3 objective's virtual entrance pupil lies behind its object. Optiland's wavefront
    /// for it has the opposite sign to this program's, which a defocus test and Zemax OpticStudio
    /// both confirm (docs/programs.md): it traces each ray from the object towards that pupil,
    /// away from the lens, along the right line but backwards. On axis, minus this program's
    /// wavefront is Optiland's to 2e-2 wave; the rest is not yet explained.
    /// </summary>
    [Fact]
    public void OptilandNegatesTheWavefrontWhenThePupilIsBehindTheObject()
    {
        const string name = "Objective_NA03_5x";
        var data = JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "TestData", "optiland", name + ".json"))).RootElement;
        var indices = data.GetProperty("indices").EnumerateArray().Select(e => e.GetDouble()).ToArray();
        var catalog = CatalogLocator.LoadBundled();
        var system = LensFile.Read(Path.Combine(AppContext.BaseDirectory, "TestData", name + ".zmx"), catalog);
        int primary = Math.Max(0, system.PrimaryWavelengthIndex);
        var lens = new LensModel(system, catalog, w => w == primary ? indices : null);
        var r = Read(data.GetProperty("fields")[0].GetProperty("tangential"));
        var points = Enumerable.Range(0, r.Px.Length).Select(k => new PupilPoint(r.Px[k], r.Py[k], 0.0)).ToList();
        var ours = WavefrontCalculator.Compute(lens, 0, primary, WavefrontOptions.Optiland, new Sampling.Given(points));
        double negated = 0.0, same = 0.0;
        for (int k = 0; k < points.Count; k++)
        {
            negated = Math.Max(negated, Math.Abs(-ours.Samples[k].W - r.Opd[k]));
            same = Math.Max(same, Math.Abs(ours.Samples[k].W - r.Opd[k]));
        }
        log.WriteLine($"on axis: |-W - Optiland| {negated:E3}, |W - Optiland| {same:E3} waves");
        Assert.True(negated < 2e-2, $"{negated:E3}");
        Assert.True(same > 0.5, $"{same:E3}");
    }

    [Theory]
    [MemberData(nameof(Lenses))]
    public void TheOptilandPresetIsOptilandsWavefront(string name)
    {
        var data = JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "TestData", "optiland", name + ".json"))).RootElement;
        var indices = data.GetProperty("indices").EnumerateArray().Select(e => e.GetDouble()).ToArray();
        var catalog = CatalogLocator.LoadBundled();
        var system = LensFile.Read(Path.Combine(AppContext.BaseDirectory, "TestData", name + ".zmx"), catalog);
        int primary = Math.Max(0, system.PrimaryWavelengthIndex);
        var lens = new LensModel(system, catalog, w => w == primary ? indices : null);

        // How the two programs' glass data differ: reported, not tested - that is not a
        // difference of convention, and it is why the comparison uses Optiland's indices.
        var catalogIndices = IndexResolver.Build(system, catalog, system.Wavelengths[primary].Value);
        for (int i = 0; i < catalogIndices.Length; i++)
            if (Math.Abs(Math.Abs(catalogIndices[i]) - indices[i]) > 1e-7)
                log.WriteLine($"{name} surface {i} ({system.Surfaces[i].Material}): index {Math.Abs(catalogIndices[i]):F6} here, {indices[i]:F6} in Optiland");

        var failures = new List<string>();
        foreach (var field in data.GetProperty("fields").EnumerateArray())
        {
            int index = field.GetProperty("index").GetInt32();
            foreach (var set in new[] { "grid", "tangential", "sagittal" })
            {
                var r = Read(field.GetProperty(set));
                var points = Enumerable.Range(0, r.Px.Length).Select(k => new PupilPoint(r.Px[k], r.Py[k], 0.0)).ToList();
                var ours = WavefrontCalculator.Compute(lens, index, primary, WavefrontOptions.Optiland, new Given(points));

                double worst = 0.0, size = 0.0; int at = -1;
                for (int k = 0; k < points.Count; k++)
                {
                    if (r.Intensity[k] <= 0.0) continue;
                    Assert.False(ours.Samples[k].Vignetted);
                    if (Math.Abs(ours.Samples[k].W - r.Opd[k]) > worst) { worst = Math.Abs(ours.Samples[k].W - r.Opd[k]); at = k; }
                    size = Math.Max(size, Math.Abs(r.Opd[k]));
                }
                log.WriteLine($"{name} field {index} {set}: largest |W - Optiland| {worst:E3} waves of {size:F4}, at ({(at >= 0 ? r.Px[at] : 0):F3}, {(at >= 0 ? r.Py[at] : 0):F3}); " +
                              $"radius {ours.Geometry.Radius:F6} against Optiland's {r.Radius:F6}");
                if (set == "tangential")
                    log.WriteLine("  W - Optiland along the tangential fan: " + string.Join(" ",
                        Enumerable.Range(0, points.Count).Where(k => k % 5 == 0 && r.Intensity[k] > 0)
                                  .Select(k => $"{r.Py[k]:F1}:{ours.Samples[k].W - r.Opd[k]:E2}")));
                // 1e-5 wave is 6 nm of path: what is left, on the fast lens, of two iterative
                // intersections with an asphere near the branch point of its sag.
                if (worst >= 1e-5) failures.Add($"field {index} {set}: {worst:E3} waves");
            }
        }
        Assert.True(failures.Count == 0, string.Join("; ", failures));
    }

    /// <summary>
    /// The other wavelengths: each referred to its own chief ray, with the exit pupil and the
    /// image-space index the primary wavelength's, as Optiland does - the Optiland preset's
    /// <see cref="ChromaticReference.OwnChief"/> - and on Optiland's indices at that wavelength.
    /// </summary>
    [Theory]
    [MemberData(nameof(Lenses))]
    public void TheOtherWavelengthsAreOptilands(string name)
    {
        var data = JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "TestData", "optiland", name + ".json"))).RootElement;
        var catalog = CatalogLocator.LoadBundled();
        var system = LensFile.Read(Path.Combine(AppContext.BaseDirectory, "TestData", name + ".zmx"), catalog);
        int primary = Math.Max(0, system.PrimaryWavelengthIndex);
        var given = new Dictionary<int, double[]>
        {
            [primary] = data.GetProperty("indices").EnumerateArray().Select(e => e.GetDouble()).ToArray(),
        };
        foreach (var other in data.GetProperty("others").EnumerateArray())
            given[other.GetProperty("index").GetInt32()] = other.GetProperty("indices").EnumerateArray().Select(e => e.GetDouble()).ToArray();
        var lens = new LensModel(system, catalog, w => given.TryGetValue(w, out var n) ? n : null);

        var failures = new List<string>();
        foreach (var other in data.GetProperty("others").EnumerateArray())
        {
            int w = other.GetProperty("index").GetInt32();
            foreach (var field in other.GetProperty("fields").EnumerateArray())
            {
                int index = field.GetProperty("index").GetInt32();
                var r = Read(field.GetProperty("grid"));
                var points = Enumerable.Range(0, r.Px.Length).Select(k => new PupilPoint(r.Px[k], r.Py[k], 0.0)).ToList();
                var ours = WavefrontCalculator.Compute(lens, index, w, WavefrontOptions.Optiland, new Given(points));
                double worst = Enumerable.Range(0, points.Count).Where(k => r.Intensity[k] > 0)
                                         .Max(k => Math.Abs(ours.Samples[k].W - r.Opd[k]));
                log.WriteLine($"{name} wavelength {lens.WavelengthUm(w):F5} field {index}: largest |W - Optiland| {worst:E3} waves");
                if (worst >= 1e-5) failures.Add($"wavelength {w} field {index}: {worst:E3}");
            }
        }
        Assert.True(failures.Count == 0, string.Join("; ", failures));
    }

    private static Rays Read(JsonElement e)
    {
        double[] A(string key) => e.GetProperty(key).EnumerateArray().Select(v => v.GetDouble()).ToArray();
        return new Rays(A("px"), A("py"), A("opd"), A("intensity"), e.GetProperty("radius").GetDouble());
    }
}
