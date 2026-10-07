using WavefrontErrorCalculator.Core;
using Xunit;

namespace WavefrontErrorCalculator.Tests;

public class ZernikeTests
{
    /// <summary>Noll (1976) Table 1: the first eleven terms.</summary>
    [Theory]
    [InlineData(1, 0, 0)] [InlineData(2, 1, 1)] [InlineData(3, 1, -1)] [InlineData(4, 2, 0)]
    [InlineData(5, 2, -2)] [InlineData(6, 2, 2)] [InlineData(7, 3, -1)] [InlineData(8, 3, 1)]
    [InlineData(9, 3, -3)] [InlineData(10, 3, 3)] [InlineData(11, 4, 0)] [InlineData(22, 6, 0)]
    public void NollsOrdering(int j, int n, int m) => Assert.Equal((n, m), Zernike.Noll(j));

    [Fact]
    public void TheFringeSetEndsOnTwelfthOrderSpherical()
    {
        Assert.Equal((10, 0), Zernike.Fringe(36));
        Assert.Equal((12, 0), Zernike.Fringe(37));
        Assert.Equal(1.0, Zernike.Value(ZernikeSet.Fringe, 2, 1.0, 0.0), 12);              // ρ cos θ
        Assert.Equal(2.0, Zernike.Value(ZernikeSet.Standard, 2, 1.0, 0.0), 12);            // 2ρ cos θ
    }

    /// <summary>The Standard set is orthonormal over the unit disk: ⟨Z_j Z_k⟩ = δ_jk.</summary>
    [Fact]
    public void TheStandardSetIsOrthonormal()
    {
        const int n = 400;
        var sums = new double[16, 16];
        int count = 0;
        for (int i = 0; i < n; i++)
            for (int k = 0; k < n; k++)
            {
                double x = -1 + (i + 0.5) * 2.0 / n, y = -1 + (k + 0.5) * 2.0 / n, rho = Math.Sqrt(x * x + y * y);
                if (rho > 1.0) continue;
                count++;
                double theta = Math.Atan2(y, x);
                var z = Enumerable.Range(1, 16).Select(j => Zernike.Value(ZernikeSet.Standard, j, rho, theta)).ToArray();
                for (int a = 0; a < 16; a++) for (int b = 0; b < 16; b++) sums[a, b] += z[a] * z[b];
            }
        for (int a = 0; a < 16; a++)
            for (int b = 0; b < 16; b++)
                Assert.True(Math.Abs(sums[a, b] / count - (a == b ? 1.0 : 0.0)) < 0.01, $"<Z{a + 1} Z{b + 1}> = {sums[a, b] / count:F4}");
    }

    /// <summary>A wavefront made of known terms is fitted back to those terms.</summary>
    [Theory]
    [InlineData(ZernikeSet.Standard)]
    [InlineData(ZernikeSet.Fringe)]
    public void AFitRecoversTheTermsAWavefrontWasMadeOf(ZernikeSet set)
    {
        var truth = new double[37];
        truth[3] = 0.4; truth[6] = -0.25; truth[10] = 0.1; truth[24] = 0.03; truth[36] = -0.02;
        var samples = new Sampling.SquareGrid(48).Points().Select(p =>
        {
            double rho = Math.Sqrt(p.Px * p.Px + p.Py * p.Py), theta = Math.Atan2(p.Py, p.Px);
            double w = Enumerable.Range(0, 37).Sum(j => truth[j] * Zernike.Value(set, j + 1, rho, theta));
            return new WavefrontSample(p.Px, p.Py, p.Px, p.Py, 0, 0, default, p.Px, p.Py, 0, p.Px, p.Py,
                                       default, default, 0, w, 1.0, false);
        }).ToList();
        var result = new WavefrontResult(0, 0, 0, WavefrontOptions.Reference,
            new ReferenceGeometry(default, default, 1, 1, 0.5, 1, default, default, 0, 0, 0),
            PupilDomain.Whole, samples, WavefrontCalculator.Statistics(samples, WavefrontOptions.Reference), []);

        var fit = Zernike.Fit(result, set, 37);
        for (int j = 0; j < 37; j++) Assert.Equal(truth[j], fit.Coefficients[j], 9);
        Assert.True(fit.ResidualRms < 1e-10);
        Assert.Empty(fit.Warnings);
    }
}
