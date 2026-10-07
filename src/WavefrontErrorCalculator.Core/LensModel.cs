using AberrationCalculator.Core.Glass;
using AberrationCalculator.Core.IO;
using AberrationCalculator.Core.Models;
using AberrationCalculator.Core.RayTrace;

namespace WavefrontErrorCalculator.Core;

/// <summary>
/// One real ray as the wavefront calculation needs it: where it meets the image plane, which
/// way it is going there, and its optical path from the ENTRANCE reference surface to that point.
///
/// <para>The entrance reference surface is the wavefront the light leaves the object on (method.md
/// §3.1): for a finite object the sphere centred on the object point, which is the point itself
/// since every ray leaves it in phase; for an object at infinity a plane across the collimated
/// beam. Measuring from there rather than from the launch point on surface 1's vertex plane is
/// what makes two rays' paths comparable: the launch points lie on a plane, and that plane is a
/// wavefront only for an axial collimated beam.</para>
/// </summary>
/// <param name="StoppedAt">The surface whose aperture stopped the ray, or -1.</param>
public readonly record struct TracedRay(bool Ok, Vec3 ImagePoint, Vec3 Direction, double OpticalPath,
                                        double LaunchPy, double LaunchPx, int StoppedAt = -1);

/// <summary>
/// A lens ready for wavefront work: its indices at every wavelength, the paraxial trace that
/// places its entrance pupil, and its ray aiming, all from AberrationCalculator.
///
/// <para>Rays of every wavelength are launched against the PRIMARY wavelength's paraxial entrance
/// pupil, so a pupil coordinate means the same place in the beam at every wavelength - the
/// convention of every program this one is compared with.</para>
/// </summary>
public sealed class LensModel
{
    private readonly double[][] _indices;
    private readonly Lazy<StopAimer?>[] _aimers;
    private readonly Lazy<double?> _stopHeight;

    /// <param name="indices">
    /// Refractive indices to use instead of the catalog's, indexed like the surfaces, for the
    /// wavelengths it returns an array for. A comparison with another program uses that program's
    /// own glass data this way, so that a difference between two catalogues - 1e-5 in index over
    /// 10 mm of glass is a sixth of a wave - is not mistaken for a difference of convention.
    /// </param>
    public LensModel(OpticalSystem system, GlassCatalog catalog, Func<int, double[]?>? indices = null)
    {
        System = system ?? throw new ArgumentNullException(nameof(system));
        if (catalog == null) throw new ArgumentNullException(nameof(catalog));
        if (system.Wavelengths.Count == 0) throw new ArgumentException("the lens has no wavelengths", nameof(system));
        if (system.Surfaces.Any(s => s.Type == AberrationCalculator.Core.Enums.SurfaceType.Paraxial))
            throw new NotSupportedException("the lens has a paraxial (ideal) surface, whose optical path is not modelled (method.md §13.5)");

        PrimaryWavelength = Math.Max(0, system.PrimaryWavelengthIndex);
        _indices = system.Wavelengths.Select((w, i) =>
        {
            var given = indices?.Invoke(i);
            if (given == null) return IndexResolver.Build(system, catalog, w.Value);
            if (given.Length != system.Surfaces.Count)
                throw new ArgumentException($"{given.Length} indices for a lens of {system.Surfaces.Count} surfaces", nameof(indices));
            return given;
        }).ToArray();
        MaxField = system.Fields.Count == 0 ? 0.0 : system.Fields.Max(f => Math.Abs(f.Y));
        Paraxial = ParaxialTrace.Trace(system, _indices[PrimaryWavelength], MaxField);

        // The stop's radius is the real one the primary wavelength's axial beam fills, for every
        // wavelength: one aperture, and on axis an aimed ray is the ray launched at the same
        // pupil coordinates (method.md §5.3).
        bool hasStop = system.StopSurfaceIndex >= 1 && system.StopSurfaceIndex <= system.LastOpticalSurface();
        _stopHeight = new Lazy<double?>(() => hasStop
            ? StopAimer.RealAxialHeight(System, _indices[PrimaryWavelength], Paraxial) ?? Paraxial.Y[System.StopSurfaceIndex]
            : null);
        _aimers = new Lazy<StopAimer?>[system.Wavelengths.Count];
        for (int w = 0; w < _aimers.Length; w++)
        {
            int wave = w;
            _aimers[w] = new Lazy<StopAimer?>(() =>
                hasStop ? StopAimer.ForSystem(System, _indices[wave], Paraxial, stopHeight: _stopHeight.Value) : null);
        }
    }

    /// <summary>Reads a lens in any format AberrationCalculator reads, with its bundled glass.</summary>
    public static LensModel Read(string path, Func<int, double[]?>? indices = null)
    {
        var catalog = CatalogLocator.LoadBundled();
        return new LensModel(LensFile.Read(path, catalog), catalog, indices);
    }

    public OpticalSystem System { get; }

    /// <summary>The primary wavelength's paraxial trace, at the largest field.</summary>
    public ParaxialResult Paraxial { get; }

