namespace WavefrontErrorCalculator.Core;

/// <summary>
/// The wavefront from the ray aberrations alone: Rayces's exact relation (1964, eq. 8),
/// ∂W/∂x = −X/(R − W), ∂W/∂y = −Y/(R − W), integrated across the pupil from the chief ray.
/// It uses only where each ray goes in image space - a point and a direction - and no optical
/// path, so it is an independent check of <see cref="WavefrontCalculator"/>, which sums the paths.
///
/// <para>Rayces's terms (his figure): Q the centre of the reference sphere, C the point where
/// the sphere and the wavefront meet, R = QC; axes with Q on the y-axis and C on the z-axis, the
/// plane z = 0 through Q. Here the frame is Q at the origin and z along Q→C, which meets those
/// conditions for any C, so the same integration serves every reference sphere whose pupil
/// point the chief ray passes through. P is the point where a ray meets the wavefront through C,
/// (x, y) its coordinates, X, Y where the ray crosses the plane z = 0. W is Nijboer's, measured
/// along the sphere's radius: W = R − |QP|.</para>
///
/// <para>P lies on its ray at |QP| = R − W, so it moves with W itself: the integration is an
/// ODE in the pupil parameter t along a path from the chief ray (P = C, W = 0). The step is the
/// implicit trapezoidal rule, which is symmetric, so its error goes as even powers of the step;
/// Romberg extrapolation over four step sizes removes them to the order of the finest step's
/// eighth power.</para>
///
/// <para>The paths (<see cref="RaycesPaths"/>): by default they are shared. One line runs along
/// the meridional axis Px = 0, and from it one row Py = constant runs out each way to the targets
/// in that row, each line integrated once through every target on it, a segment at a time. A
/// lens's vignetted pupil is convex and symmetric in Px, so a row from the axis to a target that
/// is reached stays where rays are reached. Or one straight path from the chief ray to each target
/// (<see cref="RaycesPaths.Radial"/>): many more rays, and the fallback for a target a shared line
/// does not reach. W is a function of the point, so the path does not change it.</para>
///
/// <para>Nijboer's W is not the along-ray W the programs report (Rayces §3): the two differ by
/// about ½ W θ² for the angle θ between the ray and the sphere's radius. So at the target the ray
/// is followed from P to the reference sphere, and that distance, times n′, is the ray's optical
/// path beyond the wavefront: its W in the same form and units as <see cref="WavefrontSample.W"/>.</para>
/// </summary>
public static class RaycesIntegration
{
    /// <summary>
    /// One target pupil point: its W by integration and by optical path for comparison (waves,
    /// NaN where not reached), the finest density of steps its integration took, and the estimate
    /// of that integration's error: the change made by the last Romberg extrapolation, summed over
    /// the segments of its path (waves).
    /// </summary>
    /// <remarks><paramref name="Steps"/> is per path for radial paths and per unit of pupil length for
    /// shared ones. <paramref name="Nijboer"/> is the integrated W before conversion, measured along
    /// the sphere's radius (waves, the same sign convention).</remarks>
    public readonly record struct Point(double Px, double Py, double Integrated, double ByPath, int Steps, double ErrorEstimate,
                                        double Nijboer = double.NaN);

