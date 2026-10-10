"""Builds the guide documents: the figures (SVG, into figures/) and a print-ready HTML of each
markdown document, for build.ps1 to print to PDF.

    uv run --project <any env with matplotlib> python docs/guide/build.py <html output dir>

Run from the repository root. The geometry figures are schematic and drawn to illustrate, with
the aberrations exaggerated; the data figures come from the comparison tables (docs/opd/compare)
and from `wfe rayces`, run here.
"""

from __future__ import annotations

import csv
import html
import math
import re
import subprocess
import sys
from pathlib import Path

import matplotlib

matplotlib.use("Agg")
import matplotlib.pyplot as plt
import numpy as np
from matplotlib.patches import Arc, Ellipse, FancyArrowPatch, FancyBboxPatch

HERE = Path(__file__).resolve().parent
ROOT = HERE.parents[1]
FIG = HERE / "figures"

BLUE, RED, GREEN, GREY, PURPLE = "#1f5fa8", "#c0392b", "#2e8b57", "#888888", "#7d3c98"


# ── small drawing helpers ─────────────────────────────────────────────────────────────────

def new(w=10, h=6):
    fig, ax = plt.subplots(figsize=(w, h))
    ax.set_aspect("equal")
    ax.axis("off")
    return fig, ax


def save(fig, name):
    FIG.mkdir(exist_ok=True)
    fig.savefig(FIG / name, format="svg", bbox_inches="tight")
    plt.close(fig)


def arc(ax, c, r, a1, a2, **kw):
    t = np.radians(np.linspace(a1, a2, 200))
    ax.plot(c[0] + r * np.cos(t), c[1] + r * np.sin(t), **kw)


def dot(ax, p, label=None, offset=(4, 4), color="black", size=5, **kw):
    ax.plot(*p, "o", color=color, ms=size, zorder=5)
    if label:
        ax.annotate(label, p, xytext=offset, textcoords="offset points", fontsize=11, color=color, **kw)


def note(ax, text, xy, xytext, color="black"):
    ax.annotate(text, xy, xytext=xytext, textcoords="data", fontsize=9, color=color,
                arrowprops=dict(arrowstyle="-", color=color, lw=0.7))


def line_sphere(p, d, c, r, near):
    """Point where the line p + t d meets the circle (centre c, radius r), nearest `near`."""
    p, d, c, near = map(np.asarray, (p, d, c, near))
    d = d / np.linalg.norm(d)
    delta = p - c
    b = d @ delta
    disc = b * b - (delta @ delta - r * r)
    roots = [p + (-b + s * math.sqrt(disc)) * d for s in (1, -1)]
    return min(roots, key=lambda q: np.linalg.norm(q - near))


# ── WEC method figures ────────────────────────────────────────────────────────────────────

def wec_geometry():
    fig, ax = new(11, 6.4)
    Q = np.array([0.0, 28.0])
    ER = np.array([-62.0, 0.0])                     # real chief ray crosses the axis
    zP = -78.0                                      # paraxial exit-pupil plane
    slope = (Q[1] - ER[1]) / (Q[0] - ER[0])
    EZ = np.array([zP, ER[1] + slope * (zP - ER[0])])  # chief ray crosses the paraxial pupil plane
    RW, RZ = np.linalg.norm(Q - ER), np.linalg.norm(Q - EZ)

    ax.plot([-138, 8], [0, 0], color=GREY, lw=0.8)
    ax.text(-136, 1.5, "optical axis", fontsize=8, color=GREY)
    ax.plot([0, 0], [-18, 48], color="black", lw=1.4)
    ax.text(1.5, -17, "image plane", fontsize=9)
    ax.add_patch(Ellipse((-118, 0), 9, 86, fill=False, lw=1.4))
    ax.text(-118, 46, "last lens", fontsize=9, ha="center")
    ax.plot([zP, zP], [-26, 46], ls="--", color=GREEN, lw=1)
    ax.text(zP, 47, "paraxial exit-pupil plane", fontsize=8, color=GREEN, ha="center", va="bottom")

    z0 = -116.0
    ax.plot([z0, Q[0]], [ER[1] + slope * (z0 - ER[0]), Q[1]], color="black", lw=1.6)
    ax.text(-30, 17.5, "chief ray", fontsize=9, rotation=math.degrees(math.atan(slope)))

    a0 = math.degrees(math.atan2(ER[1] - Q[1], ER[0] - Q[0]))
    arc(ax, Q, RW, a0 - 30, a0 + 28, color=BLUE, lw=1.8)
    arc(ax, Q, RZ, a0 - 27, a0 + 25, color=RED, lw=1.5, ls="-.")

    # The actual wavefront through E': the WEC sphere, deformed away from the chief ray.
    th = np.radians(np.linspace(a0 - 30, a0 + 28, 200))
    dev = 7e-3 * (np.degrees(th) - a0) ** 2 * np.sign(np.degrees(th) - a0)
    wave = np.c_[Q[0] + (RW + dev) * np.cos(th), Q[1] + (RW + dev) * np.sin(th)]
    ax.plot(wave[:, 0], wave[:, 1], color=PURPLE, lw=1.5, ls=":")

    # An aberrated ray, landing at T, above Q'.
    A, T = np.array([-116.0, 33.0]), np.array([0.0, 37.0])
    ax.plot([A[0], T[0]], [A[1], T[1]], color="#d35400", lw=1.4)
    ax.text(-112, 35.5, "aberrated ray", fontsize=9, color="#d35400")
    d = T - A
    BW = line_sphere(A, d, Q, RW, ER)
    BZ = line_sphere(A, d, Q, RZ, EZ)
    # P: where the ray meets the drawn wavefront.
    u = d / np.linalg.norm(d)
    k = np.argmin([abs(u[0] * (w - A)[1] - u[1] * (w - A)[0]) for w in wave])
    P = wave[k]

    def label(p, text, at, color="black"):
        ax.annotate(text, p, xytext=at, textcoords="data", fontsize=9, color=color,
                    arrowprops=dict(arrowstyle="-", color=color, lw=0.7))

    dot(ax, Q, "Q′", (6, -4))
    label(Q, "reference focus:\nchief ray ∩ image plane", (6, 12))
    dot(ax, T, "T", (5, 2))
    ax.annotate("", T, Q, arrowprops=dict(arrowstyle="<->", lw=0.9))
    ax.text(2.5, 31.5, "ε", fontsize=11)
    dot(ax, ER, None, color=BLUE)
    label(ER, "E′ (WEC): the real chief\nray crosses the axis", (-50, -16), BLUE)
    dot(ax, EZ, None, color=RED)
    label(EZ, "E′ (Zemax): the chief ray crosses the paraxial exit-pupil plane", (-136, -36), RED)
    dot(ax, BW, None, color=BLUE)
    label(BW, "B′ on the WEC sphere", (-46, 55), BLUE)
    dot(ax, BZ, None, color=RED)
    label(BZ, "B′ on the Zemax sphere", (-136, 55), RED)
    dot(ax, P, None, color=PURPLE)
    label(P, "P, on the wavefront", (-30, 44), PURPLE)
    label(wave[30], "actual wavefront through E′", (-24, -9), PURPLE)
    zt = Q + RZ * np.array([math.cos(math.radians(a0 - 20)), math.sin(math.radians(a0 - 20))])
    label(zt, "Zemax sphere (OPDC)", (-112, -10), RED)
    wt = Q + RW * np.array([math.cos(math.radians(a0 + 14)), math.sin(math.radians(a0 + 14))])
    label(wt, "WEC sphere: centre Q′,\nradius |Q′E′|", (-34, 2), BLUE)
    ax.text(-128, 64, "W = n′·|PB′|, along the ray: the optical path from the wavefront to the sphere",
            fontsize=9)
    ax.set_xlim(-140, 40)
    ax.set_ylim(-40, 68)
    ax.set_title("Off-axis field point: image space in the meridional plane (schematic, not to scale)", fontsize=11)
    save(fig, "wec-geometry.svg")


