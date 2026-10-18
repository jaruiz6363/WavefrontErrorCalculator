namespace WavefrontErrorCalculator.Core;

/// <summary>The point spread function on a grid of image points, from <see cref="Diffraction.Psf"/>.</summary>
/// <param name="X">The grid's sagittal offsets from the reference point Q′ (mm).</param>
/// <param name="Y">Its meridional offsets (mm).</param>
/// <param name="Intensity">Intensity[iy, ix] per mm², normalised so that it integrates to 1 over the whole image plane.</param>
/// <param name="PerfectPeak">The peak the same pupil would give with W ≡ 0, in the same units: Intensity / PerfectPeak is the Strehl ratio.</param>
/// <param name="TransmittedArea">The pupil area that transmits, as a fraction of the unit pupil's.</param>
/// <param name="Rays">The pupil points summed.</param>
public sealed record PsfResult(double[] X, double[] Y, double[,] Intensity, double PerfectPeak, double TransmittedArea, int Rays);

/// <summary>The optical transfer function at one spatial frequency, from <see cref="Diffraction.Otf"/>.</summary>
/// <param name="Real">Its real part.</param>
/// <param name="Imaginary">Its imaginary part.</param>
/// <param name="Pairs">The pupil points whose shifted partner also transmits: the overlap the integral covers.</param>
/// <param name="Rays">The pupil points that transmit.</param>
/// <param name="Unsolved">Pupil points whose partner could not be found by the iteration (they count as not overlapping).</param>
public sealed record OtfResult(double Real, double Imaginary, int Pairs, int Rays, int Unsolved)
{
    /// <summary>The MTF: the OTF's modulus.</summary>
    public double Modulus => Math.Sqrt(Real * Real + Imaginary * Imaginary);
}

/// <summary>
/// Diffraction computed directly from traced rays, without a fast Fourier transform's grids
/// (method.md §11): a reference against which other programs' PSF and MTF can be judged.
///
/// <para>The pupil function is written in the image's spatial frequencies q = n′ u⊥ / λ, where u is
/// the unit vector from the ray's point B′ on the exit reference sphere to the sphere's centre Q′
/// and u⊥ its part in the image plane. The amplitude in the image plane at Q′ + Δ is then the
/// Debye integral U(Δ) = ∫ P(q) e^{2πi q·Δ} d²q, a Fourier transform, so the PSF is |U|² and the
/// OTF is the autocorrelation of P (Goodman 2005 §6.3; Born &amp; Wolf §8.8).</para>
///
/// <para>The pupil is lit uniformly in the pupil coordinates (Px, Py): the energy through a pupil
/// element dPx dPy is the same everywhere, as in every program compared. The ray tube carries
/// it to an element d²q = J dPx dPy, J = |∂q/∂(Px, Py)| from differential rays, so P = J^(-1/2)
/// e^{-2πiW}. Nothing assumes the map from (Px, Py) to q to be linear, the pupil to be round, or
/// the samples to lie on a grid in q: off axis and at a vignetting edge, where FFT methods
/// approximate, this is exact up to the outer quadrature over the pupil.</para>
///
/// <para>The OTF at frequency ν pairs each transmitting pupil point with the point whose q is
/// q − ν. That point is found by tracing, not by interpolation: rays are iterated onto it, and
/// whether it transmits is decided by tracing it through the apertures. So the only
/// discretisation is the outer sum over a square grid of (Px, Py), whose error at the pupil's
/// edges falls as the grid is refined.</para>
/// </summary>
public static class Diffraction
{
    /// <summary>The step of the differential rays that give J (pupil units).</summary>
    private const double Step = 1e-5;

