namespace WavefrontErrorCalculator.Core;

/// <summary>Where the exit reference sphere is centred: the reference focus Q′ (method.md §5.1).</summary>
public enum ReferenceCenter
{
    /// <summary>Where the chief ray meets the image surface (Hopkins 1981 §4; Welford §7.2).</summary>
    ChiefRay,
    /// <summary>The paraxial image point (Wolf 1952).</summary>
    Gaussian,
    /// <summary>The weighted centroid of the ray intersections in the image plane.</summary>
    Centroid,
    /// <summary>The centre that minimises the variance of W over the pupil (Rimmer 1970).</summary>
    BestFitSphere,
    /// <summary>A centre given in <see cref="WavefrontOptions.UserCenter"/>.</summary>
    User,
}

/// <summary>Which point the exit reference sphere passes through, E′, and so its radius (method.md §5.2).</summary>
public enum ExitPupil
{
    /// <summary>Off axis, where the real chief ray crosses the axis; on axis, the paraxial exit pupil (Hopkins 1981 eq. 4.31).</summary>
    RealChief,
    /// <summary>The axial point of the paraxial exit pupil, at the primary wavelength (Optiland).</summary>
    ParaxialAxial,
    /// <summary>Where the chief ray crosses the paraxial exit-pupil plane.</summary>
    ParaxialChiefIntersect,
    /// <summary>A plane perpendicular to the chief ray: a sphere of infinite radius (Welford p. 101).</summary>
    Infinite,
    /// <summary>The axial point at <see cref="WavefrontOptions.UserExitPupilDistance"/> from the image.</summary>
    UserDistance,
    /// <summary>
    /// The sphere about Q′ whose radius is the paraxial exit pupil's axial distance from Q′, at the
    /// primary wavelength; E′ is where the chief ray meets it.
    /// </summary>
    ParaxialDistance,
    /// <summary>
    /// The sphere about Q′ of radius <see cref="WavefrontOptions.UserReferenceRadius"/>; E′ is where
    /// the chief ray meets it on the exit pupil's side. For a radius another program reports, such
    /// as OSLO's "real exit pupil for each field" (docs/programs.md).
    /// </summary>
    UserRadius,
    /// <summary>
    /// E′ where the chief ray crosses the last surface before the image; the sphere about Q′
    /// through it (OSLO's "last surface" reference sphere position).
    /// </summary>
    LastSurface,
    /// <summary>
    /// No exit reference: each ray's optical path is taken to the image surface itself, where it
    /// lands, so W carries the path along the ray aberration as well as the wavefront (Zemax
    /// OpticStudio's Reference OPD "Absolute"). Not a wavefront aberration in Hopkins's sense.
    /// The pupil coordinates are those of the sphere through the chief ray's crossing of the
    /// paraxial exit-pupil plane.
    /// </summary>
    ImageSurface,
}

/// <summary>
/// What a launch coordinate (Px, Py) = (0, 1) names when the paraxial entrance pupil is virtual and
/// lies behind the object, so its paraxial diameter is negative (method.md §5.3). Elsewhere the two
/// agree.
/// </summary>
public enum PupilOrientation
{
    /// <summary>The paraxial marginal ray: the ray that meets the lens on the +y side.</summary>
    MarginalRay,
    /// <summary>The point (0, +r) of the entrance-pupil plane, which that ray's line crosses; it meets the lens on the -y side (Zemax).</summary>
    EntrancePupilPlane,
}

/// <summary>How a ray's launch point is found (method.md §5.3).</summary>
public enum RayAiming
{
    /// <summary>At the paraxial entrance pupil, unaimed.</summary>
    Paraxial,
    /// <summary>At the real stop, by AberrationCalculator's StopAimer.</summary>
    RealStop,
    /// <summary>Iterated until the ray lands on a target point of the exit reference sphere.</summary>
    ExitSphereGrid,
    /// <summary>
    /// OSLO's entrance pupil mode: the paraxial entrance pupil for an object at infinity, or an
    /// object NA below 0.1; otherwise coordinates linear in the direction sines across the paraxial
    /// entrance pupil as seen from the object point (<see cref="LensModel.AplanaticLaunch"/>).
    /// </summary>
    Aplanatic,
    /// <summary>
    /// OSLO's central reference ray mode, its default: as <see cref="Aplanatic"/>, centred on the
    /// ray aimed at the centre of the real stop.
    /// </summary>
    AplanaticReference,
    /// <summary>
    /// The paraxial entrance pupil's own coordinates, moved so that (0, 0) is the ray aimed at the
    /// centre of the real stop: OSLO's central reference ray mode where it maps the pupil plane
    /// linearly (docs/programs.md).
    /// </summary>
    ParaxialReference,
}

