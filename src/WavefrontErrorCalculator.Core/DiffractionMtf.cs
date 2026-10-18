namespace WavefrontErrorCalculator.Core;

/// <summary>How <see cref="DiffractionMtf.Compute"/> samples.</summary>
public sealed record DiffractionMtfOptions
{
    /// <summary>
    /// The n×n square grid of the pupil the autocorrelation's outer integral is summed over. Its error is at the
    /// pupil's edges and falls as the grid is refined: 128 is within about 10⁻³ of a converged MTF on the test
    /// lenses (docs/verification.md).
    /// </summary>
    public int Grid { get; init; } = 128;

    /// <summary>One wavelength, by index; every wavelength, by its weight, if null.</summary>
    public int? Wavelength { get; init; }
}

/// <summary>The diffraction MTF of one field.</summary>
/// <param name="Field">The field, an index into the lens's fields.</param>
/// <param name="Frequencies">The spatial frequencies, cycles per unit length of the image plane.</param>
/// <param name="Mtf">The MTF at each frequency, sagittal (x) and tangential (y).</param>
/// <param name="DiffractionLimit">A perfect lens's MTF with the same aperture, weighted over the wavelengths.</param>
/// <param name="FresnelNumber">The primary wavelength's Fresnel number a²/(λR′), a the pupil's radius on the reference sphere.</param>
/// <param name="Unsolved">Pupil points, over every wavelength and frequency, whose partner the iteration could not find.</param>
/// <param name="Warnings">What the result should be read with.</param>
public sealed record DiffractionMtfResult(int Field, IReadOnlyList<double> Frequencies,
                                          IReadOnlyList<GeometricMtf.Modulation> Mtf,
                                          IReadOnlyList<GeometricMtf.Modulation> DiffractionLimit,
                                          double FresnelNumber, int Unsolved, IReadOnlyList<string> Warnings);

/// <summary>
/// The diffraction MTF over a lens's wavelengths, from <see cref="Diffraction.Otf"/> (method.md §11.2): at each
/// frequency, the OTF of each wavelength, sagittal and tangential, as the autocorrelation of its pupil function
/// with traced partners, summed with the wavelengths' weights, and the modulus of the sum,
///
/// <code>
///   MTF(ν) = |Σ w_λ OTF_λ(ν)| / Σ w_λ.
/// </code>
///
/// <para>Each wavelength's PSF has unit energy, so this is the MTF of their weighted sum, the image of a source
/// with that spectrum. The OTFs are added as complex numbers: every wavelength's wavefront is referred to the
/// primary wavelength's image point when <see cref="WavefrontOptions.ChromaticReference"/> is
/// <see cref="ChromaticReference.PrimaryFocus"/> or <see cref="ChromaticReference.PrimarySphere"/>, so their
/// phases carry where each wavelength's image lies, and lateral color lowers the MTF as it should. Each wavelength
/// has its own cutoff, n′·2 sin θ / λ.</para>
///
/// <para>The autocorrelation needs the pupil coordinates fixed to the stop: a chief ray at the vignetted pupil's
/// centre (<see cref="ChiefRayDefinition.VignettedCenter"/>, the reference default) is replaced by the stop's
/// centre, and the result says so.</para>
///
/// <para><b>The Debye integral</b> behind the autocorrelation holds when the Fresnel number is large. Below 10,
/// the focus moves toward the pupil and the result is flagged.</para>
/// </summary>
public static class DiffractionMtf
{
    /// <summary>The Fresnel number below which the result is flagged.</summary>
    public const double LowFresnelNumber = 10.0;

