"""Export Optiland's wavefront of a lens, ray by ray, for WavefrontErrorCalculator to compare against.

Run in Optiland's own environment:

    uv run --project <optiland checkout> python verification/optiland/export.py <lens.zmx> <out.json> [rays]

For each field of the lens, at its primary wavelength, it writes Optiland's wavefront with
strategy "chief_ray" - the default - on its "uniform" distribution and along its tangential and
sagittal lines: each ray's normalised pupil coordinates, its OPD in waves and its intensity.
It also writes the refractive index Optiland uses behind every surface, so the comparison can
be made with Optiland's own glass data rather than another catalogue's, and Optiland's paraxial
data, so a difference in how the two programs read the file shows up before anything else.

Every surface Optiland intersects by iteration has its tolerance tightened to 1e-14 mm before
tracing. Read from a .zmx file, an even asphere is given 1e-6 mm, and a ray that stops that far
from the surface carries a path error of up to (n - n') times it: a thousandth of a wave on a
fast lens. That is a setting, not a convention, and the comparison is of conventions.
"""

from __future__ import annotations

import json
import sys
import warnings

import numpy as np
from optiland.fileio import load_zemax_file
from optiland.wavefront import Wavefront


def rays(optic, field, wavelength, num_rays, distribution):
    wf = Wavefront(optic, fields=[field], wavelengths=[wavelength], num_rays=num_rays,
                   distribution=distribution, strategy="chief_ray")
    data = next(iter(wf.data.values()))
    dist = wf.distribution
    return {
        "px": np.asarray(dist.x).tolist(),
        "py": np.asarray(dist.y).tolist(),
        "opd": np.asarray(data.opd).tolist(),
        "intensity": np.asarray(data.intensity).tolist(),
        "radius": float(np.asarray(data.radius)),
    }


def main(lens: str, out: str, num_rays: int = 33) -> None:
    warnings.simplefilter("ignore")
    optic = load_zemax_file(lens)
    for surface in optic.surfaces.surfaces:
        geometry = surface.geometry
        if hasattr(geometry, "tol"):
            geometry.tol = 1e-14
            if hasattr(geometry, "max_iter"):
                geometry.max_iter = 10000
    wavelength = optic.primary_wavelength
    fields = optic.fields.fields
    max_y = max(abs(f.y) for f in fields) or 1.0
    p = optic.paraxial

    result = {
        "lens": lens.replace("\\", "/").rsplit("/", 1)[-1],
        "wavelength_um": float(wavelength),
        "indices": np.abs(np.asarray(optic.surfaces.n(wavelength))).tolist(),
        "paraxial": {"efl": float(p.f2()), "epd": float(p.EPD()), "xpl": float(p.XPL()), "xpd": float(p.XPD())},
        "fields": [],
    }
    for index, f in enumerate(fields):
        field = (0.0, f.y / max_y)            # normalised (Hx, Hy), as Optiland takes a field
        result["fields"].append({
            "index": index,
            "y": float(f.y),
            "hy": float(field[1]),
            "grid": rays(optic, field, wavelength, num_rays, "uniform"),
            "tangential": rays(optic, field, wavelength, 41, "line_y"),
            "sagittal": rays(optic, field, wavelength, 41, "line_x"),
        })

    # The other wavelengths, each with its own indices: each is referred to its own chief ray
    # in Optiland, while the exit pupil and the image-space index stay the primary's.
    result["others"] = []
    for w_index, w in enumerate(optic.wavelengths.wavelengths):
        if abs(w.value - wavelength) < 1e-12:
            continue
        result["others"].append({
            "index": w_index,
            "wavelength_um": float(w.value),
            "indices": np.abs(np.asarray(optic.surfaces.n(w.value))).tolist(),
            "fields": [
                {"index": index, "grid": rays(optic, (0.0, f.y / max_y), w.value, num_rays, "uniform")}
                for index, f in enumerate(fields)
            ],
        })

    with open(out, "w", encoding="utf-8") as fh:
        json.dump(result, fh)


if __name__ == "__main__":
    main(sys.argv[1], sys.argv[2], int(sys.argv[3]) if len(sys.argv) > 3 else 33)