/// <summary>Which ray is the chief ray (method.md §5.4).</summary>
public enum ChiefRayDefinition
{
    /// <summary>The ray through the centre of the stop.</summary>
    StopCenter,
    /// <summary>The central ray of the pencil the lens actually accepts (Hopkins 1981 §8).</summary>
    VignettedCenter,
    /// <summary>
    /// The ray aimed at the centre of the real stop, whatever the other rays' aiming: with
    /// <see cref="RayAiming.Paraxial"/>, a chief ray that is not the ray launched at the centre of
    /// the paraxial entrance pupil, so that ray's W is not quite zero (OSLO).
    /// </summary>
    RealStopCenter,
}

/// <summary>The coordinates W is mapped, fitted and integrated on (method.md §5.6).</summary>
public enum PupilCoordinates
{
    /// <summary>The normalised launch coordinates (Px, Py).</summary>
    Launch,
    /// <summary>The ray's point on the exit reference sphere, reduced by h′.</summary>
    ExitSphere,
    /// <summary>Exit-sphere coordinates scaled so the actual pupil is a unit circle (Hopkins 1964).</summary>
    Canonical,
    /// <summary>Launch-grid samples moved to their exit positions to first order (Singh 1976 eq. 13).</summary>
    LaunchRefined,
}

/// <summary>How each sample is weighted in a pupil integral (method.md §5.7).</summary>
public enum Weighting
{
    /// <summary>Every unvignetted ray counts once.</summary>
    PerRay,
    /// <summary>By the area of the exit pupil the ray stands for.</summary>
    ExitArea,
    /// <summary>By the quadrature rule the samples were placed with.</summary>
    Quadrature,
}

/// <summary>How a change of focus is applied (method.md §5.8).</summary>
public enum DefocusMethod
{
    /// <summary>Move the image surface and trace again.</summary>
    Retrace,
    /// <summary>Add N′δ₀W₂₀(x′² + y′² + z′²) (Hopkins 1981 eq. 6.7).</summary>
    ExactTerm,
    /// <summary>Add δ₀W₂₀(x′² + y′²), the common shortcut.</summary>
    ParaxialTerm,
}

/// <summary>The sign of W (method.md §5.9).</summary>
public enum WavefrontSign
{
    /// <summary>Chief ray's optical path minus the ray's (Hopkins; Welford).</summary>
    Hopkins,
    /// <summary>The ray's optical path minus the chief ray's (Wolf; Born &amp; Wolf).</summary>
    Wolf,
}

/// <summary>What each wavelength's wavefront is referred to (method.md §5.10).</summary>
public enum ChromaticReference
{
    /// <summary>The primary wavelength's reference focus (Hopkins 1981 §7).</summary>
    PrimaryFocus,
    /// <summary>
    /// The primary wavelength's whole reference sphere, its centre and the point where it meets the
    /// primary chief ray, for every wavelength: one sphere for all. Each wavelength's W is still
    /// zero on its own chief ray.
    /// </summary>
    PrimarySphere,
    /// <summary>Each wavelength's own chief ray.</summary>
    OwnChief,
    /// <summary>Conrady's differential chromatic aberration, along image-space associated rays (Hopkins 1981 §7). Not yet available.</summary>
    Conrady,
}

/// <summary>Which surfaces' apertures stop rays (method.md §5.4). The stop is never one: the pupil fills it.</summary>
public enum ApertureClipping
{
    /// <summary>Only apertures the file holds fixed. A solved semi-diameter is the beam's own size and stops nothing.</summary>
    Fixed,
    /// <summary>Every surface with a semi-diameter.</summary>
    All,
    /// <summary>None: every ray of the unit pupil is traced through.</summary>
    None,
}

/// <summary>What the RMS is taken about (method.md §7).</summary>
public enum RmsDefinition
{
    /// <summary>The standard deviation: piston removed (Welford eq. 13.2).</summary>
    StandardDeviation,
    /// <summary>The root mean square about zero: piston kept (Optiland's chief-ray reference).</summary>
    AboutZero,
}

/// <summary>
/// Every convention that decides what number a wavefront error comes out as.
///
/// <para><see cref="Reference"/> is Hopkins and Welford: the definition this program exists to
/// compute. The other presets are what reading each program's source found it does, to be
/// confirmed by fingerprinting against its output (method.md §10).</para>
/// </summary>
public sealed record WavefrontOptions
{
    public ReferenceCenter ReferenceCenter { get; init; } = ReferenceCenter.ChiefRay;
    public ExitPupil ExitPupil { get; init; } = ExitPupil.RealChief;
    public RayAiming RayAiming { get; init; } = RayAiming.RealStop;
    public ChiefRayDefinition ChiefRay { get; init; } = ChiefRayDefinition.VignettedCenter;
    public PupilCoordinates PupilCoordinates { get; init; } = PupilCoordinates.Canonical;
    public Weighting Weighting { get; init; } = Weighting.ExitArea;
    public DefocusMethod Defocus { get; init; } = DefocusMethod.Retrace;
    public WavefrontSign Sign { get; init; } = WavefrontSign.Hopkins;
    public ChromaticReference ChromaticReference { get; init; } = ChromaticReference.PrimaryFocus;

