# Verification

How WavefrontErrorCalculator's results are checked. Every check is a test in
`tests/WavefrontErrorCalculator.Tests` and runs with `dotnet test`.

## W two independent ways (`RaycesTests`, `wfe rayces`)

The optical path (method.md §4) against Rayces's exact relation between wave and ray aberration integrated across the pupil (method.md §3.4), which uses only where the rays go in image space. Largest |integrated − path| in waves over the 857 OPDC points of every field (on axis and off), at the primary wavelength, on WEC's reference sphere (`RealChief`) and on Zemax's (`ParaxialChiefIntersect`):

| Lens | Conjugate | Aiming off, `RealChief` | Aiming off, `ParaxialChiefIntersect` | Aiming real, `RealChief` | Aiming real, `ParaxialChiefIntersect` |
|---|---|---|---|---|---|
| Kingslake double Gauss | infinite | 1.1×10⁻¹⁰ | 1.1×10⁻¹⁰ | 8.9×10⁻¹¹ | 9.1×10⁻¹¹ |
| Cooke triplet | infinite | 8.3×10⁻¹⁰ | 1.1×10⁻⁹ | 1.1×10⁻⁹ | 1.1×10⁻⁹ |
| US8264785 | infinite | 6.4×10⁻⁹ | 6.9×10⁻⁹ | 7.3×10⁻⁹ | 6.5×10⁻⁹ |
| 1:1 relay | finite | 2.6×10⁻¹⁰ | 2.3×10⁻¹⁰ | 2.7×10⁻¹⁰ | 3.4×10⁻¹⁰ |
| NA 0.3 objective | finite | 1.3×10⁻¹⁰ | 1.3×10⁻¹⁰ | 1.3×10⁻¹⁰ | 1.3×10⁻¹⁰ |

The wavefronts reach 2.9 waves (US8264785 at 17.5°). Its near-grazing rim, where W climbs steeply, needs up to 16,384 integration steps; with 256 the difference there is 2.6×10⁻⁴ wave, with 1,024 1.5×10⁻⁷. Its floor of a few 10⁻⁹ is shared by its on-axis field and does not fall with the step, so it is not the integration's; most likely it is the precision of the ray trace's intersections with the aspheres, the only ones among these lenses. The rays the lens blocks have no W either way.