def wec_entrance():
    fig, axes = plt.subplots(1, 2, figsize=(11, 4.2))
    for ax in axes:
        ax.set_aspect("equal")
        ax.axis("off")

    # Object at infinity, off axis: a tilted collimated beam, the reference plane across it.
    ax = axes[0]
    ang = math.radians(14)
    u = np.array([math.cos(ang), -math.sin(ang)])
    stop = np.array([30.0, 0.0])
    ax.plot([-40, 60], [0, 0], color=GREY, lw=0.8)
    ax.add_patch(Ellipse((15, 0), 6, 50, fill=False, lw=1.3))
    ax.plot([30, 30], [12, 26], color="black", lw=2.5)
    ax.plot([30, 30], [-12, -26], color="black", lw=2.5)
    ax.text(30, 28, "stop", ha="center", fontsize=9)
    n = np.array([-u[1], u[0]])
    for h in (-9, 0, 9):
        start = stop + n * h - 62 * u
        reach = (15 - start[0]) / u[0] if h else (30 - start[0]) / u[0]
        ax.plot(*zip(start, start + reach * u), color="black" if h == 0 else "#d35400", lw=1.4 if h == 0 else 1.0)
    base = stop - 46 * u
    ax.plot(*zip(base - 14 * n, base + 14 * n), color=BLUE, lw=1.8)
    ax.text(*(base + 15 * n + np.array([-16, 2])), "entrance reference plane\n(across the chief ray)", fontsize=8, color=BLUE)
    ax.text(-40, -36, "object at infinity, field angle 14°:\nthe path starts on the plane", fontsize=9)
    ax.set_xlim(-42, 62)
    ax.set_ylim(-42, 36)

    # Finite object: the reference sphere centred on the object point.
    ax = axes[1]
    O = np.array([-40.0, 12.0])
    ax.plot([-46, 60], [0, 0], color=GREY, lw=0.8)
    ax.add_patch(Ellipse((15, 0), 6, 50, fill=False, lw=1.3))
    ax.plot([30, 30], [12, 26], color="black", lw=2.5)
    ax.plot([30, 30], [-12, -26], color="black", lw=2.5)
    for target in ((15, 20), (15, -6), (15, 7)):
        ax.plot(*zip(O, target), color="#d35400", lw=1.0)
    ax.plot(*zip(O, (30, 0)), color="black", lw=1.4)
    rad = 30
    a = math.degrees(math.atan2(-12, 70))
    arc(ax, O, rad, a - 25, a + 25, color=BLUE, lw=1.8)
    dot(ax, O, "object point", (-10, 8))
    ax.text(-46, -36, "finite object: the path starts at the object\npoint, the centre of the entrance sphere", fontsize=9)
    ax.text(-30, 26, "entrance reference sphere", fontsize=8, color=BLUE)
    ax.set_xlim(-48, 62)
    ax.set_ylim(-42, 36)
    fig.suptitle("Where the optical path starts (schematic)", fontsize=11)
    save(fig, "wec-entrance.svg")


def wec_sampling():
    fig, axes = plt.subplots(1, 2, figsize=(10, 4.8))
    ax = axes[0]
    g = [(i / 16, j / 16) for i in range(-16, 17) for j in range(-16, 17) if i * i + j * j <= 256]
    rim = [(math.cos(2 * math.pi * k / 60), math.sin(2 * math.pi * k / 60)) for k in range(60)]
    ax.plot(*zip(*g), ".", ms=2.2, color=BLUE)
    ax.plot(*zip(*rim), "o", ms=3, color=RED)
    ax.add_patch(plt.Circle((0, 0), 1, fill=False, color=GREY))
    ax.set_title(f"comparison points: {len(g)} grid + {len(rim)} rim = {len(g) + len(rim)}", fontsize=10)
    ax.set_aspect("equal")
    ax.set_xlabel("Px")
    ax.set_ylabel("Py")

    ax = axes[1]
    rings, arms = 6, 12
    xg, _ = np.polynomial.legendre.leggauss(rings)
    rho = np.sqrt((xg + 1) / 2)
    for r in rho:
        for k in range(arms):
            t = 2 * math.pi * (k + 0.5) / arms
            ax.plot(r * math.cos(t), r * math.sin(t), "o", ms=4, color=GREEN)
        ax.add_patch(plt.Circle((0, 0), r, fill=False, color=GREEN, lw=0.4, alpha=0.5))
    ax.add_patch(plt.Circle((0, 0), 1, fill=False, color=GREY))
    ax.set_title(f"Gauss quadrature: {rings} rings in ρ², {arms} arms", fontsize=10)
    ax.set_aspect("equal")
    ax.set_xlabel("Px")
    for a in axes:
        a.set_xlim(-1.1, 1.1)
        a.set_ylim(-1.1, 1.1)
    fig.tight_layout()
    save(fig, "wec-sampling.svg")


