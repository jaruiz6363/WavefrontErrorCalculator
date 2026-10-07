namespace WavefrontErrorCalculator.Core;

/// <summary>
/// The wavefront aberration of one field at one wavelength, from real rays (method.md §4).
///
/// <para>W′ = [E P₁ … P_k E′] - [B P₁ … P_k B′]: the chief ray's optical path minus the ray's,
/// each measured from the entrance reference surface to the exit reference sphere (Hopkins 1981
/// eq. 3.1; Welford eq. 7.9). The paths are summed directly in double precision, which for a lens
/// a metre long leaves a rounding floor near 1e-13 mm (method.md §4.2).</para>
///
/// <para>The steps: find the part of the pupil the pencil fills, if asked (method.md §5.4); trace
/// the chief ray and the samples to the image plane, which no convention touches; fix the
/// reference spheres, after the rays because one way of fixing them - the centroid - needs them;
/// carry each ray from the image plane back or on to the exit reference sphere and give it its W.</para>
/// </summary>
public static class WavefrontCalculator
{
    /// <summary>The wavefront of field <paramref name="field"/> at wavelength <paramref name="wavelength"/>, both indices into the lens's lists.</summary>
    public static WavefrontResult Compute(LensModel lens, int field, int wavelength, WavefrontOptions options,
                                          Sampling sampling)
    {
        if (lens == null) throw new ArgumentNullException(nameof(lens));
        if (options == null) throw new ArgumentNullException(nameof(options));
        if (sampling == null) throw new ArgumentNullException(nameof(sampling));
        if (field < 0 || field >= lens.System.Fields.Count) throw new ArgumentOutOfRangeException(nameof(field));
        if (wavelength < 0 || wavelength >= lens.System.Wavelengths.Count) throw new ArgumentOutOfRangeException(nameof(wavelength));

        var warnings = new List<string>();
        Validate(options, sampling, warnings);
        double f = lens.System.Fields[field].Y;
        // A shift of the image plane is traced only when it is to be re-traced; the terms that
        // stand in for it are added to the wavefront at the file's own plane.
        double shift = options.Defocus == DefocusMethod.Retrace ? options.FocusShift : 0.0;

        var pupil = options.ChiefRay == ChiefRayDefinition.VignettedCenter
            ? Explore(lens, f, options, warnings)
            : PupilDomain.Whole;

        var chief = TraceAt(lens, f, wavelength, ChiefOptions(options), 0.0, pupil.CenterPy, shift, clip: true, warnings);
        if (!chief.Ok) throw new InvalidOperationException($"the chief ray of field {field} does not reach the image");

        var points = sampling.Points();
        var at = points.Select(p => pupil.Map(p.Px, p.Py)).ToArray();
        var rays = at.Select(q => TraceAt(lens, f, wavelength, options, q.Px, q.Py, shift, clip: true, warnings)).ToArray();

        bool afocal = IsAfocal(lens);
        if (afocal && options.ReferenceCenter != ReferenceCenter.ChiefRay)
            warnings.Add("an afocal image is referred to the plane across the chief ray, whatever the reference centre");
        var geometry = Geometry(lens, f, wavelength, options, pupil, chief, rays, shift, afocal, null, warnings);
        geometry = Canonical(lens, f, wavelength, options, pupil, geometry, shift, warnings);

        if (options.RayAiming == RayAiming.ExitSphereGrid)
            (rays, at) = AimOntoExitGrid(lens, f, wavelength, options, pupil, points, geometry, shift, warnings);

        var differential = DifferentialRays(lens, f, wavelength, options, pupil, points, rays, sampling, shift, warnings);
        var samples = Samples(points, at, rays, chief, geometry, options, sampling, differential);

        if (options.ReferenceCenter == ReferenceCenter.BestFitSphere && !afocal)
        {
            // Rimmer (1970): the centre that leaves the least variance. W changes with the centre
            // by (n′/R′)(B′ - E′)·δ to first order (Welford eq. 7.18), so a weighted least-squares
            // fit of that, with a piston, gives the step; each step is then taken exactly, by
            // re-referring every ray, and repeated until the centre stops moving.
            for (int iteration = 0; iteration < 20; iteration++)
            {
                var step = BestFitStep(samples, geometry, ScaleOf(options, geometry));
                if (step is not Vec3 delta) break;
                geometry = Geometry(lens, f, wavelength, options, pupil, chief, rays, shift, afocal,
                                    geometry.Center + delta, warnings) with { HPrimeS = geometry.HPrimeS, HPrimeT = geometry.HPrimeT };
                samples = Samples(points, at, rays, chief, geometry, options, sampling, differential);
                if (delta.Length < 1e-10) break;
            }
        }

        return new WavefrontResult(field, f, wavelength, options, geometry, pupil, samples,
                                   Statistics(samples, options), warnings.Distinct().ToList());
    }

