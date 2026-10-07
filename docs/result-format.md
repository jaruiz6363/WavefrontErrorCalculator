# Result format

Wavefront results from another optical design program, as data. They live in `tests/TestData/<program>/`, one JSON file per lens. The parity tests read them and compare them with WavefrontErrorCalculator's presets (method.md §10).

This repository holds no code that runs a licensed program. The files are produced outside it: by automation kept privately, or by hand for programs that are run manually. Anything that can write this format will do.

## Layout

```json
{
  "format": "wavefront-result/1",
  "program": "name of the program",
  "version": "its version, as it reports it",
  "date": "2026-10-04",
  "lens": "KingslakeDG.zmx",
  "settings": {
    "ray_aiming": "off",
    "reference": "chief ray",
    "any other setting that affects the wavefront": "its value"
  },
  "wavelengths": [
    {
      "index": 1,
      "wavelength_um": 0.58756,
      "indices": [1.0, 1.61272, 1.0],
      "fields": [
        {
          "index": 2,
          "value": 14.0,
          "fans": {
            "tangential": { "px": [], "py": [], "opd": [], "vignetted": [] },
            "sagittal":   { "px": [], "py": [], "opd": [], "vignetted": [] }
          },
          "map": { "px": [], "py": [], "opd": [], "vignetted": [] },
          "statistics": { "rms": 1.30, "pv": 7.12, "rms_about": "chief ray" },
          "zernike": { "set": "standard", "coefficients": [] },
          "reference": { "exit_pupil_distance": -101.35, "radius": 104.53 }
        }
      ]
    }
  ]
}
```

## Fields

**`format`** is always `"wavefront-result/1"`.

**`program`, `version`, `date`** say where the numbers came from. Results from different versions of the same program are different files.

**`lens`** is the lens file's name. It is the same file in `tests/TestData/`, so both programs read the same prescription.

**`settings`** is free-form: every option of the program that changes the wavefront, as the program names it. Examples are the reference for OPD, ray aiming, pupil sampling, how RMS is referenced, and vignetting. A setting left out is assumed to be the program's default, which then has to be stated in the program's notes in `docs/programs.md`.

**`wavelengths[]`** gives one entry per wavelength the program computed. `index` is the wavelength's position in the lens file, counting from 0.

**`indices`** (optional) are the refractive indices the program used behind each surface, indexed like the surfaces. When present, the comparison uses them in place of WavefrontErrorCalculator's own glass data. This keeps a difference between glass catalogues from being mistaken for a difference of convention: 10⁻⁵ in index over 10 mm of glass is a sixth of a wave.

**`fields[]`** gives one entry per field. `index` is the field's position in the lens file, counting from 0, and `value` is the field as the file states it.

**`fans`, `map`** hold rays:
- **`px`, `py`:** the pupil coordinates the program used, sagittal then meridional, as fractions of the pupil radius.
- **`opd`:** the program's wavefront aberration in waves at that wavelength, with the program's own sign.
- **`vignetted`:** true where the program reports no ray.

Any of the fans or the map may be omitted.

**`statistics`** (optional): the program's RMS and P-V in waves. `rms_about` says what the program reports the RMS as taken about: the chief ray, the centroid, or the mean.

**`zernike`** (optional): the program's Zernike coefficients in waves. `set` is `"standard"` (Noll) or `"fringe"`, and term 1 is first.

**`reference`** (optional): whatever the program reports about its reference sphere, in mm. Examples are the exit pupil distance, the sphere's radius and its centre.

A program that reports less than this simply leaves the rest out. The fans alone are enough to identify most conventions.