    /// <summary>
    /// W at each of <paramref name="targets"/> for field <paramref name="field"/> at the primary
    /// wavelength. <paramref name="steps"/> (a power of two, at least 8) is the number of trapezoids
    /// per radial path, or per unit of pupil length on shared lines, at least 16 to a segment. A
    /// point whose error estimate exceeds <paramref name="tolerance"/> waves is done again with four
    /// times as many, up to <paramref name="maxSteps"/>. The steep rim of a lens near grazing
    /// incidence needs them: US8264785 at 17.5° takes 4096. A point still above the tolerance, or
    /// not reached along a shared line, is done once more on its own straight path with its nodes
    /// closer together toward it (<see cref="Toward"/>), and keeps whichever estimate is smaller.
    /// </summary>
    public static IReadOnlyList<Point> Compute(LensModel lens, int field, WavefrontOptions options,
                                              IReadOnlyList<(double Px, double Py)> targets, int steps = 256,
                                              double tolerance = 1e-10, int maxSteps = 16384,
                                              RaycesPaths paths = RaycesPaths.Shared)
    {
        if (steps < 8 || (steps & (steps - 1)) != 0) throw new ArgumentException("steps must be a power of two, at least 8", nameof(steps));

        Point[] Refined(IReadOnlyList<(double Px, double Py)> t, bool shared, bool clustered)
        {
            Point[] Once(IReadOnlyList<(double Px, double Py)> u, int n) =>
                shared ? Shared(lens, field, options, u, n) : Radial(lens, field, options, u, n, clustered);
            var output = Once(t, steps);
            for (int n = steps * 4; n <= maxSteps; n *= 4)
            {
                var again = Enumerable.Range(0, output.Length)
                                      .Where(j => !double.IsNaN(output[j].Integrated) && output[j].ErrorEstimate > tolerance)
                                      .ToList();
                if (again.Count == 0) break;
                var redone = Once(again.Select(j => t[j]).ToList(), n);
                for (int k = 0; k < again.Count; k++) output[again[k]] = redone[k];
            }
            return output;
        }

        var result = Refined(targets, paths == RaycesPaths.Shared, clustered: false);

        // A target whose ray passes but which was not reached (its shared line crosses a ray that
        // does not pass) or not to the tolerance (a pupil ending at total internal reflection).
        var rest = Enumerable.Range(0, result.Length)
                             .Where(j => !double.IsNaN(result[j].ByPath)
                                         && (double.IsNaN(result[j].Integrated) || result[j].ErrorEstimate > tolerance))
                             .ToList();
        if (rest.Count > 0)
        {
            var clustered = Refined(rest.Select(j => targets[j]).ToList(), shared: false, clustered: true);
            for (int k = 0; k < rest.Count; k++)
            {
                var (was, now) = (result[rest[k]], clustered[k]);
                if (double.IsNaN(was.Integrated) || now.ErrorEstimate < was.ErrorEstimate) result[rest[k]] = now;
            }
        }
        return result;
    }

    /// <summary>
    /// One straight path from the chief ray to each target, with <paramref name="steps"/>
    /// trapezoids, evenly spaced or <paramref name="clustered"/> toward the target.
    /// </summary>
    private static Point[] Radial(LensModel lens, int field, WavefrontOptions options,
                                  IReadOnlyList<(double Px, double Py)> targets, int steps, bool clustered)
    {
        // Every node of every path, the chief ray first: one trace for all of them.
        var points = new List<PupilPoint> { new(0.0, 0.0, 0.0) };
        foreach (var (px, py) in targets)
            for (int k = 1; k <= steps; k++)
            {
                double s = clustered ? Toward(k, steps) : (double)k / steps;
                points.Add(new PupilPoint(px * s, py * s, 0.0));
            }
        var b = new Bundle(lens, field, options, points);

        var output = new Point[targets.Count];
        for (int j = 0; j < targets.Count; j++)
        {
            int last = j * steps + steps;
            var (wN, error) = b.Romberg(b.Chief, 1 + j * steps, steps) ?? (double.NaN, double.NaN);
            output[j] = b.Result(targets[j], last, wN, error, steps);
        }
        return output;
    }
    /// <summary>
    /// The meridional line Px = 0 from the chief ray, each way, through every row's Py; then each
    /// row from the line, each way, through its targets. <paramref name="density"/> is trapezoids
    /// per unit of pupil length.
    /// </summary>
    private static Point[] Shared(LensModel lens, int field, WavefrontOptions options,
                                  IReadOnlyList<(double Px, double Py)> targets, int density)
    {
        var points = new List<PupilPoint> { new(0.0, 0.0, 0.0) };
        var segments = new List<(int From, int First, int Steps)>();     // From: the node it starts at
        var nodeAt = new Dictionary<(double, double), int> { [(0.0, 0.0)] = 0 };

        // Lays out a line from node `from` at (x0, y0) through the stops, in order.
        void Line(int from, double x0, double y0, IEnumerable<(double X, double Y)> stops)
        {
            foreach (var (x, y) in stops)
            {
                double length = Math.Sqrt((x - x0) * (x - x0) + (y - y0) * (y - y0));
                int n = 16;
                while (n < length * density) n *= 2;
                int first = points.Count;
                for (int k = 1; k <= n; k++)
                    points.Add(new PupilPoint(x0 + (x - x0) * k / n, y0 + (y - y0) * k / n, 0.0));
                segments.Add((from, first, n));
                from = points.Count - 1;
                nodeAt[(x, y)] = from;
                (x0, y0) = (x, y);
            }
        }

        var rows = targets.Select(t => t.Py).Distinct().ToList();
        Line(0, 0.0, 0.0, rows.Where(y => y > 0.0).OrderBy(y => y).Select(y => (0.0, y)));
        Line(0, 0.0, 0.0, rows.Where(y => y < 0.0).OrderByDescending(y => y).Select(y => (0.0, y)));
        foreach (var y in rows)
        {
            int start = nodeAt[(0.0, y)];
            var xs = targets.Where(t => t.Py == y).Select(t => t.Px).Distinct().ToList();
            Line(start, 0.0, y, xs.Where(x => x > 0.0).OrderBy(x => x).Select(x => (x, y)));
            Line(start, 0.0, y, xs.Where(x => x < 0.0).OrderByDescending(x => x).Select(x => (x, y)));
        }
        var b = new Bundle(lens, field, options, points);

        // In the order laid out, every segment's start is settled before it.
        var settled = new Dictionary<int, (State? At, double Error)> { [0] = (b.Chief, 0.0) };
        foreach (var (from, first, n) in segments)
        {
            int end = first + n - 1;
            var (start, before) = settled[from];
            if (start is State s && b.Romberg(s, first, n) is (double wN, double error))
                settled[end] = (b.At(end, wN), before + error);
            else
                settled[end] = (null, double.NaN);
        }

        var output = new Point[targets.Count];
        for (int j = 0; j < targets.Count; j++)
        {
            int end = nodeAt[targets[j]];
            var (at, error) = settled[end];
            output[j] = b.Result(targets[j], end, at?.W ?? double.NaN, error, density);
        }
        return output;
    }

