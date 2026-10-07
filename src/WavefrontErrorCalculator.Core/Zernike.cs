namespace WavefrontErrorCalculator.Core;

/// <summary>Which Zernike polynomials.</summary>
public enum ZernikeSet
{
    /// <summary>Noll's ordering and normalisation: orthonormal over the unit disk, Z₁ = 1, Z₂ = 2ρ cos θ.</summary>
    Standard,
    /// <summary>The Fringe (University of Arizona) ordering, 37 terms, unnormalised: Z₂ = ρ cos θ.</summary>
    Fringe,
}

/// <summary>A Zernike fit to one wavefront.</summary>
/// <param name="Coefficients">In waves; element k is term k + 1.</param>
/// <param name="ResidualRms">The weighted RMS of the wavefront less the fit, in waves.</param>
/// <param name="RmsFromCoefficients">
/// √(Σ_{j≥2} c_j²) for the Standard set, which is the wavefront's RMS only when the samples fill
/// the unit disk uniformly; NaN for the Fringe set, which is not orthonormal.
/// </param>
/// <param name="MaxRadius">The largest ρ among the samples fitted.</param>
public sealed record ZernikeFit(ZernikeSet Set, PupilCoordinates Coordinates, double[] Coefficients,
                                double ResidualRms, double RmsFromCoefficients, double MaxRadius,
                                IReadOnlyList<string> Warnings);

/// <summary>
/// Zernike polynomials, and a weighted least-squares fit of them to a wavefront (method.md §7).
///
/// <para>Welford (p. 246) is the caution: Zernike's polynomials are orthogonal over the unit
/// circle and nothing else, so for a vignetted pupil they are only a fitting basis unless the
/// coordinates make that pupil a circle - which the canonical coordinates do (Hopkins 1964;
/// King 1968). The fit warns when the samples are not on a unit circle.</para>
/// </summary>
public static class Zernike
{
    private static readonly (int N, int M)[] FringeTable =
    {
        (0, 0), (1, 1), (1, -1), (2, 0), (2, 2), (2, -2), (3, 1), (3, -1), (4, 0), (3, 3),
        (3, -3), (4, 2), (4, -2), (5, 1), (5, -1), (6, 0), (4, 4), (4, -4), (5, 3), (5, -3),
        (6, 2), (6, -2), (7, 1), (7, -1), (8, 0), (5, 5), (5, -5), (6, 4), (6, -4), (7, 3),
        (7, -3), (8, 2), (8, -2), (9, 1), (9, -1), (10, 0), (12, 0),
    };

    /// <summary>
    /// Noll's term <paramref name="j"/> as (n, m): m &gt; 0 for cos mθ, m &lt; 0 for sin |m|θ.
    /// Within a row n the terms run in increasing |m|, cosine and sine paired, the even j the
    /// cosine (Noll 1976).
    /// </summary>
    public static (int N, int M) Noll(int j)
    {
        if (j < 1) throw new ArgumentOutOfRangeException(nameof(j));
        int n = 0;
        while ((n + 1) * (n + 2) / 2 < j) n++;
        int k = j - n * (n + 1) / 2;                      // 1-based position within row n
        int m = n % 2 == 0 ? 2 * (k / 2) : 2 * ((k - 1) / 2) + 1;
        if (m == 0) return (n, 0);
        return (n, j % 2 == 0 ? m : -m);
    }

    /// <summary>The Fringe set's term <paramref name="j"/>, 1 to 37, as (n, m).</summary>
    public static (int N, int M) Fringe(int j)
    {
        if (j < 1 || j > FringeTable.Length) throw new ArgumentOutOfRangeException(nameof(j), "the Fringe set has 37 terms");
        return FringeTable[j - 1];
    }

    /// <summary>The radial polynomial R_n^|m|(ρ).</summary>
    public static double Radial(int n, int m, double rho)
    {
        m = Math.Abs(m);
        double sum = 0.0;
        for (int k = 0; k <= (n - m) / 2; k++)
        {
            double c = Factorial(n - k) / (Factorial(k) * Factorial((n + m) / 2 - k) * Factorial((n - m) / 2 - k));
            sum += (k % 2 == 0 ? c : -c) * Math.Pow(rho, n - 2 * k);
        }
        return sum;
    }

    /// <summary>Term <paramref name="j"/> of <paramref name="set"/> at polar (ρ, θ), θ from the sagittal (x) axis.</summary>
    public static double Value(ZernikeSet set, int j, double rho, double theta)
    {
        var (n, m) = set == ZernikeSet.Standard ? Noll(j) : Fringe(j);
        double angular = m > 0 ? Math.Cos(m * theta) : m < 0 ? Math.Sin(-m * theta) : 1.0;
        double norm = set == ZernikeSet.Fringe ? 1.0 : m == 0 ? Math.Sqrt(n + 1.0) : Math.Sqrt(2.0 * (n + 1.0));
        return norm * Radial(n, m, rho) * angular;
    }

