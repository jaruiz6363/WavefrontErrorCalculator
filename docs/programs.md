# Programs

How each optical design program computes its wavefront, stated as WavefrontErrorCalculator's
switches (method.md §5), and how that was established. A program's convention is taken as found
only when a preset reproduces the program's own numbers, ray by ray, on the test lenses. Anything
read from documentation or source code alone is marked as such.

The comparisons hold the glass data equal. Every result file carries the refractive indices the
program used, and WavefrontErrorCalculator traces with them (result-format.md).

## Zemax OpticStudio

**Version:** OpticStudio 2022 R2.02 (the API reports 22.2.2).

**Data:** `tests/TestData/zemax/`. There are 40 files: five lenses (the Kingslake double Gauss,
the 40° Cooke triplet with fixed apertures, the fast lens with two even aspheres, and two finite
conjugates, a 1:1 relay and an NA 0.3 objective), each with:
- Reference OPD (System Explorer → Advanced) set to each of Exit Pupil, Infinity, Absolute and
  Absolute 2;
- ray aiming Off and Real.

OpticStudio's third ray-aiming setting, **Paraxial**, has not been gathered or compared: nothing here or in `docs/opd` covers it. (WEC's own `RayAiming` value named Paraxial is OpticStudio's Off, not this setting.)

The files hold:
- **The OPD of the batch ray trace,** normalised unpolarised with OPD on, at every field and
  wavelength: along the tangential and sagittal fans (41 rays each) and over a 17×17 grid. This
  OPD is the one the OPD fan plots; the fan's text output, to six decimals, agrees with it.
- **The Zernike Standard Coefficients analysis** at "256×256" sampling, 37 terms: the
  coefficients, and the RMS and P-V it reports from the rays.
- **The refractive indices** (the INDX operand) at each surface and wavelength.

### The OPD (`Zemax` preset)

| OpticStudio setting | WavefrontErrorCalculator |
|---|---|
| (always) | `Sign` Hopkins (chief − ray), `ChiefRay` StopCenter, `PupilCoordinates` Launch, `Weighting` PerRay, `ReferenceCenter` ChiefRay |
| Reference OPD: Exit Pupil (the default) | `ExitPupil` ParaxialChiefIntersect: the sphere about the chief ray's image point, through the chief ray's crossing of the paraxial exit-pupil plane |
| Reference OPD: Infinity | `ExitPupil` Infinite: the plane across the chief ray |
| Reference OPD: Absolute, Absolute 2 | `ExitPupil` ImageSurface: no reference sphere; each ray's optical path is taken to the image surface where it lands. The two settings gave the same numbers on every test lens. |
| Ray aiming: Off | `RayAiming` Paraxial: unaimed, launched at the paraxial entrance pupil |
| Ray aiming: Paraxial | not tested |
| Ray aiming: Real | `RayAiming` RealStop, with the stop's radius taken where the primary wavelength's real axial marginal ray crosses it. On axis, an aimed ray is then the ray launched at the same pupil coordinates. |
| Wavelengths other than the primary | `ChromaticReference` PrimarySphere: one reference sphere for every wavelength, the primary's. Each wavelength's OPD is still zero on its own chief ray. |
| Normalised pupil coordinates of a finite object whose entrance pupil lies behind it | `PupilOrientation` EntrancePupilPlane: (Px, Py) is a point of the entrance-pupil plane, so (0, 1) is the ray that meets the lens below the axis. The objective (lens H) needs this; with it, its OPD agrees to 6×10⁻⁸ wave. |

**Agreement:** every ray of every file, at every field and wavelength.
- Ray aiming off: the largest difference is 1.3×10⁻⁸ wave.
- Ray aiming real: at most 5.6×10⁻⁷ wave, except under Absolute, where it reaches 2.5×10⁻⁶ wave.
- The same rays are vignetted in both programs.

With ray aiming on, the remainder grows with the slope of W across the pupil, and is largest under
Absolute, where W spans over 160 waves at full field. Aimed rays landing about 10⁻⁸ of the pupil
radius apart in the two programs would account for it. That points to where each program stops its
aiming iteration rather than to a difference of convention, though it has not been shown directly.

