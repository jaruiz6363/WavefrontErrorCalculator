namespace WavefrontErrorCalculator.Core;

/// <summary>
/// The geometric MTF by Kidger's direct summation (Fundamental Optical Design, SPIE 2002, §4.5.3;
/// Proc. SPIE 46, 49, 1974): for rays spread evenly over the pupil, landing at (x, y) on the image
/// plane,
///
/// <code>
///   MTF_S(f) = |Σ w exp(-2πi f x)| / Σ w        MTF_T(f) = |Σ w exp(-2πi f y)| / Σ w
/// </code>
///
/// <para>Each is the modulus of the Fourier transform of the spot's line spread function, taken
/// straight from the rays with no binning and no FFT. The modulus discards where the spot is, so
/// no reference point is needed. Only rays that arrive are summed; several wavelengths are summed
/// together, each ray weighted by its wavelength's weight, so lateral colour spreads the spot.</para>
///
/// <para><b>The rays</b> are the wavefront's: traced as <see cref="WavefrontCalculator"/> traces
/// them under the same options - aimed at the real stop or not, stopped by the same apertures -
/// with the pupil lit uniformly in (Px, Py), as <see cref="Diffraction"/> lights it. So the
/// geometric MTF is the limit of <see cref="Diffraction.Otf"/> as the wavelength goes to zero,
/// which the tests check, as Kidger checked his programs.</para>
///
/// <para><b>The diffraction limit.</b> As λ → 0 this is exact; at finite λ it overstates a nearly
/// perfect lens. Kidger's correction multiplies by a perfect lens's MTF: right for large aberrations
/// and for very small ones, and pessimistic in between. <see cref="MtfOptions.DiffractionLimited"/>
/// asks for it; by default the MTF is the geometric one alone, with the perfect lens's beside it.
/// The perfect lens's cutoff, sagittal and tangential, comes from the spread of direction cosines
/// in the arriving beam (<see cref="Cutoffs"/>), so it narrows off axis and under vignetting.</para>
/// </summary>
public static class GeometricMtf
{
    /// <summary>A ray's landing point on the image plane, and its weight in the sums.</summary>
    public readonly record struct Spot(double X, double Y, double Weight);

    /// <summary>The MTF in the sagittal (x) and tangential (y) directions.</summary>
    public readonly record struct Modulation(double Sagittal, double Tangential);

    /// <summary>
    /// Kidger's ray pattern (his Fig. 4.25): <paramref name="rings"/> rings of equal area, each cut
    /// into the same number of equal sectors, one ray at the centre of each cell, as (Px, Py) - so
    /// every ray stands for the same area of the pupil. Each ring has 2 × rings sectors, rounded up
    /// to a multiple of 4, so the pattern is symmetric about both axes and on axis the sagittal and
    /// tangential MTF agree. Unlike Kidger, the whole pupil is traced: the sums are not symmetric
    /// in x for an off-axis coma, even when the lens is.
    /// </summary>
    public static (double Px, double Py)[] Pupil(int rings)
    {
        if (rings < 1) throw new ArgumentOutOfRangeException(nameof(rings), "at least one ring");
        int arms = 2 * rings;
        if (arms % 4 != 0) arms += 4 - arms % 4;
        var points = new (double, double)[rings * arms];
        int k = 0;
        for (int ring = 0; ring < rings; ring++)
        {
            double rho = Math.Sqrt((ring + 0.5) / rings);
            for (int arm = 0; arm < arms; arm++)
            {
                double theta = 2.0 * Math.PI * (arm + 0.5) / arms;
                points[k++] = (rho * Math.Cos(theta), rho * Math.Sin(theta));
            }
        }
        return points;
    }

    /// <summary>The geometric MTF of a spot at one spatial frequency, in cycles per unit of x and y.</summary>
    public static Modulation Geometric(IReadOnlyList<Spot> spots, double frequency)
    {
        if (spots == null) throw new ArgumentNullException(nameof(spots));
        double sr = 0.0, si = 0.0, tr = 0.0, ti = 0.0, total = 0.0;
        double k = -2.0 * Math.PI * frequency;
        foreach (var s in spots)
        {
            double ps = k * s.X, pt = k * s.Y;
            sr += s.Weight * Math.Cos(ps); si += s.Weight * Math.Sin(ps);
            tr += s.Weight * Math.Cos(pt); ti += s.Weight * Math.Sin(pt);
            total += s.Weight;
        }
        if (!(total > 0.0)) return new Modulation(double.NaN, double.NaN);
        return new Modulation(Math.Sqrt(sr * sr + si * si) / total, Math.Sqrt(tr * tr + ti * ti) / total);
    }