    /// <summary>
    /// Fits the first <paramref name="terms"/> terms to the unvignetted samples of
    /// <paramref name="result"/>, weighted as they were for its statistics, on the coordinates
    /// <paramref name="coordinates"/> (by default the ones its options name).
    /// </summary>
    public static ZernikeFit Fit(WavefrontResult result, ZernikeSet set, int terms, PupilCoordinates? coordinates = null)
    {
        if (result == null) throw new ArgumentNullException(nameof(result));
        if (terms < 1) throw new ArgumentOutOfRangeException(nameof(terms));
        if (set == ZernikeSet.Fringe && terms > FringeTable.Length) throw new ArgumentOutOfRangeException(nameof(terms), "the Fringe set has 37 terms");
        var coords = coordinates ?? result.Options.PupilCoordinates;
        var g = result.Geometry;
        if (coords == PupilCoordinates.LaunchRefined && (g.IsInfinite || double.IsNaN(g.HPrimeS)))
            throw new NotSupportedException("refining to the launch grid needs a finite reference sphere and canonical coordinates");

        var used = result.Samples.Where(s => !s.Vignetted && s.Weight > 0.0).ToList();
        if (used.Count < terms) throw new InvalidOperationException($"{used.Count} samples cannot fit {terms} terms");

        (double X, double Y) At(WavefrontSample s) => coords switch
        {
            PupilCoordinates.Launch => (s.Px, s.Py),
            PupilCoordinates.ExitSphere => (s.ExitX, s.ExitY),
            PupilCoordinates.LaunchRefined => (s.U, s.V),
            _ => (s.CanonicalX, s.CanonicalY),
        };

        // Singh (1976) eq. 13: the ray landed at canonical (x′, y′), not at the grid point (u, v)
        // it was launched for, unless the image is isoplanatic. Its W is carried from one to the
        // other along the wavefront's slope, which the ray's own transverse aberration gives
        // exactly (Welford eq. 7.14): ∂W/∂X = -n′ δξ / |Q₀P′| on the sphere, times h′_S per
        // canonical unit.
        double scale = (result.Options.Sign == WavefrontSign.Hopkins ? 1.0 : -1.0) / (g.WavelengthUm * 1e-3)
                       / (result.Options.DivideByImageIndex ? g.ImageIndex : 1.0);
        double ValueAt(WavefrontSample s)
        {
            if (coords != PupilCoordinates.LaunchRefined) return s.W;
            double distance = (s.ImagePoint - s.Sphere).Length;
            double gx = -scale * g.ImageIndex * (s.ImagePoint.X - g.Center.X) / distance * g.HPrimeS;
            double gy = -scale * g.ImageIndex * (s.ImagePoint.Y - g.Center.Y) / distance * g.HPrimeT;
            return s.W + (s.U - s.CanonicalX) * gx + (s.V - s.CanonicalY) * gy;
        }

        int rows = used.Count;
        var a = new double[rows, terms];
        var b = new double[rows];
        double total = used.Sum(s => s.Weight);
        double maxRho = 0.0;
        for (int i = 0; i < rows; i++)
        {
            var (x, y) = At(used[i]);
            double rho = Math.Sqrt(x * x + y * y), theta = Math.Atan2(y, x);
            maxRho = Math.Max(maxRho, rho);
            double root = Math.Sqrt(used[i].Weight / total);
            for (int j = 0; j < terms; j++) a[i, j] = root * Value(set, j + 1, rho, theta);
            b[i] = root * ValueAt(used[i]);
        }
        var c = Numerics.LeastSquares(a, b);

        double residual = 0.0;
        for (int i = 0; i < rows; i++)
        {
            double fit = 0.0;
            for (int j = 0; j < terms; j++) fit += a[i, j] * c[j];
            residual += (b[i] - fit) * (b[i] - fit);
        }

        var warnings = new List<string>();
        // Canonical coordinates are first order about the chief ray, so a rim a per cent or two off
        // the unit circle is the pupil's own aberration and expected; beyond that the coordinates
        // do not describe the pupil as a circle.
        if (maxRho > 1.05) warnings.Add($"samples reach ρ = {maxRho:F3}: the pupil is not inside the unit circle in these coordinates");
        if (result.Pupil.IsVignetted && coords != PupilCoordinates.Canonical)
            warnings.Add("the pupil is vignetted and these coordinates do not make it a circle; Zernike terms are then a fitting basis, not orthogonal aberrations (Welford p. 246)");

        double fromCoefficients = set == ZernikeSet.Standard ? Math.Sqrt(c.Skip(1).Sum(v => v * v)) : double.NaN;
        return new ZernikeFit(set, coords, c, Math.Sqrt(residual), fromCoefficients, maxRho, warnings);
    }

    private static double Factorial(int n)
    {
        double f = 1.0;
        for (int k = 2; k <= n; k++) f *= k;
        return f;
    }
}