    private static double ScaleOf(WavefrontOptions o, ReferenceGeometry g) =>
        (o.Sign == WavefrontSign.Hopkins ? 1.0 : -1.0) / (g.WavelengthUm * 1e-3) / (o.DivideByImageIndex ? g.ImageIndex : 1.0);

    /// <summary>Gives every traced ray its W, its coordinates and its weight against one set of reference spheres.</summary>
    private static WavefrontSample[] Samples(IReadOnlyList<PupilPoint> points, (double Px, double Py)[] at, TracedRay[] rays,
                                             TracedRay chief, ReferenceGeometry geometry, WavefrontOptions options,
                                             Sampling sampling, TracedRay[]? differential)
    {
        double scale = ScaleOf(options, geometry);
        double h = geometry.HPrime;
        var focusTerm = FocusTerm(options, geometry);
        double chiefTerm = focusTerm(ToReference(chief, geometry)!.Value.Sphere);

        var samples = new WavefrontSample[points.Count];
        for (int k = 0; k < points.Count; k++)
        {
            var p = points[k];
            var (px, py) = at[k];
            var r = rays[k];
            double weight = options.Weighting == Weighting.Quadrature ? p.Area : 1.0;
            if (r.Ok && ToReference(r, geometry) is (Vec3 sphere, double path))
            {
                if (differential != null) weight = p.Area * ExitJacobian(differential, k, sphere, geometry);
                var e = sphere - geometry.PupilPoint;
                double w = scale * (geometry.ChiefPath - path + focusTerm(sphere) - chiefTerm);
                samples[k] = new WavefrontSample(p.Px, p.Py, px, py, r.LaunchPx, r.LaunchPy, sphere,
                                                 e.X / h, e.Y / h, e.Z / h, e.X / geometry.HPrimeS, e.Y / geometry.HPrimeT,
                                                 r.ImagePoint, r.Direction, path, w, weight, false);
            }
            else
            {
                samples[k] = new WavefrontSample(p.Px, p.Py, px, py, r.LaunchPx, r.LaunchPy, default,
                                                 double.NaN, double.NaN, double.NaN, double.NaN, double.NaN,
                                                 r.ImagePoint, r.Direction, double.NaN, double.NaN, 0.0, true, r.StoppedAt);
            }
        }

        if (options.Weighting == Weighting.ExitArea && sampling is Sampling.SquareGrid)
            AreaWeights(points, samples);
        return samples;
    }

    /// <summary>The launch step of the differential rays that give quadrature nodes their exit area.</summary>
    private const double DifferentialStep = 1e-5;

    /// <summary>
    /// For <see cref="Weighting.ExitArea"/> on Gauss quadrature: four rays a small step either side
    /// of each node in Px and Py, whose crossings of the reference sphere give the Jacobian
    /// ∂(x′,y′)/∂(Px,Py) by central differences. The nodes then integrate over the exit pupil's
    /// own area, as the square grid's finite differences do, but to the quadrature's accuracy.
    /// Null when the weights need no rays: other weightings or samplings, or rays aimed onto the
    /// exit sphere's grid, whose nodes already lie uniformly on it.
    /// </summary>
    private static TracedRay[]? DifferentialRays(LensModel lens, double f, int wavelength, WavefrontOptions options,
                                                 PupilDomain pupil, IReadOnlyList<PupilPoint> points, TracedRay[] rays,
                                                 Sampling sampling, double shift, List<string> warnings)
    {
        if (options.Weighting != Weighting.ExitArea || sampling is not Sampling.GaussQuadrature
            || options.RayAiming == RayAiming.ExitSphereGrid) return null;
        const double h = DifferentialStep;
        var differential = new TracedRay[4 * points.Count];
        for (int k = 0; k < points.Count; k++)
        {
            if (!rays[k].Ok) continue;
            var p = points[k];
            (double Px, double Py)[] around = { (p.Px + h, p.Py), (p.Px - h, p.Py), (p.Px, p.Py + h), (p.Px, p.Py - h) };
            for (int d = 0; d < 4; d++)
            {
                var (px, py) = pupil.Map(around[d].Px, around[d].Py);
                differential[4 * k + d] = TraceAt(lens, f, wavelength, options, px, py, shift, clip: true, warnings);
            }
        }
        return differential;
    }

    /// <summary>|∂(x′,y′)/∂(Px,Py)| at node k from its differential rays; one-sided where one is vignetted.</summary>
    private static double ExitJacobian(TracedRay[] differential, int k, Vec3 center, ReferenceGeometry geometry)
    {
        const double h = DifferentialStep;
        Vec3? At(int d) => differential[4 * k + d] is { Ok: true } r && ToReference(r, geometry) is (Vec3 s, _) ? s : null;
        Vec3 Derivative(Vec3? plus, Vec3? minus) => (plus, minus) switch
        {
            (Vec3 a, Vec3 b) => 1.0 / (2 * h) * (a - b),
            (Vec3 a, null) => 1.0 / h * (a - center),
            (null, Vec3 b) => 1.0 / h * (center - b),
            _ => new Vec3(double.NaN, double.NaN, double.NaN),
        };
        var dx = Derivative(At(0), At(1));
        var dy = Derivative(At(2), At(3));
        return Math.Abs(dx.X * dy.Y - dy.X * dx.Y);
    }

