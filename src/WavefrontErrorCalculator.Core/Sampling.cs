namespace WavefrontErrorCalculator.Core;

/// <summary>Which line across the pupil an OPD fan runs along.</summary>
public enum FanDirection
{
    /// <summary>The meridional line, Px = 0, Py from -1 to 1.</summary>
    Tangential,
    /// <summary>The sagittal line, Py = 0, Px from -1 to 1.</summary>
    Sagittal,
}

/// <summary>A point of the pupil to trace a ray through: (Px, Py), sagittal then meridional.</summary>
/// <param name="Area">
/// The area of the unit pupil the point stands for, or 0 for a sampling that does not divide the
/// pupil into cells (a fan, hexapolar rings).
/// </param>
/// <param name="I">Column on a square grid, -1 otherwise.</param>
/// <param name="J">Row on a square grid, -1 otherwise.</param>
public readonly record struct PupilPoint(double Px, double Py, double Area, int I = -1, int J = -1);

/// <summary>How the pupil is sampled (method.md §5.5).</summary>
public abstract record Sampling
{
    public abstract IReadOnlyList<PupilPoint> Points();

    /// <summary>
    /// An n×n grid of cell centres over the square circumscribing the unit pupil, keeping the
    /// cells whose centres fall inside it. Each cell stands for (2/n)² of the unit pupil. Cells
    /// the rim cuts are kept whole; the rim's own treatment arrives with the pupil exploration
    /// (method.md §6.2).
    /// </summary>
    public sealed record SquareGrid(int N) : Sampling
    {
        public override IReadOnlyList<PupilPoint> Points()
        {
            if (N < 1) throw new ArgumentOutOfRangeException(nameof(N));
            double step = 2.0 / N, area = step * step;
            var points = new List<PupilPoint>();
            for (int j = 0; j < N; j++)
                for (int i = 0; i < N; i++)
                {
                    double px = -1.0 + (i + 0.5) * step, py = -1.0 + (j + 0.5) * step;
                    if (px * px + py * py <= 1.0) points.Add(new PupilPoint(px, py, area, i, j));
                }
            return points;
        }
    }

    /// <summary>
    /// An n×n grid of nodes from -1 to 1 inclusive, spaced 2/(n - 1), keeping those inside the
    /// unit pupil or on its rim; each stands for an equal share of it. With n odd the grid has a
    /// node at the centre and at the four ends of the axes. Zemax OpticStudio's analyses sample so:
    /// its "256 x 256" is 255 nodes a side (docs/programs.md).
    /// </summary>
    public sealed record NodeGrid(int N) : Sampling
    {
        public override IReadOnlyList<PupilPoint> Points()
        {
            if (N < 2) throw new ArgumentOutOfRangeException(nameof(N));
            double step = 2.0 / (N - 1);
            var points = new List<PupilPoint>();
            for (int j = 0; j < N; j++)
                for (int i = 0; i < N; i++)
                {
                    double px = -1.0 + i * step, py = -1.0 + j * step;
                    if (px * px + py * py <= 1.0 + 1e-12) points.Add(new PupilPoint(px, py, step * step, i, j));
                }
            return points;
        }
    }

    /// <summary>The centre and <see cref="Rings"/> rings of 6k points at radii k / Rings.</summary>
    public sealed record Hexapolar(int Rings) : Sampling
    {
        public override IReadOnlyList<PupilPoint> Points()
        {
            if (Rings < 1) throw new ArgumentOutOfRangeException(nameof(Rings));
            var points = new List<PupilPoint> { new(0.0, 0.0, 0.0) };
            for (int k = 1; k <= Rings; k++)
                for (int m = 0; m < 6 * k; m++)
                {
                    double r = (double)k / Rings, a = 2.0 * Math.PI * m / (6 * k);
                    points.Add(new PupilPoint(r * Math.Sin(a), r * Math.Cos(a), 0.0));
                }
            return points;
        }
    }

    /// <summary>
    /// Gauss quadrature over the unit disk: Gauss-Legendre in t = ρ², which the area element
    /// dA = ½ dt dθ makes the natural variable, and <see cref="Arms"/> equally spaced azimuths,
    /// offset half a step from the meridian. Exact for a polynomial in x and y up to degree
    /// 2·<see cref="Rings"/> - 1 in ρ² and below <see cref="Arms"/> in θ (Forbes 1988). Each
    /// point's <see cref="PupilPoint.Area"/> is its quadrature weight, and they sum to π.
    /// </summary>
    public sealed record GaussQuadrature(int Rings, int Arms) : Sampling
    {
        public override IReadOnlyList<PupilPoint> Points()
        {
            if (Rings < 1) throw new ArgumentOutOfRangeException(nameof(Rings));
            if (Arms < 1) throw new ArgumentOutOfRangeException(nameof(Arms));
            var (nodes, weights) = GaussLegendre(Rings);
            var points = new List<PupilPoint>();
            for (int i = 0; i < Rings; i++)
            {
                double t = 0.5 * (nodes[i] + 1.0), rho = Math.Sqrt(t);
                double area = 0.5 * weights[i] * 0.5 * (2.0 * Math.PI / Arms);   // ½ dt dθ, with dt = ½ dx
                for (int j = 0; j < Arms; j++)
                {
                    double a = 2.0 * Math.PI * (j + 0.5) / Arms;
                    points.Add(new PupilPoint(rho * Math.Sin(a), rho * Math.Cos(a), area));
                }
            }
            return points;
        }

        /// <summary>Gauss-Legendre nodes and weights on [-1, 1], by Newton's method on Pₙ.</summary>
        internal static (double[] Nodes, double[] Weights) GaussLegendre(int n)
        {
            var x = new double[n];
            var w = new double[n];
            for (int i = 0; i < n; i++)
            {
                double z = Math.Cos(Math.PI * (i + 0.75) / (n + 0.5)), dp = 0.0;
                for (int k = 0; k < 100; k++)
                {
                    double p0 = 1.0, p1 = z;
                    for (int m = 2; m <= n; m++) { double p2 = ((2 * m - 1) * z * p1 - (m - 1) * p0) / m; p0 = p1; p1 = p2; }
                    double pn = n == 1 ? z : p1, pm = n == 1 ? 1.0 : p0;
                    dp = n * (z * pn - pm) / (z * z - 1.0);
                    double dz = pn / dp;
                    z -= dz;
                    if (Math.Abs(dz) < 1e-15) break;
                }
                x[i] = z;
                w[i] = 2.0 / ((1.0 - z * z) * dp * dp);
            }
            return (x, w);
        }
    }

    /// <summary><see cref="Count"/> points evenly along one line across the pupil, ends included.</summary>
    public sealed record Fan(FanDirection Direction, int Count) : Sampling
    {
        public override IReadOnlyList<PupilPoint> Points()
        {
            if (Count < 2) throw new ArgumentOutOfRangeException(nameof(Count));
            return Enumerable.Range(0, Count).Select(k =>
            {
                double t = -1.0 + 2.0 * k / (Count - 1);
                return Direction == FanDirection.Tangential ? new PupilPoint(0.0, t, 0.0) : new PupilPoint(t, 0.0, 0.0);
            }).ToList();
        }
    }

    /// <summary>Given points, such as another program's own pupil points, traced as they are.</summary>
    public sealed record Given(IReadOnlyList<PupilPoint> List) : Sampling
    {
        public override IReadOnlyList<PupilPoint> Points() => List;
    }
}
