using AberrationCalculator.Core.RayTrace;

namespace WavefrontErrorCalculator.Core;

/// <summary>
/// The wavefront by Hopkins's surface-contribution formula (1952, eq. 7) and Tatian's focal shift
/// (1972, eq. 1): a third way to W, beside the optical-path sum (<see cref="WavefrontCalculator"/>)
/// and Rayces's integration (<see cref="RaycesIntegration"/>).
///
/// <para>Hopkins: for two rays of a pencil, the mid-point of their shortest join is an
/// <i>invariant focus</i> - the optical-path difference between the rays, measured to a sphere
/// centred there, is the same on every wavefront. The aberration Ω of a ray against the reference
/// (chief) ray, referred to that focus, changes at each surface by Δ(N e), with
/// e = Σ(λ + λ̄)(X − X̄) / (1 + Σλλ̄): (X, Y, Z) and (X̄, Ȳ, Z̄) the two rays' points of
/// incidence, λ and λ̄ their direction cosines, taken before the surface for N e and after it for
/// N′ e′. The sum over the surfaces is Ω in image space. Only the rays' intersections and
/// directions enter, no optical path, and the difference between the two rays is computed
/// directly rather than by subtracting two long paths.</para>
///
/// <para>Tatian: the focal shift from that focus to the chosen image point I is −N(QD − Q̄D̄), with
/// Q, Q̄ the feet of the perpendiculars from I to the two rays and D, D̄ the ends of their shortest
/// join (his eq. 1). The result is Hamilton's mixed characteristic referred to I, and it makes no
/// reference to an exit pupil. It is the W of a reference "sphere" of infinite radius: each ray
/// measured to the foot of the perpendicular from I, which is this program's
/// <see cref="ExitPupil.Infinite"/> and Zemax OpticStudio's Reference OPD "Infinity". The default
/// reference sphere through the exit pupil differs from it by terms that grow with the aberration
/// and as the exit pupil comes nearer the image (Tatian, p. 79).</para>
///
/// <para>Signs: W here is the chief ray's path minus the ray's, as everywhere in this program
/// (<see cref="WavefrontSign.Hopkins"/>); Tatian writes the focal shift for the opposite
/// difference, so it enters with his sign reversed. Written out with the shortest join
/// eliminated, his eq. 1 is N[e(P, P̄) + λ·(I − P) − λ̄·(I − P̄)] with P, P̄ any points of the two
/// rays, which stays exact for rays nearly parallel to the chief ray, where the shortest join runs
/// off to infinity.</para>
/// </summary>
public static class HopkinsTatian
{
    /// <summary>
    /// One pupil point. All in waves, signed by the options: <paramref name="Hopkins"/> the summed
    /// surface contributions (referred to the invariant focus), <paramref name="FocalShift"/>
    /// Tatian's shift to the image point, <paramref name="Total"/> their sum, <paramref name="ByPath"/>
    /// the optical-path W with the infinite reference, for comparison, and <paramref name="Join"/>
    /// the focal shift from the shortest join itself, where it is well conditioned (NaN otherwise).
    /// NaN where the ray does not arrive.
    /// </summary>
    public readonly record struct Point(double Px, double Py, double Hopkins, double FocalShift, double Total,
                                        double ByPath, double Join);

    /// <summary>
    /// W at the points of <paramref name="sampling"/> for field <paramref name="field"/> at
    /// wavelength <paramref name="wavelength"/>. The rays, the chief ray and the image point I are
    /// those of <paramref name="options"/> with the reference made infinite.
    /// </summary>
    public static IReadOnlyList<Point> Compute(LensModel lens, int field, int wavelength, WavefrontOptions options,
                                              Sampling sampling)
    {
        if (lens.System.Surfaces.Any(LocalFrame.IsPerturbed))
            throw new NotSupportedException("a tilted or decentred surface: the surface sums here assume the surfaces' frames are only shifted along the axis");
        if (WavefrontCalculator.IsAfocal(lens))
            throw new NotSupportedException("an afocal image: Tatian's focal shift needs a reference point near the lens (his note added in proof)");

        var o = options with
        {
            ExitPupil = ExitPupil.Infinite,
            ReferenceCenter = options.ReferenceCenter == ReferenceCenter.BestFitSphere ? ReferenceCenter.ChiefRay : options.ReferenceCenter,
            Defocus = DefocusMethod.Retrace,
        };
        var result = WavefrontCalculator.Compute(lens, field, wavelength, o, sampling);
        var g = result.Geometry;
        double f = result.FieldValue;
        var chief = Record(lens, f, wavelength, g.ChiefLaunchPy, g.ChiefLaunchPx)
                    ?? throw new InvalidOperationException("the chief ray does not reach the image");
        double scale = (o.Sign == WavefrontSign.Hopkins ? 1.0 : -1.0) / (g.WavelengthUm * 1e-3)
                       / (o.DivideByImageIndex ? g.ImageIndex : 1.0);
        double n = lens.ImageIndex(wavelength);
        var indices = lens.Indices(wavelength);

        var points = new Point[result.Samples.Count];
        for (int k = 0; k < points.Length; k++)
        {
            var s = result.Samples[k];
            var ray = s.Vignetted ? null : Record(lens, f, wavelength, s.LaunchPy, s.LaunchPx);
            if (ray is not Traced r)
            {
                points[k] = new Point(s.Px, s.Py, double.NaN, double.NaN, double.NaN, double.NaN, double.NaN);
                continue;
            }
            double omega = Contributions(lens, indices, r, chief);
            double shift = FocalShift(n, r.Image, r.Direction, chief.Image, chief.Direction, g.Center);
            double join = JoinShift(n, r.Image, r.Direction, chief.Image, chief.Direction, g.Center);
            points[k] = new Point(s.Px, s.Py, scale * omega, scale * shift, scale * (omega + shift), s.W, scale * join);
        }
        return points;
    }