    /// <summary>
    /// The MTF of a perfect lens with a circular pupil, or an annular one whose central obscuration
    /// is <paramref name="obscuration"/> of its radius, at <paramref name="frequency"/> for a cutoff
    /// of <paramref name="cutoff"/> (O'Neill 1956). An elliptical pupil has the circle's MTF along
    /// each axis with that axis's cutoff, which is what lets an off-axis beam use it.
    /// </summary>
    public static double DiffractionLimit(double frequency, double cutoff, double obscuration = 0.0)
    {
        if (!(cutoff > 0.0)) return 0.0;
        double v = Math.Abs(frequency) / cutoff;
        if (v >= 1.0) return 0.0;
        double e = Math.Clamp(obscuration, 0.0, 0.999999);
        double a = Circle(v);
        if (e == 0.0) return a;
        double e2 = e * e;
        double b = v < e ? e2 * Circle(v / e) : 0.0;
        double c;
        if (v <= (1.0 - e) / 2.0) c = -2.0 * e2;
        else if (v >= (1.0 + e) / 2.0) c = 0.0;
        else
        {
            double phi = Math.Acos(Math.Clamp((1.0 + e2 - 4.0 * v * v) / (2.0 * e), -1.0, 1.0));
            c = 2.0 * e / Math.PI * Math.Sin(phi) + (1.0 + e2) / Math.PI * phi
                - 2.0 * (1.0 - e2) / Math.PI * Math.Atan((1.0 + e) / (1.0 - e) * Math.Tan(phi / 2.0)) - 2.0 * e2;
        }
        return Math.Max(0.0, (a + b + c) / (1.0 - e2));
    }

    private static double Circle(double v) =>
        v >= 1.0 ? 0.0 : 2.0 / Math.PI * (Math.Acos(v) - v * Math.Sqrt(1.0 - v * v));

    /// <summary>
    /// The rays of field <paramref name="field"/> and wavelength <paramref name="wavelength"/> (indices
    /// into the lens's lists) through the pupil points given, as <see cref="WavefrontCalculator"/>
    /// traces them under <paramref name="options"/>: aimed as they ask, through their apertures, to
    /// the image plane (moved by a re-traced focus shift). A ray that does not arrive is not Ok.
    /// </summary>
    public static TracedRay[] Trace(LensModel lens, int field, int wavelength, WavefrontOptions options,
                                    IReadOnlyList<(double Px, double Py)> points)
    {
        if (lens == null) throw new ArgumentNullException(nameof(lens));
        if (options == null) throw new ArgumentNullException(nameof(options));
        if (field < 0 || field >= lens.System.Fields.Count) throw new ArgumentOutOfRangeException(nameof(field));
        if (options.RayAiming == RayAiming.ExitSphereGrid)
            throw new NotSupportedException("rays aimed onto the exit sphere's grid are not traced at the pupil points given");
        double f = lens.System.Fields[field].Y;
        double shift = options.Defocus == DefocusMethod.Retrace ? options.FocusShift : 0.0;
        var warnings = new List<string>();
        return points.Select(p => WavefrontCalculator.TraceAt(lens, f, wavelength, options, p.Px, p.Py, shift, clip: true, warnings)).ToArray();
    }

    /// <summary>
    /// The extent of the arriving beam in direction-cosine space, times the image index: the
    /// diameter of the pupil, sagittal and tangential, as the image plane sees it. Divided by a
    /// vacuum wavelength it is the perfect lens's cutoff in cycles per unit length, since direction
    /// cosines are the Fourier conjugates of image-plane position; on axis it is 2 NA. Each rim is
    /// found along the pupil's axes: the ray at the rim or, if it is stopped, the outermost ray that
    /// arrives. Also the central obscuration, as a fraction of the pupil's sagittal radius, from
    /// the innermost ray that arrives.
    /// </summary>
    public static (double Sagittal, double Tangential, double Obscuration) Cutoffs(LensModel lens, int field, int wavelength, WavefrontOptions options)
    {
        TracedRay At(double px, double py) => Trace(lens, field, wavelength, options, new[] { (px, py) })[0];
        TracedRay? Rim(double cx, double cy)
        {
            var ray = At(cx, cy);
            if (ray.Ok) return ray;
            double lo = 0.0, hi = 1.0;
            TracedRay? best = null;
            for (int i = 0; i < 30; i++)
            {
                double mid = 0.5 * (lo + hi);
                var r = At(mid * cx, mid * cy);
                if (r.Ok) { best = r; lo = mid; } else hi = mid;
            }
            return best;
        }
        double n = lens.ImageIndex(wavelength);
        var (up, down, right, left) = (Rim(0.0, 1.0), Rim(0.0, -1.0), Rim(1.0, 0.0), Rim(-1.0, 0.0));
        double t = up is { } u && down is { } d ? Math.Abs(u.Direction.Y - d.Direction.Y) * n : 0.0;
        double s = right is { } r1 && left is { } l1 ? Math.Abs(r1.Direction.X - l1.Direction.X) * n : 0.0;

        double obscuration = 0.0;
        if (!At(0.0, 0.0).Ok && right is { })
        {
            double lo = 0.0, hi = 1.0;
            for (int i = 0; i < 30; i++)
            {
                double mid = 0.5 * (lo + hi);
                if (At(mid, 0.0).Ok) hi = mid; else lo = mid;
            }
            obscuration = hi;
        }
        return (s, t, obscuration);
    }