def boxes(name, steps, title):
    fig, ax = plt.subplots(figsize=(11, 2.0 + 0.0))
    ax.axis("off")
    n = len(steps)
    w, gap = 1.0 / n - 0.025, 0.025
    for i, (head, body) in enumerate(steps):
        x = i * (w + gap)
        ax.add_patch(FancyBboxPatch((x, 0.1), w, 0.8, boxstyle="round,pad=0.01", fc="#eef3fa", ec=BLUE))
        ax.text(x + w / 2, 0.75, head, ha="center", va="center", fontsize=9.5, weight="bold")
        ax.text(x + w / 2, 0.42, body, ha="center", va="center", fontsize=8)
        if i < n - 1:
            ax.add_patch(FancyArrowPatch((x + w, 0.5), (x + w + gap, 0.5), arrowstyle="->", mutation_scale=10))
    ax.set_xlim(-0.015, 1.0)
    ax.set_ylim(0.0, 1.0)
    ax.set_title(title, fontsize=11)
    save(fig, name)


def wec_pipeline():
    boxes("wec-pipeline.svg", [
        ("1  Pupil", "the part of the\npupil the beam\nfills (if vignetted)"),
        ("2  Chief ray", "trace it; fix Q′, E′\nand R′ = |E′Q′|"),
        ("3  Rays", "aim each (Px, Py),\ntrace to the image,\nback to the sphere"),
        ("4  W", "(chief path − ray path)\n/ λ, at B′"),
        ("5  Statistics", "mean, RMS, P-V,\nZernike, by\nquadrature"),
    ], "One field point, one wavelength")


def wec_spheres():
    fig, ax = new(10, 5)
    Q = np.array([0.0, 0.0])
    R1, R2 = 60.0, 75.0
    arc(ax, Q, R1, 150, 210, color=BLUE, lw=1.8)
    arc(ax, Q, R2, 150, 210, color=RED, lw=1.5, ls="-.")
    dot(ax, Q, "Q′", (6, -4))
    for ang in (180, 197):
        d = np.array([math.cos(math.radians(ang)), math.sin(math.radians(ang))])
        ax.plot(*zip(Q + 82 * d, Q), color="black", lw=1.0)
    ax.annotate("a ray through Q′ crosses the gap along\nthe radius, as the chief ray does,\nso its W is unchanged", (-30, 0),
                xytext=(-48, 20), fontsize=8.5, arrowprops=dict(arrowstyle="->", lw=0.7))
    A, B = np.array([-84.0, -14.0]), np.array([0.0, 14.0])
    ax.plot(*zip(A, B), color="#d35400", lw=1.3)
    p1 = line_sphere(A, B - A, Q, R1, (-60, -10))
    p2 = line_sphere(A, B - A, Q, R2, (-75, -10))
    dot(ax, p1, None, color=BLUE)
    dot(ax, p2, None, color=RED)
    ax.annotate("an aberrated ray misses Q′ and\ncrosses the gap obliquely: a longer\nway, so its W depends on the radius",
                (p1 + p2) / 2, xytext=(-44, -36), fontsize=8.5, arrowprops=dict(arrowstyle="->", lw=0.7))
    dot(ax, B, "lands ε from Q′", (4, 2))
    ax.text(-62, 33, "radius R₁", color=BLUE, fontsize=9)
    ax.text(-80, 40, "radius R₂", color=RED, fontsize=9)
    ax.set_xlim(-90, 30)
    ax.set_ylim(-42, 46)
    ax.set_title("Two concentric reference spheres (schematic)", fontsize=11)
    save(fig, "wec-spheres.svg")


def read_values(path, field):
    text = path.read_text(encoding="utf-8")
    block = re.split(r"^## Field ", text, flags=re.M)
    for b in block[1:]:
        lines = b.splitlines()
        if lines[0].strip() != field:
            continue
        rows = [l for l in lines if l.startswith("|")]
        cols = [c.strip() for c in rows[0].strip("|").split("|")]
        out = []
        for r in rows[2:]:
            cells = [c.strip() for c in r.strip("|").split("|")]
            out.append({c: (None if v in ("–", "") else float(v)) for c, v in zip(cols, cells)})
        return out
    raise KeyError(field)


def wec_cooke_spheres():
    rows = read_values(ROOT / "docs/opd/compare/Cooke_40deg_FC_AimingOff.md", "20")
    line = sorted([r for r in rows if abs(r["Px"]) < 1e-9 and r["WEC"] is not None and r["Zemax"] is not None], key=lambda r: r["Py"])
    py = [r["Py"] for r in line]
    fig, (a1, a2) = plt.subplots(2, 1, figsize=(9, 5.6), sharex=True, gridspec_kw={"height_ratios": [3, 2]})
    a1.plot(py, [r["WEC"] for r in line], color=BLUE, lw=2, label="WEC sphere (RealChief)")
    a1.plot(py, [r["Zemax"] for r in line], color=RED, lw=1.2, ls="--", marker="o", ms=2.5, label="Zemax sphere (OPDC)")
    a1.set_ylabel("W (waves)")
    a1.legend(fontsize=9)
    a1.grid(alpha=0.3)
    a2.plot(py, [r["Zemax"] - r["WEC"] for r in line], color=RED, lw=1.2)
    a2.axhline(0, color="black", lw=0.7)
    a2.set_ylabel("Zemax − WEC (waves)")
    a2.set_xlabel("Py (Px = 0)")
    a2.grid(alpha=0.3)
    fig.suptitle("Cooke triplet, 20°, ray aiming off: the two reference spheres", fontsize=11)
    fig.tight_layout()
    save(fig, "wec-cooke-spheres.svg")