    /// <summary>
    /// One pupil point under Hopkins's own focal shift (1952 §4), in waves signed by the options:
    /// <paramref name="Hopkins"/> the summed surface contributions, as in <see cref="Point"/>;
    /// <paramref name="Exact"/> Ω plus the shift taken exactly, the optical path along the ray from
    /// the reference sphere (centred on the image point I, through E) to the sphere about the
    /// invariant focus through the same E; <paramref name="Printed"/> Ω plus his eq. 13, which
    /// drops δ²; <paramref name="ByPath"/> the optical-path W on the same reference sphere. NaN where
    /// the ray does not arrive, or, for <paramref name="Exact"/>, where the ray is too nearly
    /// parallel to the chief ray for its invariant focus to be found.
    /// </summary>
    public readonly record struct Point1952(double Px, double Py, double Hopkins, double Exact, double Printed, double ByPath);

    /// <summary>
    /// W by Hopkins's surface contributions with HIS focal shift (1952 eq. 13) rather than
    /// Tatian's: referred to the reference sphere centred on the image point I that cuts the chief
    /// ray at E, "where the principal ray cuts the exit pupil". E and I are those of
    /// <paramref name="options"/> (its <see cref="WavefrontOptions.ExitPupil"/> and
    /// <see cref="WavefrontOptions.ReferenceCenter"/>), so the result is compared with the
    /// optical-path W on that same sphere; <see cref="ExitPupil.RealChief"/> is the real exit pupil.
    /// </summary>
    public static IReadOnlyList<Point1952> Compute1952(LensModel lens, int field, int wavelength, WavefrontOptions options,
                                                      Sampling sampling)
    {
        if (lens.System.Surfaces.Any(LocalFrame.IsPerturbed))
            throw new NotSupportedException("a tilted or decentred surface: the surface sums here assume the surfaces' frames are only shifted along the axis");
        if (WavefrontCalculator.IsAfocal(lens))
            throw new NotSupportedException("an afocal image");
        if (options.ExitPupil is ExitPupil.Infinite or ExitPupil.ImageSurface)
            throw new NotSupportedException("Hopkins's focal shift needs a reference sphere through the exit pupil");

        var o = options with
        {
            ReferenceCenter = options.ReferenceCenter == ReferenceCenter.BestFitSphere ? ReferenceCenter.ChiefRay : options.ReferenceCenter,
            Defocus = DefocusMethod.Retrace,
        };
        var result = WavefrontCalculator.Compute(lens, field, wavelength, o, sampling);
        var g = result.Geometry;
        double f = result.FieldValue;
        var chief = Record(lens, f, wavelength, g.ChiefLaunchPy, g.ChiefLaunchPx)
                    ?? throw new InvalidOperationException("the chief ray does not reach the image");
        double scale = (o.Sign == WavefrontSign.Hopkins ? 1.0 : -1.0) / (g.WavelengthUm * 1e-3)
                       / (o.DivideByImageIndex ? g.ImageIndex : 1.0);
        double n = lens.ImageIndex(wavelength);
        var indices = lens.Indices(wavelength);

        var points = new Point1952[result.Samples.Count];
        for (int k = 0; k < points.Length; k++)
        {
            var s = result.Samples[k];
            var ray = s.Vignetted ? null : Record(lens, f, wavelength, s.LaunchPy, s.LaunchPx);
            if (ray is not Traced r)
            {
                points[k] = new Point1952(s.Px, s.Py, double.NaN, double.NaN, double.NaN, double.NaN);
                continue;
            }
            double omega = Contributions(lens, indices, r, chief);
            double exact = ExactShift1952(n, r.Image, r.Direction, chief.Image, chief.Direction, g.Center, g.PupilPoint);
            double printed = PrintedShift1952(n, r.Image, r.Direction, chief.Image, chief.Direction, g.Center, g.PupilPoint);
            points[k] = new Point1952(s.Px, s.Py, scale * omega, scale * (omega + exact), scale * (omega + printed), s.W);
        }
        return points;
    }