    /// <summary>The diffraction MTF of field <paramref name="field"/> at <paramref name="frequencies"/>, cycles per unit length of the image plane.</summary>
    public static DiffractionMtfResult Compute(LensModel lens, int field, IReadOnlyList<double> frequencies,
                                               WavefrontOptions options, DiffractionMtfOptions? mtf = null)
    {
        if (lens == null) throw new ArgumentNullException(nameof(lens));
        if (frequencies == null) throw new ArgumentNullException(nameof(frequencies));
        if (options == null) throw new ArgumentNullException(nameof(options));
        if (field < 0 || field >= lens.System.Fields.Count) throw new ArgumentOutOfRangeException(nameof(field));
        mtf ??= new DiffractionMtfOptions();
        var warnings = new List<string>();
        if (options.ChiefRay == ChiefRayDefinition.VignettedCenter)
        {
            options = options with { ChiefRay = ChiefRayDefinition.StopCenter };
            warnings.Add("the chief ray is the ray through the stop's centre, which the autocorrelation needs");
        }
        if (options.ChromaticReference == ChromaticReference.OwnChief && mtf.Wavelength == null && lens.System.Wavelengths.Count > 1)
            warnings.Add("each wavelength is referred to its own chief ray, so lateral color is not in the polychromatic MTF");

        var system = lens.System;
        var waves = mtf.Wavelength is int only ? new[] { only } : Enumerable.Range(0, system.Wavelengths.Count).ToArray();
        double weightSum = waves.Sum(w => system.Wavelengths[w].Weight);
        if (!(weightSum > 0.0)) throw new ArgumentException("the wavelengths have no weight");

        int n = frequencies.Count, unsolved = 0;
        var sRe = new double[n]; var sIm = new double[n];
        var tRe = new double[n]; var tIm = new double[n];
        double fresnel = double.NaN;
        foreach (int w in waves)
        {
            double weight = system.Wavelengths[w].Weight / weightSum;
            if (weight == 0.0) continue;
            var pupil = new Diffraction.TracedPupil(lens, field, w, options, mtf.Grid);
            if (w == lens.PrimaryWavelength || double.IsNaN(fresnel)) fresnel = FresnelNumber(lens, field, w, options, pupil);
            for (int i = 0; i < n; i++)
            {
                var s = pupil.Otf(frequencies[i], 0.0);
                var t = pupil.Otf(0.0, frequencies[i]);
                sRe[i] += weight * s.Real; sIm[i] += weight * s.Imaginary;
                tRe[i] += weight * t.Real; tIm[i] += weight * t.Imaginary;
                unsolved += s.Unsolved + t.Unsolved;
            }
        }
        if (fresnel < LowFresnelNumber)
            warnings.Add(FormattableString.Invariant(
                $"the Fresnel number is {fresnel:G3}, below {LowFresnelNumber}: the Debye integral the MTF rests on loses accuracy"));
        if (unsolved > 0) warnings.Add($"{unsolved} pupil points' partners were not found and count as not overlapping");

        var result = new GeometricMtf.Modulation[n];
        for (int i = 0; i < n; i++)
            result[i] = new GeometricMtf.Modulation(Math.Sqrt(sRe[i] * sRe[i] + sIm[i] * sIm[i]), Math.Sqrt(tRe[i] * tRe[i] + tIm[i] * tIm[i]));

        var (extentS, extentT, obscuration) = GeometricMtf.Cutoffs(lens, field, lens.PrimaryWavelength, options);
        var limit = frequencies.Select(f =>
        {
            double ls = 0.0, lt = 0.0;
            foreach (int w in waves)
            {
                double weight = system.Wavelengths[w].Weight / weightSum;
                double lambda = lens.WavelengthUm(w) * 1e-3;
                ls += weight * GeometricMtf.DiffractionLimit(f, extentS / lambda, obscuration);
                lt += weight * GeometricMtf.DiffractionLimit(f, extentT / lambda, obscuration);
            }
            return new GeometricMtf.Modulation(ls, lt);
        }).ToArray();
        return new DiffractionMtfResult(field, frequencies.ToArray(), result, limit, fresnel, unsolved, warnings);
    }

    /// <summary>
    /// a²/(λR′) with λ the wavelength in the image space: a = R′ sin θ, the pupil's radius on the reference sphere,
    /// from the spread of the transmitting rays' spatial frequencies about their mean, n′ sin θ = λ₀ |q − q̄|max.
    /// </summary>
    private static double FresnelNumber(LensModel lens, int field, int wavelength, WavefrontOptions options,
                                        Diffraction.TracedPupil pupil)
    {
        var g = WavefrontCalculator.Compute(lens, field, wavelength, options,
            new Sampling.Given(new[] { new PupilPoint(0.0, 0.0, 1.0) })).Geometry;
        var q = pupil.Frequencies.ToList();
        double mx = q.Average(p => p.Qx), my = q.Average(p => p.Qy);
        double spread = q.Max(p => Math.Sqrt((p.Qx - mx) * (p.Qx - mx) + (p.Qy - my) * (p.Qy - my)));
        double lambda = g.WavelengthUm * 1e-3;
        double sine = spread * lambda / g.ImageIndex;
        return Math.Abs(g.Radius) * sine * sine * g.ImageIndex / lambda;
    }
}