    /// <summary>
    /// The geometric MTF of field <paramref name="field"/> (an index into the lens's fields) at the
    /// given frequencies, in cycles per unit length of the image plane (cycles/mm for a lens in
    /// millimetres), the rays traced under <paramref name="options"/>. Afocal systems are not supported.
    /// </summary>
    public static MtfResult Compute(LensModel lens, int field, IReadOnlyList<double> frequencies, WavefrontOptions options,
                                    MtfOptions? mtf = null)
    {
        if (lens == null) throw new ArgumentNullException(nameof(lens));
        if (frequencies == null) throw new ArgumentNullException(nameof(frequencies));
        if (lens.System.IsAfocal || WavefrontCalculator.IsAfocal(lens))
            throw new NotSupportedException("the geometric MTF of an afocal system is not supported");
        mtf ??= new MtfOptions();
        var system = lens.System;
        var waves = mtf.Wavelength is int only ? new[] { only } : Enumerable.Range(0, system.Wavelengths.Count).ToArray();
        double weightSum = waves.Sum(w => system.Wavelengths[w].Weight);
        if (!(weightSum > 0.0)) throw new ArgumentException("the wavelengths have no weight");

        var pupil = Pupil(mtf.Rings);
        var spots = new List<Spot>(pupil.Length * waves.Length);
        foreach (int w in waves)
        {
            double weight = system.Wavelengths[w].Weight / weightSum;
            foreach (var ray in Trace(lens, field, w, options, pupil))
                if (ray.Ok) spots.Add(new Spot(ray.ImagePoint.X, ray.ImagePoint.Y, weight));
        }

        // The beam's extent is the aperture's, so it is measured once, at the primary wavelength,
        // and each wavelength's cutoff is that over its own wavelength.
        int primary = lens.PrimaryWavelength;
        var (extentS, extentT, obscuration) = Cutoffs(lens, field, primary, options);
        double Cutoff(double extent, int w) => extent / (lens.WavelengthUm(w) * 1e-3);

        var geometric = new Modulation[frequencies.Count];
        var limit = new Modulation[frequencies.Count];
        var result = new Modulation[frequencies.Count];
        for (int i = 0; i < frequencies.Count; i++)
        {
            double f = frequencies[i];
            geometric[i] = spots.Count == 0 ? new Modulation(double.NaN, double.NaN) : Geometric(spots, f);
            double ls = 0.0, lt = 0.0;
            foreach (int w in waves)
            {
                double weight = system.Wavelengths[w].Weight / weightSum;
                ls += weight * DiffractionLimit(f, Cutoff(extentS, w), obscuration);
                lt += weight * DiffractionLimit(f, Cutoff(extentT, w), obscuration);
            }
            limit[i] = new Modulation(ls, lt);
            result[i] = mtf.DiffractionLimited
                ? new Modulation(geometric[i].Sagittal * ls, geometric[i].Tangential * lt)
                : geometric[i];
        }
        return new MtfResult(field, frequencies.ToArray(), result, geometric, limit,
                             Cutoff(extentS, primary), Cutoff(extentT, primary), obscuration,
                             pupil.Length * waves.Length, spots.Count);
    }
}

/// <summary>How <see cref="GeometricMtf.Compute"/> samples and corrects.</summary>
public sealed record MtfOptions
{
    /// <summary>
    /// Rings of Kidger's pattern; the rays are 2 rings² or a little more, per wavelength. The error
    /// grows with the spot's size times the frequency: on the Cooke triplet at 20° and 80 cycles/mm,
    /// 30 rings are within 0.005 of a converged pattern, 15 within 0.016; on axis 15 are within 0.0004.
    /// </summary>
    public int Rings { get; init; } = 30;

    /// <summary>Multiply by a perfect lens's MTF (Kidger's correction); off by default.</summary>
    public bool DiffractionLimited { get; init; }

    /// <summary>One wavelength, by index; every wavelength, weighted, if null.</summary>
    public int? Wavelength { get; init; }
}

/// <summary>The geometric MTF of one field.</summary>
/// <param name="Field">The field, an index into the lens's fields.</param>
/// <param name="Mtf">The MTF reported: the geometric, or times the diffraction limit if asked for.</param>
/// <param name="Geometric">The geometric MTF alone.</param>
/// <param name="DiffractionLimit">A perfect lens's MTF with the same aperture, weighted over the wavelengths.</param>
/// <param name="CutoffSagittal">The perfect lens's cutoff at the primary wavelength, sagittal.</param>
/// <param name="CutoffTangential">The same, tangential.</param>
/// <param name="Obscuration">The central obscuration as a fraction of the pupil's radius.</param>
/// <param name="RaysTraced">Rays launched, over every wavelength.</param>
/// <param name="RaysArrived">Of them, those that reached the image.</param>
public sealed record MtfResult(int Field, IReadOnlyList<double> Frequencies,
                               IReadOnlyList<GeometricMtf.Modulation> Mtf,
                               IReadOnlyList<GeometricMtf.Modulation> Geometric,
                               IReadOnlyList<GeometricMtf.Modulation> DiffractionLimit,
                               double CutoffSagittal, double CutoffTangential, double Obscuration,
                               int RaysTraced, int RaysArrived);
