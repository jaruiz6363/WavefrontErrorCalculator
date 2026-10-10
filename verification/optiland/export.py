"""Export Optiland's wavefront of a lens, ray by ray, for WavefrontErrorCalculator to compare against.

Run in Optiland's own environment:

    uv run --project <optiland checkout> python verification/optiland/export.py <lens.zmx> <out.json> [rays] [--field-type T]

With --field-type real_image_height or paraxial_image_height (an object at infinity), every
angle field of the lens is given to Optiland instead as an image height: the height at which
its real chief ray meets the image. Optiland then solves for the angle that reaches it, and
each field records that height and the angle Optiland launched it at ("image_height",
"angle_deg"), so the same field can be traced here by angle. An image-height field is the same
collimated beam as the angle field aimed at the same chief ray, and its wavefront must be the same.

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
import optiland.backend as be
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


def as_image_heights(optic, field_type: str, wavelength: float) -> list[float]:
    """Redefines the optic's angle fields as image heights of type `field_type`: the height at
    which each field's real chief ray meets the image. Returns the heights, in field order."""
    fields = optic.fields.fields
    max_y = max(abs(f.y) for f in fields) or 1.0
    heights = []
    for f in fields:
        image = optic.trace_generic(0.0, f.y / max_y, 0.0, 0.0, wavelength)
        heights.append(float(np.asarray(be.to_numpy(image.y)).reshape(-1)[0]))
    while optic.fields.num_fields:
        optic.fields.remove(0)
    optic.fields.set_type(field_type)
    for h in heights:
        optic.fields.add(y=h)
    return heights


def launch_angle(optic, hy: float, wavelength: float) -> float:
    """The angle, in degrees, of the chief ray Optiland launches for normalised field hy."""
    launch = optic.ray_tracer.ray_generator.generate_rays(0.0, hy, 0.0, 0.0, wavelength)
    m = float(np.asarray(be.to_numpy(launch.M)).reshape(-1)[0])
    n = float(np.asarray(be.to_numpy(launch.N)).reshape(-1)[0])
    return float(np.degrees(np.arctan2(m, n)))


def main(lens: str, out: str, num_rays: int = 33, field_type: str | None = None) -> None:
    warnings.simplefilter("ignore")
    optic = load_zemax_file(lens)
    for surface in optic.surfaces.surfaces:
        geometry = surface.geometry
        if hasattr(geometry, "tol"):
            geometry.tol = 1e-14
            if hasattr(geometry, "max_iter"):
                geometry.max_iter = 10000
    wavelength = optic.primary_wavelength
    heights = as_image_heights(optic, field_type, wavelength) if field_type else None
    fields = optic.fields.fields
    max_y = max(abs(f.y) for f in fields) or 1.0
    p = optic.paraxial

    result = {
        "lens": lens.replace("\\", "/").rsplit("/", 1)[-1],
        "field_type": field_type or "as in the file",
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
        if heights is not None:
            result["fields"][-1]["image_height"] = heights[index]
            result["fields"][-1]["angle_deg"] = launch_angle(optic, field[1], wavelength)

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
    args = sys.argv[1:]
    kind = None
    if "--field-type" in args:
        at = args.index("--field-type")
        kind = args[at + 1]
        del args[at:at + 2]
    main(args[0], args[1], int(args[2]) if len(args) > 2 else 33, kind)