# ── Rayces figures ────────────────────────────────────────────────────────────────────────

def rayces_geometry():
    fig, ax = new(10.5, 6.2)
    H = 30.0
    Q = np.array([0.0, H])
    C = np.array([72.0, 0.0])
    R = np.linalg.norm(C - Q)
    a0 = math.degrees(math.atan2(C[1] - Q[1], C[0] - Q[0]))
    ax.plot([-8, 100], [0, 0], color=GREY, lw=0.8)
    ax.text(98, -4, "z", fontsize=11)
    ax.plot([0, 0], [-10, 62], color="black", lw=1.3)
    ax.text(-3, 60, "y", fontsize=11, ha="right")
    ax.text(1.5, -17, "image plane z = 0", fontsize=8)
    arc(ax, Q, R, a0 - 18, a0 + 46, color=BLUE, lw=1.8)
    th = np.radians(np.linspace(a0 - 18, a0 + 46, 200))
    dev = -6e-3 * (np.degrees(th) - a0) ** 2
    wave = np.c_[Q[0] + (R + dev) * np.cos(th), Q[1] + (R + dev) * np.sin(th)]
    ax.plot(wave[:, 0], wave[:, 1], color=PURPLE, lw=1.6, ls=":")
    k = 150
    P = wave[k]
    tangent = wave[k + 1] - wave[k - 1]
    normal = np.array([tangent[1], -tangent[0]])
    normal /= np.linalg.norm(normal)
    if normal[0] > 0:
        normal = -normal
    t = -P[0] / normal[0]
    T = P + t * normal
    ax.plot(*zip(P, T), color="#d35400", lw=1.3)
    radial = (P - Q) / np.linalg.norm(P - Q)
    S = Q + R * radial
    ax.plot(*zip(Q, S), color="black", lw=0.9, ls="--")
    ax.plot(*zip(Q, C), color="black", lw=0.9, ls="--")

    def label(p, text, at, color="black", size=9):
        ax.annotate(text, p, xytext=at, textcoords="data", fontsize=size, color=color,
                    arrowprops=dict(arrowstyle="-", color=color, lw=0.7))

    dot(ax, (0, 0), "O", (-12, -12))
    dot(ax, Q, "Q", (-14, 2))
    dot(ax, C, "C", (4, -12))
    dot(ax, P, None, color=PURPLE)
    label(P, "P (x, y): a point of the wavefront", (24, 62), PURPLE)
    dot(ax, S, None, color=BLUE)
    label(S, "S, on the sphere", (86, 30), BLUE)
    dot(ax, T, "T", (-14, -4), color="#d35400")
    label(0.35 * P + 0.65 * T, "ray PT, normal to the wavefront", (6, 2), "#d35400")
    label(Q + R * np.array([math.cos(math.radians(a0 + 40)), math.sin(math.radians(a0 + 40))]),
          "reference sphere: centre Q, radius R", (80, 66), BLUE)
    label(wave[20], "wavefront, propagating to the left", (78, -12), PURPLE)
    ax.annotate("", (0, 0), (0, H), arrowprops=dict(arrowstyle="<->", lw=0.8))
    ax.text(-6, H / 2, "H", fontsize=10)
    ax.annotate("", Q, T, arrowprops=dict(arrowstyle="<->", lw=0.8, color="#d35400"))
    ax.text(-7, (Q[1] + T[1]) / 2, "Y", fontsize=10, color="#d35400")
    ax.text(*((Q + C) / 2 + np.array([1, 2])), "R = QC", fontsize=9)
    ax.text(*((Q + P) / 2 + np.array([-4, 2.5])), "r = QP", fontsize=9)
    label((P + S) / 2, "W = PS = R − r", (96, 50), size=10)
    ax.set_xlim(-12, 122)
    ax.set_ylim(-20, 70)
    ax.set_title("Rayces (1964): the meridional section of his figure (schematic)", fontsize=11)
    save(fig, "rayces-geometry.svg")


def rayces_two_w():
    fig, ax = new(8.5, 5)
    Q = np.array([0.0, -200.0])
    R = 200.0
    arc(ax, Q, R, 84, 96, color=BLUE, lw=2)
    ax.text(-36, 3, "reference sphere", fontsize=9, color=BLUE)
    P = np.array([3.0, -14.0])
    radial = (P - Q) / np.linalg.norm(P - Q)
    S = Q + R * radial
    ang = math.radians(78)
    d = np.array([math.cos(ang), math.sin(ang)])
    B = line_sphere(P, d, Q, R, P)
    ax.plot(*zip(P - 6 * radial, S + 3 * radial), color="black", ls="--", lw=0.9)
    ax.plot(*zip(P - 8 * d, B + 3 * d), color="#d35400", lw=1.4)
    dot(ax, P, "P  (on the wavefront)", (-120, -2), color=PURPLE)
    dot(ax, S, "S", (-12, 6), color=BLUE)
    dot(ax, B, "B′", (6, 4), color="#d35400")
    ax.annotate("Nijboer's W = |PS|,\nalong the sphere's radius through P\n(Rayces's W)", (P + S) / 2, xytext=(-40, -6),
                fontsize=9, arrowprops=dict(arrowstyle="->", lw=0.7))
    ax.annotate("the programs' W = n′·|PB′|,\nalong the ray", (P + B) / 2, xytext=(14, -10), fontsize=9,
                arrowprops=dict(arrowstyle="->", lw=0.7, color="#d35400"), color="#d35400")
    ax.text(-38, -24, "to the sphere's centre Q ↓", fontsize=8, color=GREY)
    ax.set_xlim(-40, 40)
    ax.set_ylim(-26, 8)
    ax.set_title("Two ways to measure W at P (schematic; the angle is exaggerated)", fontsize=11)
    save(fig, "rayces-two-w.svg")


