namespace WavefrontErrorCalculator.Core;

/// <summary>
/// One ray's wavefront aberration and where it is in the pupil (method.md §6.1).
/// </summary>
/// <param name="U">The sample's coordinate on the unit disk the sampling covers, sagittal.</param>
/// <param name="V">The same, meridional. When the whole pupil is sampled these are <see cref="Px"/> and <see cref="Py"/>.</param>
/// <param name="Px">The pupil point traced, sagittal: a fraction of the stop (aimed) or of the paraxial entrance pupil (unaimed).</param>
/// <param name="Py">The pupil point traced, meridional.</param>
/// <param name="LaunchPx">Where in the paraxial entrance pupil the ray was launched, sagittal.</param>
/// <param name="LaunchPy">Where in the paraxial entrance pupil the ray was launched, meridional.</param>
/// <param name="Sphere">The ray's point B′ on the exit reference sphere, in the image frame (mm).</param>
/// <param name="ExitX">Exit-sphere coordinate x′: (B′ - E′)ₓ / h′.</param>
/// <param name="ExitY">Exit-sphere coordinate y′: (B′ - E′)ᵧ / h′.</param>
/// <param name="ExitZ">Exit-sphere coordinate z′: (B′ - E′)_z / h′.</param>
/// <param name="CanonicalX">Canonical exit coordinate x′_S: (B′ - E′)ₓ / h′_S, so the pupil's rim is near ±1 (method.md §3.2).</param>
/// <param name="CanonicalY">Canonical exit coordinate y′_T: (B′ - E′)ᵧ / h′_T.</param>
/// <param name="ImagePoint">Where the ray meets the image plane (mm).</param>
/// <param name="Direction">The ray's direction cosines in the image space.</param>
/// <param name="OpticalPath">From the entrance reference surface to B′ (mm).</param>
/// <param name="W">The wavefront aberration in waves at the analysis wavelength, signed by the options.</param>
/// <param name="Weight">Its weight in pupil integrals, before normalisation; 0 when vignetted.</param>
/// <param name="Vignetted">The ray did not reach the reference sphere.</param>
/// <param name="StoppedAt">The surface whose aperture stopped it, or -1.</param>
public readonly record struct WavefrontSample(
    double U, double V, double Px, double Py, double LaunchPx, double LaunchPy,
    Vec3 Sphere, double ExitX, double ExitY, double ExitZ, double CanonicalX, double CanonicalY,
    Vec3 ImagePoint, Vec3 Direction, double OpticalPath, double W, double Weight, bool Vignetted,
    int StoppedAt = -1);

/// <summary>
/// The part of the pupil a field's pencil actually fills (method.md §5.4): in pupil coordinates,
/// an ellipse centred on the meridian at <see cref="CenterPy"/> with sagittal and tangential
/// semi-axes <see cref="SemiX"/> and <see cref="SemiY"/>. The whole unit pupil when nothing
/// vignettes or no exploration was asked for.
/// </summary>
public sealed record PupilDomain(double CenterPy, double SemiX, double SemiY, double Upper, double Lower)
{
    public static PupilDomain Whole { get; } = new(0.0, 1.0, 1.0, 1.0, -1.0);

    public bool IsVignetted => SemiX < 1.0 - 1e-9 || SemiY < 1.0 - 1e-9;

    /// <summary>The pupil point a unit-disk sample (u, v) stands for.</summary>
    public (double Px, double Py) Map(double u, double v) => (SemiX * u, CenterPy + SemiY * v);
}

/// <summary>
/// The reference spheres for one field and wavelength, and the chief ray that fixes them
/// (method.md §5.1, §5.2).
/// </summary>
/// <param name="Center">Q′, the reference focus, in the image frame (mm).</param>
/// <param name="PupilPoint">E′, the point the exit reference sphere passes through.</param>
/// <param name="Radius">R′ = |E′Q′|; infinity when the reference is the eikonal's (<see cref="ExitPupil.Infinite"/>).</param>
/// <param name="HPrime">h′, the axial exit pupil's semi-diameter, which reduces the exit coordinates.</param>
/// <param name="HPrimeS">h′_S, the exit sphere's sagittal extent of the pupil's rim, which makes the canonical x′_S.</param>
/// <param name="HPrimeT">h′_T, the same in the meridian, for y′_T.</param>
/// <param name="ChiefPath">The chief ray's optical path from the entrance reference to the exit one (mm).</param>
public sealed record ReferenceGeometry(
    Vec3 Center, Vec3 PupilPoint, double Radius, double ImageIndex, double WavelengthUm, double HPrime,
    Vec3 ChiefImagePoint, Vec3 ChiefDirection, double ChiefLaunchPx, double ChiefLaunchPy, double ChiefPath)
{
    public bool IsInfinite => double.IsInfinity(Radius);

    /// <summary>For an afocal image, the normal of the reference plane through E′: the chief ray's direction. Null otherwise.</summary>
    public Vec3? PlaneNormal { get; init; }

    public bool IsAfocal => PlaneNormal != null;

    /// <summary>
    /// The optical path is taken to the image surface, not to the sphere: no exit reference at all
    /// (<see cref="ExitPupil.ImageSurface"/>). The sphere still gives the pupil coordinates.
    /// </summary>
    public bool PathToImage { get; init; }
    public double HPrimeS { get; init; } = double.NaN;
    public double HPrimeT { get; init; } = double.NaN;
}

/// <summary>Statistics of W over the unvignetted samples, weighted (method.md §7). All in waves.</summary>
/// <param name="Mean">The weighted mean, W̄: the piston.</param>
/// <param name="Rms">The RMS the options ask for.</param>
/// <param name="RmsStandardDeviation">√(⟨W²⟩ - W̄²): Welford eq. 13.2's.</param>
/// <param name="RmsAboutZero">√⟨W²⟩, piston included.</param>
/// <param name="PeakToValley">max W - min W.</param>
/// <param name="StrehlMarechal">1 - (2π σ)², σ the standard deviation in waves (Welford eq. 13.2).</param>
/// <param name="StrehlExponential">exp(-(2π σ)²).</param>
/// <param name="Count">Unvignetted samples.</param>
/// <param name="Vignetted">Samples that did not arrive.</param>
public sealed record WavefrontStatistics(
    double Mean, double Rms, double RmsStandardDeviation, double RmsAboutZero, double PeakToValley,
    double StrehlMarechal, double StrehlExponential, int Count, int Vignetted)
{
    /// <summary>Welford p. 246: the Strehl formula holds only to about λ/14 RMS.</summary>
    public bool StrehlIsValid => RmsStandardDeviation <= 1.0 / 14.0;
}

/// <summary>The wavefront of one field at one wavelength.</summary>
public sealed record WavefrontResult(
    int Field, double FieldValue, int Wavelength, WavefrontOptions Options,
    ReferenceGeometry Geometry, PupilDomain Pupil, IReadOnlyList<WavefrontSample> Samples,
    WavefrontStatistics Statistics, IReadOnlyList<string> Warnings);