    /// <summary>
    /// Hopkins's focal shift taken exactly, with this program's sign: N(t_M − t_I), t_M and t_I
    /// where the ray meets the sphere about its invariant focus M and the reference sphere about I,
    /// both through E, measured along the ray from P. Ω is referred to any sphere about M; on the
    /// one through E the chief ray's path is the reference sphere's, so the change of sphere is the
    /// ray's path between the two. NaN when M is beyond 10⁸ mm (rays all but parallel).
    /// </summary>
    private static double ExactShift1952(double n, Vec3 p, Vec3 l, Vec3 pb, Vec3 lb, Vec3 i, Vec3 e)
    {
        var w0 = p - pb;
        double b = l.Dot(lb), d = l.Dot(w0), ee = lb.Dot(w0), den = 1.0 - b * b;
        if (den < 1e-16) return double.NaN;
        var dj = p + ((b * ee - d) / den) * l;
        var djb = pb + ((ee - b * d) / den) * lb;
        var m = 0.5 * (dj + djb);
        if ((m - e).Length > 1e8) return double.NaN;
        double? tM = Meet(p, l, m, (e - m).Length, e), tI = Meet(p, l, i, (e - i).Length, e);
        return tM is double a && tI is double c ? n * (a - c) : double.NaN;
    }

    /// <summary>Where the line P + t l meets the sphere about C of radius R, the root nearer E.</summary>
    private static double? Meet(Vec3 p, Vec3 l, Vec3 c, double radius, Vec3 e)
    {
        var delta = p - c;
        double b = l.Dot(delta), cc = delta.Dot(delta) - radius * radius, disc = b * b - cc;
        if (disc < 0.0) return null;
        double q = -(b + (b >= 0.0 ? Math.Sqrt(disc) : -Math.Sqrt(disc)));
        double t1 = q, t2 = q != 0.0 ? cc / q : -b;
        return ((p + t1 * l) - e).Length <= ((p + t2 * l) - e).Length ? t1 : t2;
    }

    /// <summary>
    /// Hopkins's eq. 13 as printed, which drops δ², with this program's sign. In his notation, with
    /// P̄E = p̄ and P̄Q = q along the chief ray (Q its point on the image, here I), e his eq. 6 and
    /// cos U = Σλλ̄: δW′ = N[Σ(X − X̄ − eλ)² − 2(q + p̄)Σλ̄(X − X̄ − eλ) + 2qp̄(1 − cos U)]
    /// / 2[Σλ(X − X̄) + (p̄ − e) − q cos U]. Written for any points P, P̄ of the two rays; his X axis
    /// along the lens's is not needed.
    /// </summary>
    private static double PrintedShift1952(double n, Vec3 p, Vec3 l, Vec3 pb, Vec3 lb, Vec3 i, Vec3 e)
    {
        double cosU = l.Dot(lb);
        double eh = (l + lb).Dot(p - pb) / (1.0 + cosU);
        double pBar = (e - pb).Dot(lb), q = (i - pb).Dot(lb);
        var a = (p - pb) - eh * l;
        double num = a.Dot(a) - 2.0 * (q + pBar) * lb.Dot(a) + 2.0 * q * pBar * (1.0 - cosU);
        double den = 2.0 * (l.Dot(p - pb) + (pBar - eh) - q * cosU);
        // His δ (EM along the ray, the sphere about the invariant focus to the one about I) is
        // −num/den, and δW′ = −Nδ; with Ω referred to the first sphere and W to the second, that is
        // the shift in this program's sign as well.
        return den == 0.0 ? double.NaN : n * num / den;
    }

    /// <summary>A ray's points of incidence and directions at every surface, and where it starts.</summary>
    private sealed record Traced(RealRayTrace.SurfaceHit[] Hits, Vec3 Launch, Vec3 Image, Vec3 Direction);