    /// <summary>
    /// One step of the best-fit centre: the δ that, with a piston, minimises the weighted
    /// variance of W + (n′/R′)(B′ - E′)·δ, scaled to W's units. Null when there is too little to fit.
    /// </summary>
    private static Vec3? BestFitStep(WavefrontSample[] samples, ReferenceGeometry g, double scale)
    {
        var used = samples.Where(s => !s.Vignetted && s.Weight > 0.0).ToList();
        if (used.Count < 5) return null;
        double total = used.Sum(s => s.Weight);
        double k = scale * g.ImageIndex / g.Radius;
        var a = new double[used.Count, 4];
        var b = new double[used.Count];
        for (int i = 0; i < used.Count; i++)
        {
            var s = used[i];
            var e = s.Sphere - g.PupilPoint;
            double root = Math.Sqrt(s.Weight / total);
            a[i, 0] = root;
            a[i, 1] = root * k * e.X;
            a[i, 2] = root * k * e.Y;
            a[i, 3] = root * k * e.Z;
            b[i] = -root * s.W;
        }
        var c = Numerics.LeastSquares(a, b);
        return new Vec3(c[1], c[2], c[3]);
    }

    /// <summary>
    /// Aims every sample onto its point of the exit sphere's canonical grid (method.md §5.3): the
    /// unit-disk coordinates (u, v) of the sampling become targets for the canonical coordinates
    /// (x′_S, y′_T), and the ray's pupil coordinates are corrected by the miss until it lands there
    /// - Singh's (1976 §4) iterative ray-tracing, which makes W a function of the exit pupil's own
    /// coordinates rather than the entrance's.
    /// </summary>
    private static (TracedRay[] Rays, (double Px, double Py)[] At) AimOntoExitGrid(
        LensModel lens, double f, int w, WavefrontOptions o, PupilDomain pupil, IReadOnlyList<PupilPoint> points,
        ReferenceGeometry g, double shift, List<string> warnings)
    {
        if (double.IsNaN(g.HPrimeS) || double.IsNaN(g.HPrimeT) || g.HPrimeS == 0.0 || g.HPrimeT == 0.0)
            throw new InvalidOperationException("the canonical coordinates could not be found, so the rays cannot be aimed onto them");
        var rays = new TracedRay[points.Count];
        var at = new (double Px, double Py)[points.Count];
        for (int k = 0; k < points.Count; k++)
        {
            double u = points[k].Px, v = points[k].Py;
            var (px, py) = pupil.Map(u, v);
            bool landed = false;
            for (int iteration = 0; iteration < 30; iteration++)
            {
                var r = TraceAt(lens, f, w, o, px, py, shift, clip: false, warnings);
                if (!r.Ok || ToReference(r, g) is not (Vec3 sphere, _)) break;
                var e = sphere - g.PupilPoint;
                double du = u - e.X / g.HPrimeS, dv = v - e.Y / g.HPrimeT;
                if (Math.Abs(du) < 1e-10 && Math.Abs(dv) < 1e-10) { landed = true; break; }
                px += pupil.SemiX * du;
                py += pupil.SemiY * dv;
            }
            at[k] = (px, py);
            rays[k] = landed ? TraceAt(lens, f, w, o, px, py, shift, clip: true, warnings) : default;
        }
        return (rays, at);
    }

    /// <summary>
    /// Whether the image is at infinity: the file says so, or the paraxial image lies beyond 10¹⁰
    /// mm. Not whether the lens has power - a plate has none, and images a near object nearby.
    /// </summary>
    public static bool IsAfocal(LensModel lens)
    {
        if (lens.System.IsAfocal) return true;
        double distance = Math.Abs(lens.Paraxial.ParaxialFocusDistance);
        return double.IsInfinity(distance) || double.IsNaN(distance) || distance > 1e10;
    }

    /// <summary>An OPD fan: <see cref="Compute"/> along one line across the pupil.</summary>
    public static WavefrontResult Fan(LensModel lens, int field, int wavelength, WavefrontOptions options,
                                      FanDirection direction, int count = 101) =>
        Compute(lens, field, wavelength, options, new Sampling.Fan(direction, count));

