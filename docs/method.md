# WavefrontErrorCalculator — Specification

Revised 2026-10-06. Author: Javier Ruiz, with Claude Code.

## 1. Purpose

WavefrontErrorCalculator (WEC) is a reference for deciding whether an optical design program's wavefront error is correct. It computes the wavefront error of an optical system from real-ray tracing, using AberrationCalculator for lens input, the paraxial trace and the real-ray trace.

Programs disagree about the OPD of the same lens, and agreement between two of them proves only that they are consistent: they can share a convention, or an error. WEC settles the question in two steps:

1. **It is shown to be right on its own.** W is computed exactly as defined by H. H. Hopkins (1950, 1952, 1981) and W. T. Welford (1986), with every convention stated and every approximation either removed or labelled, and checked without appeal to any other program: against cases with exact answers and the small-aperture limit of aberration theory (§9.1), against internal consistency checks, and against an independent method, Rayces's exact relation integrated from the rays' directions alone (§3.4, §9.2).
2. **Each program is then judged against it.** Every choice that makes optical design programs disagree is exposed as a switch, so WEC can compute W exactly as a program defines it. Matching LensHH-LT, Optiland, Zemax OpticStudio and OSLO identifies the conventions each uses (§10, §12). Under a program's own conventions, any difference that remains is an error in that program, not a matter of definition.

Non-goals for v1: polarization, apodization (non-uniform pupil amplitude), diffraction-based PSF/MTF (W is produced in a form ready for them; see §11), non-sequential systems, gradient-index media.

## 2. Sources

| Ref | Source | Used for |
|---|---|---|
| H50 | H. H. Hopkins, *Wave Theory of Aberrations* (1950), ch. I and X | Definition, focal-shift formulas (eqs. 11–19), per-surface transfer term (eq. 195) |
| H52 | H. H. Hopkins, Proc. Phys. Soc. B 65, 934 (1952) | Invariant foci; equally-inclined chord formula |
| H64 | H. H. Hopkins, Jpn. J. Appl. Phys. 4 Suppl. 1, 31 (1965) | Canonical pupil coordinates |
| HY70 | Hopkins & Yzuel, Optica Acta 17, 157 (1970) | Diffraction integral on the reference sphere; exact defocus term with z′² |
| H81 | H. H. Hopkins, Optica Acta 28, 667 (1981) | **The reference algorithm** (W′ = Ω′ + δΩ′), exit pupil choice, reduced coordinates |
| W86 | W. T. Welford, *Aberrations of Optical Systems* (1986), ch. 4, 7, 13 | Definition (eq. 7.9), centre shift (7.18), radius change (§7.4), direct-sum formula (7.29), Strehl/RMS (13.2), P-V (§13.3), large-field pupil coordinates (§13.6) |
| Wo52 | E. Wolf, JOSA 42, 547 (1952) | Gaussian-reference-sphere convention (opposite sign); ray–wave relation exact only to sixth order |
| R64 | J. L. Rayces, Optica Acta 11, 85 (1964) | Exact ray–wave relation ∂W/∂x = −X/(R−W) |
| R70 | M. Rimmer, Appl. Opt. 9, 533 (1970) | Least-squares best reference sphere |
| K68 | W. B. King, Appl. Opt. 7, 489 (1968) | Variance on the equivalent circular pupil |
| S76 | R. N. Singh, Optica Acta 23, 621 (1976) | Correcting W from launch to exit-pupil coordinates (eq. 13) |
| M71 | J. Macdonald, Optica Acta 18, 269 (1971) | Canonical coordinates in OTF work; exact defocus |

The papers are cited by these keys throughout.

## 3. Definitions

### 3.1 The wavefront aberration

For an object point Q with principal (chief) ray QEP₁…P_kE′, and an aperture ray QBP₁…P_kB′ (H81 eq. 3.1; W86 eq. 7.9):

> **W′(ray) = [B P₁ … P_k B′] measured along the chief ray − the same measured along the ray**
>
> W′ = [E P₁ … P_k E′] − [B P₁ … P_k B′]

- B, E lie on the **entrance reference sphere**: centre Q, through the entrance-pupil point E. For an object at infinity this is a plane through E perpendicular to the chief ray.
- B′, E′ lie on the **exit reference sphere**: centre Q′ (the reference focus, §5.1), through the exit-pupil point E′ (§5.2). For an image at infinity (afocal) it is a plane through E′ perpendicular to the chief ray.
- Square brackets are optical path lengths, Σ nᵢ·(geometric length).
- **Sign:** W′ > 0 when the ray's optical path is *shorter* than the chief ray's, i.e. the wavefront is ahead of the reference sphere. This is the Hopkins/Welford sign. Wolf (Wo52) and Born & Wolf use the opposite sign; it is available as a switch (§5.9).
- **Units:** the native unit is optical path in lens units (mm). Results are reported in waves at the analysis wavelength (vacuum λ) and in µm.

### 3.2 Pupil coordinates

Every traced ray carries three coordinate sets:

| Set | Symbol | Definition |
|---|---|---|
| Launch | (Px, Py) | Normalized entrance-pupil coordinates used to aim the ray: the fraction of the paraxial entrance-pupil radius. This is what most programs label "pupil coordinates". |
| Exit sphere | (x′, y′, z′) | The ray's point B′ on the exit reference sphere, relative to E′, divided by the axial exit-pupil semi-height h′ (H81 eqs. 5.1, 5.7). Transverse components are measured in a plane **perpendicular to the optical axis**, not to the chief ray (W86 §13.6; HY70 §2). |
| Canonical | (x′_S, y′_T) | Exit-sphere coordinates scaled by the sagittal and tangential semi-heights h′_S and N′h′_T of the actual (possibly vignetted) pencil, so that the pupil becomes a unit circle (H64; HY70 eq. 14; M71 §4). |

