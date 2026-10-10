# User guide

How to use WavefrontErrorCalculator (WEC), task by task. What each number means and where it
comes from is in [`method.md`](method.md); how it was checked is in
[`verification.md`](verification.md). Every example below was run on the test lenses in
`tests/TestData`.

## Before you start

**Build and run.** [`BUILDING.md`](../BUILDING.md) has the steps. The command is `wfe`: either the
built `src/WavefrontErrorCalculator.Cli/bin/<Debug|Release>/net8.0/wfe.exe`, or
`dotnet run --project src/WavefrontErrorCalculator.Cli -- <arguments>`. Run any command without
arguments to see its options.

**Lens files.** WEC reads OpticStudio `.zmx`, CODE V `.seq`, OSLO `.len` and `.osl`, OPTALIX
`.otx` and `.opt`, Optiland `.json`, and LensHH-LT `.lhlt`, through AberrationCalculator. The
fields, wavelengths, aperture and ray-aiming setting come from the file.

**Glass.** The glass catalogs ship with WEC and are found without being pointed at: beside the
program, or in a `catalogs/Glass` folder above it or above the working directory. To use other
catalogs, set the environment variable `ABCALC_GLASS_DIR` to their folder; with none found, WEC
stops with "No glass catalogs found" and the folders it searched. A glass named with no catalog
can resolve differently in different programs: the double Gauss's `F4` is Schott's here and in
OpticStudio (n = 1.616592), CDGM's in Optiland and LensHH-LT (1.620047), which moves the wavefront
by several waves at the rim. When you compare with another program, give WEC that program's indices (see [Check a program](#check-whether-a-programs-wavefront-is-correct)).

**Indices count from 0.** `--field 2` is the third field of the file, `--wave 1` the second
wavelength.

## Compute a lens's wavefront error

```
wfe KingslakeDG.zmx
KingslakeDG.zmx: Q′ ChiefRay, E′ RealChief, aiming RealStop (aimed at the real stop: OpticStudio's Real), chief VignettedCenter, weights ExitArea, RMS StandardDeviation, sign Hopkins, chromatic PrimaryFocus

 field     value   wave  lambda(um)   RMS(w)    P-V(w)   piston(w)  Strehl   rays  vign   R'(mm)   pupil (centre, x, y)
     0         0      1      0.5876   0.05820   0.18867   -0.07097  0.8663   3228     0   101.357   0.0000, 1.0000, 1.0000
     1        10      1      0.5876   0.71484   3.97402   -0.35535       -   3228     0   102.953   0.0000, 1.0000, 1.0000
     2        14      1      0.5876   1.28019   6.97143   -0.70484       -   3228     0   104.529   0.0000, 1.0000, 1.0000
```

The first line states every convention in force; with no options it is `Reference`, Hopkins and
Welford's definition. One row per field, at the primary wavelength (`--wave all` for every
wavelength, `--field 1` for one field):

| Column | Meaning |
|---|---|
| `RMS(w)`, `P-V(w)` | the RMS and peak-to-valley of W over the pupil, in waves |
| `piston(w)` | the weighted mean of W, which the standard-deviation RMS removes |
| `Strehl` | Maréchal's 1 − (2πσ)², shown only where σ ≤ λ/14 |
| `rays`, `vign` | rays traced, and how many the lens stopped |
| `R'(mm)` | the exit reference sphere's radius |
| `pupil` | the part of the pupil the pencil fills: its centre on the meridian and semi-axes, in pupil coordinates |