    private static void Validate(WavefrontOptions o, Sampling sampling, List<string> warnings)
    {
        if (o.Weighting == Weighting.Quadrature && sampling is not Sampling.GaussQuadrature)
            throw new ArgumentException("Weighting.Quadrature needs Gauss quadrature sampling");
        if (o.ReferenceCenter == ReferenceCenter.BestFitSphere && o.ExitPupil == ExitPupil.Infinite)
            throw new NotSupportedException("the best-fit sphere needs a finite radius");
        if (o.ChromaticReference == ChromaticReference.Conrady)
            throw new NotSupportedException("ChromaticReference.Conrady arrives in phase 5 (method.md §14)");
        if (o.ReferenceCenter == ReferenceCenter.User && o.UserCenter == null)
            throw new ArgumentException("ReferenceCenter.User needs UserCenter");
        if (o.ExitPupil == ExitPupil.UserRadius && o.UserReferenceRadius == null)
            throw new ArgumentException("ExitPupil.UserRadius needs UserReferenceRadius");
        if (o.ExitPupil == ExitPupil.UserDistance && o.UserExitPupilDistance == null)
            throw new ArgumentException("ExitPupil.UserDistance needs UserExitPupilDistance");
        if (o.Defocus != DefocusMethod.Retrace && o.FocusShift != 0.0 && o.ExitPupil == ExitPupil.Infinite)
            throw new NotSupportedException("a focus term needs a reference sphere of finite radius; re-trace instead");
        if (o.Weighting == Weighting.ExitArea && sampling is Sampling.Hexapolar)
            warnings.Add("exit-area weights need a square grid or Gauss quadrature; these samples are weighted equally");
    }

    /// <summary>The options the chief ray is traced under: aimed at the real stop for <see cref="ChiefRayDefinition.RealStopCenter"/>.</summary>
    private static WavefrontOptions ChiefOptions(WavefrontOptions o) =>
        o.ChiefRay == ChiefRayDefinition.RealStopCenter && o.RayAiming is RayAiming.Paraxial or RayAiming.Aplanatic or RayAiming.AplanaticReference or RayAiming.ParaxialReference
            ? o with { RayAiming = RayAiming.RealStop }
            : o;

    /// <summary>
    /// Traces the ray through pupil point (<paramref name="px"/>, <paramref name="py"/>): aimed at
    /// that point of the stop, or launched at that point of the paraxial entrance pupil.
    /// </summary>
    private static TracedRay TraceAt(LensModel lens, double f, int w, WavefrontOptions o, double px, double py,
                                     double shift, bool clip, List<string> warnings)
    {
        var clipping = clip ? o.Apertures : ApertureClipping.None;
        // A virtual entrance pupil behind the object has a negative paraxial diameter: the
        // paraxial marginal ray, which (Px, Py) = (0, 1) follows by default, crosses the pupil
        // plane below the axis. Taken as a point of that plane instead, the labels change sign.
        double s = o.PupilOrientation == PupilOrientation.EntrancePupilPlane && lens.Paraxial.Epd < 0.0 ? -1.0 : 1.0;
        px *= s;
        py *= s;
        if (o.RayAiming is RayAiming.Aplanatic or RayAiming.AplanaticReference or RayAiming.ParaxialReference)
        {
            var aplanatic = lens.AplanaticLaunch(f, w, py, px, o.RayAiming != RayAiming.Aplanatic,
                                                 plane: o.RayAiming == RayAiming.ParaxialReference);
            return aplanatic is (double ay, double ax) ? lens.Trace(f, w, ay, ax, clipping, shift) : default;
        }
        // Aiming onto the exit grid adjusts the pupil coordinates; the rays it adjusts are aimed at the stop.
        if (o.RayAiming is RayAiming.RealStop or RayAiming.ExitSphereGrid)
        {
            var aimer = lens.Aimer(w);
            if (aimer != null && aimer.CanAim(f))
            {
                var launch = aimer.Launch(f, py, px);
                return launch is (double ly, double lx) ? lens.Trace(f, w, ly, lx, clipping, shift) : default;
            }
            warnings.Add("the rays could not be aimed at the stop and were launched at the paraxial entrance pupil");
        }
        return lens.Trace(f, w, py, px, clipping, shift);
    }

    /// <summary>
    /// The part of the pupil the pencil of field <paramref name="f"/> fills (Hopkins 1981 §8;
    /// Hopkins &amp; Yzuel 1970 §4): the meridional limits by bisection along the meridian, then
    /// the sagittal limit by bisection across the pencil's centre, each to a millionth of the
    /// pupil. Explored at the primary wavelength, so that a pupil coordinate names the same rays
    /// of the beam at every wavelength.
    /// </summary>
    public static PupilDomain Explore(LensModel lens, double f, WavefrontOptions o, List<string>? warnings = null)
    {
        warnings ??= new List<string>();
        int w = lens.PrimaryWavelength;
        bool Passes(double px, double py) => TraceAt(lens, f, w, o, px, py, 0.0, clip: true, warnings).Ok;

        double Limit(double inside, double outside, Func<double, bool> passes)
        {
            if (passes(outside)) return outside;
            for (int k = 0; k < 40 && Math.Abs(outside - inside) > 1e-6; k++)
            {
                double mid = 0.5 * (inside + outside);
                if (passes(mid)) inside = mid; else outside = mid;
            }
            return inside;
        }

        double start = 0.0;
        if (!Passes(0.0, 0.0))
        {
            var found = Enumerable.Range(0, 41).Select(k => -1.0 + k / 20.0).Where(v => Passes(0.0, v)).ToList();
            if (found.Count == 0) throw new InvalidOperationException("no ray of this field passes the lens");
            start = found[found.Count / 2];
        }
        double upper = Limit(start, 1.0, v => Passes(0.0, v));
        double lower = Limit(start, -1.0, v => Passes(0.0, v));
        double center = 0.5 * (upper + lower);
        double side = Limit(0.0, 1.0, u => Passes(u, center));
        return new PupilDomain(center, side, 0.5 * (upper - lower), upper, lower);
    }