Both Hopkins and Welford state that W is a function of the coordinates of B′ on the reference sphere. The launch coordinates equal the exit-sphere coordinates only for an isoplanatic image (H64 eq. 14).

### 3.3 The ray–wave relation

Reduced transverse aberrations (H81 eqs. 5.9–5.13): δG′ = n′ sin α′·δξ′ and δH′ = n′ sin α′·δη′, measured from Q′ in the image plane.

They relate to W by N′δG′ = −∂W/∂x′ and N′δH′ = −∂W/∂y′ (H81 eq. 10.30; W86 eq. 7.14). WEC uses this relation only as a consistency check (§9.2), never to construct W.

### 3.4 W from the ray aberrations alone (R64)

Rayces's relation is exact: ∂W/∂x = −X/(R − W), ∂W/∂y = −Y/(R − W) (R64 eq. 8), with W Nijboer's - measured along the reference sphere's radius, W = R − |QP| for P on the wavefront - and X, Y where the ray crosses the plane through Q across QC. `RaycesIntegration` (`wfe rayces`) integrates it across the pupil from the chief ray, where W = 0 at C, using only each ray's image-space line: no optical path. It is the independent check of §4.

- **Frame:** Q at the origin, z along Q→C. Rayces needs only Q on the y-axis and C on the z-axis, so this serves any sphere through a point of the chief ray: E′ `RealChief`, `ParaxialChiefIntersect` and the others.
- **The ODE:** P lies on its ray at |QP| = R − W, so it moves with W. Along the straight pupil path from the chief ray to a point, W(t) is found by the implicit trapezoidal rule, symmetric so its error is in even powers of the step, and Romberg extrapolation over four step sizes. A point whose last extrapolation changes it by more than 10⁻¹⁰ wave is done again with four times the steps.
- **To the along-ray W:** Nijboer's W differs from the along-ray W the programs report by about ½·W·θ², θ being the angle at P between the ray and the sphere's radius (θ ≈ ε/R for a ray that misses Q by ε); exactly, W_along-ray = |√(R² − r² sin²θ) − r cos θ| with r = R − W. At the point, the ray is followed from P to the reference sphere: n′ times that distance is its optical path beyond the wavefront, the W of §3.1.
- **Agreement:** to 10⁻¹⁰ wave on the double Gauss, the relay and the objective, 10⁻⁹ on the Cooke triplet and 7×10⁻⁹ on US8264785 (docs/verification.md).

## 4. Reference algorithm

### 4.1 Overview

For each field point and wavelength:

1. Find the pupil: run the pupil exploration (§5.4) to find the rim of the accepted pencil.
2. Find the chief ray (§5.4).
3. Fix the reference geometry: E (entrance), E′ (exit pupil point, §5.2), Q′ (reference focus, §5.1), and the sphere radius R′ = |E′Q′|.
4. Trace the chief ray and accumulate its optical path from E to E′.
5. For each aperture ray: aim it (§5.3), trace it, accumulate its optical path from B to B′, compute W′ = OPL_chief − OPL_ray, and compute (x′, y′, z′), (δG′, δH′) and the ray's area weight (§6.2).
6. Post-process: piston/tilt/focus removal, RMS, P-V, Zernike fit (§7).

### 4.2 Optical path computation

**Default method: direct sum in double precision** (W86 eq. 7.25):

OPL = n₀·[B P₁] + Σⱼ nⱼ·|P_{j+1} − P_j| + n_k·[P_k B′]

In double precision the rounding floor for a 1 m system is about 10⁻¹³ mm, far below a wavelength. Welford's and Hopkins's stable reformulations (W86 eq. 7.29; H81 eqs. 3.9–3.22) were designed for six-digit computers.

The first segment depends on the object:
- **Finite object:** the entrance reference sphere is centred on Q, so it is an equiphase surface, and the path is measured from Q itself: n₀·|P₁ − Q|.
- **Object at infinity:** the path is measured from a fixed reference plane perpendicular to the chief-ray direction **d**: n₀·**d**·(P₁ − O), where O is any fixed point. Every ray in the collimated beam shares **d**, so the constant cancels in W.

Mirrors take a negative n after reflection, as in AberrationCalculator's existing convention.

The last segment ends on the exit reference sphere: solve |P_k + t·**d**_k − Q′|² = R′² for t. Use the numerically stable root form (W86 eq. 4.12; H81 eq. 4.21), and choose the root whose point B′ is nearest E′. The segment may be traced backwards from the image plane; the result is the same line.

**Cross-check method: Hopkins chord formulation** (H81 eqs. 3.9, 3.12–3.22, 4.21). Implemented for centred systems only. It must agree with the direct sum to 10⁻⁶ wave on every test lens. It also gives the invariant aberration Ω′ separately from the focal-shift term δΩ′, which is useful in reports because Ω′ does not depend on where E′ is.

### 4.3 Afocal systems

When the image is at infinity, W is referred to a plane through E′ perpendicular to the chief ray, and the transverse aberrations are reported as angular reduced aberrations (H81 §5: the reduced forms stay finite). Auto-detection: the paraxial image distance exceeds 10⁶ × EFL, or the file's afocal flag is set.