    public int PrimaryWavelength { get; }

    /// <summary>The largest field, in the lens's field units.</summary>
    public double MaxField { get; }

    public double WavelengthUm(int w) => System.Wavelengths[w].Value;

    public double[] Indices(int w) => _indices[w];

    /// <summary>The index of the image space, unsigned.</summary>
    public double ImageIndex(int w) => Math.Abs(_indices[w][System.LastOpticalSurface()]);

    /// <summary>Ray aiming at the stop for this wavelength; null when the lens has no stop.</summary>
    public StopAimer? Aimer(int w) => _aimers[w].Value;

    /// <summary>
    /// The object NA from which OSLO EDU 6.6 maps a finite object's pupil by direction sines
    /// rather than by the entrance pupil's plane: it changed between 0.095 and 0.100 when the
    /// relay's NA was stepped by 0.005 (docs/programs.md).
    /// </summary>
    public const double AplanaticNaThreshold = 0.1;

    /// <summary>
    /// Where <paramref name="ray"/>, traced from its launch coordinates, crosses the last surface
    /// before the image, in the image frame (z from the file's image plane); null if it fails.
    /// </summary>
    public Vec3? LastSurfaceHit(double field, int w, TracedRay ray)
    {
        int last = System.Surfaces.Count - 2;
        if (last < 1) return null;
        RealRayTrace.SurfaceHit[] hits;
        try
        {
            hits = RealRayTrace.TraceRecord(System, _indices[w], Paraxial, field, ray.LaunchPy, ray.LaunchPx,
                                            atParaxialFocus: false);
        }
        catch (InvalidOperationException) { return null; }
        var hit = hits[last];
        if (!hit.Ok) return null;
        return new Vec3(hit.X, hit.Y, hit.Z - System.Surfaces[last].Thickness);
    }

    /// <summary>
    /// The paraxial launch coordinates (Py, Px) of the ray that OSLO's "aplanatic" fractional
    /// coordinates (<paramref name="fy"/>, <paramref name="fx"/>) name (method.md §5.3).
    ///
    /// <para>For an object at infinity they are the paraxial entrance pupil's own coordinates, moved
    /// so that (0, 0) is the ray aimed at the centre of the real stop when
    /// <paramref name="aboutReference"/>. For a finite object they are linear in the direction
    /// sines of the ray leaving the object point: the paraxial entrance pupil, a disc, seen from
    /// the object point spans sines L_c ± A_y across the meridian and ±A_x across the sagittal
    /// plane, and the ray is L = L_c + FY·A_y, K = FX·A_x. With <paramref name="aboutReference"/>
    /// the disc is centred instead where the ray aimed at the centre of the real stop crosses the
    /// pupil plane. Upright: FY = 1 leaves the object upwards whichever side of it the pupil
    /// lies.</para>
    /// </summary>
    /// <param name="plane">
    /// Linear in the paraxial entrance pupil's plane even for a finite object, as for an object at
    /// infinity. OSLO maps so by itself when the object NA is below <see cref="AplanaticNaThreshold"/>.
    /// </param>
    public (double Py, double Px)? AplanaticLaunch(double field, int w, double fy, double fx, bool aboutReference, bool plane = false)
    {
        double epr = 0.5 * Paraxial.Epd;
        double ep = Paraxial.EntrancePupilPosition;
        double objectThickness = System.Surfaces[0].Thickness;
        (double Py, double Px)? chief = null;
        if (aboutReference)
        {
            var aimer = Aimer(w);
            if (aimer == null || !aimer.CanAim(field) || aimer.Launch(field, 0.0, 0.0) is not (double cy, double cx))
                return null;
            chief = (cy, cx);
        }

        // OSLO aims at its entrance sphere, by direction sines, only for an object NA of 0.1 or
        // more; below it, at the pupil's plane (seen to switch between NA 0.095 and 0.100).
        bool atInfinity = double.IsInfinity(objectThickness) || Math.Abs(objectThickness) >= 1e12;
        if (!atInfinity && !plane)
        {
            double toPupil = Math.Abs(ep + Math.Abs(objectThickness));
            double objectNa = Math.Abs(epr) / Math.Sqrt(epr * epr + toPupil * toPupil);
            plane = objectNa < AplanaticNaThreshold - 1e-9;
        }
        if (plane || atInfinity)
            return chief is (double py0, double px0) ? (py0 + fy, px0 + fx) : (fy, fx);

        // The object point: the paraxial launch line through the pupil's centre, taken back to it.
        double distance = Math.Abs(objectThickness);
        var (_, y1, dx1, dy1, dz1) = RealRayTrace.LaunchRay(System, Paraxial, field, 0.0, 0.0);
        double h = y1 - dy1 / dz1 * distance;
        double d = ep + distance;                              // object to pupil plane, signed
        double r = Math.Abs(epr), s = Math.Sign(d), ad = Math.Abs(d);

        // Direction sines, forward, from the object point to a point (x, y) of the pupil plane.
        double SineY(double y, double x) => s * (y - h) / Math.Sqrt(x * x + (y - h) * (y - h) + d * d);
        double SineX(double y, double x) => s * x / Math.Sqrt(x * x + (y - h) * (y - h) + d * d);
        (double Lc, double Ay, double Ax) Disc(double c)
        {
            double up = SineY(c + r, 0.0), down = SineY(c - r, 0.0);
            return (0.5 * (up + down), 0.5 * Math.Abs(up - down), Math.Abs(SineX(c, r)));
        }

        // About the reference ray, the disc is centred where that ray crosses the pupil plane.
        double centre = chief is (double pyc, double _) ? pyc * epr : 0.0;
        var (lc, ay, ax) = Disc(centre);
        double l = lc + fy * ay, k = fx * ax, n2 = 1.0 - l * l - k * k;
        if (n2 <= 0.0) return null;
        double nz = Math.Sqrt(n2);
        // Back to the pupil plane along that direction: forward is +z, the plane is at s·|d| ... at d.
        double yp = h + s * l / nz * ad, xp = s * k / nz * ad;
        return (yp / epr, xp / epr);
    }