    /// <summary>The reference spheres (method.md §5.1, §5.2, §5.10).</summary>
    /// <param name="afocal">
    /// The image is at infinity: the exit reference is then the plane through E′ across the chief
    /// ray (method.md §4.3), a sphere of infinite radius centred on the image point at infinity.
    /// </param>
    /// <param name="centerOverride">A reference focus to use instead of the one the options name, for the best-fit iteration.</param>
    private static ReferenceGeometry Geometry(LensModel lens, double f, int w, WavefrontOptions o, PupilDomain pupil,
                                              TracedRay chief, TracedRay[] rays, double shift, bool afocal,
                                              Vec3? centerOverride, List<string> warnings)
    {
        int primary = lens.PrimaryWavelength;
        bool toPrimary = w != primary && o.ChromaticReference is ChromaticReference.PrimaryFocus or ChromaticReference.PrimarySphere;
        var primaryChief = toPrimary
            ? TraceAt(lens, f, primary, ChiefOptions(o), 0.0, pupil.CenterPy, shift, clip: false, warnings)
            : chief;
        if (!primaryChief.Ok) throw new InvalidOperationException("the primary wavelength's chief ray does not reach the image");

        double zXp = ParaxialExitPupilZ(lens);

        // E′, first for the ray that sets the pupil plane - the primary's chief when the
        // wavelengths share a focus - then, under PrimaryFocus, moved onto this wavelength's own
        // chief ray. PrimarySphere leaves it on the primary's: one sphere for every wavelength.
        Vec3 Pupil(TracedRay c)
        {
            switch (o.ExitPupil)
            {
                case ExitPupil.ParaxialAxial:
                case ExitPupil.Infinite:
                    return new Vec3(0.0, 0.0, zXp);
                case ExitPupil.ParaxialChiefIntersect:
                case ExitPupil.ImageSurface:
                case ExitPupil.ParaxialDistance:
                case ExitPupil.UserRadius:
                case ExitPupil.LastSurface:
                    return AtZ(c, zXp);
                case ExitPupil.UserDistance:
                    return new Vec3(0.0, 0.0, -o.UserExitPupilDistance!.Value * Math.Sign(c.Direction.Z));
                default:
                    return AxisPoint(c) ?? new Vec3(0.0, 0.0, zXp);
            }
        }
        var pupilPoint = Pupil(primaryChief);
        if (toPrimary && o.ChromaticReference == ChromaticReference.PrimaryFocus
            && o.ExitPupil is ExitPupil.RealChief or ExitPupil.ParaxialChiefIntersect or ExitPupil.ImageSurface)
            pupilPoint = AtZ(chief, pupilPoint.Z);

        Vec3 center = afocal ? primaryChief.ImagePoint : centerOverride ?? o.ReferenceCenter switch
        {
            ReferenceCenter.ChiefRay or ReferenceCenter.BestFitSphere => primaryChief.ImagePoint,
            ReferenceCenter.Gaussian => GaussianImage(lens, f, toPrimary ? primary : w, shift),
            ReferenceCenter.Centroid => Centroid(rays),
            ReferenceCenter.User => new Vec3(o.UserCenter!.Value.X, o.UserCenter.Value.Y, shift),
            _ => throw new NotSupportedException(o.ReferenceCenter.ToString()),
        };

        if (!afocal && o.ExitPupil == ExitPupil.ParaxialDistance
            && OnSphere(o.ChromaticReference == ChromaticReference.PrimarySphere ? primaryChief : chief, center, Math.Abs(zXp - center.Z), zXp) is Vec3 onSphere)
            pupilPoint = onSphere;
        if (!afocal && o.ExitPupil == ExitPupil.UserRadius
            && OnSphere(o.ChromaticReference == ChromaticReference.PrimarySphere ? primaryChief : chief, center, o.UserReferenceRadius!.Value, zXp) is Vec3 onUser)
            pupilPoint = onUser;
        if (!afocal && o.ExitPupil == ExitPupil.LastSurface
            && lens.LastSurfaceHit(f, o.ChromaticReference == ChromaticReference.PrimarySphere ? primary : w,
                                   o.ChromaticReference == ChromaticReference.PrimarySphere ? primaryChief : chief) is Vec3 onLast)
            pupilPoint = onLast;
        double radius = afocal || o.ExitPupil == ExitPupil.Infinite ? double.PositiveInfinity : (pupilPoint - center).Length;
        if (!afocal && (double.IsNaN(radius) || double.IsInfinity(zXp) && o.ExitPupil == ExitPupil.ParaxialAxial))
        {
            warnings.Add("the exit pupil is at infinity; the reference is the plane across the chief ray");
            radius = double.PositiveInfinity;
        }
        if (afocal && (double.IsNaN(pupilPoint.Z) || double.IsInfinity(pupilPoint.Z)))
            pupilPoint = primaryChief.ImagePoint;

        double hPrime = 0.5 * lens.Paraxial.ExitPupilDiameter;
        if (!(hPrime > 0.0) || double.IsInfinity(hPrime)) hPrime = double.NaN;

        var partial = new ReferenceGeometry(center, pupilPoint, radius, lens.ImageIndex(w), lens.WavelengthUm(w), hPrime,
                                            chief.ImagePoint, chief.Direction, chief.LaunchPx, chief.LaunchPy, 0.0)
        {
            PlaneNormal = afocal ? primaryChief.Direction : null,
            PathToImage = o.ExitPupil == ExitPupil.ImageSurface,
        };
        if (ToReference(chief, partial) is not (_, double chiefPath))
            throw new InvalidOperationException("the chief ray does not meet its own reference sphere");
        return partial with { ChiefPath = chiefPath };
    }

