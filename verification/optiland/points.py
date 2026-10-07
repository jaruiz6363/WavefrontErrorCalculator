"""Optiland's wavefront at given pupil points, in WavefrontErrorCalculator's wavefront-result/1 format.

Run in Optiland's own environment:

    uv run --project <optiland checkout> python verification/optiland/points.py <lens.zmx> <points.json> <out.json> [--indices <result.json>]

--indices gives every surface the refractive indices another program's result file records, at
each of its wavelengths (the medium after each surface), in place of Optiland's own glass data:
with Zemax's file, Optiland traces on exactly the AGF catalog values OpticStudio used.

points.json lists {"set", "px", "py"} under "points" (the sets "map" and "rim" become the result's
map and a set of that name). For every field and wavelength, Optiland's Wavefront with strategy
"chief_ray", its default, is evaluated on exactly those normalised pupil points: each ray's OPD in
waves against its own wavelength's chief ray. A ray Optiland gives no intensity is vignetted.
As in export.py, every iteratively intersected surface has its tolerance tightened to 1e-14 mm.
"""

from __future__ import annotations

import json
import sys
import warnings

import numpy as np
import optiland.backend as be
from optiland.distribution import BaseDistribution
from optiland.fileio import load_zemax_file
from optiland.materials.base import BaseMaterial
from optiland.wavefront import Wavefront


class TabulatedMaterial(BaseMaterial):
    """A refractive index given at each of a few wavelengths (µm), looked up exactly."""

    def __init__(self, table: dict[float, float]):
        super().__init__()
        self.table = table

    def _cache_state(self):
        return None

    def _lookup(self, wavelength: float) -> float:
        for lam, n in self.table.items():
            if abs(lam - wavelength) < 1e-6:
                return n
        raise ValueError(f"no index tabulated at {wavelength} µm")

    def _calculate_n(self, wavelength, **kwargs):
        if be.is_array_like(wavelength) and be.size(wavelength) > 1:
            w = np.asarray(be.to_numpy(wavelength), dtype=float)
            return be.array(np.vectorize(self._lookup)(w))
        return be.array(self._lookup(float(be.to_numpy(wavelength).reshape(-1)[0]) if be.is_array_like(wavelength) else float(wavelength)))

    def _calculate_k(self, wavelength, **kwargs):
        if be.is_array_like(wavelength) and be.size(wavelength) > 1:
            return be.zeros_like(wavelength)
        return be.array(0.0)


def use_indices(optic, result_path: str) -> None:
    """Every surface's following medium takes the result file's index at each wavelength."""
    with open(result_path, encoding="utf-8") as fh:
        waves = json.load(fh)["wavelengths"]
    surfaces = optic.surfaces.surfaces
    counts = {len(w["indices"]) for w in waves}
    if counts != {len(surfaces)}:
        raise ValueError(f"{result_path} lists {counts} indices, the lens has {len(surfaces)} surfaces")
    for i, surface in enumerate(surfaces):
        surface.material_post = TabulatedMaterial({float(w["wavelength_um"]): float(w["indices"][i]) for w in waves})


class GivenDistribution(BaseDistribution):
    """The pupil points as given."""

    def __init__(self, x, y):
        self.x = be.array(np.asarray(x, dtype=float))
        self.y = be.array(np.asarray(y, dtype=float))

    def generate_points(self, num_points: int):
        pass


def main(lens: str, points_path: str, out: str, indices: str | None = None) -> None:
    warnings.simplefilter("ignore")
    optic = load_zemax_file(lens)
    if indices:
        use_indices(optic, indices)
    for surface in optic.surfaces.surfaces:
        geometry = surface.geometry
        if hasattr(geometry, "tol"):
            geometry.tol = 1e-14
            if hasattr(geometry, "max_iter"):
                geometry.max_iter = 10000

    with open(points_path, encoding="utf-8") as fh:
        points = json.load(fh)["points"]
    sets = sorted({p["set"] for p in points}, key=lambda s: (s != "map", s))
    px = [p["px"] for p in points]
    py = [p["py"] for p in points]
    distribution = GivenDistribution(px, py)

    fields = optic.fields.fields
    max_y = max(abs(f.y) for f in fields) or 1.0
    primary = optic.primary_wavelength
    wavelengths = []
    for w_index, w in enumerate(optic.wavelengths.wavelengths):
        out_fields = []
        for index, f in enumerate(fields):
            field = (0.0, f.y / max_y)
            wf = Wavefront(optic, fields=[field], wavelengths=[w.value], num_rays=len(points),
                           distribution=distribution, strategy="chief_ray")
            data = next(iter(wf.data.values()))
            opd = np.asarray(be.to_numpy(data.opd), dtype=float)
            intensity = np.asarray(be.to_numpy(data.intensity), dtype=float)
            entry = {"index": index, "value": float(f.y), "fans": {}}
            for s in sets:
                k = [i for i, p in enumerate(points) if p["set"] == s]
                rays = {
                    "px": [px[i] for i in k],
                    "py": [py[i] for i in k],
                    "opd": [None if intensity[i] == 0 or not np.isfinite(opd[i]) else float(opd[i]) for i in k],
                    "vignetted": [bool(intensity[i] == 0 or not np.isfinite(opd[i])) for i in k],
                }
                if s == "map":
                    entry["map"] = rays
                else:
                    entry["fans"][s] = rays
            out_fields.append(entry)
        wavelengths.append({
            "index": w_index,
            "wavelength_um": float(w.value),
            "indices": np.abs(np.asarray(be.to_numpy(optic.surfaces.n(w.value)))).tolist(),
            "fields": out_fields,
        })

    result = {
        "format": "wavefront-result/1",
        "program": "Optiland",
        "version": "a3fb3e1b",
        "lens": lens.replace("\\", "/").rsplit("/", 1)[-1],
        "settings": {
            "strategy": "chief_ray",
            **({"indices": "from " + indices.replace("\\", "/").rsplit("/", 1)[-1]} if indices else {}),
            "rays": f"Wavefront at given pupil points, {len(points)} points",
            "primary_wavelength": next(i for i, w in enumerate(optic.wavelengths.wavelengths)
                                       if abs(w.value - primary) < 1e-12),
        },
        "wavelengths": wavelengths,
    }
    with open(out, "w", encoding="utf-8") as fh:
        json.dump(result, fh)


if __name__ == "__main__":
    args = sys.argv[1:]
    extra = None
    if "--indices" in args:
        k = args.index("--indices")
        extra = args[k + 1]
        del args[k:k + 2]
    main(args[0], args[1], args[2], extra)