## 5. Convention switches

Each switch has a **reference default** (Hopkins/Welford) plus the alternatives that other programs are known to use or might use.

### 5.1 Reference focus Q′ (`ReferenceCenter`)

| Value | Definition | Source |
|---|---|---|
| **`ChiefRay`** (default) | Chief ray ∩ image surface | H81 §4; W86 §7.2 |
| `Gaussian` | Paraxial image point η′_G | Wo52 |
| `Centroid` | Weighted centroid of ray intersections in the image plane | Optiland `centroid` |
| `BestFitSphere` | Q′ (3 degrees of freedom) chosen to minimise the variance of W over the pupil, with R′ tied to E′ | R70; W86 §13.3 "with choice of focus/lateral shift" |
| `User` | Given coordinates (G̃′₀, H̃′₀) or (ξ′, η′) | H81 §4 |

`BestFitSphere` is solved by linearization and then refined exactly. The linear model is δW = N′(δG′₀x′ + δH′₀y′) + N′δ₀W₂₀(x′² + y′² + z′²) (H81 eqs. 6.7, 6.16; W86 eq. 7.18). Solve the weighted least-squares problem for (δG′₀, δH′₀, δ₀W₂₀), move Q′, re-trace, and iterate until the change is below 10⁻⁶ wave RMS.

### 5.2 Exit-pupil point E′ and sphere radius (`ExitPupil`)

| Value | E′ | R′ | Source / user |
|---|---|---|---|
| **`RealChief`** (default) | Off axis: the point where the real chief ray crosses the optical axis (closest approach for a skew chief ray). On axis: the paraxial exit pupil. | \|E′Q′\| | H81 §4, eq. 4.31 |
| `ParaxialAxial` | Axial point of the paraxial exit pupil, at the primary wavelength | \|E′Q′\| | Optiland |
| `ParaxialChiefIntersect` | Chief ray ∩ the paraxial exit-pupil plane | \|E′Q′\| | H81 §2 general form; OpticStudio and LensHH-LT |
| `Infinite` | Reference plane perpendicular to the chief ray | ∞ | W86 p. 101 (the eikonal) |
| `UserDistance` | Axial point at a given distance from the image | \|E′Q′\| | — |
| `ParaxialDistance` | Chief ray ∩ the sphere about Q′ whose radius is the paraxial exit pupil's axial distance from Q′ | \|z_XP − z_Q′\| | — |
| `UserRadius` | Chief ray ∩ the sphere about Q′ of a given radius | given | OSLO's reported exit-pupil radius |
| `LastSurface` | Chief ray ∩ the last surface before the image | \|E′Q′\| | OSLO `wrsp lsf` |
| `ImageSurface` | No exit reference: each ray's path is taken to the image surface where it lands. Pupil coordinates as for `ParaxialChiefIntersect`. Not a wavefront aberration in Hopkins's sense: W also carries the path along the ray aberration. | — | Zemax "Absolute" (docs/programs.md) |

Expected size of the effect: changing the radius by δR changes W by about N′θ²δR/2, where θ is the angular ray aberration (W86 §7.4; H50 eq. 13: −N(1−cos θ)δX). WEC reports this predicted difference next to the measured one, as a check.

### 5.3 Ray aiming (`RayAiming`)