    /// <summary>
    /// h′_S and h′_T: how far across the exit reference sphere the rim of the pupil domain
    /// reaches, to first order about the chief ray - the transverse displacement on the sphere
    /// per unit of the unit-disk coordinates, from rays a whisker either side of the chief ray.
    /// Dividing by them makes the canonical coordinates, in which the pupil is a unit circle
    /// (Hopkins 1964; Hopkins &amp; Yzuel 1970 eq. 14; Macdonald 1971 §4).
    /// </summary>
    private static ReferenceGeometry Canonical(LensModel lens, double f, int w, WavefrontOptions o, PupilDomain pupil,
                                               ReferenceGeometry g, double shift, List<string> warnings)
    {
        const double d = 1e-3;
        Vec3? At(double u, double v)
        {
            var (px, py) = pupil.Map(u, v);
            var r = TraceAt(lens, f, w, o, px, py, shift, clip: false, warnings);
            return r.Ok && ToReference(r, g) is (Vec3 s, _) ? s : null;
        }
        if (At(d, 0.0) is not Vec3 xp || At(-d, 0.0) is not Vec3 xm || At(0.0, d) is not Vec3 yp || At(0.0, -d) is not Vec3 ym)
            return g;
        return g with { HPrimeS = (xp.X - xm.X) / (2 * d), HPrimeT = (yp.Y - ym.Y) / (2 * d) };
    }

    /// <summary>
    /// The change in a ray's W when the focus is moved by a term rather than by tracing to the
    /// moved plane (method.md §5.8), as a function of its point B′ on the exit reference sphere,
    /// in mm of optical path.
    ///
    /// <para>The new reference sphere passes through E′ with its centre moved along E′Q′ to the
    /// new image plane, radius R′. A ray that met the old sphere, radius R′₀, a distance s from E′
    /// meets the new one ½ s² (1/R′₀ - 1/R′) earlier along itself, so its W rises by n′ times that
    /// (Hopkins 1981 eqs. 6.5-6.8). <see cref="DefocusMethod.ExactTerm"/> takes s² = |B′ - E′|²,
    /// the whole of it; <see cref="DefocusMethod.ParaxialTerm"/> drops the axial component,
    /// which is the shortcut Hopkins &amp; Yzuel (1970, eq. 39) show to be in error by terms in
    /// sin²α′.</para>
    /// </summary>
    private static Func<Vec3, double> FocusTerm(WavefrontOptions o, ReferenceGeometry g)
    {
        if (o.Defocus == DefocusMethod.Retrace || o.FocusShift == 0.0 || g.IsInfinite) return _ => 0.0;
        var axis = g.Center - g.PupilPoint;
        double stretch = 1.0 + o.FocusShift / axis.Z;           // new centre = E′ + stretch (Q′₀ - E′)
        double r0 = g.Radius, r = stretch * g.Radius;
        double factor = 0.5 * g.ImageIndex * (1.0 / r0 - 1.0 / r);
        return o.Defocus == DefocusMethod.ExactTerm
            ? b => { var e = b - g.PupilPoint; return factor * e.Dot(e); }
            : b => { var e = b - g.PupilPoint; return factor * (e.X * e.X + e.Y * e.Y); };
    }