    private static Traced? Record(LensModel lens, double field, int w, double launchPy, double launchPx)
    {
        RealRayTrace.SurfaceHit[] hits;
        try
        {
            hits = RealRayTrace.TraceRecord(lens.System, lens.Indices(w), lens.Paraxial, field, launchPy, launchPx,
                                            atParaxialFocus: false);
        }
        catch (InvalidOperationException) { return null; }
        var end = hits[^1];
        if (!end.Ok) return null;
        var (_, _, dx, dy, dz) = RealRayTrace.LaunchRay(lens.System, lens.Paraxial, field, launchPy, launchPx);
        double len = Math.Sqrt(dx * dx + dy * dy + dz * dz);
        return new Traced(hits, new Vec3(dx / len, dy / len, dz / len), new Vec3(end.X, end.Y, end.Z), new Vec3(end.L, end.M, end.N));
    }

    /// <summary>
    /// Hopkins's eq. 7 summed over the surfaces: Ω in image space, the chief ray's path minus the
    /// ray's, referred to their invariant focus. Zero in object space, where both rays leave the
    /// same object point or, from infinity, the same plane wavefront.
    /// </summary>
    private static double Contributions(LensModel lens, double[] indices, Traced ray, Traced chief)
    {
        int last = lens.System.LastOpticalSurface();
        Vec3 dIn = ray.Launch, dInRef = chief.Launch;
        double sum = 0.0;
        for (int i = 1; i <= last; i++)
        {
            var h = ray.Hits[i];
            var hb = chief.Hits[i];
            // The incident directions are the previous surface's emergent ones; the frames differ
            // only by a shift along the axis, so the direction vectors carry over unchanged.
            var x = new Vec3(h.X, h.Y, h.Z);
            var xb = new Vec3(hb.X, hb.Y, hb.Z);
            var dOut = new Vec3(h.L, h.M, h.N);
            var dOutRef = new Vec3(hb.L, hb.M, hb.N);
            // Unsigned indices with the directions the light actually travels: N e is then the
            // optical path from the point of incidence to the shortest join along each ray.
            double nIn = Math.Abs(indices[i - 1]), nOut = Math.Abs(indices[i]);
            sum += nOut * E(dOut, dOutRef, x, xb) - nIn * E(dIn, dInRef, x, xb);
            dIn = dOut;
            dInRef = dOutRef;
        }
        // e = p̄ - p, the reference ray's distance to its end of the shortest join less the ray's,
        // so N e is (chief - ray) and the sum is Ω with this program's sign.
        return sum;
    }

    /// <summary>Hopkins's e: Σ(λ + λ̄)(X − X̄) / (1 + Σλλ̄) for points X, X̄ on the two rays.</summary>
    private static double E(Vec3 l, Vec3 lb, Vec3 x, Vec3 xb) => (l + lb).Dot(x - xb) / (1.0 + l.Dot(lb));

    /// <summary>
    /// Tatian's eq. 1, from the invariant focus to the image point I, with this program's sign:
    /// N[ΣX*(λ − λ̄) − Σλ X + Σλ̄ X̄ + Σ(λ + λ̄)(X − X̄)/(1 + Σλλ̄)] for points X, X̄ of the two rays.
    /// Tatian prints it, for the opposite difference, as
    /// −N{ΣX*(λ̄ − λ) + [Σλλ̄(ΣλX − Σλ̄X̄) + ΣλX̄ − Σλ̄X] / (1 + Σλλ̄)}; the two are the same expression.
    /// </summary>
    private static double FocalShift(double n, Vec3 p, Vec3 l, Vec3 pb, Vec3 lb, Vec3 i)
    {
        double b = l.Dot(lb);
        double tatian = -n * (i.Dot(lb - l) + (b * (l.Dot(p) - lb.Dot(pb)) + l.Dot(pb) - lb.Dot(p)) / (1.0 + b));
        return -tatian;
    }

    /// <summary>
    /// The same focal shift from the geometry itself: the ends D, D̄ of the shortest join and the
    /// feet Q, Q̄ of the perpendiculars from I, N(QD − Q̄D̄). A check on the closed form; NaN where
    /// the rays are so nearly parallel that the join is ill conditioned.
    /// </summary>
    private static double JoinShift(double n, Vec3 p, Vec3 l, Vec3 pb, Vec3 lb, Vec3 i)
    {
        var w0 = p - pb;
        double b = l.Dot(lb), d = l.Dot(w0), e = lb.Dot(w0), den = 1.0 - b * b;
        if (den < 1e-10) return double.NaN;
        double s = (b * e - d) / den, t = (e - b * d) / den;       // D = P + s λ, D̄ = P̄ + t λ̄
        double q = l.Dot(i - p), qb = lb.Dot(i - pb);              // Q = P + q λ, Q̄ = P̄ + q̄ λ̄
        return n * ((s - q) - (t - qb));
    }
}