    /// <summary>What the launch coordinates name when the entrance pupil lies behind the object.</summary>
    public PupilOrientation PupilOrientation { get; init; } = PupilOrientation.MarginalRay;
    public RmsDefinition Rms { get; init; } = RmsDefinition.StandardDeviation;
    public ApertureClipping Apertures { get; init; } = ApertureClipping.Fixed;

    /// <summary>
    /// How far to move the image plane along the axis, in mm, in the image space's own frame
    /// (+z is the frame's axis, which after an odd number of mirrors points against the light).
    /// Applied by <see cref="Defocus"/>.
    /// </summary>
    public double FocusShift { get; init; }

    /// <summary>Count a vignetted ray as W = 0 rather than leaving it out (Optiland's RMS vs field).</summary>
    public bool IncludeVignettedAsZero { get; init; }

    /// <summary>Report W as a geometric length, the optical path divided by n′ (Wolf 1952 eq. 4).</summary>
    public bool DivideByImageIndex { get; init; }

    /// <summary>The reference focus in image-plane coordinates (x, y), for <see cref="ReferenceCenter.User"/>.</summary>
    public (double X, double Y)? UserCenter { get; init; }

    /// <summary>The exit pupil's distance from the image, for <see cref="ExitPupil.UserDistance"/>.</summary>
    public double? UserExitPupilDistance { get; init; }

    /// <summary>The reference sphere's radius, for <see cref="ExitPupil.UserRadius"/>.</summary>
    public double? UserReferenceRadius { get; init; }

    /// <summary>Hopkins and Welford.</summary>
    public static WavefrontOptions Reference { get; } = new();

    /// <summary>Optiland's wavefront with strategy "chief_ray", as its source reads (Optiland a3fb3e1b).</summary>
    public static WavefrontOptions Optiland { get; } = new()
    {
        ExitPupil = ExitPupil.ParaxialAxial,
        RayAiming = RayAiming.Paraxial,
        ChiefRay = ChiefRayDefinition.StopCenter,
        PupilCoordinates = PupilCoordinates.Launch,
        Weighting = Weighting.PerRay,
        ChromaticReference = ChromaticReference.OwnChief,
        Rms = RmsDefinition.AboutZero,
    };


    /// <summary>
    /// Zemax OpticStudio's OPD - its OPD fan and the OPD of its batch ray trace - with Reference
    /// OPD "Exit Pupil" (its default) and ray aiming off, as found by comparing ray by ray
    /// (docs/programs.md). With ray aiming on, set RayAiming to RealStop; Reference OPD "Infinity"
    /// is ExitPupil Infinite, and "Absolute" ExitPupil ImageSurface.
    /// </summary>
    public static WavefrontOptions Zemax { get; } = new()
    {
        ExitPupil = ExitPupil.ParaxialChiefIntersect,
        RayAiming = RayAiming.Paraxial,
        ChiefRay = ChiefRayDefinition.StopCenter,
        PupilCoordinates = PupilCoordinates.Launch,
        Weighting = Weighting.PerRay,
        ChromaticReference = ChromaticReference.PrimarySphere,
        PupilOrientation = PupilOrientation.EntrancePupilPlane,
    };

    /// <summary>
    /// LensHH-LT's OPD fan and wavefront map, with ray aiming off (as it reads a .zmx file), as
    /// found by comparing ray by ray (docs/programs.md). LensHH-LT computes OpticStudio's OPDC, so
    /// these are the <see cref="Zemax"/> conventions; with ray aiming on, set RayAiming to RealStop.
    /// Declared after it, since static initialisers run in order.
    /// </summary>
    public static WavefrontOptions LensHHLT { get; } = Zemax;

    /// <summary>
    /// Zemax OpticStudio's Zernike Standard Coefficients analysis, and its RMS and P-V: as
    /// <see cref="Zemax"/>, but each wavelength referred to its own chief ray. Sampled on
    /// <see cref="Sampling.NodeGrid"/> of n - 1 nodes for OpticStudio's n x n (docs/programs.md).
    /// </summary>
    public static WavefrontOptions ZemaxZernike { get; } = Zemax with { ChromaticReference = ChromaticReference.OwnChief };

    /// <summary>The preset of this name, ignoring case.</summary>
    public static WavefrontOptions Preset(string name) => name.ToLowerInvariant() switch
    {
        "reference" or "hopkins" or "welford" => Reference,
        "optiland" => Optiland,
        "lenshhlt" or "lenshh-lt" => LensHHLT,
        "zemax" or "opticstudio" => Zemax,
        "zemaxzernike" => ZemaxZernike,
        _ => throw new ArgumentException($"no preset '{name}'; there are Reference, Optiland, LensHHLT, Zemax and ZemaxZernike", nameof(name)),
    };
}