    /// <summary>
    /// The PSF on the grid of points (<paramref name="x"/>, <paramref name="y"/>), in mm from the
    /// reference sphere's centre along <paramref name="axisX"/> and <paramref name="axisY"/> - by
    /// default the image plane's x and y - by summing the Debye integral over an n×n square grid
    /// of the pupil. Other axes give the PSF on another plane through the centre, such as the one
    /// perpendicular to the chief ray.
    /// </summary>
    public static PsfResult Psf(LensModel lens, int field, int wavelength, WavefrontOptions options, int grid,
                                IReadOnlyList<double> x, IReadOnlyList<double> y, Vec3? axisX = null, Vec3? axisY = null)
    {
        var ex = axisX ?? new Vec3(1, 0, 0);
        var ey = axisY ?? new Vec3(0, 1, 0);
        var pupil = new PupilMap(lens, field, wavelength, options);
        var nodes = new Sampling.SquareGrid(grid).Points();
        var rays = pupil.Trace(nodes.Select(p => (p.Px, p.Py)).ToArray(), transmission: true, jacobian: true);
        double cell = (2.0 / grid) * (2.0 / grid);

        var used = Enumerable.Range(0, rays.Length).Where(k => rays[k].Transmitted).ToArray();
        int m = used.Length;
        var cRe = new double[m];
        var cIm = new double[m];
        var qx = new double[m];
        var qy = new double[m];
        double amplitude = 0.0;
        for (int i = 0; i < m; i++)
        {
            var r = rays[used[i]];
            // P(q) d²q, with P = J^(-1/2) e^{2πi φ} and d²q = J dPx dPy.
            double a = cell * Math.Sqrt(r.Jacobian);
            cRe[i] = a * Math.Cos(2 * Math.PI * r.Phase);
            cIm[i] = a * Math.Sin(2 * Math.PI * r.Phase);
            // The spatial frequencies along the plane's axes: q₃·e, q₃ = n′u/λ.
            qx[i] = r.Qx * ex.X + r.Qy * ex.Y + r.Qz * ex.Z;
            qy[i] = r.Qx * ey.X + r.Qy * ey.Y + r.Qz * ey.Z;
            amplitude += a;
        }
        double energy = m * cell;

        int nx = x.Count, ny = y.Count;
        var intensity = new double[ny, nx];
        Parallel.For(0, ny, iy =>
        {
            var re = new double[nx];
            var im = new double[nx];
            for (int i = 0; i < m; i++)
            {
                // c e^{2πi qy y}, then times e^{2πi qx x} along the row.
                double py = 2 * Math.PI * qy[i] * y[iy];
                double ar = cRe[i] * Math.Cos(py) - cIm[i] * Math.Sin(py);
                double ai = cRe[i] * Math.Sin(py) + cIm[i] * Math.Cos(py);
                for (int ix = 0; ix < nx; ix++)
                {
                    double px = 2 * Math.PI * qx[i] * x[ix];
                    double cos = Math.Cos(px), sin = Math.Sin(px);
                    re[ix] += ar * cos - ai * sin;
                    im[ix] += ar * sin + ai * cos;
                }
            }
            for (int ix = 0; ix < nx; ix++) intensity[iy, ix] = (re[ix] * re[ix] + im[ix] * im[ix]) / energy;
        });
        return new PsfResult(x.ToArray(), y.ToArray(), intensity, amplitude * amplitude / energy, energy / Math.PI, m);
    }

    /// <summary>
    /// The OTF at spatial frequency (<paramref name="nuX"/>, <paramref name="nuY"/>) in cycles/mm,
    /// sagittal and meridional, as the normalised autocorrelation of the pupil function: the
    /// outer integral over an n×n square grid of the pupil, each point's partner traced.
    /// </summary>
    public static OtfResult Otf(LensModel lens, int field, int wavelength, WavefrontOptions options, int grid,
                                double nuX, double nuY) =>
        new TracedPupil(lens, field, wavelength, options, grid).Otf(nuX, nuY);

    /// <summary>
    /// A pupil traced once on its grid, for the OTF at as many frequencies as wanted: the outer
    /// integral's rays, with their q, phase and ∂q/∂P, are the same at every frequency.
    /// </summary>
    internal sealed class TracedPupil
    {
        private readonly PupilMap _pupil;
        private readonly IReadOnlyList<PupilPoint> _nodes;
        private readonly PupilRay[] _rays;
        private readonly int[] _used;

        public TracedPupil(LensModel lens, int field, int wavelength, WavefrontOptions options, int grid)
        {
            _pupil = new PupilMap(lens, field, wavelength, options);
            _nodes = new Sampling.SquareGrid(grid).Points();
            _rays = _pupil.Trace(_nodes.Select(p => (p.Px, p.Py)).ToArray(), transmission: true, jacobian: true);
            _used = Enumerable.Range(0, _rays.Length).Where(k => _rays[k].Transmitted).ToArray();
            if (_used.Length == 0) throw new InvalidOperationException("no ray of the pupil transmits");
        }

        /// <summary>The transmitting rays' spatial frequencies, (qx, qy), cycles/mm.</summary>
        public IEnumerable<(double Qx, double Qy)> Frequencies => _used.Select(k => (_rays[k].Qx, _rays[k].Qy));