    /// <summary>
    /// Carries a ray from the image plane to the exit reference surface, forwards or back along
    /// itself: its point B′ there and its optical path from the entrance reference to it.
    ///
    /// <para>On a sphere, the root of |P + t d - Q′|² = R′² nearest E′, taken in the form that
    /// does not lose figures when |P - Q′| is small against R′ (Welford eq. 4.12). On the
    /// eikonal's reference, the foot of the perpendicular from Q′ to the ray (Welford p. 101).</para>
    /// </summary>
    private static (Vec3 Sphere, double Path)? ToReference(TracedRay r, ReferenceGeometry g)
    {
        var p = r.ImagePoint;
        var d = r.Direction;
        double t;
        if (g.PlaneNormal is Vec3 normal)
        {
            // Afocal: the plane through E′ across the chief ray.
            double along = d.Dot(normal);
            if (Math.Abs(along) < 1e-15) return null;
            t = (g.PupilPoint - p).Dot(normal) / along;
        }
        else if (g.IsInfinite)
        {
            t = (g.Center - p).Dot(d);
        }
        else
        {
            var delta = p - g.Center;
            double b = d.Dot(delta);
            double c = delta.Dot(delta) - g.Radius * g.Radius;
            double disc = b * b - c;
            if (disc < 0.0) return null;
            double q = -(b + (b >= 0.0 ? Math.Sqrt(disc) : -Math.Sqrt(disc)));
            double t1 = q, t2 = q != 0.0 ? c / q : -b;
            t = ((p + t1 * d) - g.PupilPoint).Length <= ((p + t2 * d) - g.PupilPoint).Length ? t1 : t2;
        }
        return (p + t * d, g.PathToImage ? r.OpticalPath : r.OpticalPath + g.ImageIndex * t);
    }

    /// <summary>
    /// Where a ray, extended back from the image, meets the sphere of radius <paramref name="radius"/>
    /// about <paramref name="center"/> on the side of the plane z = <paramref name="towardZ"/>.
    /// </summary>
    private static Vec3? OnSphere(TracedRay r, Vec3 center, double radius, double towardZ)
    {
        var p = r.ImagePoint;
        var d = r.Direction;
        var delta = p - center;
        double b = d.Dot(delta), c = delta.Dot(delta) - radius * radius, disc = b * b - c;
        if (disc < 0.0 || double.IsNaN(disc)) return null;
        double root = Math.Sqrt(disc);
        var a1 = p + (-b + root) * d;
        var a2 = p + (-b - root) * d;
        return Math.Abs(a1.Z - towardZ) <= Math.Abs(a2.Z - towardZ) ? a1 : a2;
    }

    /// <summary>Where a ray crosses the plane z = <paramref name="z"/> of the image frame.</summary>
    private static Vec3 AtZ(TracedRay r, double z)
    {
        if (Math.Abs(r.Direction.Z) < 1e-15) return new Vec3(double.NaN, double.NaN, z);
        double s = (z - r.ImagePoint.Z) / r.Direction.Z;
        return r.ImagePoint + s * r.Direction;
    }

    /// <summary>The point of a ray nearest the axis; null for a ray parallel to the axis.</summary>
    private static Vec3? AxisPoint(TracedRay r)
    {
        var p = r.ImagePoint;
        var d = r.Direction;
        double across = d.X * d.X + d.Y * d.Y;
        if (across < 1e-24) return null;
        double s = -(p.X * d.X + p.Y * d.Y) / across;
        return p + s * d;
    }

    // The field the paraxial limits are taken at: small enough that the ray is paraxial to a
    // part in a million, with the remainder, which goes as its square, extrapolated away.
    private static double TinyField(LensModel lens) => (lens.MaxField > 0.0 ? lens.MaxField : 1.0) * 1e-3;

    /// <summary>
    /// The paraxial exit pupil, as the image-frame z where the chief ray of a vanishing field
    /// crosses the axis. Measured on real rays rather than read off the paraxial trace so it is
    /// in the same frame as everything else - a mirror's image space runs backwards along z -
    /// and Richardson-extrapolated from two small fields to the limit.
    /// </summary>
    public static double ParaxialExitPupilZ(LensModel lens)
    {
        int w = lens.PrimaryWavelength;
        double e = TinyField(lens);
        double Z(double field)
        {
            var r = lens.Trace(field, w, 0.0, 0.0);
            if (!r.Ok) return double.NaN;
            return AxisPoint(r) is Vec3 a ? a.Z : double.PositiveInfinity;
        }
        double z1 = Z(e), z2 = Z(2 * e);
        if (double.IsInfinity(z1) || double.IsInfinity(z2)) return double.PositiveInfinity;
        return (4.0 * z1 - z2) / 3.0;
    }