US8264785's file has no apertures, so with WEC's defaults (real aiming, the chief ray at the vignetted pupil's centre) the top of its pupil at 17.5° is where the ray meets total internal reflection at surface 5, leaving it at 89.97°. W climbs there from 0.27 to 17.9 waves over the last 6% of the pupil, with a slope that goes as 1/√(distance to the edge). Evenly spaced nodes stopped at 3×10⁻⁴ wave however many there were; the integration now redoes such a point with its nodes closer together toward it (p = 1 − (1 − u)², which makes the square root smooth in u) and reaches 4.6×10⁻⁹ (`APupilEndingAtTotalInternalReflectionIsReached`).

### Extreme cases

Three singlets of N-BK7 (R ±50 mm, 10 mm thick, f = 50 mm) built to push the check far beyond ordinary lenses: `FastSinglet`, at f/1.25 (EPD 40) with fields 0°, 5° and 10°; `FastSinglet_Defocused`, the same with the image 1 mm beyond the paraxial focus; and `ShortPupilSinglet`, EPD 10 with its stop 6.7 mm before the image (the exit pupil 18 mm from the image), fields 0° and 3°. Same measure, at the 857 points of every field, chief ray at the stop's centre:

| Lens | Largest W (waves) | Aiming off, `RealChief` | Aiming off, `ParaxialChiefIntersect` | Aiming real, `RealChief` | Aiming real, `ParaxialChiefIntersect` |
|---|---|---|---|---|---|
| `FastSinglet` | 1,427 | 1.5×10⁻⁹ | 1.1×10⁻⁹ | 2.7×10⁻¹⁰ | 1.1×10⁻⁹ |
| `FastSinglet_Defocused` | 1,522 | 5.5×10⁻¹⁰ | 9.3×10⁻¹⁰ | 9.5×10⁻¹⁰ | 9.7×10⁻¹⁰ |
| `ShortPupilSinglet` | 3,928 (9,426 on Zemax's sphere) | 1.3×10⁻¹⁰ | 2.5×10⁻⁹ | 1.3×10⁻¹⁰ | 2.5×10⁻⁹ |

On `ShortPupilSinglet` at 3°, 59 of the 857 rays do not meet Zemax's sphere at all, so have no W either way. The rays there meet the reference sphere up to 32° from its radius, and Nijboer's W parts from the along-ray W by 685 of 3,928 waves at the rim (17%); the integration, converted, still agrees (`WithThePupilCloseToTheImageTheTwoDefinitionsOfWPartButTheMethodsAgree`; guide/rayces-method.md §4). `RaycesTests` runs all three.

## W a third way: Hopkins's surface contributions and Tatian's focal shift (`HopkinsTatianTests`, `wfe hopkins`)

H. H. Hopkins (1952, *Proc. Phys. Soc. B* 65, 934, eq. 7) gives the change at each surface in the aberration of a ray against the chief ray, referred to their *invariant focus*, the mid-point of their shortest join, from the two rays' points of incidence and directions alone: Δ(N e), e = Σ(λ + λ̄)(X − X̄)/(1 + Σλλ̄). Summed over the surfaces it is the aberration in image space, computed as a difference between the two rays rather than by subtracting two long paths. B. Tatian (1972, *Optica Acta* 19, 79, eq. 1) moves the reference from that focus to the chosen image point I by −N(QD − Q̄D̄), Q and Q̄ the feet of the perpendiculars from I to the two rays: Hamilton's mixed characteristic, with no exit pupil. That is W measured to the foot of the perpendicular from I on each ray, which is this program's `ExitPupil=Infinite` and Zemax OpticStudio's Reference OPD "Infinity". Largest |Hopkins-Tatian − path| in waves, the two methods on the same rays and the same (infinite) reference, over the 857 OPDC points of every field at the primary wavelength (Zemax preset, I at the chief ray's image point). The check is run with ray aiming off and again with it on; the columns are two runs, not compared with each other:

| Lens | Largest W (waves) | Ray aiming off | Ray aiming on (real) |
|---|---|---|---|
| Kingslake double Gauss | 6.1 | 8.0×10⁻¹¹ | 6.6×10⁻¹¹ |
| Cooke triplet | 3.7 | 8.8×10⁻¹⁰ | 1.1×10⁻⁹ |
| US8264785 | 5.4 | 6.5×10⁻⁹ | 7.1×10⁻⁹ |
| 1:1 relay | 3.6 | 1.9×10⁻¹⁰ | 2.6×10⁻¹⁰ |
| NA 0.3 objective | 1.6 | 1.5×10⁻¹⁰ | 1.4×10⁻¹⁰ |
| `FastSinglet` | 5,360 | 4.4×10⁻¹¹ | 5.6×10⁻¹¹ |
| `FastSinglet_Defocused` | 5,683 | 4.5×10⁻¹¹ | 4.4×10⁻¹¹ |
| `ShortPupilSinglet` | 2,095 | 3.7×10⁻¹¹ | 3.7×10⁻¹¹ |

Tatian's eq. 1 in closed form agrees with the same focal shift taken from the shortest join's own geometry to 2.3×10⁻¹⁰ wave wherever the join is well conditioned.

This W and the W on a reference sphere through the exit pupil are different quantities, and they part company as the aberration grows, more so as the exit pupil nears the image (Tatian p. 79). Largest |OPDC − Hopkins-Tatian|, OPDC being Zemax's default (the sphere through the chief ray's crossing of the paraxial exit pupil), aiming off:

| Lens | On axis | Largest field |
|---|---|---|
| Kingslake double Gauss | 2.5×10⁻⁴ (W 0.19) | 0.51 (W 5.9, 14°) |
| Cooke triplet | 1.2×10⁻² (W 1.3) | 2.5×10⁻² (W 3.5, 20°) |
| US8264785 | 9.4×10⁻⁵ (W 0.08) | 0.87 (W 2.9, 17.5°) |
| 1:1 relay | 1.4×10⁻² (W 2.0) | 1.3×10⁻² (W 3.6) |
| NA 0.3 objective | 8.8×10⁻³ (W 0.62) | 3.7×10⁻² (W 1.6) |
| `ShortPupilSinglet` | 0.71 (W 3.2) | 1.05×10⁴ (W 2,095, 3°) |

So two calculations, one with each definition, can differ by about a wave at a few waves of aberration with neither in error.

Against OpticStudio directly (`HopkinsAndTatiansWIsOpticStudiosInfinityOpd`): Hopkins-Tatian at OpticStudio 2022 R2's own pupil points and indices, against its ray-trace OPD with Reference OPD "Infinity", both with ray aiming real (fans and grid, every field and wavelength, about 2,500 rays per lens):

| Lens | Largest difference (waves) |
|---|---|
| Kingslake double Gauss | 1.4×10⁻⁷ |
| Cooke triplet | 5.7×10⁻⁸ |
| US8264785 | 1.3×10⁻⁷ |
| 1:1 relay | 1.1×10⁻⁷ |
| NA 0.3 objective | 8.5×10⁻⁸ |

What remains is where the two programs' aiming iterations stop, as in every comparison with OpticStudio above. OpticStudio's Reference OPD "Infinity" computes Hopkins and Tatian's W. (The test also runs the results gathered with ray aiming off.)

### Hopkins 1952 without Tatian (`HopkinssOwnFocalShiftGivesTheRealExitPupilSpheresW`, `wfe hopkins --1952`)

Hopkins's own focal shift (1952 eq. 13) refers Ω to the sphere about the image point that cuts the chief ray in the exit pupil, measured along the ray: the definition of the optical-path W on a sphere through E′. With E′ in the real exit pupil (`ExitPupil=RealChief`), the shift taken exactly agrees with the optical path to 1.3×10⁻¹⁰ wave (double Gauss), 8×10⁻¹⁰ (Cooke), 6.4×10⁻⁹ (US8264785), 2×10⁻¹⁰ (relay), 1.3×10⁻¹⁰ (objective), 6.4×10⁻¹¹ and 6×10⁻¹⁰ on the singlets. Eq. 13 as printed drops δ², δ the along-ray distance between the two spheres, and that costs, at the largest field: 4.5×10⁻⁴ wave (double Gauss, W 6.4), 7.5×10⁻⁵ (Cooke, W 3.5), 1.2×10⁻² (US8264785, W 2.9), 3.5×10⁻⁵ (relay), 3.3×10⁻⁵ (objective), and tens to hundreds of waves on the singlets with a thousand. Hopkins 1952 with the real exit pupil is therefore this program's `RealChief` sphere, to that approximation as printed, and it differs from Hopkins-Tatian much as OPDC does (double Gauss 0.51, US8264785 0.82, Cooke 0.035 wave at the largest field): the real and paraxial exit pupils are close, the infinite reference is not.

## Cases with exact answers (`AnalyticTests`, `SwitchTests`)

| Case | Expected | Result |
|---|---|---|
| Paraboloid, collimated axial beam | W ≡ 0 | below 10⁻⁶ wave, under every preset |
| Sphere imaging its centre of curvature | W ≡ 0 at any aperture | below 10⁻⁶ wave, under every preset |
| Aplanatic points of a refracting sphere, object immersed in glass | W ≡ 0 | below 10⁻⁶ wave |
| Plate in a diverging beam, eikonal reference | closed form, ray by ray | to 10⁻⁶ wave, at about 9 waves of aberration |
| Confocal paraboloids (afocal) | flat wavefront | below 10⁻⁶ wave |
| Defocused perfect image, best-fit sphere | centre returns to the focus, no residual | RMS below 10⁻⁶ wave |

## Aberration theory (`TheoryTests`, `SwitchTests`)

| Check | Result |
|---|---|
| f/20 singlet against AberrationCalculator's Seidel sums | W040 = S1/8 to better than 0.001%; W131 = S2/2 to 0.02% |
| Welford eq. 7.14, δη = -(Q₀P′/n′) ∂W/∂y, double Gauss at full field | 2.5×10⁻⁶ mm against a 0.27 mm ray aberration |
| Welford eq. 7.18, moving the reference centre | as predicted, to 10⁻⁴ wave |
| Defocus at NA 0.8, 2.4 waves | Hopkins's exact term matches re-tracing to 9×10⁻⁶ wave; the paraxial ρ² term is off by 0.34 wave |
| Best-fit sphere | a minimum in every direction of the centre |
| Gauss quadrature, 240 points | the RMS of a 256×256 grid, to 2×10⁻⁴ |
| Gauss quadrature with exit-area weights (differential rays) | converged to 10⁻¹⁰ by 8 rings; the 256×256 exit-area grid within 2×10⁻⁴ of it |
| Aiming onto the exit grid | rays land on their canonical targets to 10⁻⁹ |
| RMS over the same exit pupil, two ways | launch grid with exit-area weights vs uniform exit grid, to 10⁻³ |
| Singh's refinement | Zernike coefficients within 2×10⁻⁴ wave of exact aiming (1.4×10⁻² unrefined) |

Note on the last two: the canonical unit circle is the image of the pupil only to first order. The real exit pupil, the image of the stop's rim, differs from it by the pupil's own aberration, about 1% of its radius for the double Gauss at full field. Because W is steepest at the rim, comparisons of integrated quantities must be made over the same domain.

## Optiland (`OptilandParityTests`)

`verification/optiland/export.py` runs Optiland (checkout a3fb3e1b) on a lens and writes its wavefront ray by ray:
- strategy `"chief_ray"`, its default;
- the `"uniform"` distribution plus tangential and sagittal fans;
- every field and every wavelength;
- the refractive indices it used.

The exports are in `tests/TestData/optiland`. The test traces the same pupil points under the `Optiland` preset, using Optiland's own indices, and compares W ray by ray.

| Lens | Largest difference from Optiland's own wavefront, all fields and wavelengths |
|---|---|
| Double Gauss (Kingslake) | 2×10⁻¹⁰ wave |
| Cooke triplet with fixed apertures (vignetting) | 2×10⁻⁸ wave; the same rays vignetted |
| Fast lens with two even aspheres, object at 11 m (US 8,264,785 Ex. 4) | 8×10⁻⁶ wave |

The `Optiland` preset therefore reproduces Optiland's wavefront.

Two things had to be held equal for the comparison to be about conventions:

- **Glass data.** A glass name with no catalog can resolve to different glasses in different programs. The double Gauss file names `F4` and no catalog: Optiland uses n = 1.620047 at 0.5876 µm, while AberrationCalculator's bundled catalog gives 1.616592. That alone changes the on-axis P-V from 0.19 to 4.6 waves. The test therefore uses Optiland's indices, and logs any surface where the two programs' indices differ.
- **Intersection tolerance.** Optiland gives an even asphere read from a `.zmx` file an intersection tolerance of 10⁻⁶ mm. On the fast lens that moves its OPD by up to 10⁻³ wave. The export script tightens every iterative surface's tolerance to 10⁻¹⁴ mm, and what remains is about 6 nm of path.

## Zemax OpticStudio (`ZemaxParityTests`)

`tests/TestData/zemax` holds OpticStudio 2022 R2's results for the same three lenses:
- under each Reference OPD setting (Exit Pupil, Infinity, Absolute, Absolute 2), with ray aiming off and real;
- the OPD of its batch ray trace along both fans and over a grid;
- its Zernike Standard Coefficients analysis, with the RMS and P-V it reports;
- the refractive indices it used.

They were gathered outside this repository (method.md §10). The tests trace the same pupil points, on those indices, under the `Zemax` and `ZemaxZernike` presets with the switches the file's settings call for. `docs/programs.md` says what each setting corresponds to.

| Comparison | Largest difference, all fields and wavelengths |
|---|---|
| OPD, ray aiming off, all 12 results | 1.3×10⁻⁸ wave; the same rays vignetted |
| OPD, ray aiming real, all 12 results | 5.6×10⁻⁷ wave; 2.5×10⁻⁶ under Absolute, where W spans over 160 waves |
| Zernike analysis, ray aiming off: RMS, P-V and 37 coefficients (51,000 rays per field and wavelength) | at the 10⁻⁸ wave OpticStudio prints to |
| The same, ray aiming real | 4×10⁻⁸ wave |

`wfe parity <result.json> --preset Zemax` repeats the OPD comparison for any result file. With `--rays file.md` or `file.csv`, it writes the comparison ray by ray.

### OPDC at every pupil point (`TheZemaxPresetIsOpticStudiosOpdcOperand`)

OpticStudio's `OPDC` operand, read from its Merit Function Editor at 857 pupil points per field and wavelength: a grid of spacing 1/16, and 60 points on the rim. That is 7,713 points per lens, with ray aiming off and real, against the `Zemax` preset.

| Comparison | Largest difference |
|---|---|
| OPDC, ray aiming off, five lenses | 5.7×10⁻⁸ wave; the same 7 rays vignetted |
| OPDC, ray aiming real, five lenses | 1.1×10⁻⁶ wave; 6.1×10⁻⁶ at 3 points of US8264785 near grazing rays; the same 25 rays vignetted |

OpticStudio stops evaluating a merit function at the first ray that fails, and every row after it reads 0, as a vignetted ray does. The rows after each failure were evaluated again until none was left. [`docs/opd/zemax`](opd/zemax/README.md) has the tables, point by point.

[`docs/opd/compare`](opd/compare/README.md) puts OpticStudio's OPDC beside Optiland's, LensHH-LT's and OSLO's OPD at the same 857 points, each against WEC's own W, point by point.

## LensHH-LT (`LensHHLTParityTests`)

`tests/TestData/lenshh-lt` holds LensHH-LT's results for the five lenses, with ray aiming off and real:
- its OPD fans and a 16×16 wavefront map, ray by ray;
- the RMS and P-V of its 64×64 map;
- its Zernike Standard and Fringe coefficients;
- the refractive indices it used.

`tests/TestData/lenshh-lt-opdc` holds its wavefront map's OPD at the 857 OPDC points. Both were gathered from its engine outside this repository. `docs/programs.md` describes what it computes: OpticStudio's OPDC, so the `LensHHLT` preset is the `Zemax` one.

| Comparison | Result |
|---|---|
| OPD fans and map, aiming off | 2.1×10⁻⁸ wave; the same rays vignetted |
| OPD fans and map, aiming real | 2.3×10⁻⁸ wave; 6.7×10⁻⁷ on US8264785 at full field |
| OPD at the 857 OPDC points | 2.2×10⁻⁸ wave aiming off; 3.7×10⁻⁷ aiming real |
| RMS and P-V of the 64×64 map, at LensHH-LT's own nodes | to 10⁻⁶ of their size |
| Zernike Standard coefficients | reproduced by fitting at LensHH-LT's fit coordinates, which differ from where its map is traced; fitted where traced, they differ by up to 0.17 wave |

## Finite conjugates

Two lenses with near objects were added for these comparisons (method.md §9.3):
- G, a 1:1 relay at NA 0.1;
- H, an objective at object-space NA 0.3 and −5×, whose virtual entrance pupil lies behind its object.

They found a fault in AberrationCalculator's real ray trace, fixed in 3b20f5d. When a finite object's entrance pupil lies behind it, rays were launched from the object towards that pupil, away from the lens, so every optical path was negated. The objective's wavefront came out with the wrong sign. A defocus test settles which sign is right: moving the image plane towards the lens must change the rim's W the same way for every converging beam. `OpticalPathTests.AnEntrancePupilBehindTheObjectStillSendsTheLightForward` now checks the trace.

| Program | Relay (G) | Objective (H) |
|---|---|---|
| Zemax, every Reference OPD setting, aiming off and real | 10⁻⁹ wave (10⁻⁶ aimed) | 6×10⁻⁸ wave (2×10⁻⁶ aimed), with `PupilOrientation` EntrancePupilPlane; the Zernike analysis to its printed precision |
| Optiland | as on the other lenses | the wavefront negated; negated back, 7×10⁻² wave |
| LensHH-LT | 4.4×10⁻¹⁰ wave (1.8×10⁻⁸ aimed) | 2.0×10⁻¹⁰ wave (6.2×10⁻⁹ aimed) |

## OSLO (`OsloParityTests`)

`tests/TestData/oslo` holds OSLO EDU 6.6's results for the five lenses, gathered by hand with a CCL macro kept outside this repository. They cover both ray aiming modes (`enp`, `crr`) and all three reference sphere positions (exit pupil, infinity, last surface): 30 files. The test traces OSLO's own (FY, FX), on OSLO's indices, with the switches `docs/programs.md` lists.

| Comparison | Largest difference |
|---|---|
| OPD, all 30 files, every field and wavelength, fans and grid | 1.1×10⁻⁸ wave; the same rays vignetted |
| `wavefront(ref)` RMS and P-V, all 30 files | 8×10⁻⁹ wave |
| `wavefront()` RMS and P-V (lateral reference shift), all 30 files | 2.1×10⁻⁸ wave |
| `zernike_fit` Fringe 36 on axis, 20 files (not the reference at infinity) | 2×10⁻⁹ wave on the double Gauss at its primary wavelength; within 2×10⁻⁴ elsewhere |

The exit pupil sphere's radius is taken from OSLO's own report, its rule not yet being identified. The last-surface sphere is computed (`ExitPupil.LastSurface`), and so is everything else.


## Sampling error against the integral (`--quadrature`)

The RMS a program reports is an estimate of an integral over the pupil, made from a finite sample. `Sampling.GaussQuadrature` with the matching weights computes that integral itself: Gauss–Legendre in ρ² across the rings and evenly spaced arms. On a smooth wavefront over an unvignetted pupil, it converges to about 10⁻¹¹ wave by 8 rings. The weights match the program:
- `Weighting.Quadrature`, the rule's own weights, is the integral over the launch disc. It is what counting each ray once (`PerRay`) estimates.
- `ExitArea` on quadrature nodes integrates over the exit pupil's own area. Each node's Jacobian ∂(x′,y′)/∂(Px,Py) comes from four differential rays 10⁻⁵ either side of it in the launch coordinates.

The exact exit-area RMS for the double Gauss at 14° is 1.2708992521 wave (8 and 24 rings agree to 10⁻¹⁰), and the square grid's finite-difference exit-area weights approach it as the grid is refined: 1.280187 at 64×64, 1.271128 at 256², 1.271094 at 512² (`SwitchTests.GaussQuadratureIntegratesOverTheExitArea`).

Aiming onto the exit sphere's grid (`RayAiming.ExitSphereGrid`) is not a substitute: its unit circle is the canonical pupil, which is the image of the stop only to first order. Over that disc the same field gives 1.255791 wave.

The table gives each program's sampling error at the primary wavelength: its RMS from its own sample, minus the integral (48 rings × 192 arms) under the same switches, on the indices of its own result file. For each program:
- **WEC:** the `Reference` preset with its default 64×64 grid and exit-area weights.
- **Zemax OpticStudio:** `ZemaxZernike` on the 255-node grid its 256×256 Zernike analysis traces.
- **LensHH-LT:** `LensHHLT` on its 64-node map.
- **OSLO:** its 232-cell spot grid, equally weighted, with `crr` aiming and the exit-pupil sphere at OSLO's reported radius.

The integrals differ between programs because their conventions differ (`docs/programs.md`). On the double Gauss, LensHH-LT also reads different glass. Only the differences in each program's own column are sampling error.

| Lens | Field | WEC 64×64 | OpticStudio 256×256 | LensHH-LT 64 | OSLO 232 |
|---|---|---|---|---|---|
| KingslakeDG | 0 | +3.1×10⁻⁴ | −9.8×10⁻⁵ | +3.2×10⁻³ | +2.1×10⁻³ |
| KingslakeDG | 10 | +5.1×10⁻³ | −1.6×10⁻³ | +5.3×10⁻³ | +2.6×10⁻² |
| KingslakeDG | 14 | +9.3×10⁻³ | −2.8×10⁻³ | +7.5×10⁻³ | +4.5×10⁻² |
| Cooke_40deg_FC | 0 | +2.4×10⁻³ | −7.6×10⁻⁴ | +1.6×10⁻³ | +1.7×10⁻² |
| Cooke_40deg_FC | 14 | +4.5×10⁻³ | −1.3×10⁻³ | +2.5×10⁻³ | +1.6×10⁻² |
| Cooke_40deg_FC | 20 | +1.9×10⁻³ | −6.6×10⁻⁴ | +1.4×10⁻³ | +1.1×10⁻² |
| US8264785_Ex4 | 0 | −3.8×10⁻⁵ | +1.2×10⁻⁵ | −2.4×10⁻⁵ | −3.7×10⁻⁴ |
| US8264785_Ex4 | 12 | +9.4×10⁻⁴ | −2.7×10⁻⁴ | +5.9×10⁻⁴ | +5.9×10⁻³ |
| US8264785_Ex4 | 17.5 | −1.1×10⁻¹ † | −7.9×10⁻⁴ | +1.6×10⁻³ | +2.1×10⁻² |
| Relay_1to1 | 0 | +1.5×10⁻³ | −4.7×10⁻⁴ | +8.2×10⁻⁴ | +1.6×10⁻² |
| Relay_1to1 | 5 | +8.1×10⁻⁴ | −2.4×10⁻⁴ | +4.9×10⁻⁴ | −1.1×10⁻³ |
| Relay_1to1 | 7 | +3.1×10⁻³ | −9.6×10⁻⁴ | +2.0×10⁻³ | +1.0×10⁻² |
| Objective_NA03_5x | 0 | −2.3×10⁻⁴ | +6.7×10⁻⁵ | −1.7×10⁻⁴ | −9.9×10⁻⁴ |
| Objective_NA03_5x | 0.35 | +4.7×10⁻⁴ | −1.4×10⁻⁴ | +3.1×10⁻⁴ | +1.5×10⁻³ |
| Objective_NA03_5x | 0.5 | +1.1×10⁻³ | −3.5×10⁻⁴ | +7.6×10⁻⁴ | +5.4×10⁻³ |

Field values are as the lens files give them, angles in degrees or object heights in mm. Errors are in waves. Between 24 and 48 rings, the integral moves by less than 10⁻⁹ wave with these exceptions: 5×10⁻⁸ for WEC on the relay at 7, 2×10⁻⁷ for the other programs on US8264785 at 17.5°, and the entry marked †, whose integral is not converged (below).

Three things the table shows:
- **The grids are not converged.**
  - OpticStudio's 256×256 grid is within 3×10⁻³ wave everywhere.
  - The 64-wide grids of WEC and LensHH-LT are within about 10⁻² wave, 0.7% of the RMS on the double Gauss at 14°.
  - OSLO's 232 rays are off by up to 4.5×10⁻² wave, about 4%.

  Each program's statistics agree with its own grid to 10⁻⁸ (the sections above), so these numbers are the cost of its sample, not of its conventions.
- **WEC's exit-area grid mostly sits above the integral.** The cells at the rim are weighted by one-sided differences there. `--quadrature 8` removes this for the cost of 256 rays (1,280 with the differential rays).
- **Quadrature is exact only where the integrand is smooth over a disc.** Vignetting cuts the pupil to a shape that is not a disc, and W can have a kink at the cut. There the rule converges only algebraically, so compare 24 and 48 rings before taking the result as exact.
  - US8264785 at 17.5° under real-stop aiming is the extreme case. Just inside the upper rim of the real stop (Py ≈ 0.98), W climbs from 0.27 to 18 waves within 5% of the pupil radius, and the exit-area Jacobian grows with it. That points to a ray approaching grazing incidence on one of the lens's aspheres.
  - There, the integral itself moves by 4×10⁻² wave between 24 and 48 rings, and the square grid's exit-area RMS drifts away as the grid is refined (0.790 at 64², 0.810 at 256², 0.834 at 512²).
  - The paraxially aimed programs launch inside the paraxial pupil and never reach that zone.