    /// <summary>Nijboer's W at a node of a path, in mm, with P's coordinates and the slopes there.</summary>
    private readonly record struct State(double W, double X, double Y, double Gx, double Gy);

    /// <summary>The traced rays of one set of paths, in the frame of the reference sphere.</summary>
    private sealed class Bundle
    {
        private readonly IReadOnlyList<WavefrontSample> samples;
        private readonly ReferenceGeometry g;
        private readonly Vec3 q, ex, ey;
        private readonly double radius, scale;
        private readonly double[] bx, by;
        private readonly bool[] ok;

        public Bundle(LensModel lens, int field, WavefrontOptions options, List<PupilPoint> points)
        {
            if (options.FocusShift != 0.0) throw new NotSupportedException("a focus shift is not integrated");
            if (options.ExitPupil is ExitPupil.ImageSurface or ExitPupil.Infinite)
                throw new NotSupportedException("the integration needs a reference sphere of finite radius");
            var result = WavefrontCalculator.Compute(lens, field, lens.PrimaryWavelength, options, new Sampling.Given(points));
            g = result.Geometry;
            if (g.IsInfinite || g.IsAfocal) throw new NotSupportedException("the integration needs a reference sphere of finite radius");

            q = g.Center;
            radius = g.Radius;
            var ez = (1.0 / radius) * (g.PupilPoint - q);
            ex = Normalized(new Vec3(1.0, 0.0, 0.0) - ez.X * ez);
            if (ex.Length < 0.5) ex = Normalized(new Vec3(0.0, 1.0, 0.0) - ez.Y * ez);
            ey = Cross(ez, ex);
            scale = (options.Sign == WavefrontSign.Hopkins ? 1.0 : -1.0) / (g.WavelengthUm * 1e-3)
                    / (options.DivideByImageIndex ? g.ImageIndex : 1.0);

            // The ray of node i: its transverse aberration (X, Y) in the plane through Q across QC.
            samples = result.Samples;
            int n = samples.Count;
            bx = new double[n];
            by = new double[n];
            ok = new bool[n];
            for (int i = 0; i < n; i++)
            {
                var s = samples[i];
                double along = s.Direction.Dot(ez);
                if (s.Vignetted || Math.Abs(along) < 1e-15) continue;
                var t = s.ImagePoint + ((q - s.ImagePoint).Dot(ez) / along) * s.Direction;
                bx[i] = (t - q).Dot(ex);
                by[i] = (t - q).Dot(ey);
                ok[i] = true;
            }
            if (!ok[0]) throw new InvalidOperationException("the chief ray does not reach the image");
        }

        /// <summary>The chief ray's node, where every path starts: P = C, W = 0.</summary>
        public State Chief => new(0.0, 0.0, 0.0, bx[0] / radius, by[0] / radius);

        /// <summary>The state at node i with Nijboer's W = <paramref name="w"/>; null if its ray is not reached.</summary>
        public State? At(int i, double w)
        {
            if (!ok[i] || OnRay(i, radius - w) is not Vec3 p) return null;
            return new State(w, (p - q).Dot(ex), (p - q).Dot(ey), bx[i] / (radius - w), by[i] / (radius - w));
        }

        /// <summary>P on node i's ray at distance rho from Q, on the side of C; null if the ray misses.</summary>
        private Vec3? OnRay(int i, double rho)
        {
            var s = samples[i];
            var delta = s.ImagePoint - q;
            double b = s.Direction.Dot(delta), c = delta.Dot(delta) - rho * rho, disc = b * b - c;
            if (disc < 0.0) return null;
            double root = Math.Sqrt(disc);
            var a1 = s.ImagePoint + (-b + root) * s.Direction;
            var a2 = s.ImagePoint + (-b - root) * s.Direction;
            return (a1 - g.PupilPoint).Length <= (a2 - g.PupilPoint).Length ? a1 : a2;
        }