    /// <summary>
    /// The Gaussian image of field <paramref name="f"/> on the image plane: the landing of a
    /// vanishing field's chief ray, scaled up linearly, extrapolated as above.
    /// </summary>
    private static Vec3 GaussianImage(LensModel lens, double f, int w, double shift)
    {
        double e = TinyField(lens);
        Vec3 Scaled(double field)
        {
            var r = lens.Trace(field, w, 0.0, 0.0, ApertureClipping.None, shift);
            if (!r.Ok) throw new InvalidOperationException("a paraxial chief ray does not reach the image");
            return new Vec3(f / field * r.ImagePoint.X, f / field * r.ImagePoint.Y, r.ImagePoint.Z);
        }
        var g1 = Scaled(e);
        var g2 = Scaled(2 * e);
        return new Vec3((4.0 * g1.X - g2.X) / 3.0, (4.0 * g1.Y - g2.Y) / 3.0, g1.Z);
    }

    private static Vec3 Centroid(TracedRay[] rays)
    {
        var ok = rays.Where(r => r.Ok).ToList();
        if (ok.Count == 0) throw new InvalidOperationException("no ray reaches the image");
        return new Vec3(ok.Average(r => r.ImagePoint.X), ok.Average(r => r.ImagePoint.Y), ok[0].ImagePoint.Z);
    }

    /// <summary>
    /// Each grid cell's weight is its area on the unit disk times the Jacobian of its point on
    /// the exit sphere against its unit-disk coordinates: the area of the exit pupil it stands
    /// for (method.md §6.2). Central differences between neighbouring cells, one-sided at the
    /// rim; a cell with no neighbour in some direction takes the mean of the others.
    /// </summary>
    private static void AreaWeights(IReadOnlyList<PupilPoint> points, WavefrontSample[] samples)
    {
        var at = new Dictionary<(int, int), int>();
        for (int k = 0; k < points.Count; k++)
            if (!samples[k].Vignetted) at[(points[k].I, points[k].J)] = k;

        double Derivative(int k, int di, int dj, Func<WavefrontSample, double> of, double step)
        {
            var (i, j) = (points[k].I, points[k].J);
            bool plus = at.TryGetValue((i + di, j + dj), out int kp);
            bool minus = at.TryGetValue((i - di, j - dj), out int km);
            if (plus && minus) return (of(samples[kp]) - of(samples[km])) / (2 * step);
            if (plus) return (of(samples[kp]) - of(samples[k])) / step;
            if (minus) return (of(samples[k]) - of(samples[km])) / step;
            return double.NaN;
        }

        var weights = new double[samples.Length];
        var missing = new List<int>();
        foreach (var k in at.Values)
        {
            double step = Math.Sqrt(points[k].Area);
            double xx = Derivative(k, 1, 0, s => s.Sphere.X, step), xy = Derivative(k, 0, 1, s => s.Sphere.X, step);
            double yx = Derivative(k, 1, 0, s => s.Sphere.Y, step), yy = Derivative(k, 0, 1, s => s.Sphere.Y, step);
            double det = Math.Abs(xx * yy - xy * yx);
            if (double.IsNaN(det)) missing.Add(k);
            else weights[k] = det * points[k].Area;
        }
        double mean = at.Values.Except(missing).Select(k => weights[k]).DefaultIfEmpty(1.0).Average();
        foreach (var k in missing) weights[k] = mean;
        foreach (var k in at.Values) samples[k] = samples[k] with { Weight = weights[k] };
    }

    /// <summary>Weighted statistics of W over the unvignetted samples (method.md §7).</summary>
    public static WavefrontStatistics Statistics(IReadOnlyList<WavefrontSample> samples, WavefrontOptions o)
    {
        var used = samples.Where(s => !s.Vignetted).Select(s => (s.W, s.Weight)).ToList();
        int vignetted = samples.Count - used.Count;
        if (o.IncludeVignettedAsZero && vignetted > 0)
        {
            double meanWeight = used.Count > 0 ? used.Average(u => u.Weight) : 1.0;
            used.AddRange(Enumerable.Repeat((0.0, meanWeight), vignetted));
        }
        if (used.Count == 0)
            return new WavefrontStatistics(double.NaN, double.NaN, double.NaN, double.NaN, double.NaN,
                                           double.NaN, double.NaN, 0, vignetted);

        double total = used.Sum(u => u.Weight);
        double mean = used.Sum(u => u.Weight * u.W) / total;
        double square = used.Sum(u => u.Weight * u.W * u.W) / total;
        double std = Math.Sqrt(Math.Max(0.0, square - mean * mean));
        double aboutZero = Math.Sqrt(square);
        double pv = used.Max(u => u.W) - used.Min(u => u.W);
        double phase = 2.0 * Math.PI * std;
        return new WavefrontStatistics(mean, o.Rms == RmsDefinition.StandardDeviation ? std : aboutZero,
                                       std, aboutZero, pv, 1.0 - phase * phase, Math.Exp(-phase * phase),
                                       used.Count, vignetted);
    }
}