### The Zernike analysis, RMS and P-V (`ZemaxZernike` preset)

The Zernike Standard Coefficients analysis is the `Zemax` preset with one switch changed, under
every Reference OPD and ray-aiming setting checked:
- **`ChromaticReference` OwnChief:** each wavelength is referred to its own chief ray, not to the
  primary's sphere. The OPD of the ray trace and the analysis therefore differ at wavelengths
  other than the primary.
- **Sampling `NodeGrid(255)`:** "256×256" samples 255 nodes a side, from −1 to 1 inclusive. The rim
  is included, so the P-V reaches the edge of the pupil.
- **The fit:** Standard (Noll) ordering, fitted unweighted in launch coordinates.
- **RMS "to chief":** the standard deviation, with piston removed. Its P-V "to chief" is max − min.

**Agreement:** the RMS, P-V and all 37 coefficients match to the eight decimals OpticStudio
prints, at every field and wavelength. The checked results are the double Gauss under all four
Reference OPD settings and with ray aiming on, and the triplet and the fast lens under the default.
- With ray aiming on, the coefficients agree to 4×10⁻⁸ wave.
- One P-V of 44 waves, from a ray grazing the fast lens's asphere, agrees to 1.4×10⁻⁸ of its size.

**Not yet compared:**
- the RMS and P-V "to centroid", which the analysis also reports, with tilt removed as well;
- the Wavefront Map analysis;
- the Fringe and Annular sets;
- vignetting factors;
- afocal mode;
- a defocused image surface.

### Glass

On the double Gauss, whose file names `F4` with no catalog, OpticStudio used Schott F4
(n = 1.616592 at 0.5876 µm), as AberrationCalculator's bundled catalog does. Optiland resolves the
same name to CDGM's F4 (verification.md).

## Optiland

The `Optiland` preset, found from the source and confirmed ray by ray: see verification.md and
method.md §12. It holds on the 1:1 relay as on the other lenses.

**The NA 0.3 objective** (lens H), whose virtual entrance pupil lies behind its object:
- Optiland traces each ray from the object towards that pupil, away from the lens, so its
  wavefront has the opposite sign: +0.62 wave at the rim on axis where the defocus test and
  Zemax give −0.48.
- Negated, it still differs from the preset by up to 7×10⁻² wave, which is not yet explained.

## LensHH-LT

**Version:** 1.0.161 (engine 1870903), the released build's assemblies with its Windows native
core.

**Data:** two sets, gathered from its engine outside this repository, after confirming that the
gatherer reproduces the engine's own map exactly on the map's nodes.
- `tests/TestData/lenshh-lt/`: the five test lenses, with ray aiming Off and Real. Each file holds:
  - the engine's OPD fans (`OpdFan`, 41 points);
  - its wavefront map on a 16×16 grid, ray by ray, with each node's pupil coordinates as the
    engine traces them, px = (j − n/2)/(n/2 − ½) and py = (i − (n/2 − 1))/(n/2 − ½);
  - the RMS and P-V of its 64×64 map;
  - its Zernike Standard and Fringe coefficients (37 terms, from the 64×64 map);
  - the refractive indices it used.
- `tests/TestData/lenshh-lt-opdc/`: the OPD of its wavefront map's own computation at the 857
  OPDC points, for the five lenses with ray aiming off and real. The double Gauss is also gathered
  with only the Schott catalog (`*_Schott`), so that its F4 is OpticStudio's.

### The OPD (`LensHHLT` preset)

LensHH-LT's per-ray OPD is OpticStudio's OPDC, so the `LensHHLT` preset is the `Zemax` one:

| LensHH-LT | WavefrontErrorCalculator |
|---|---|
| (always) | `Sign` Hopkins (chief − ray), `ChiefRay` StopCenter, `PupilCoordinates` Launch, `Weighting` PerRay, `ReferenceCenter` ChiefRay |
| The reference sphere | `ExitPupil` ParaxialChiefIntersect: centred on the primary wavelength's chief-ray image point, through the chief ray's crossing of the paraxial exit-pupil plane, so of radius \|EP\|/\|N\| along the chief ray. Each ray crosses it exactly, on the exit pupil's side of the image |
| Other wavelengths | `ChromaticReference` PrimarySphere: the same sphere for every wavelength |
| Ray aiming Off (as a .zmx file is read) | `RayAiming` Paraxial: unaimed, launched at the paraxial entrance pupil |
| Ray aiming Real | `RayAiming` RealStop, with the stop's radius that of the real axial marginal ray, from the object point for a finite object |
| An entrance pupil behind the object | `PupilOrientation` EntrancePupilPlane; every ray is launched toward the lens |