        public OtfResult Otf(double nuX, double nuY)
        {
            var (pupil, nodes, rays, used) = (_pupil, _nodes, _rays, _used);
            int m = used.Length;

            // Each point's partner: the pupil point whose q is q - ν. Iterated with the point's own
            // ∂q/∂P (a chord method), from the first-order guess.
            var target = new (double X, double Y)[m];
            var at = new (double Px, double Py)[m];
            var inverse = new (double A, double B, double C, double D)[m];
            for (int i = 0; i < m; i++)
            {
                var r = rays[used[i]];
                var p = nodes[used[i]];
                target[i] = (r.Qx - nuX, r.Qy - nuY);
                double det = r.Dqx_dPx * r.Dqy_dPy - r.Dqx_dPy * r.Dqy_dPx;
                inverse[i] = (r.Dqy_dPy / det, -r.Dqx_dPy / det, -r.Dqy_dPx / det, r.Dqx_dPx / det);
                at[i] = (p.Px - (inverse[i].A * nuX + inverse[i].B * nuY), p.Py - (inverse[i].C * nuX + inverse[i].D * nuY));
            }

            double scale = Math.Max(1.0, Math.Sqrt(nuX * nuX + nuY * nuY));
            var done = new bool[m];
            var lost = new bool[m];
            var active = Enumerable.Range(0, m).ToList();
            for (int iteration = 0; iteration < 40 && active.Count > 0; iteration++)
            {
                var traced = pupil.Trace(active.Select(i => at[i]).ToArray(), transmission: false, jacobian: false);
                var still = new List<int>();
                for (int k = 0; k < active.Count; k++)
                {
                    int i = active[k];
                    var t = traced[k];
                    if (!t.Arrived) { lost[i] = true; continue; }
                    double ex = target[i].X - t.Qx, ey = target[i].Y - t.Qy;
                    if (Math.Sqrt(ex * ex + ey * ey) < 1e-10 * scale) { done[i] = true; continue; }
                    var (a, b, c, d) = inverse[i];
                    at[i] = (at[i].Px + a * ex + b * ey, at[i].Py + c * ex + d * ey);
                    // Far outside the pupil the partner cannot transmit; stop iterating it.
                    if (at[i].Px * at[i].Px + at[i].Py * at[i].Py > 4.0) { lost[i] = true; continue; }
                    still.Add(i);
                }
                active = still;
            }
            int unsolved = active.Count;

            // The partners inside the unit pupil, traced through the apertures with their own J.
            var inside = Enumerable.Range(0, m)
                .Where(i => done[i] && at[i].Px * at[i].Px + at[i].Py * at[i].Py <= 1.0).ToArray();
            var partners = pupil.Trace(inside.Select(i => at[i]).ToArray(), transmission: true, jacobian: true);

            double re = 0.0, im = 0.0;
            int pairs = 0;
            for (int k = 0; k < inside.Length; k++)
            {
                var b = partners[k];
                if (!b.Transmitted) continue;
                var a = rays[used[inside[k]]];
                double weight = Math.Sqrt(a.Jacobian / b.Jacobian);
                double phase = 2 * Math.PI * (a.Phase - b.Phase);
                re += weight * Math.Cos(phase);
                im += weight * Math.Sin(phase);
                pairs++;
            }
            return new OtfResult(re / m, im / m, pairs, m, unsolved);
        }
    }

    /// <summary>One traced pupil point in the image's spatial frequencies.</summary>
    internal readonly record struct PupilRay(
        bool Arrived, bool Transmitted, double Qx, double Qy, double Qz, double Phase,
        double Dqx_dPx, double Dqx_dPy, double Dqy_dPx, double Dqy_dPy)
    {
        /// <summary>J = |∂q/∂(Px, Py)| (cycles/mm per pupil unit, squared).</summary>
        public double Jacobian => Math.Abs(Dqx_dPx * Dqy_dPy - Dqx_dPy * Dqy_dPx);
    }

    /// <summary>
    /// The map from pupil coordinates to the image's spatial frequencies and the phase, for one
    /// field and wavelength. Rays are traced without apertures for the map, which is smooth
    /// across a vignetting edge, and through them to decide whether a point transmits.
    /// </summary>
    internal sealed class PupilMap
    {
        private readonly LensModel _lens;
        private readonly int _field, _wavelength;
        private readonly WavefrontOptions _clipped, _free;

        public PupilMap(LensModel lens, int field, int wavelength, WavefrontOptions options)
        {
            if (options.ReferenceCenter is ReferenceCenter.Centroid or ReferenceCenter.BestFitSphere)
                throw new NotSupportedException("the reference centre must not depend on the rays traced: use the chief ray's, the Gaussian image or a given point");
            if (options.ChiefRay == ChiefRayDefinition.VignettedCenter)
                throw new NotSupportedException("the vignetted pupil's centre moves the pupil coordinates; use the stop's centre");
            if (options.RayAiming == RayAiming.ExitSphereGrid)
                throw new NotSupportedException("rays aimed onto the exit sphere's grid are not traced at the pupil points given");
            if (options.Weighting == Weighting.Quadrature)
                options = options with { Weighting = Weighting.PerRay };
            _lens = lens;
            _field = field;
            _wavelength = wavelength;
            _clipped = options;
            _free = options with { Apertures = ApertureClipping.None };
        }