        /// <summary>The implicit trapezoid from <paramref name="start"/> over nodes first … first + n − 1, every stride-th: Nijboer's W, in mm.</summary>
        private double? Trapezoid(State start, int first, int n, int stride)
        {
            var (wN, x0, y0, gx0, gy0) = start;
            for (int k = stride; k <= n; k += stride)
            {
                int i = first + k - 1;
                if (!ok[i]) return null;
                double next = wN;
                for (int iteration = 0; iteration < 50; iteration++)
                {
                    if (OnRay(i, radius - next) is not Vec3 p) return null;
                    double x1 = (p - q).Dot(ex), y1 = (p - q).Dot(ey);
                    double gx1 = bx[i] / (radius - next), gy1 = by[i] / (radius - next);
                    double updated = wN - 0.5 * ((gx0 + gx1) * (x1 - x0) + (gy0 + gy1) * (y1 - y0));
                    bool done = Math.Abs(updated - next) <= 1e-16 * radius;
                    next = updated;
                    if (done) break;
                }
                if (At(i, next) is not State settled) return null;
                (wN, x0, y0, gx0, gy0) = settled;
            }
            return wN;
        }

        /// <summary>
        /// Romberg over the finest step and three coarser ones, from <paramref name="start"/> across
        /// n nodes: Nijboer's W at the last (mm), and the last extrapolation's change (waves).
        /// </summary>
        public (double W, double Error)? Romberg(State start, int first, int n)
        {
            var table = new double[4];
            for (int level = 0; level < 4; level++)
            {
                if (Trapezoid(start, first, n, 1 << (3 - level)) is double v) table[level] = v; else return null;
            }
            // In place: after pass m, table[m..3] hold the m-th extrapolation, so table[2] ends
            // as the second diagonal entry and table[3] as the third.
            for (int m = 1; m < 4; m++)
            {
                double factor = Math.Pow(4.0, m);
                for (int level = 3; level >= m; level--)
                    table[level] = (factor * table[level] - table[level - 1]) / (factor - 1.0);
            }
            return (table[3], Math.Abs(scale * g.ImageIndex * (table[3] - table[2])));
        }

        /// <summary>The target at node i with Nijboer's W = <paramref name="wN"/>, converted to the along-ray W, beside its optical-path W.</summary>
        public Point Result((double Px, double Py) target, int i, double wN, double error, int steps)
        {
            double byPath = samples[i].Vignetted ? double.NaN : samples[i].W;
            double integrated = double.NaN, nijboer = double.NaN;
            // From P on the wavefront along the ray to the reference sphere: the path beyond the wavefront.
            if (!double.IsNaN(wN) && OnRay(i, radius - wN) is Vec3 p && OnRay(i, radius) is Vec3 onSphere)
            {
                double d = (onSphere - p).Dot(samples[i].Direction);
                integrated = scale * (-g.ImageIndex * d);
                nijboer = scale * g.ImageIndex * wN;
            }
            else error = double.NaN;
            return new Point(target.Px, target.Py, integrated, byPath, steps, error, nijboer);
        }
    }

    /// <summary>
    /// Node k of n along a path, as a fraction of it: 1 − (1 − k/n)², closer together toward the
    /// end. Where a pupil ends at total internal reflection, as US8264785's does at 17.5° with no
    /// aperture in the file, the ray's direction goes as the square root of the distance to the
    /// edge, and a slope that steep defeats the trapezoid and Romberg; in u = k/n the square root
    /// is smooth again. Elsewhere it only coarsens the start of the path, so it is kept for the
    /// points that need it.
    /// </summary>
    private static double Toward(int k, int n)
    {
        double rest = 1.0 - (double)k / n;
        return 1.0 - rest * rest;
    }

    private static Vec3 Normalized(Vec3 v) => (1.0 / v.Length) * v;

    private static Vec3 Cross(Vec3 a, Vec3 b) =>
        new(a.Y * b.Z - a.Z * b.Y, a.Z * b.X - a.X * b.Z, a.X * b.Y - a.Y * b.X);
}

/// <summary>How <see cref="RaycesIntegration"/> lays its paths across the pupil.</summary>
public enum RaycesPaths
{
    /// <summary>Along the meridional axis, then along rows of constant Py, each line integrated once through every target on it.</summary>
    Shared,

    /// <summary>One straight path from the chief ray to each target.</summary>
    Radial,
}