def rayces_frame():
    fig, ax = new(10.5, 6)
    Q = np.array([0.0, 28.0])
    C = np.array([-62.0, 0.0])
    zP = -78.0
    slope = (Q[1] - C[1]) / (Q[0] - C[0])
    CZ = np.array([zP, C[1] + slope * (zP - C[0])])
    ax.plot([-100, 10], [0, 0], color=GREY, lw=0.8)
    ax.text(-24, 1.5, "optical axis", fontsize=8, color=GREY)
    ax.plot([0, 0], [-12, 50], color="black", lw=1.3)
    ax.text(1.5, -11, "image plane", fontsize=9)
    ax.plot(*zip(np.array([-95, C[1] + slope * (-95 - C[0])]), Q), color="black", lw=1.5)
    ax.text(-82, -14.5, "chief ray", fontsize=9, rotation=math.degrees(math.atan(slope)))
    ez = (C - Q) / np.linalg.norm(C - Q)
    ey = np.array([-ez[1], ez[0]])
    ax.add_patch(FancyArrowPatch(Q, Q + 26 * ez, arrowstyle="-|>", mutation_scale=14, color=BLUE, lw=1.6))
    ax.text(*(Q + 28 * ez + np.array([-4, 3])), "z, toward C", fontsize=9, color=BLUE)
    ax.add_patch(FancyArrowPatch(Q, Q + 16 * ey, arrowstyle="-|>", mutation_scale=14, color=BLUE, lw=1.6))
    ax.text(*(Q + 17 * ey + np.array([2, 0])), "y", fontsize=9, color=BLUE)
    ax.plot(*zip(Q - 18 * ey, Q + 22 * ey), color=BLUE, ls="--", lw=1)
    ax.text(*(Q - 19 * ey + np.array([-2, 0])), "plane z = 0: through Q, across QC", fontsize=8.5, color=BLUE, ha="right")
    A = np.array([-95.0, 35.0])
    T0 = np.array([0.0, 37.0])
    d = (T0 - A) / np.linalg.norm(T0 - A)
    t = ((Q - A) @ ez) / (d @ ez)
    T = A + t * d
    ax.plot(*zip(A, T0), color="#d35400", lw=1.2)
    dot(ax, T, "T", (4, 4), color="#d35400")
    ax.annotate("", T, Q, arrowprops=dict(arrowstyle="<->", lw=0.8, color="#d35400"))
    ax.text(*((T + Q) / 2 + np.array([2.5, -1])), "(X, Y)", fontsize=9, color="#d35400")
    dot(ax, Q, "Q = Q′", (6, -6))
    dot(ax, C, None, color=BLUE)
    ax.annotate("C = E′ (WEC)", C, xytext=(-56, -9), fontsize=9, color=BLUE,
                arrowprops=dict(arrowstyle="-", color=BLUE, lw=0.7))
    dot(ax, CZ, None, color=RED)
    ax.annotate("or C = E′ (Zemax)", CZ, xytext=(-99, 4), fontsize=9, color=RED,
                arrowprops=dict(arrowstyle="-", color=RED, lw=0.7))
    ax.set_xlim(-100, 30)
    ax.set_ylim(-16, 56)
    ax.set_title("WEC's frame for the integration, off axis (schematic)", fontsize=11)
    save(fig, "rayces-frame.svg")


def rayces_paths():
    # Left: where the rays are chosen (pupil coordinates). Right: where the integral runs (the
    # wavefront point P of each of those rays, in image-space mm). The map between them is drawn
    # with a made-up distortion, to show that a straight line in the pupil is generally curved on
    # the wavefront; it is schematic, not a traced lens.
    def to_wavefront(px, py):
        return 6.0 * (px * (1 + 0.18 * py)), 6.0 * (py + 0.14 * (px * px + py * py) - 0.05)

    fig, (a1, a2) = plt.subplots(1, 2, figsize=(10.5, 5.0))
    target = (0.75, 0.5)
    others = [(0.0, 1.0), (-0.6, -0.8), (-0.9, 0.3), (0.3, -0.45)]
    t = np.linspace(0, 1, 9)

    a1.add_patch(plt.Circle((0, 0), 1, fill=False, color=GREY))
    for px, py in others:
        a1.plot(px * t, py * t, color=GREY, lw=0.8, alpha=0.6)
        a1.plot(px, py, "o", color=GREY, ms=4, alpha=0.6)
    a1.plot(target[0] * t, target[1] * t, color=BLUE, lw=1.4)
    a1.plot(target[0] * t[1:-1], target[1] * t[1:-1], ".", color=BLUE, ms=7)
    a1.plot(*target, "o", color=RED, ms=7)
    a1.plot(0, 0, "ks", ms=6)
    a1.annotate("chief ray (0, 0): W = 0", (0, 0), xytext=(-1.0, -0.22), fontsize=9,
                arrowprops=dict(arrowstyle="->", lw=0.7))
    a1.annotate("target point", target, xytext=(0.2, 0.86), fontsize=9, arrowprops=dict(arrowstyle="->", lw=0.7))
    a1.annotate("one ray traced\nat each node", (target[0] * 0.5, target[1] * 0.5), xytext=(0.35, -0.2), fontsize=9,
                arrowprops=dict(arrowstyle="->", lw=0.7))
    a1.text(-1.08, -1.16, "one straight path per target point (857 per field;\na few shown), 256 to 16,384 rays along each", fontsize=8)
    a1.set_aspect("equal")
    a1.set_xlim(-1.15, 1.15)
    a1.set_ylim(-1.22, 1.12)
    a1.set_xlabel("Px")
    a1.set_ylabel("Py")
    a1.set_title("1. Choose the rays: a path in pupil coordinates", fontsize=10)

    rim = np.linspace(0, 2 * np.pi, 200)
    rx, ry = to_wavefront(np.cos(rim), np.sin(rim))
    a2.plot(rx, ry, color=GREY, lw=0.8)
    for px, py in others:
        wx, wy = to_wavefront(px * t, py * t)
        a2.plot(wx, wy, color=GREY, lw=0.8, alpha=0.6)
    s = np.linspace(0, 1, 100)
    wx, wy = to_wavefront(target[0] * s, target[1] * s)
    a2.plot(wx, wy, color=BLUE, lw=1.4)
    nx, ny = to_wavefront(target[0] * t[1:-1], target[1] * t[1:-1])
    a2.plot(nx, ny, ".", color=BLUE, ms=7)
    c = to_wavefront(0.0, 0.0)
    a2.plot(*c, "ks", ms=6)
    a2.plot(*to_wavefront(*target), "o", color=RED, ms=7)
    a2.annotate("C, on the chief ray: W = 0", c, xytext=(-6.4, -2.2), fontsize=9, arrowprops=dict(arrowstyle="->", lw=0.7))
    a2.annotate("each ray's point P on the wavefront,\nwith its own X, Y", (nx[3], ny[3]), xytext=(-1.0, 4.9), fontsize=9,
                arrowprops=dict(arrowstyle="->", lw=0.7))
    a2.annotate("W here = the sum of\n−(X dx + Y dy)/(R − W)\nalong the path", to_wavefront(*target), xytext=(1.6, -4.3),
                fontsize=9, arrowprops=dict(arrowstyle="->", lw=0.7))
    a2.set_aspect("equal")
    a2.set_xlabel("x on the wavefront (mm)")
    a2.set_ylabel("y on the wavefront (mm)")
    a2.set_title("2. Integrate: the same path on the wavefront (schematic)", fontsize=10)
    fig.tight_layout()
    save(fig, "rayces-paths.svg")