**Agreement** (`LensHHLTParityTests`): the same rays are vignetted, and the OPD agrees to:

| Data | Aiming off | Aiming real |
|---|---|---|
| Fans and 16×16 map, five lenses | 2.1×10⁻⁸ wave | 2.3×10⁻⁸ wave; 6.7×10⁻⁷ on US8264785 at full field |
| The 857 OPDC points, five lenses | 2.2×10⁻⁸ wave | 3.7×10⁻⁷ wave, on US8264785 at full field |

Against OpticStudio's own OPDC values, ray by ray on the same glass, it agrees to 1.1×10⁻⁶ wave,
and 6.2×10⁻⁶ at US8264785's near-grazing rim (`docs/opd/compare`). That includes both finite-
conjugate lenses: the 1:1 relay with ray aiming on, and the NA 0.3 objective, whose virtual
entrance pupil lies behind its object.

### Statistics and Zernike coefficients

- **RMS and P-V:** the standard deviation of the 64×64 map, and its max − min. With the `LensHHLT`
  preset at the engine's own 64×64 nodes, they agree to 10⁻⁶ of their size.
- **Zernike coefficients:** LensHH-LT fits them at px = −1 + 2(j + ½)/n, py = −1 + 2(i + ½)/n,
  which are half a node from where the map was traced and (n − 1)/n its scale.
  - Fitted at those coordinates, WavefrontErrorCalculator's wavefront gives LensHH-LT's
    coefficients (to 10⁻⁶ wave on axis, 5×10⁻³ off axis).
  - Fitted where it was traced, the coefficients differ by up to 0.17 wave on the double Gauss.
    On axis, where the wavefront is symmetric, a tilt appears.

### Glass

With every catalog loaded, LensHH-LT resolves the double Gauss's uncatalogued `F4` to CDGM
(n = 1.620047 at 0.5876 µm), as Optiland does; OpticStudio takes Schott's (1.616592). The
comparisons are made at LensHH-LT's own indices, and the double Gauss also on a Schott-only gather.

## OSLO

**Version:** OSLO EDU 6.6.

**Data:** `tests/TestData/oslo/`, with 30 files. They cover the five test lenses, under ray aiming
`enp` (the entrance pupil) and `crr` (the central reference ray, OSLO's default), with the reference
sphere at the exit pupil (`wrsp xpu`, the default), at infinity and at the last surface. OSLO is
run by hand: a CCL macro, kept outside this repository, traces single rays at OSLO's own
fractional pupil coordinates (FY, FX) along both fans and over a 17 × 17 grid, at every field and
wavelength. For each ray it writes the OPD, the indices OSLO used, and the reference sphere radius
OSLO reports.

### The OPD

| OSLO | WavefrontErrorCalculator |
|---|---|
| (always) | `Sign` Hopkins, `PupilCoordinates` Launch, `ReferenceCenter` ChiefRay |
| The reference ray, aimed at the centre of the real stop in both aiming modes | `ChiefRay` RealStopCenter. In entrance pupil mode it is not the ray at (FY, FX) = (0, 0), whose OPD is therefore not quite zero |
| Each wavelength, with its reference ray traced at that wavelength | `ChromaticReference` OwnChief |
| Reference sphere at infinity | `ExitPupil` Infinite |
| Reference sphere at the last surface | `ExitPupil` LastSurface: through the reference ray's crossing of the last surface before the image (matches OSLO's reported radius to 1e-12 mm) |
| Reference sphere at the exit pupil | the real exit pupil for each field, as OSLO's help says. On axis it is each wavelength's paraxial exit pupil. Off axis its radius grows with the field squared, by a rule not yet identified. The tests take the radius OSLO reports (`ExitPupil` UserRadius) |
| Ray aiming `enp`, object at infinity | `RayAiming` Aplanatic, which is the paraxial entrance pupil's own coordinates |
| Ray aiming `crr`, object at infinity | `RayAiming` AplanaticReference: the same grid, moved so that (0, 0) is the reference ray |
| Ray aiming `enp`, finite object | `RayAiming` Aplanatic: for an object NA of 0.1 or more, linear in the direction sines across the paraxial entrance pupil as the object point sees it, and the pupil plane below that; (FY, FX) name points of the pupil's plane (`PupilOrientation` EntrancePupilPlane) |
| Ray aiming `crr`, finite object | `RayAiming` AplanaticReference: the same, with the disc centred where the reference ray crosses the pupil plane; upright |