W is the chief ray's optical path minus the ray's (Hopkins's sign), so a ray that arrives late
has negative W.

## Look at the wavefront

**A fan,** W along a line through the pupil: `--fan T` (tangential) or `--fan S` (sagittal), with
the number of rays.

```
wfe KingslakeDG.zmx --field 2 --fan T 11
field 2 (14), wavelength 1 (0.5876 um)
      Px        Py      W(waves)
  0.0000  -1.0000     -6.681831
  0.0000  -0.8000     -2.770535
  ...
  0.0000   0.0000      0.000000
  ...
  0.0000   1.0000     -3.346587
```

**Every ray,** with `--csv rays.csv`. One row per ray, with these columns:

| Columns | Meaning |
|---|---|
| `field`, `wavelength` | indices |
| `Px`, `Py` | the pupil point asked for |
| `launchPx`, `launchPy` | where the ray was launched in the paraxial entrance pupil (after aiming) |
| `x'`, `y'`, `z'` | its point on the exit reference sphere, reduced by the exit pupil's semi-diameter |
| `xS'`, `yT'` | canonical exit coordinates, in which the pupil's rim is near 1 |
| `imageX`, `imageY` | where it meets the image plane, in mm |
| `L`, `M`, `N` | its direction cosines in image space |
| `W` | its wavefront aberration, in waves |
| `weight` | its weight in the RMS |
| `vignetted`, `stoppedAt` | 1 if the lens stopped it, and at which surface (−1 for none) |

**Zernike coefficients,** with `--zernike standard:37` (Noll) or `--zernike fringe:37`:

```
wfe KingslakeDG.zmx --field 0 --zernike standard:9
Zernike Standard, field 0, wavelength 1, on Canonical coordinates: residual RMS 0.010938 w, RMS from the coefficients 0.056286 w
  Z1   n=0  m=0      -0.069449
  Z4   n=2  m=0      -0.056286
  ...
```

The fit is made on the coordinates and weights the options set (`PupilCoordinates`,
`Weighting`), so a program's coefficients are only comparable under that program's preset.

## Choose the sampling

| Option | Samples | Use it for |
|---|---|---|
| `--grid n` (default 64) | n × n rays at the cells' centres | a general map; how most programs sample |
| `--nodes n` | n × n nodes from −1 to 1, rim included | matching a program that samples nodes (OpticStudio's "n × n" analyses are `--nodes n−1`) |
| `--hexapolar rings` | hexapolar rings | matching a program that samples that way |
| `--quadrature rings [arms]` | Gauss rings | the RMS as an exact integral over the pupil, not an estimate |

A grid only estimates the integral the RMS is. `--quadrature 8` computes it, to about 10⁻¹⁰ wave
on a smooth, unvignetted pupil, with 256 rays; a 64×64 grid can differ from it by 10⁻² wave
(`verification.md`). Quadrature nodes lie inside the rim, so its P-V falls short of a grid's.

## Compute it as a given program does

`--preset` sets every convention a program uses; `--set Switch=Value` changes one (repeatable,
names and values in any case).

```
wfe KingslakeDG.zmx --preset Zemax
wfe KingslakeDG.zmx --preset Zemax --set RayAiming=RealStop      OpticStudio with ray aiming Real
```

| Preset | Is |
|---|---|
| `Reference` | Hopkins and Welford (the default) |
| `Zemax` | OpticStudio's OPD (OPDC), Reference OPD "Exit Pupil", ray aiming Off |
| `ZemaxZernike` | OpticStudio's Zernike Standard analysis, with its RMS and P-V (sample with `--nodes n−1` for its n × n) |
| `LensHHLT` | LensHH-LT, which computes OPDC: the `Zemax` preset |
| `Optiland` | Optiland's wavefront, strategy `chief_ray` |

OSLO has no preset; set its conventions directly ([`programs.md`](programs.md) has them all). For
its default, the central reference ray (`crr`) and the reference sphere at the exit pupil:

```
--set ChiefRay=RealStopCenter --set ChromaticReference=OwnChief --set PupilCoordinates=Launch
--set RayAiming=AplanaticReference --set ExitPupil=UserRadius --set UserReferenceRadius=<the radius OSLO reports>
```

With OSLO's entrance-pupil aiming (`enp`) use `RayAiming=Aplanatic`; with its reference sphere at
infinity or at the last surface, `ExitPupil=Infinite` or `ExitPupil=LastSurface`.

**OpticStudio's ray aiming has three settings,** Off, Paraxial and Real. WEC's `Paraxial` is
OpticStudio's **Off** (unaimed, at the paraxial entrance pupil); `RealStop` is its **Real**.
OpticStudio's Paraxial setting has not been compared.

The switches:

| Switch | Values (default first) |
|---|---|
| `ReferenceCenter` | `ChiefRay`, `Gaussian`, `Centroid`, `BestFitSphere` |
| `ExitPupil` | `RealChief`, `ParaxialAxial`, `ParaxialChiefIntersect`, `Infinite`, `UserDistance` (with `UserExitPupilDistance`), `ParaxialDistance`, `UserRadius` (with `UserReferenceRadius`), `LastSurface`, `ImageSurface` (OpticStudio's "Absolute": the path to the image surface, no sphere) |
| `RayAiming` | `RealStop`, `Paraxial`, `ExitSphereGrid`, `Aplanatic`, `AplanaticReference`, `ParaxialReference` |
| `ChiefRay` | `VignettedCenter`, `StopCenter`, `RealStopCenter` |
| `PupilCoordinates` | `Canonical`, `Launch`, `ExitSphere`, `LaunchRefined` |
| `Weighting` | `ExitArea`, `PerRay`, `Quadrature` |
| `Sign` | `Hopkins` (chief − ray), `Wolf` (ray − chief) |
| `ChromaticReference` | `PrimaryFocus`, `PrimarySphere`, `OwnChief` |
| `Rms` | `StandardDeviation`, `AboutZero` |
| `Apertures` | `Fixed` (only apertures the file fixes), `All`, `None` |
| `PupilOrientation` | `MarginalRay`, `EntrancePupilPlane` (only differs when the entrance pupil lies behind the object) |
| `Defocus` | `Retrace`, `ExactTerm`, `ParaxialTerm`; with `FocusShift` in mm |
| `IncludeVignettedAsZero`, `DivideByImageIndex` | `false`, `true` |

[`method.md`](method.md) §5 defines each, with its source.

## See what a convention changes

`wfe compare` runs two presets, then the first with each differing switch taken from the second,
one at a time, so you can see which convention moves the number and by how much:

```
wfe compare KingslakeDG.zmx --presets Reference,Zemax --grid 32
 field     value  RMS Reference    RMS Zemax  difference  P-V Reference    P-V Zemax  difference
     2        14       1.29246      1.23755    -0.05491       6.69966      6.44629    -0.25337

Each switch on its own: Reference with the one switch from Zemax, change in RMS (waves).
  switch                                  field 0      field 1      field 2
  ExitPupil = ParaxialChiefIntersect      0.00000     +0.00002     +0.00016
  RayAiming = Paraxial                    0.00000     -0.00547     -0.04930
  Weighting = PerRay                     -0.00006     -0.00112     -0.00211
  ...
```

Here almost all of the 0.055 wave between the two at 14° is ray aiming; the reference sphere
moves it by 2×10⁻⁴.

## Check whether a program's wavefront is correct

This is what WEC is for. The steps:

1. **Get the program's results into the result format.** [`result-format.md`](result-format.md)
   describes it: one JSON file per lens and setting, holding the program's OPD at known pupil
   points (fans, a map, or any list of points), the settings it ran with, and, best, the
   refractive indices it used. Any tool can write it; WEC contains no code that runs a licensed
   program. Put the lens file beside the result file, or in the folder above it.
2. **Run `wfe parity` under the program's preset**, with any switches its settings call for:

   ```
   wfe parity zemax/KingslakeDG_ExitPupil_Off.json --preset Zemax
    wave  field   set          rays   largest |W - program|   size     vignetting differs
       0      0   tangential     41              2.228E-010     0.9016        0
       0      2   tangential     41              9.811E-010     8.7225        0
       ...
   ```

   WEC traces the same pupil points on the program's own indices (where the file gives them), so
   a difference in glass data does not show as a difference. For each wavelength, field and set
   of rays it reports the largest |W − program|, the size of the wavefront, and how many rays
   are vignetted in one and not the other.
3. **Read the result.**
   - Differences at the level of the program's printed precision (10⁻⁸ wave or so): the program
     computes what its conventions say, correctly, on these rays.
   - A larger difference that `wfe compare` or a change of switch removes: a convention not yet
     identified. Find it before calling anything an error.
   - A difference no convention explains, under the program's own conventions: an error in the
     program. `--rays file.md` (or `.csv`) writes the comparison point by point, to see where it
     is: at the rim, at one field, at one wavelength, in sign.
   - "vignetting differs": the two disagree about which rays pass, usually through the
     apertures (`--set Apertures=...`) or ray aiming.

`docs/programs.md` records what this found for OpticStudio, LensHH-LT, Optiland and OSLO.

## Compare several programs at the same points

`wfe opd-table` puts several programs' OPD beside WEC's W, one table per field, one row per pupil
point:

```
wfe opd-table KingslakeDG.zmx Zemax=zemax-opdc/KingslakeDG_OPDC_Off.json LensHH-LT=lenshh-lt-opdc/KingslakeDG_OPDC_Off_Schott.json --preset Zemax --out KingslakeDG.md
```

Each program's Δ = program − W is computed with W on that program's indices; the W column is on
the first file's. `--values` writes the OPD values alone, side by side, without differences. The
tables in `docs/opd/compare` were made this way.

## Confirm WEC itself on a lens

`wfe rayces` computes W a second way, from the rays' directions alone with no optical path
(Rayces's exact relation, [`guide/rayces-method.md`](guide/rayces-method.md)), beside W from the
optical path:

```
wfe rayces Cooke_40deg_FC.zmx --points zemax-opdc/Cooke_40deg_FC_OPDC_Off.json --preset Zemax
  field 0: 857 points, largest |integrated - path| 5.303E-010 waves (|W| up to 1.3267; up to 256 steps)
  field 14: 857 points, largest |integrated - path| 1.135E-009 waves (|W| up to 3.4188; up to 256 steps)
  field 20: 857 points, largest |integrated - path| 1.009E-009 waves (|W| up to 3.5062; up to 256 steps)
largest |integrated - path| 1.135E-009 waves
```

`wfe hopkins --1952` computes W a third way: H. H. Hopkins's (1952) contribution of each surface
to the difference between the ray and the chief ray, referred to the same reference sphere, beside
W from the optical path:

```
wfe hopkins KingslakeDG.zmx --1952 --set RayAiming=RealStop --set ExitPupil=RealChief --set ChiefRay=StopCenter
  field 14: 857 points, |exact shift - path| 7.855E-011, |eq. 13 as printed - path| 4.906E-004, |W| up to 6.6818 waves
largest |exact - path| 7.855E-011, |eq. 13 - path| 4.906E-004 waves
```

Without `--1952` it uses Tatian's (1972) focal shift instead, which refers W to the reference of
infinite radius (`ExitPupil=Infinite`, OpticStudio's Reference OPD "Infinity"), and compares on
that. [`guide/hopkins-tatian-method.md`](guide/hopkins-tatian-method.md) explains both.

Agreement at 10⁻⁹ wave or so means the per-ray W is right on that lens, whatever its
conventions; it works under any preset whose reference sphere has a finite radius. Without
`--points` it uses 857 points over the pupil; `--csv` writes them all. It takes a few seconds per
lens.

## When something looks wrong

| Symptom | Likely cause |
|---|---|
| `No glass catalogs found` | the catalogs are not beside the program or above it: set `ABCALC_GLASS_DIR` to a folder of `.agf` files |
| A large difference from another program on one lens only, the double Gauss for instance | a different glass for the same name: compare on the program's own indices (`indices` in its result file) |
| `the chief ray of field n does not reach the image` | the field is beyond what the lens passes: its chief ray fails on the way |
| "vignetting differs" in `wfe parity` | the programs stop different rays: check `Apertures` and ray aiming |
| A slow or steep result near the rim | a ray near grazing incidence, where W changes very fast; `wfe rayces` refines there automatically |
| `the integration needs a reference sphere of finite radius` | `wfe rayces` with `ExitPupil=Infinite` or `ImageSurface`, which it does not cover |
| `no preset '...'` | the presets are `Reference`, `Optiland`, `LensHHLT`, `Zemax` and `ZemaxZernike`; OSLO is set by switches |