def wfe_rayces_csv(lens: str, out: Path):
    wfe = ROOT / "src/WavefrontErrorCalculator.Cli/bin/Debug/net8.0/wfe.dll"
    subprocess.run(["dotnet", "build", str(ROOT / "src/WavefrontErrorCalculator.Cli"), "-c", "Debug", "-v", "q", "-nologo"],
                   check=True, capture_output=True)
    subprocess.run(["dotnet", str(wfe), "rayces", str(ROOT / f"tests/TestData/{lens}.zmx"),
                    "--set", "RayAiming=Paraxial", "--set", "ChiefRay=StopCenter",
                    "--set", "PupilOrientation=EntrancePupilPlane", "--csv", str(out)], check=True, capture_output=True)


def rayces_cooke(tmp: Path):
    path = tmp / "rayces_cooke.csv"
    wfe_rayces_csv("Cooke_40deg_FC", path)
    rows = [r for r in csv.DictReader(path.open()) if r["field"] == "2" and abs(float(r["px"])) < 1e-12]
    rows = sorted(rows, key=lambda r: float(r["py"]))
    py = [float(r["py"]) for r in rows]
    fig, (a1, a2) = plt.subplots(2, 1, figsize=(9, 5.6), sharex=True, gridspec_kw={"height_ratios": [3, 2]})
    a1.plot(py, [float(r["path"]) for r in rows], color=BLUE, lw=2.2, label="by optical path")
    a1.plot(py, [float(r["integrated"]) for r in rows], color=RED, lw=0, marker="o", ms=3, label="by integrating Rayces's relation")
    a1.set_ylabel("W (waves)")
    a1.legend(fontsize=9)
    a1.grid(alpha=0.3)
    a2.plot(py, [float(r["difference"]) for r in rows], color=RED, lw=1.2, marker=".")
    a2.axhline(0, color="black", lw=0.7)
    a2.set_ylabel("integrated − path (waves)")
    a2.set_xlabel("Py (Px = 0)")
    a2.grid(alpha=0.3)
    fig.suptitle("Cooke triplet, 20°, ray aiming off, WEC's sphere", fontsize=11)
    fig.tight_layout()
    save(fig, "rayces-cooke.svg")


def rayces_convergence():
    # Measured with `wfe rayces` on US8264785 at 17.5° (aiming off, WEC's sphere), at a fixed
    # number of steps before adaptive refinement was added, and with it (2026-10-05).
    steps = [256, 1024, 4096]
    worst = [2.598e-4, 1.493e-7, 6.422e-9]
    fig, ax = plt.subplots(figsize=(7, 4.2))
    ax.loglog(steps, worst, "o-", color=BLUE, lw=1.5)
    ax.axhline(6.422e-9, color=GREEN, ls="--", lw=1)
    ax.text(300, 9e-9, "adaptive (up to 16,384 steps): 6.4×10⁻⁹", fontsize=9, color=GREEN)
    for s, v in zip(steps, worst):
        ax.annotate(f"{v:.1e}", (s, v), xytext=(6, 4), textcoords="offset points", fontsize=8)
    ax.set_xticks(steps)
    ax.set_xticklabels([f"{s:,}" for s in steps])
    ax.minorticks_off()
    ax.set_xlabel("integration steps per path")
    ax.set_ylabel("largest |integrated − path| (waves)")
    ax.set_title("US8264785 at 17.5°: convergence near grazing incidence", fontsize=11)
    ax.grid(alpha=0.3, which="both")
    fig.tight_layout()
    save(fig, "rayces-convergence.svg")


# ── Hopkins-Tatian figures ────────────────────────────────────────────────────────────────