For a finite object, OSLO maps by direction sines only when the object NA is 0.1 or more. Below
that it maps the pupil plane linearly, as for an object at infinity. Two probes established this.
On the relay, NA 0.1 maps by sines at object distances from 192 mm to 1001 mm. NA 0.095, 0.090, …
down to 0.001 map by the plane. The switch lies between NA 0.095 and 0.100. The Cooke triplet (NA
0.005) and the fast lens (NA 0.0003) map by the plane at every distance tried. Turning on OSLO's
`epxy` (plane aiming) forces the plane mapping. `RayAiming` Aplanatic and AplanaticReference apply
this rule themselves (`LensModel.AplanaticNaThreshold`).

**Agreement:** every ray of all 30 files, at every field and wavelength, to below 1.1×10⁻⁸ wave.
The same rays are vignetted in both programs.

The objective, whose entrance pupil lies behind its object, comes out with the right sign. OSLO
traces its rays towards the lens, unlike Optiland and LensHH-LT.

**Not yet compared:**
- the rule for the exit pupil sphere's radius. It is the same in both aiming modes and symmetric
  about the axis. It grows as R = R₀ + k·FBY², with k itself rising slowly with field: from 3.623
  to 3.636 on the double Gauss, and from 6.19 to 7.54 on the Cooke triplet. None of these fits it:
  - the tangential or sagittal image of the stop's centre;
  - the reference ray's crossing of the paraxial exit pupil's plane or sphere;
  - the sphere through the axial pupil point;
- the unit radius of OSLO's Zernike fit off axis (below).

### Statistics and Zernike coefficients

OSLO computes both from its spot diagram. With the default 17.03 aperture divisions this is a
grid of cell centres spaced 2/17.03 of the pupil, scaled by 0.999724, inside the unit circle:
232 rays, each counted once. The 0.999724 was found from the double Gauss on axis and then holds
for all 30 files.

- **`wavefront(ref)`:** the standard deviation and max − min of W over those rays. Reproduced
  to 8×10⁻⁹ wave on all 30 files.
- **`wavefront()`, the default:** the same after the lateral shift of the reference point that
  minimises the RMS. That shift removes tilt linear in where the rays cross the reference sphere
  (Welford eq. 7.18), or linear in their image-space directions when the reference is at
  infinity. Reproduced to 2.1×10⁻⁸ wave on all 30 files.
- **`zernike_fit()`:** Fringe order, 36 terms, unweighted.
  - It is fitted in exit-sphere coordinates (where each ray crosses the reference sphere), not in
    (FY, FX), with OSLO's angle measured from the y-axis.
  - On axis the unit radius is the rim ray's (FY = 1) exit radius at that wavelength. That gives
    the double Gauss's coefficients to 2×10⁻⁹ wave at its primary wavelength.
  - Elsewhere the best-fitting unit differs from the rim ray's by up to 2×10⁻⁴ of it.
  - On the NA 0.3 objective and the fast aspheric lens, a misfit of 10⁻⁷ to 10⁻⁵ wave remains
    whatever the unit, against coefficients of order a wave.
  - Off axis the unit departs further, by 0.2–0.5 % of the rim ray's at full field.
  - Ray weights were tested (exit-pupil area, its inverse) and change none of this. The tests
    check the on-axis fit to 2×10⁻⁴ wave.

### Glass

OSLO resolves the double Gauss's uncatalogued `F4` to Schott's F4, as Zemax does.