        /// <summary>
        /// Traces the pupil points: q and the phase from rays traced without apertures, whether
        /// each transmits from the same rays traced through them, and ∂q/∂P from four differential
        /// rays each. A point outside the unit pupil never transmits: the stop is the pupil.
        /// </summary>
        public PupilRay[] Trace((double Px, double Py)[] points, bool transmission, bool jacobian)
        {
            var result = new PupilRay[points.Length];
            if (points.Length == 0) return result;
            const int chunk = 512;
            int chunks = (points.Length + chunk - 1) / chunk;
            Parallel.For(0, chunks, c =>
            {
                int start = c * chunk, count = Math.Min(chunk, points.Length - start);
                var part = points.Skip(start).Take(count).ToArray();
                var free = Values(part);
                bool[]? passes = transmission ? Passes(part) : null;
                (double Qx, double Qy)[]? xp = null, xm = null, yp = null, ym = null;
                if (jacobian)
                {
                    xp = Frequencies(part.Select(p => (p.Px + Step, p.Py)).ToArray());
                    xm = Frequencies(part.Select(p => (p.Px - Step, p.Py)).ToArray());
                    yp = Frequencies(part.Select(p => (p.Px, p.Py + Step)).ToArray());
                    ym = Frequencies(part.Select(p => (p.Px, p.Py - Step)).ToArray());
                }
                for (int k = 0; k < count; k++)
                {
                    var (arrived, qx, qy, qz, phase) = free[k];
                    bool inPupil = part[k].Px * part[k].Px + part[k].Py * part[k].Py <= 1.0;
                    double a = double.NaN, b = double.NaN, cc = double.NaN, d = double.NaN;
                    if (jacobian)
                    {
                        a = (xp![k].Qx - xm![k].Qx) / (2 * Step);
                        cc = (xp[k].Qy - xm[k].Qy) / (2 * Step);
                        b = (yp![k].Qx - ym![k].Qx) / (2 * Step);
                        d = (yp[k].Qy - ym[k].Qy) / (2 * Step);
                    }
                    bool transmitted = arrived && inPupil && (passes?[k] ?? false)
                                       && (!jacobian || !(double.IsNaN(a) || double.IsNaN(b) || double.IsNaN(cc) || double.IsNaN(d)));
                    result[start + k] = new PupilRay(arrived, transmitted, qx, qy, qz, phase, a, b, cc, d);
                }
            });
            return result;
        }

        private (bool Arrived, double Qx, double Qy, double Qz, double Phase)[] Values((double Px, double Py)[] points)
        {
            var r = WavefrontCalculator.Compute(_lens, _field, _wavelength, _free, Given(points));
            var g = r.Geometry;
            if (g.IsInfinite || g.IsAfocal || g.PathToImage)
                throw new NotSupportedException("the diffraction reference needs a reference sphere of finite radius about an image point");
            double perWave = (_free.Sign == WavefrontSign.Hopkins ? 1.0 : -1.0) * (_free.DivideByImageIndex ? g.ImageIndex : 1.0);
            double k = g.ImageIndex / (g.WavelengthUm * 1e-3);
            return r.Samples.Select(s =>
            {
                if (s.Vignetted) return (false, double.NaN, double.NaN, double.NaN, double.NaN);
                var u = g.Center - s.Sphere;
                double length = u.Length;
                // The optical path is the chief ray's less λ·W, so the phase in waves is -W.
                return (true, k * u.X / length, k * u.Y / length, k * u.Z / length, -perWave * s.W);
            }).ToArray();
        }

        private (double Qx, double Qy)[] Frequencies((double Px, double Py)[] points) =>
            Values(points).Select(v => (v.Qx, v.Qy)).ToArray();

        private bool[] Passes((double Px, double Py)[] points) =>
            WavefrontCalculator.Compute(_lens, _field, _wavelength, _clipped, Given(points)).Samples
                .Select(s => !s.Vignetted).ToArray();

        private static Sampling Given((double Px, double Py)[] points) =>
            new Sampling.Given(points.Select(p => new PupilPoint(p.Px, p.Py, 1.0)).ToList());
    }
}