def hopkins_geometry():
    """Image space: a ray and the chief ray, their invariant focus M, the image point I, the two
    focal shifts - Hopkins's along the ray between spheres through E′, Tatian's to the feet Q, Q̄
    of the perpendiculars from I."""
    fig, ax = new(11, 6.2)
    I = np.array([10.0, 0.0])                     # image point: where the chief ray lands
    E = np.array([0.0, 0.6])                      # chief ray's point in the exit pupil
    lb = (I - E) / np.linalg.norm(I - E)          # chief ray direction
    M = E + 0.84 * (I - E)                        # on the chief ray, short of I
    P0 = np.array([0.0, 3.0])                     # the ray, through M and on to the image plane
    l = (M - P0) / np.linalg.norm(M - P0)
    T = P0 + ((I[0] - P0[0]) / l[0]) * l          # where the ray meets the image plane

    ax.plot([I[0], I[0]], [-2.6, 3.4], color=GREY, lw=1)
    ax.text(I[0] + 0.1, -2.9, "image plane", fontsize=9, color=GREY)
    ax.plot([-0.4, -0.4], [-2.6, 3.4], color=GREY, lw=1, ls=":")
    ax.text(-0.3, -2.9, "exit pupil", fontsize=9, color=GREY)
    ax.plot(*zip(E - 0.4 * lb, I + 0.8 * lb), color=BLUE, lw=1.8)
    ax.plot(*zip(P0, T + 0.8 * l), color=RED, lw=1.8)
    ax.text(*(E + 4.0 * lb + np.array([0.0, -0.5])), "chief ray", color=BLUE, fontsize=10)
    ax.text(*(P0 + 4.0 * l + np.array([0.0, 0.3])), "ray", color=RED, fontsize=10)

    # Spheres through E: about I (the reference sphere) and about M.
    rI, rM = np.linalg.norm(E - I), np.linalg.norm(E - M)
    aI = math.degrees(math.atan2(E[1] - I[1], E[0] - I[0]))
    aM = math.degrees(math.atan2(E[1] - M[1], E[0] - M[0]))
    arc(ax, I, rI, aI - 22, aI + 22, color=GREEN, lw=1.4)
    arc(ax, M, rM, aM - 30, aM + 24, color=PURPLE, lw=1.4, ls="--")
    BI = line_sphere(P0, l, I, rI, E)
    BM = line_sphere(P0, l, M, rM, E)
    dot(ax, BI, None, color=GREEN, size=4)
    dot(ax, BM, None, color=PURPLE, size=4)
    note(ax, "sphere about I through E′\n(reference sphere)", BI + np.array([-0.05, 0.25]), (0.9, 4.3), GREEN)
    note(ax, "sphere about M\nthrough E′", BM + np.array([0.0, -0.1]), (-1.1, -2.0), PURPLE)

    # Feet of the perpendiculars from I.
    Q = P0 + ((I - P0) @ l) * l
    ax.plot(*zip(I, Q), color=GREY, lw=0.8, ls="--")
    dot(ax, Q, "Q", (-6, -16), color=RED, size=4)

    dot(ax, E, "E′", (-18, -14), color=BLUE)
    dot(ax, M, "M", (-4, 10), color=PURPLE)
    dot(ax, I, "I", (6, 6), color=BLUE)
    note(ax, "M: the invariant focus\n(mid-point of the shortest join;\nhere, in one plane, where they cross)", M, (5.2, -2.7), PURPLE)
    note(ax, "Hopkins 1952: shift between\nthe two spheres, along the ray", 0.5 * (BI + BM), (1.6, -1.2), GREEN)
    note(ax, "Tatian 1972: to the foot Q of the\nperpendicular from I (Q̄ = I itself\non the chief ray); no exit pupil", Q, (10.5, 3.9), RED)
    ax.set_xlim(-1.4, 14.8)
    ax.set_ylim(-3.2, 5.1)
    save(fig, "hopkins-geometry.svg")


def wfe_hopkins_csv(lens: str, out: Path):
    wfe = ROOT / "src/WavefrontErrorCalculator.Cli/bin/Debug/net8.0/wfe.dll"
    subprocess.run(["dotnet", "build", str(ROOT / "src/WavefrontErrorCalculator.Cli"), "-c", "Debug", "-v", "q", "-nologo"],
                   check=True, capture_output=True)
    subprocess.run(["dotnet", str(wfe), "hopkins", str(ROOT / f"tests/TestData/{lens}.zmx"),
                    "--preset", "Zemax", "--csv", str(out)], check=True, capture_output=True)


def hopkins_definitions(tmp: Path):
    path = tmp / "hopkins_dg.csv"
    wfe_hopkins_csv("KingslakeDG", path)
    rows = [r for r in csv.DictReader(path.open()) if r["field"] == "2" and abs(float(r["px"])) < 1e-12
            and r["preset_sphere"] not in ("NaN", "")]
    rows = sorted(rows, key=lambda r: float(r["py"]))
    py = [float(r["py"]) for r in rows]
    ht = [float(r["hopkins_tatian"]) for r in rows]
    sph = [float(r["preset_sphere"]) for r in rows]
    fig, (a1, a2) = plt.subplots(2, 1, figsize=(9, 5.6), sharex=True, gridspec_kw={"height_ratios": [3, 2]})
    a1.plot(py, sph, color=BLUE, lw=2.2, label="on the sphere through the exit pupil (OPDC)")
    a1.plot(py, ht, color=RED, lw=1.4, ls="--", label="Hopkins and Tatian: no exit pupil")
    a1.set_ylabel("W (waves)")
    a1.legend(fontsize=9)
    a1.grid(alpha=0.3)
    a2.plot(py, [s - h for s, h in zip(sph, ht)], color=PURPLE, lw=1.4)
    a2.axhline(0, color="black", lw=0.7)
    a2.set_ylabel("difference (waves)")
    a2.set_xlabel("Py (Px = 0)")
    a2.grid(alpha=0.3)
    fig.suptitle("Kingslake double Gauss, 14°, ray aiming off: the two definitions of W", fontsize=11)
    fig.tight_layout()
    save(fig, "hopkins-definitions.svg")


# ── markdown to print HTML ────────────────────────────────────────────────────────────────

def inline(s: str) -> str:
    s = html.escape(s, quote=False)
    s = re.sub(r"\*\*(.+?)\*\*", r"<b>\1</b>", s)
    s = re.sub(r"(?<![\w*])\*(?!\s)(.+?)(?<!\s)\*(?![\w*])", r"<i>\1</i>", s)
    s = re.sub(r"\[([^\]]+)\]\([^)]+\)", r"\1", s)          # a link: its text (the page has nowhere to go)
    s = re.sub(r"`(.+?)`", r"<code>\1</code>", s)
    return s


