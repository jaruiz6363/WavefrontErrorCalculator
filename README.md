# WavefrontErrorCalculator

WavefrontErrorCalculator (WEC) is a reference for deciding whether an optical design program's
wavefront error is correct. Programs disagree about the OPD of the same lens, and agreement
between two of them proves only that they are consistent: they can share a convention, or an
error. WEC settles the question in two steps.

1. **It is shown to be right on its own,** without appeal to any other program: against cases
   with exact answers, against aberration theory, and against an independent method that reaches
   the wavefront by another road - Rayces's exact relation between wave and ray aberration,
   integrated from the rays' directions alone with no optical path. The two methods agree to
   better than 10⁻⁸ wave on every test lens, on axis and off, at infinite and finite conjugates.
2. **Each program is then judged against it.** Every convention that changes the number - where
   the reference sphere is centred, which exit pupil it passes through, how rays are aimed, how
   the pupil is sampled and weighted, what the RMS is taken about - is a switch, so WEC can compute
   the wavefront exactly as a program defines it. Under that program's own conventions, any
   difference that remains is an error in the program, not a matter of definition.

The wavefront is computed as Hopkins and Welford define it: the chief ray's optical path minus
each ray's, between the entrance and exit reference spheres. The specification, with its sources,
is [`docs/method.md`](docs/method.md). The optics - lens files, glass, the paraxial and real ray
traces, ray aiming - are [AberrationCalculator](https://github.com/jaruiz6363/AberrationCalculator)'s,
carried in `external/AberrationCalculator`. Building is in [`BUILDING.md`](BUILDING.md).

## Use

```
wfe <lens> [--field i|all] [--wave i|all] [--preset Reference] [--set Switch=Value ...]
           [--grid n | --nodes n | --hexapolar rings | --quadrature rings [arms]
            | --fan T|S [count]] [--zernike standard:37|fringe:37] [--csv rays.csv]
```

```
wfe KingslakeDG.zmx
 field     value   wave  lambda(um)   RMS(w)    P-V(w)   piston(w)  Strehl   rays  vign   R'(mm)   pupil (centre, x, y)
     0         0      1      0.5876   0.05820   0.18867   -0.07097  0.8663   3228     0   101.357   0.0000, 1.0000, 1.0000
     1        10      1      0.5876   0.71484   3.97402   -0.35535       -   3228     0   102.953   0.0000, 1.0000, 1.0000
     2        14      1      0.5876   1.28019   6.97143   -0.70484       -   3228     0   104.529   0.0000, 1.0000, 1.0000
```

The grid estimates an integral over the exit pupil. `--quadrature 8` computes that integral: 256 rays on Gauss rings, weighted by exit-pupil area through differential rays, converged to about 10⁻¹⁰ wave on a smooth, unvignetted pupil. It differs from the 64×64 grid by up to 10⁻² wave; see `docs/verification.md`.

```
wfe KingslakeDG.zmx --quadrature 8
 field     value   wave  lambda(um)   RMS(w)    P-V(w)   piston(w)  Strehl   rays  vign   R'(mm)   pupil (centre, x, y)
     0         0      1      0.5876   0.05789   0.18299   -0.07056  0.8677    256     0   101.357   0.0000, 1.0000, 1.0000
     1        10      1      0.5876   0.70974   3.88846   -0.35240       -    256     0   102.953   0.0000, 1.0000, 1.0000
     2        14      1      0.5876   1.27090   6.83252   -0.69912       -    256     0   104.529   0.0000, 1.0000, 1.0000
```

The P-V from quadrature nodes is the largest difference among the nodes, which lie inside the rim, so it falls short of the grid's.

The other commands (each prints its options when run without arguments):

| Command | What it does |
|---|---|
| `wfe rayces <lens>` | The independent check: W by integrating Rayces's relation from the rays' directions, beside W from the optical path, point by point |
| `wfe hopkins <lens>` | A third way: W by Hopkins's surface contributions and Tatian's focal shift (no exit pupil), beside the optical path with the infinite reference and the preset's own sphere |
| `wfe parity <result.json>` | Another program's wavefront, in the format of [`docs/result-format.md`](docs/result-format.md), against WEC's under a preset: ray by ray, on that program's own refractive indices |
| `wfe opd-table <lens> <name>=<result.json> ...` | Several programs' OPD at the same pupil points, side by side with WEC's W, as a table |
| `wfe compare <lens> --presets A,B` | What each switch on which two presets differ does to the RMS and P-V, one switch at a time |

## Presets

`--preset` sets the switches together; `--set Switch=Value` changes one.

| Preset | Conventions of | Reproduces it to |
|---|---|---|
| `Reference` (default) | Hopkins and Welford: the real exit pupil, rays aimed at the real stop, exit-pupil area weights, canonical coordinates | - |
| `Zemax` | Zemax OpticStudio's OPD (OPDC), with Reference OPD "Exit Pupil"; ray aiming off, or `--set RayAiming=RealStop` for real | 6×10⁻⁸ wave aiming off, 1.1×10⁻⁶ real (6×10⁻⁶ at three near-grazing points of US8264785), at 857 points per field |
| `ZemaxZernike` | OpticStudio's Zernike Standard analysis, with its RMS and P-V | 10⁻⁸ wave, the precision it prints |
| `LensHHLT` | LensHH-LT, which computes OpticStudio's OPDC: the `Zemax` preset | 2×10⁻⁸ wave aiming off, 7×10⁻⁷ real; RMS and P-V to 10⁻⁶ of their size |
| `Optiland` | Optiland's wavefront with strategy `chief_ray` | 2×10⁻⁸ wave (7.5×10⁻⁶ on US8264785's aspheres); not the NA 0.3 objective, where Optiland's wavefront has the wrong sign |