    /// <summary>
    /// The semi-diameter that stops rays at each surface under <paramref name="clipping"/>, 0 for
    /// none. The stop is always 0: the pupil coordinates are what fill it.
    /// </summary>
    public double[] Apertures(ApertureClipping clipping)
    {
        var sd = new double[System.Surfaces.Count];
        if (clipping == ApertureClipping.None) return sd;
        int stop = System.StopSurfaceIndex;
        for (int i = 1; i <= System.LastOpticalSurface(); i++)
        {
            var s = System.Surfaces[i];
            if (i == stop || !(s.SemiDiameter > 0.0)) continue;
            if (clipping == ApertureClipping.All || s.SemiDiameterMode == AberrationCalculator.Core.Enums.SemiDiameterMode.Fixed)
                sd[i] = s.SemiDiameter * Math.Max(0.0, s.ClearAperturePercent) / 100.0;
        }
        return sd;
    }

    /// <summary>
    /// Traces the ray launched at (<paramref name="launchPy"/>, <paramref name="launchPx"/>) in the
    /// paraxial entrance pupil - meridional, then sagittal, as fractions of its radius - to the
    /// image plane the file specifies, moved along the axis by <paramref name="imageShift"/>.
    /// A ray outside a surface's aperture under <paramref name="clipping"/>, or inside its
    /// central obscuration, is stopped there.
    /// </summary>
    public TracedRay Trace(double field, int w, double launchPy, double launchPx,
                           ApertureClipping clipping = ApertureClipping.None, double imageShift = 0.0)
    {
        var n = _indices[w];
        var path = new double[System.Surfaces.Count];
        RealRayTrace.SurfaceHit[] hits;
        try
        {
            hits = RealRayTrace.TraceRecord(System, n, Paraxial, field, launchPy, launchPx,
                                            atParaxialFocus: false, null, null, path);
        }
        catch (InvalidOperationException) { return default; }

        int image = System.Surfaces.Count - 1;
        var end = hits[image];
        if (!end.Ok) return default;

        if (clipping != ApertureClipping.None)
        {
            var sd = _apertures.GetOrAdd(clipping, Apertures);
            for (int i = 1; i < image; i++)
            {
                double r = Math.Sqrt(hits[i].X * hits[i].X + hits[i].Y * hits[i].Y);
                bool outside = sd[i] > 0.0 && r > sd[i] * (1.0 + 1e-9);
                bool obscured = System.Surfaces[i].ObscurationRadius > 0.0 && r < System.Surfaces[i].ObscurationRadius;
                if (outside || obscured)
                    return new TracedRay(false, default, default, double.NaN, launchPy, launchPx, i);
            }
        }

        // From the entrance reference surface to the launch point, along the ray.
        var (x, y, dx, dy, dz) = RealRayTrace.LaunchRay(System, Paraxial, field, launchPy, launchPx);
        double len = Math.Sqrt(dx * dx + dy * dy + dz * dz);
        dx /= len; dy /= len; dz /= len;
        double objectThickness = System.Surfaces[0].Thickness;
        double toLaunch = double.IsInfinity(objectThickness) || Math.Abs(objectThickness) >= 1e12
            ? x * dx + y * dy                                   // from the plane through the origin across the beam
            : Math.Abs(objectThickness) / dz;                   // from the object point
        double entrance = Math.Abs(n[0]) * toLaunch;

        // A moved image plane is met by carrying the ray straight on, or back, from the file's: no
        // surface lies between them.
        var point = new Vec3(end.X, end.Y, 0.0);
        var direction = new Vec3(end.L, end.M, end.N);
        double extra = 0.0;
        if (imageShift != 0.0)
        {
            double t = imageShift / end.N;
            point = point + t * direction;
            extra = ImageIndex(w) * t;
        }
        return new TracedRay(true, point, direction, entrance + path[image] + extra, launchPy, launchPx);
    }

    private readonly System.Collections.Concurrent.ConcurrentDictionary<ApertureClipping, double[]> _apertures = new();
}