def to_html(md: str, title: str) -> str:
    out, para, items, kind = [], [], [], None
    ol_start = 1          # a numbered list split by a code block starts again at its own number

    def flush():
        nonlocal para, items, kind
        if para:
            out.append("<p>" + inline(" ".join(para)) + "</p>")
            para = []
        if items:
            tag = "ol" if kind == "ol" else "ul"
            start = f" start='{ol_start}'" if kind == "ol" else ""
            out.append(f"<{tag}{start}>" + "".join(items) + f"</{tag}>")
            items, kind = [], None

    lines = md.splitlines()
    i = 0
    while i < len(lines):
        l = lines[i].rstrip()
        m_img = re.match(r"^!\[(.*)\]\((.+)\)$", l)
        if l.strip().startswith("```"):
            # A fenced block, kept as written (indented under a list item or not); a numbered list
            # around it carries on from where it was.
            flush()
            indent = len(l) - len(l.lstrip())
            block = []
            i += 1
            while i < len(lines) and not lines[i].strip().startswith("```"):
                block.append(lines[i][indent:] if lines[i][:indent].strip() == "" else lines[i])
                i += 1
            out.append("<pre>" + html.escape("\n".join(block)) + "</pre>")
        elif m_img:
            flush()
            svg = (HERE / m_img.group(2)).read_text(encoding="utf-8")
            svg = svg[svg.index("<svg"):]
            out.append(f"<figure>{svg}<figcaption>{inline(m_img.group(1))}</figcaption></figure>")
        elif l.startswith("#"):
            flush()
            level = len(l) - len(l.lstrip("#"))
            out.append(f"<h{level}>{inline(l[level:].strip())}</h{level}>")
        elif l.startswith(">"):
            flush()
            out.append(f"<blockquote>{inline(l.lstrip('> ').strip())}</blockquote>")
        elif l.startswith("|"):
            flush()
            rows = []
            while i < len(lines) and lines[i].startswith("|"):
                rows.append(lines[i])
                i += 1
            rows = [r for r in rows if not re.match(r"^\|[-:| ]+\|$", r)]
            t = ["<table>"]
            for k, r in enumerate(rows):
                tag = "th" if k == 0 else "td"
                t.append("<tr>" + "".join(f"<{tag}>{inline(c.strip())}</{tag}>" for c in r.strip().strip("|").split("|")) + "</tr>")
            t.append("</table>")
            out.append("\n".join(t))
            continue
        elif re.match(r"^\s*- ", l):
            if para:
                out.append("<p>" + inline(" ".join(para)) + "</p>")
                para = []
            depth = (len(l) - len(l.lstrip())) // 2
            if kind == "ol" and depth > 0 and items:
                # Bullets under a numbered step: a list inside that step, not more steps.
                bullet = f"<li>{inline(l.strip()[2:])}</li>"
                if items[-1].endswith("</ul></li>"):
                    items[-1] = items[-1][:-len("</ul></li>")] + bullet + "</ul></li>"
                else:
                    items[-1] = items[-1][:-len("</li>")] + "<ul>" + bullet + "</ul></li>"
                i += 1
                continue
            if kind not in (None, "ul") and depth == 0:
                flush()
            kind = kind or "ul"
            items.append(f"<li class='d{depth}'>{inline(l.strip()[2:])}</li>")
        elif re.match(r"^\d+\. ", l):
            if para:
                out.append("<p>" + inline(" ".join(para)) + "</p>")
                para = []
            if kind not in (None, "ol"):
                flush()
            number = int(re.match(r"^(\d+)\. ", l).group(1))
            if not items:
                ol_start = number
            kind = "ol"
            items.append(f"<li>{inline(re.sub(r'^\d+\. ', '', l))}</li>")
        elif not l.strip():
            # A blank line inside a list item (before its next paragraph or code) does not end the list.
            if not (items and i + 1 < len(lines) and lines[i + 1].startswith(" ")):
                flush()
        else:
            if items and lines[i].startswith(" "):
                # The item goes on (inside a nested list, its last bullet does).
                if items[-1].endswith("</li></ul></li>"):
                    items[-1] = items[-1][:-len("</li></ul></li>")] + " " + inline(l.strip()) + "</li></ul></li>"
                else:
                    items[-1] = items[-1][:-len("</li>")] + " " + inline(l.strip()) + "</li>"
            else:
                if items:
                    flush()
                para.append(l.strip())
        i += 1
    flush()
    css = """
    body { font-family: 'Segoe UI', Arial, sans-serif; font-size: 10.5pt; color: #111; line-height: 1.4; }
    h1 { font-size: 19pt; margin: 0 0 10px 0; }
    h2 { font-size: 13.5pt; margin: 18px 0 6px 0; border-bottom: 1px solid #999; }
    p { margin: 6px 0; }
    blockquote { margin: 8px 24px; padding: 4px 10px; border-left: 3px solid #1f5fa8; background: #f4f7fb; }
    table { border-collapse: collapse; margin: 8px 0; font-size: 9pt; }
    th, td { border: 1px solid #bbb; padding: 3px 7px; }
    th { background: #eee; }
    code { font-family: Consolas, monospace; font-size: 9pt; }
    pre { font-family: Consolas, monospace; font-size: 7pt; background: #f6f6f6; border: 1px solid #ddd; padding: 6px 8px; white-space: pre-wrap; page-break-inside: avoid; }
    li.d1 { margin-left: 18px; } li.d2 { margin-left: 36px; }
    figure { margin: 10px 0 14px 0; page-break-inside: avoid; text-align: center; }
    figure svg { max-width: 100%; height: auto; }
    figcaption { font-size: 9pt; color: #444; margin-top: 4px; }
    @page { size: A4; margin: 15mm 14mm; }
    """
    return f"<!doctype html><html><head><meta charset='utf-8'><title>{html.escape(title)}</title><style>{css}</style></head><body>{''.join(out)}</body></html>"


def main(out_dir: str) -> None:
    out = Path(out_dir)
    out.mkdir(parents=True, exist_ok=True)
    wec_geometry()
    wec_entrance()
    wec_sampling()
    wec_pipeline()
    wec_spheres()
    wec_cooke_spheres()
    rayces_geometry()
    rayces_two_w()
    rayces_frame()
    rayces_paths()
    rayces_cooke(out)
    rayces_convergence()
    hopkins_geometry()
    hopkins_definitions(out)
    for name, title, source in [("wec-method", "WEC method", HERE / "wec-method.md"),
                                ("rayces-method", "Rayces method", HERE / "rayces-method.md"),
                                ("hopkins-tatian-method", "Hopkins-Tatian method", HERE / "hopkins-tatian-method.md"),
                                ("user-guide", "WEC user guide", HERE.parent / "user-guide.md")]:
        page = to_html(source.read_text(encoding="utf-8"), title)
        (out / f"{name}.html").write_text(page, encoding="utf-8")
        print(f"wrote {out / (name + '.html')}")


if __name__ == "__main__":
    main(sys.argv[1])