| Value | Definition |
|---|---|
| `Paraxial` | Launch towards the paraxial entrance pupil at (Px, Py), unaimed (AberrationCalculator's current behaviour). This is OpticStudio's ray aiming **Off**. OpticStudio also has a ray-aiming mode it calls Paraxial; that is a different setting, and WEC has not been compared against it (docs/programs.md). |
| **`RealStop`** (default) | Solve for the launch so the ray meets the real stop at (Px, Py) × stop radius. Port GhostAnalysis's Broyden solver (`GhostTracer.cs:188-264`). The stop radius is where the primary wavelength's real axial marginal ray crosses the stop: the stop the beam actually fills, the same aperture for every wavelength. On axis an aimed ray is therefore the ray launched at the same coordinates. (Zemax's real ray aiming scales the stop the same way.) |
| `ExitSphereGrid` | Iterate the launch until the ray's exit-sphere or canonical coordinates equal the target (x′, y′). Used to sample uniformly in exit coordinates (S76 §4 "iterative ray-tracing"). |
| `Aplanatic` | OSLO's entrance pupil mode: the paraxial entrance pupil for an object at infinity or an object NA below 0.1; otherwise linear in the direction sines across the paraxial entrance pupil as seen from the object point |
| `AplanaticReference` | OSLO's central reference ray mode: as `Aplanatic`, centred on the ray aimed at the real stop's centre |
| `ParaxialReference` | The paraxial entrance pupil's coordinates, moved so that (0, 0) is the ray aimed at the real stop's centre |

**Pupil orientation (`PupilOrientation`).** A finite object's virtual entrance pupil can lie *behind* the object, on the far side from the lens, as in a short-conjugate objective whose stop is imaged back past its object. The paraxial entrance-pupil diameter is then negative. The light still leaves the object towards the lens: tracing from the object towards that pupil runs every ray backwards and negates its optical path (fixed in AberrationCalculator 3b20f5d). What (Px, Py) = (0, 1) names is then a convention:

| Value | (0, 1) is | Source |
|---|---|---|
| **`MarginalRay`** (default) | the paraxial marginal ray: the ray that meets the lens on the +y side | AberrationCalculator |
| `EntrancePupilPlane` | the point (0, +r) of the entrance-pupil plane, which that ray's line crosses; the ray meets the lens on the −y side, and the stop target is flipped likewise | Zemax |

When the pupil is in front of the object, or the object is at infinity, the two agree.

### 5.4 Chief ray and pupil exploration (`ChiefRay`)

| Value | Definition | Source |
|---|---|---|
| `StopCenter` | The ray through the centre of the stop (Px = Py = 0 under `RealStop`) | Optiland, Powell 1978 |
| **`VignettedCenter`** (default) | The central ray of the accepted pencil. Find the upper, lower and sagittal rim limits by bisection to 10⁻⁶ of the pupil radius; the chief ray is the midpoint of the tangential limits (in a symmetric system the sagittal limit is symmetric). | H81 §8; HY70 §4; S76 §3.2 |
| `RealStopCenter` | The ray aimed at the real stop's centre, whatever the other rays' aiming | OSLO |

The rim limits also define the pupil-domain ellipse (K68), which is used for canonical normalization (§3.2) and to place sample points.

- **Which wavelength:** the pupil is explored at the primary wavelength, so a pupil coordinate names the same rays at every wavelength.
- **Ellipse fit:** the ellipse passes through the rim's extreme points. A pencil cut by two apertures can be a "cat's eye", pointed at top and bottom, which the ellipse overshoots there. Rays in the overshoot are stopped and left out like any vignetted ray; only the sampling is a little less efficient.

**Which apertures stop rays (`Apertures`):**

| Value | Definition |
|---|---|
| **`Fixed`** (default) | Only semi-diameters the file holds fixed. A solved (automatic) semi-diameter is the beam's own size, rounded, and would clip rim rays it should not. |
| `All` | Every surface with a semi-diameter. |
| `None` | No clipping. |

In every case:
- the clear-aperture percentage scales the semi-diameter;
- a central obscuration stops the rays inside it;
- the stop itself is never clipped, because the pupil coordinates are what fill it.

### 5.5 Sampling (`Sampling`)

| Value | Definition |
|---|---|
| **`SquareGrid(n)`** (default for maps) | n×n grid on the unit disk, in the coordinate set chosen by §5.6 |
| `Hexapolar(rings)` | Rings of 6k points (Optiland and Zemax style) |
| `GaussQuadrature(rings, arms)` | Polar Gauss–Legendre in r² with exact area weights (the default for RMS when exact integrals are wanted) |
| `NodeGrid(n)` | n×n nodes from −1 to 1 inclusive, the rim included; equal shares. Zemax's analyses sample so: its "256×256" is 255 nodes a side |
| `Fan(T or S, n)` | Tangential or sagittal line, for OPD fans |
| `Given(points)` | Points given from outside, such as another program's own |
| `Universal(type)` | Hopkins/Singh/King ray patterns with tabulated universal coefficients (K68, S76). Optional; v2. |

### 5.6 Coordinate set for maps, fits and RMS (`PupilCoordinates`)

| Value | Definition | Notes |
|---|---|---|
| `Launch` | W(Px, Py) | Optiland's OPD map and Zernike fit; Welford ch. 4 says pupil distortion "usually does not matter" |
| `ExitSphere` | W(x′, y′) | H81, W86 §7.2 |
| **`Canonical`** (default) | W(x′_S, y′_T) | H64, HY70, M71: the vignetted pupil becomes a unit circle, which keeps Zernike polynomials valid (W86 p. 246) |
| `LaunchRefined` | W from launch-coordinate samples, corrected to the exit position by W(x) = W(x′) + (x′−x)·δG′ + (y′−y)·δH′ | S76 eq. 13 |

### 5.7 Weighting (`Weighting`)

| Value | Definition |
|---|---|
| `PerRay` | Every unvignetted ray has equal weight (Optiland) |
| **`ExitArea`** (default) | w = \|∂(x′,y′)/∂(Px,Py)\| × the cell's launch area, so integrals are taken over the actual exit-pupil area (W86 eq. 13.2; K68 eq. 3). The Jacobian comes from finite differences on a square grid, or, on Gauss quadrature nodes, from four differential rays 10⁻⁵ either side of each node, which makes the RMS the exact integral over the exit pupil (verification.md, "Sampling error against the integral"). |
| `Quadrature` | Gauss quadrature weights (with `GaussQuadrature` sampling) |

Vignetted rays are **excluded**: weight 0 and removed from every statistic. Including them as W = 0 (Optiland's RMS-vs-field) is available only as `IncludeVignettedAsZero`, for fingerprinting.

### 5.8 Defocus (`Defocus`)

| Value | Definition | Source |
|---|---|---|
| **`Retrace`** (default) | Move the image surface by δz and recompute everything (Q′ moves along E′Q′) | exact |
| `ExactTerm` | Add N′δ₀W₂₀{(x′−x̄′)² + (y′−ȳ′)² + z′²}, with δ₀W₂₀ = ½n′h′²(1/R′₀ − 1/R′) | H81 eqs. 6.7–6.8; HY70 eq. 13 |
| `ParaxialTerm` | Add δ₀W₂₀(x′² + y′²) | common shortcut; error grows as sin²α′ (HY70 eq. 39) |

A transverse focal shift is always applied as N′(δG′₀x′ + δH′₀y′) (H81 eq. 6.16).

**How the terms are implemented.** The shift is `FocusShift`, in mm along the image frame's z. The new reference sphere passes through E′, with its centre moved along E′Q′ to the new image plane; its radius is R′, the old one's R′₀. A ray that met the old sphere at B′ meets the new one ½|B′ − E′|²(1/R′₀ − 1/R′) earlier along itself, so its W rises by n′ times that. This is H81 eqs. 6.5–6.6, with oblique radii in place of the axial ones and the factor N′ that converts between them.
- `ExactTerm` takes the whole of |B′ − E′|².
- `ParaxialTerm` takes only its transverse part.
- The chief ray's own change is subtracted from both.

**Checked:** on a sphere imaging its centre of curvature at NA 0.8, with 2.4 waves of defocus:
- `ExactTerm` agrees with re-tracing to 9×10⁻⁶ wave;
- `ParaxialTerm` is off by 0.34 wave.

### 5.9 Sign (`Sign`)

**`Hopkins`** (default) is chief − ray; `Wolf` is ray − chief.

`Wolf` also offers a `/n′` option, which reports W as a geometric length (Wo52 eq. 4) rather than an optical path.

### 5.10 Chromatic reference (`ChromaticReference`)

| Value | Definition | Source |
|---|---|---|
| **`PrimaryFocus`** (default) | Every wavelength is referred to the primary wavelength's Q′. Each wavelength's E′ is where its own chief ray cuts the primary wavelength's exit-pupil plane. | H81 §7 |
| `PrimarySphere` | One reference sphere for every wavelength: the primary wavelength's Q′ and E′. Each wavelength's W is still zero on its own chief ray. | Zemax OPD fan and ray trace |
| `OwnChief` | Every wavelength uses its own chief ray, Q′ and E′ | Optiland |
| `Conrady` | Differential chromatic aberration using image-space associated rays. v2. | H81 eqs. 7.5–7.10 |

The image-space index is always taken **at the analysis wavelength**. Optiland fixes it at the primary wavelength, which is a defect.

## 6. Per-ray data

### 6.1 Ray record

`WavefrontSample`: `Field`, `Wavelength`, `Px`, `Py`, `X′`, `Y′`, `Z′` (lens units), `x′`, `y′`, `z′` (reduced), `xS′`, `yT′` (canonical), `OplRay`, `W` (waves), `Omega` (invariant part, chord method only), `dG′`, `dH′` (reduced transverse aberration), `ξ′`, `η′` (image-plane intersection), `Weight`, `Vignetted`, `VignettingSurface`.

### 6.2 Area weights

For a square or polar launch grid, cell (i, j) has launch area a_ij. Its exit area is |J_ij|·a_ij, where J is the Jacobian of (x′, y′) with respect to (Px, Py), estimated by central differences from neighbouring samples (one-sided at the rim). Rim cells cut by the pupil boundary are clipped against the rim ellipse found by the pupil exploration.

## 7. Derived quantities

All statistics use the unvignetted samples and their weights wᵢ, with Σwᵢ normalized to 1.

| Quantity | Formula | Notes |
|---|---|---|
| Mean (piston) | W̄ = Σwᵢ Wᵢ | |
| **RMS (default)** | σ = √(Σwᵢ Wᵢ² − W̄²) | The standard deviation: W86 eq. 13.2, K68 eq. 3 |
| RMS about zero | √(Σwᵢ Wᵢ²) | Optiland `chief_ray`; for fingerprinting |
| RMS, tilt removed | σ of the residual after a weighted least-squares fit of {1, x, y} | In the §5.6 coordinates |
| RMS, best sphere | σ under `ReferenceCenter = BestFitSphere` | Exact, not a polynomial fit |
| RMS, Zernike | From the fit coefficients, excluding piston (and tilt, focus on request) | Valid only on the unit circle; warn if the coordinate set isn't `Canonical` and the pupil is vignetted (W86 p. 246) |
| **P-V (default)** | max − min of W | |
| P-V, best sphere | max − min under `BestFitSphere` | Rayleigh's "between two concentric spheres" (W86 p. 244). Note: minimising P-V and minimising variance give different spheres. v1 uses the variance optimum and flags this. |
| Strehl (Maréchal) | S ≈ 1 − (2πσ/λ)², and also exp(−(2πσ/λ)²) | W86 eq. 13.2; warn when σ > λ/14 |

**Zernike fitting:**
- Two sets: **Standard** (Noll ordering, Noll normalization) and **Fringe** (37 terms, unnormalized).
- The fit is weighted least squares on the chosen coordinates, normalized to the unit circle.
- Reported: the coefficients, the RMS of the fit residual, and the RMS from the coefficients alongside the RMS from the raw data.

## 8. Software design

### 8.1 Repository

[github.com/jaruiz6363/WavefrontErrorCalculator](https://github.com/jaruiz6363/WavefrontErrorCalculator), laid out as GhostAnalysis is:

```
WavefrontErrorCalculator.sln
Directory.Build.props          net8.0, nullable, Version 0.1.0, MIT
external/Directory.Build.props empty firewall
external/AberrationCalculator  git subtree (squash), with "Carry AberrationCalculator <sha>" commits
src/WavefrontErrorCalculator.Core
src/WavefrontErrorCalculator.Cli   AssemblyName = wfe
tests/WavefrontErrorCalculator.Tests   xUnit
tests/TestData/*.zmx
verification/                  per-program outputs and comparison scripts
docs/                          user-guide.md, method.md (this spec), verification.md, references.md
```

### 8.2 Dependencies on AberrationCalculator

| Need | Status in AberrationCalculator | Plan |
|---|---|---|
| Real ray trace with per-surface points and direction cosines | `RealRayTrace.TraceRecord` / `TraceRecordFrom` return local-frame `SurfaceHit`s | Use as is |
| Global coordinates for segment lengths | `LocalFrame` exists | Verify that the per-surface frames can be chained to measure segment lengths (phase 0) |
| Optical path accumulation | **Absent** | Preferably add `OpticalPath` to the trace upstream in AberrationCalculator (one field; benefits every consumer), then carry it in via subtree. Fallback: compute it in WEC from the hits. |
| Ray aiming to the real stop | Absent; exists in GhostAnalysis | Preferably move GhostAnalysis's solver upstream into AberrationCalculator. Fallback: a copy in WEC. |
| Paraxial pupils, Lagrange invariant, h′, u′ | `ParaxialTrace.Trace` → `ParaxialResult` | Use as is |
| Lens readers (.zmx, .seq, .len, .json, .lhlt) | `LensFile.Read` | Use as is. The same file feeds every program. |
| Surface types | Standard, EvenAsphere, Paraxial | v1 supports Standard and EvenAsphere. **Paraxial (ideal) surfaces need an explicit optical-path model**, the ideal-lens phase; Zemax has several modes for this. Excluded from v1, with an error message. |

Numerical type: code that sits inside the trace uses AberrationCalculator's `Scalar` so it can be compiled for automatic differentiation later. WEC's own post-processing uses `double`.

### 8.3 Public API (sketch)

```csharp
var lens = LensFile.Read(path, CatalogLocator.LoadBundled());
var options = WavefrontOptions.Reference;           // the Hopkins/Welford defaults
// or WavefrontOptions.Preset("Optiland"), with { ExitPupil = ExitPupilMode.ParaxialAxial }, ...
WavefrontResult r = WavefrontCalculator.Compute(lens, field: 2, wavelength: 1, options);
r.Samples        // IReadOnlyList<WavefrontSample>
r.Rms, r.RmsAboutZero, r.RmsTiltRemoved, r.RmsBestSphere, r.PeakToValley, r.Strehl
r.Reference      // Q′, E′, R′, chief ray, rim limits, h′, h′S, h′T
r.Zernike(ZernikeSet.Standard, terms: 37)
WavefrontCalculator.OpdFan(lens, field, wavelength, FanDirection.Tangential, n: 101, options)
```

`WavefrontOptions` is an immutable record holding every switch in §5. Named presets:
- `Reference`: all defaults.
- `Welford`: launch coordinates (W86 ch. 4), real exit pupil.
- `Optiland`: `ParaxialAxial`, `StopCenter`, `Paraxial` aiming, `Launch`, `PerRay`, RMS about zero, `OwnChief`.
- `LensHHLT`: the `Zemax` preset, since LensHH-LT computes OpticStudio's OPDC. Confirmed (docs/programs.md).
- `Zemax`: `ParaxialChiefIntersect`, `Paraxial` aiming (OpticStudio's ray aiming Off; `RealStop` for Real; its Paraxial setting is not tested), `StopCenter`, `Launch`, `PerRay`, `PrimarySphere`. Confirmed (docs/programs.md).
- `ZemaxZernike`: `Zemax` with `OwnChief`; sampled on `NodeGrid(255)`. Confirmed.
- `OSLO`: filled in by fingerprinting (§10).

### 8.4 CLI

```
wfe <lens> [--field i|all] [--wave i|all] [--preset Reference|Welford|Optiland|...]
           [--set ExitPupil=ParaxialAxial ...] [--grid 64] [--fan T|S]
           [--zernike standard:37] [--csv out.csv] [--json out.json]
wfe compare <lens> --presets Reference,Optiland      # per-switch difference report
wfe parity <result.json> --preset Zemax [--set ...]   # another program's wavefront, ray by ray
```

## 9. Verification

### 9.1 Analytic cases (unit tests, tolerance 10⁻⁶ wave unless stated)

1. A paraboloid mirror with an on-axis object at infinity: W ≡ 0.
2. A spherical mirror with the object at its centre of curvature: W ≡ 0 at any aperture.
3. A plane-parallel plate in a converging beam: closed-form spherical aberration.
4. The aplanatic points of a sphere: W ≡ 0 on axis.
5. The small-aperture limit: the traced W, fitted to ρ⁴, ρ³cos φ and so on, converges to AberrationCalculator's W040, W131, W222, W220 and W311 as the aperture and field go to 0.
6. A pure defocus by δz: `Retrace` and `ExactTerm` agree to 10⁻⁶ wave at NA 0.9. `ParaxialTerm`'s error matches HY70 eq. 39.

### 9.2 Internal consistency (property tests)

- **Ray–wave relation:** the numerical derivative of W(x′, y′) equals −N′δG′ and −N′δH′ (H81 eq. 10.30) to the expected truncation.
- **W from the ray aberrations:** Rayces's exact relation integrated across the pupil (§3.4) gives the optical-path W to 10⁻⁹ wave or better, 7×10⁻⁹ on US8264785's aspheres (`RaycesTests`).
- **Piston invariance:** adding a constant path to every ray leaves the RMS (standard deviation) and P-V unchanged.
- **Radius sensitivity:** changing R′ by δR changes W by about N′θ²δR/2 (W86 §7.4).
- **Chord vs direct sum:** the two methods agree (§4.2).
- **Centre-shift linearity:** small moves of Q′ follow W86 eq. 7.18.
- **Coordinate invariance:** with the `ExitArea` weighting, the RMS from launch-grid samples matches the RMS from `ExitSphereGrid` samples to the quadrature error, over the same domain. The canonical circle the exit grid fills is the real exit pupil only to first order, so the exit grid must reach past it to the image of the real stop.

### 9.3 Test lenses for fingerprinting

Each lens isolates one or two switches:

| Lens | Purpose |
|---|---|
| A. Singlet, on axis, f/4 | Baseline; sign; piston handling |
| B. Fast (f/1.2), non-telecentric, 30° field, strong pupil aberration | `ExitPupil`, `PupilCoordinates`, `Weighting` |
| C. Double Gauss with ~35% vignetting at the field edge (Powell 1978 system) | `ChiefRay`, `Weighting`, vignetted-ray handling |
| D. Afocal telescope (Keplerian) | Afocal handling, reference plane |
| E. Microscope objective, NA 0.9 | `Defocus` term; large-aperture effects |
| F. Achromat at 3 wavelengths | `ChromaticReference`; image index per wavelength |
| G. 1:1 relay (`Relay_1to1.zmx`): two cemented doublets about a central stop, NA 0.1, object 192 mm, m = −1.0 | Finite conjugate at a near object |
| H. Objective (`Objective_NA03_5x.zmx`): two cemented doublets, object-space NA 0.3, working distance 11.3 mm, m = −5.0 | Short conjugate at high object-side NA; its virtual entrance pupil lies behind the object (`PupilOrientation`) |

Lenses G and H were designed for these tests in Zemax OpticStudio, from rough starting points, with Schott glass.

Each lens is stored as `.zmx`, the master copy, and exported to `.json` (Optiland), `.lhlt` (LensHH-LT) and `.len` (OSLO) with AberrationCalculator's writers.

## 10. Fingerprinting procedure

1. For each program and test lens, export:
   - the OPD fan (T and S, 101 points);
   - the wavefront map (a 64×64 grid, with its pupil coordinates);
   - RMS and P-V;
   - Zernike Standard coefficients 1–37;
   
   at every field and wavelength.
2. Run WEC over the switch combinations: the product of §5.1–5.10, pruned to about 200 plausible combinations.
3. For each combination, compute the difference from the program's output, after the program's documented piston/tilt handling.
4. The matching combination is the one whose maximum difference is below 10⁻⁴ wave. When several match, extend the test lenses until only one does.
5. Record the result as a named preset and as a row in the convention table (`docs/programs.md`), citing the program's own documentation where it exists.

How each program's data is obtained:
- **Optiland** is open source and needs no licence. It is run by `verification/optiland/export.py` in this repository.
- **Zemax OpticStudio, LensHH-LT and OSLO** each need a licence. **This repository contains no code that runs them.** Their results are gathered outside it and committed here only as data, in the program-neutral format of `docs/result-format.md`, under `tests/TestData/<program>/`. Each result file records:
  - the program;
  - its version;
  - every setting that affects the wavefront.
  
  OSLO's results are exported by hand.

## 11. Ready for diffraction (not in v1)

Samples on `Canonical` coordinates, together with the reference geometry (h′_S, h′_T, N′, R′), are exactly the inputs needed for PSF/MTF in Hopkins form (HY70 eq. 29; H81 eqs. 10.11–10.26; M71 eq. 4.15). A later version, or another consumer, can build the pupil function f = exp(i2πW/λ) without re-deriving the coordinates.

## 12. Known program behaviours (as found so far)

The Optiland, Zemax and LensHH-LT columns have been confirmed at every field and wavelength (docs/verification.md, docs/programs.md):
- the `Optiland` preset reproduces Optiland's own wavefront ray by ray, to 10⁻⁵ wave or better;
- the `Zemax` and `ZemaxZernike` presets reproduce OpticStudio 2022 R2's OPD, RMS, P-V and Zernike coefficients;
- the `LensHHLT` preset, which is the `Zemax` one, reproduces LensHH-LT's OPD, RMS and P-V, to 2×10⁻⁸ wave with ray aiming off and 7×10⁻⁷ with it on.

OSLO is still to be determined.

| Switch | Reference (WEC) | Optiland (a3fb3e1b, `chief_ray`) | LensHH-LT (1.0.161) | Zemax (OpticStudio 2022 R2) | OSLO (EDU 6.6) |
|---|---|---|---|---|---|
| Sign | chief − ray | chief − ray | chief − ray | chief − ray | chief − ray |
| Q′ | chief ray | chief ray (centroid and best fit available) | primary-λ chief ray, used for every λ | primary-λ chief ray (OPD fan, ray trace); each λ's own chief ray (Zernike analysis) | each λ's own reference ray, aimed at the real stop's centre |
| E′ / R′ | real chief (off axis) | paraxial axial XP, primary λ | chief ray ∩ paraxial XP plane, as OpticStudio's OPDC (`ParaxialChiefIntersect`), crossed exactly | chief ray ∩ paraxial XP plane (Reference OPD "Exit Pupil", the default); plane across the chief ray ("Infinity"); none, the path to the image surface ("Absolute"; "Absolute 2" the same on these lenses) | real exit pupil per field (default; radius rule not yet identified); infinity; the reference ray at the last surface |
| Aiming | real stop | paraxial (iterative available) | paraxial EP when off; real stop when on, the stop radius being the real axial marginal ray from the object point | paraxial EP when off; real stop when real, the stop radius being the primary λ's real axial marginal ray; its third setting, ray aiming Paraxial, not tested | aplanatic: direction sines for a near finite object, the pupil plane otherwise; `enp` about the paraxial pupil, `crr` (default) about the reference ray |
| Chief ray | vignetted centre | stop centre | stop centre (unless VDX/VDY vignetting factors are set) | stop centre (vignetting factors not yet tested) | the reference ray (real stop centre) |
| Map/fit coordinates | canonical | launch (Px, Py) | launch (Px, Py) | launch (Px, Py) | OSLO fractional coordinates |
| Weighting | exit area | per ray | per ray, on its map grid px = (j − n/2)/(n/2 − ½); Forbes quadrature for the WAVEX/M/C operands (not compared) | per ray; the analyses sample n − 1 nodes a side, rim included, for "n × n" | each ray once, on its spot-diagram grid (cell centres, 2/17.03 of the pupil, scaled 0.999724) |
| RMS | standard deviation | RMS about zero; vignetted rays counted as 0 in RMS vs field | standard deviation (piston removed), referenced to the chief ray; afocal: piston + tilt removed | standard deviation ("to chief": piston removed); "to centroid" also removes tilt (not yet compared) | standard deviation about the reference sphere (`ref`); default: after the lateral shift of the reference point that minimises it |
| P-V | max − min | not implemented | max − min | max − min over the analysis grid | max − min over the spot-diagram grid |
| Defocus | re-trace | re-trace | re-trace only (no polynomial term) | not yet tested | TBD |
| Chromatic | primary Q′ | own chief; n′ at primary λ | one sphere, the primary λ's, for every λ; each λ zero on its own chief ray | OPD fan and ray trace: one sphere, the primary λ's Q′ and E′, each λ zero on its own chief ray; Zernike analysis: each λ's own chief ray | each λ's own reference ray and exit pupil |
| Zernike | Standard/Fringe, weighted, canonical | Fringe 37, unweighted, launch coordinates | Standard (Noll) and Fringe 37, unweighted, fitted at coordinates other than where the map was traced (below); RMS from the raw map | Standard (Noll), unweighted, launch coordinates | Fringe 36, unweighted, exit-sphere coordinates, angle from y; unit radius the rim ray on axis (off axis not yet identified) |

Defects found (reading the code, then by test where marked):

- **Optiland:**
  - RMS vs field counts vignetted rays as W = 0.
  - Under `chief_ray`, RMS is taken about zero, so piston is included.
  - The image-space index is fixed at the primary wavelength.
- **Optiland** (confirmed by test): when a finite object's entrance pupil lies behind it (lens H), each ray is traced from the object towards that pupil, away from the lens. The wavefront comes out with the wrong sign: 0.62 wave instead of −0.48 at the rim on axis, as a defocus test shows and Zemax agrees. Beyond the sign, it differs by up to 7×10⁻² wave.
- **LensHH-LT:**
  - **Zernike sample positions don't match the map grid** (confirmed by test). The fit assumes `px = −1+2(j+½)/n`, but the map was traced at `px = (j−n/2)/(n/2−½)`: a half-pixel shift and a (n−1)/n scale error, about 1.6% at n = 64. Coefficients move by up to 0.17 wave, and a symmetric on-axis wavefront shows a tilt.
  - Fringe term 37 duplicates term 36 (it should be n = 12).
  - Noll terms above 37 fall back to m = 0.

## 13. Decisions (made 2026-10-04)

1. **Optical path goes upstream.** Optical-path accumulation is added to AberrationCalculator's `RealRayTrace` and carried into WEC by `git subtree pull`.
2. **Ray aiming goes upstream.** GhostAnalysis's real-stop aiming (Broyden solver, `GhostTracer.cs:188-264`) moves into AberrationCalculator. GhostAnalysis then switches to the shared version.
3. **Repository:** on GitHub under the same owner as AberrationCalculator and GhostAnalysis, and public.
4. **Gathering results from licensed programs** (revised 2026-10-04): no code that runs Zemax, LensHH-LT or OSLO is distributed. It is kept outside this repository, and only the results are committed here, as data (§10, `docs/result-format.md`). OSLO is run by hand.
5. **Paraxial (ideal) surfaces are out of scope for v1.** WEC reports a clear error for a lens that contains one.

## 14. Phases

| Phase | Deliverable |
|---|---|
| 0 | Repo skeleton; subtree; confirm the trace frames allow segment lengths; decisions in §13 made (**done**) |
| 1 | Optical path (direct sum), reference geometry, `Reference` preset, OPD fan, RMS/P-V; analytic tests §9.1 (1–4) (**done**) |
| 2 | Pupil exploration, vignetted chief ray, canonical coordinates, area weights, Zernike; tests §9.1 (5–6) and §9.2 (**done**; `LaunchRefined` and the coordinate-invariance test wait for `ExitSphereGrid` aiming in phase 3) |
| 3 | All switches; `compare` command; Optiland preset verified on test lenses A–F (**done** except lenses D–F: afocal, NA 0.9, achromat; the preset is confirmed on three lenses — double Gauss, vignetted Cooke triplet, fast aspheric lens — at all fields and wavelengths) |
| 4 | LensHH-LT and Zemax fingerprinting; presets; `docs/programs.md` (**done** for Zemax: every Reference OPD setting, ray aiming off and real, and the Zernike analysis; and for LensHH-LT: OPD, RMS, P-V and the Zernike fit, ray aiming off and real, on five lenses) |
| 5 | OSLO (**done**: OPD at 857 points per field on five lenses, statistics, and the Zernike fit on axis); chord-method cross-check; `Conrady` chromatic mode; universal-coefficient sampling |