OSLO has no preset of its own: its conventions are reproduced by switches (`ChiefRay` RealStopCenter, `RayAiming` Aplanatic or AplanaticReference, `ExitPupil` UserRadius with the radius OSLO reports), to 1×10⁻⁸ wave. [`docs/programs.md`](docs/programs.md) gives each program's conventions, how they were found, and the errors that remain under them.

## What it computes

- **The optical path,** summed along real rays from the entrance reference surface.
- **The exit reference sphere,** centred on the chief ray, the Gaussian image, the centroid, the best-fit point or a given point. It passes through the real exit pupil, the paraxial one, the chief ray's crossing of the paraxial pupil plane, a given radius or the last surface, or it is the reference at infinity.
- **Rays,** aimed at the real stop, launched at the paraxial pupil, or mapped as OSLO maps a finite object's pupil.
- **Apertures and vignetting.** The pupil the pencil actually fills is explored, and its centre is the chief ray.
- **Canonical pupil coordinates,** in which that pupil is a circle.
- **Exit-pupil area weights,** and Gauss quadrature for the exact pupil integral.
- **Statistics:** RMS, P-V and Strehl.
- **Zernike fits:** Standard (Noll) and Fringe.
- **A change of focus,** by re-tracing or by Hopkins's exact term, or by the paraxial ρ² shortcut for comparison.
- **Afocal images,** referred to a plane.
- **Output:** maps, OPD fans and per-ray CSV.

## How it is verified

[`docs/verification.md`](docs/verification.md) has every check and its result. In brief:
- **Exact answers:** a paraboloid, a sphere at its centre of curvature, the aplanatic points of a refracting sphere, a plate in a diverging beam, confocal paraboloids (afocal), and a defocused perfect image.
- **Aberration theory:** the Seidel limit (W040 and W131 to 0.02%), Welford's exact relation between wave and ray aberration (to 10⁻⁵), and the exact change of focus at NA 0.8 (to 10⁻⁵ wave).
- **An independent method:** Rayces's integration agrees with the optical path to 10⁻¹⁰–7×10⁻⁹ wave on the five test lenses, and to 2.5×10⁻⁹ on three singlets built to be extreme, with up to 3,900 waves of aberration and an exit pupil 18 mm from the image.
- **Sampling:** the grids against the exact pupil integral.

## Documentation

| Document | Contents |
|---|---|
| [`docs/user-guide.md`](docs/user-guide.md) | How to use WEC, task by task: computing a wavefront, choosing the sampling, matching a program, checking a program's results, confirming WEC on a lens |
| [`docs/method.md`](docs/method.md) | The specification: definitions, every switch, the presets, the test lenses, each program's conventions and defects |
| [`docs/programs.md`](docs/programs.md) | Optiland, Zemax OpticStudio, LensHH-LT and OSLO: what each computes, and how closely WEC reproduces it |
| [`docs/verification.md`](docs/verification.md) | Every check of WEC itself and of the programs, with results |
| [`docs/guide/wec-method.md`](docs/guide/wec-method.md) | How WEC computes the wavefront, with diagrams |
| [`docs/guide/rayces-method.md`](docs/guide/rayces-method.md) | Rayces's exact relation and the integration that checks WEC, with diagrams |
| [`docs/opd/zemax`](docs/opd/zemax/README.md), [`docs/opd/compare`](docs/opd/compare/README.md) | OPD at 857 pupil points per field: OpticStudio against WEC, and every program side by side |
| [`docs/result-format.md`](docs/result-format.md) | The file format for another program's results |
| [`BUILDING.md`](BUILDING.md) | Building, testing, and updating AberrationCalculator |

## Not yet done

- OpticStudio's third ray-aiming setting, Paraxial, has not been gathered; Off and Real have.
- The rule by which OSLO sizes its exit-pupil sphere off axis is not identified; WEC takes the radius OSLO reports.
- Test lenses D to F of `docs/method.md` (afocal, NA 0.9, achromat), Conrady's chromatic mode, and universal-coefficient sampling.
