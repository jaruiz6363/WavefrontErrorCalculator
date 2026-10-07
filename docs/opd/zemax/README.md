# Zemax OpticStudio OPDC, ray by ray

These tables compare OpticStudio's `OPDC` operand with WavefrontErrorCalculator's W at each pupil point. `OPDC` is the optical path difference with respect to the chief ray, in waves, at the Reference OPD setting "Exit Pupil". An RMS can hide where two wavefronts part; a point-by-point comparison cannot.

**How the values were read.** For every lens, wavelength and field, `OPDC` operands in OpticStudio 2022 R2's Merit Function Editor sampled 857 pupil points:
- a square grid of spacing 1/16 (797 points inside the pupil);
- 60 points on the rim, ρ = 1, every 6°, beyond the four the grid already has.

Each merit function was loaded as a `.MF` file, and the values were read back from the file OpticStudio saved, to 13 significant figures. That is 7,713 points per lens and aiming mode. The gathering scripts are kept outside this repository; the values are in `tests/TestData/zemax-opdc`.

**A pitfall in reading OPDs from a merit function.** OpticStudio stops evaluating a merit function at the first ray that fails, and every row after it then reads 0, just as a vignetted ray does. On US8264785 at 17.5°, a single failed ray near the top of the pupil zeroed the 60 rim rows after it, though most of those rays trace normally. The rows after each failure were therefore evaluated again in further passes until none was left. Without that, 54 good rays would have been reported as vignetted.

**What WEC traces.** The same normalised pupil points (Px, Py), on the refractive indices OpticStudio reports, under the `Zemax` preset; with ray aiming on, also `RayAiming=RealStop`. The `.md` files list every pupil point at the primary wavelength, with OpticStudio's OPDC and W − OPDC for each field. Other wavelengths, or a CSV of every ray, come from:

```
wfe parity tests/TestData/zemax-opdc/<lens>_OPDC_Off.json --preset Zemax --rays <file.md|file.csv>
wfe parity tests/TestData/zemax-opdc/<lens>_OPDC_Real.json --preset Zemax --set RayAiming=RealStop --rays ...
```

The largest |W − OPDC| in waves, over all three wavelengths and fields:

| Result | Grid | Rim | All | Rays compared | Vignetted in both |
|---|---|---|---|---|---|
| [KingslakeDG_OPDC_Off](KingslakeDG_OPDC_Off.md) | 9.8e-10 | 9.6e-10 | 9.8e-10 | 7713 | 0 |
| [Cooke_40deg_FC_OPDC_Off](Cooke_40deg_FC_OPDC_Off.md) | 1.6e-09 | 1.5e-09 | 1.6e-09 | 7713 | 0 |
| [US8264785_Ex4_OPDC_Off](US8264785_Ex4_OPDC_Off.md) | 1.4e-08 | 1.0e-08 | 1.4e-08 | 7706 | 7 |
| [Relay_1to1_OPDC_Off](Relay_1to1_OPDC_Off.md) | 9.8e-10 | 8.7e-10 | 9.8e-10 | 7713 | 0 |
| [Objective_NA03_5x_OPDC_Off](Objective_NA03_5x_OPDC_Off.md) | 5.7e-08 | 5.6e-08 | 5.7e-08 | 7713 | 0 |
| [KingslakeDG_OPDC_Real](KingslakeDG_OPDC_Real.md) | 4.5e-07 | 4.3e-07 | 4.5e-07 | 7713 | 0 |
| [Cooke_40deg_FC_OPDC_Real](Cooke_40deg_FC_OPDC_Real.md) | 7.6e-08 | 5.4e-08 | 7.6e-08 | 7713 | 0 |
| [US8264785_Ex4_OPDC_Real](US8264785_Ex4_OPDC_Real.md) | 6.1e-06 | 4.8e-06 | 6.1e-06 | 7688 | 25 |
| [Relay_1to1_OPDC_Real](Relay_1to1_OPDC_Real.md) | 1.1e-06 | 1.1e-06 | 1.1e-06 | 7713 | 0 |
| [Objective_NA03_5x_OPDC_Real](Objective_NA03_5x_OPDC_Real.md) | 2.1e-07 | 2.0e-07 | 2.1e-07 | 7713 | 0 |

No ray is vignetted in one program and not the other.

- **Ray aiming off:** the two agree to 6×10⁻⁸ wave at every point.
- **Ray aiming on:** they agree to 1.1×10⁻⁶ wave at every point on four lenses. The two aiming iterations stop at slightly different rays, and what is left grows with the slope of W. The exception is US8264785 at 17.5° near the top of the pupil, where W climbs steeply toward grazing rays. There, 3 points differ by more than 10⁻⁶, by 6.1×10⁻⁶ at most. At that wavelength and field, OpticStudio's own values at mirror-image points (Px = ∓0.1875, Py = 0.9375) differ by 7×10⁻⁷, so its aiming has not fully converged there either.

`ZemaxParityTests.TheZemaxPresetIsOpticStudiosOpdcOperand` checks all ten files.
