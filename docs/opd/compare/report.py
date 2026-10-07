"""A PDF-ready HTML report of the point-by-point OPD comparison in this folder.

    uv run --project <any env with matplotlib> python docs/opd/compare/report.py <out.html>

Reads the per-lens tables generate.sh writes (wfe opd-table --values) and, for every lens, ray
aiming and field, plots each program's OPD along the tangential line (Px = 0) and the sagittal line
(Py = 0), with each program's difference from WEC beneath, and tabulates the values at nine
tangential and four sagittal points. Print the HTML to PDF with a browser (report.ps1 does it
with Edge). The full 857-point tables are in the markdown files.
"""

from __future__ import annotations

import html
import io
import re
import sys
from pathlib import Path

import matplotlib

matplotlib.use("Agg")
import matplotlib.pyplot as plt

HERE = Path(__file__).resolve().parent

LENSES = [
    ("KingslakeDG", "Kingslake double Gauss", "infinite", "°"),
    ("Cooke_40deg_FC", "Cooke triplet, 40°", "infinite", "°"),
    ("US8264785_Ex4", "US8264785 example 4", "infinite", "°"),
    ("Relay_1to1", "1:1 relay, NA 0.1", "finite", " mm object height"),
    ("Objective_NA03_5x", "5× objective, NA 0.3 (entrance pupil behind the object)", "finite", " mm object height"),
]
STYLE = {
    "WEC": dict(color="black", lw=2.2, ls="-", marker=None),
    "Zemax": dict(color="#1f77b4", lw=1.6, ls="--", marker="o", ms=3),
    "LensHH-LT": dict(color="#d62728", lw=1.2, ls=":", marker="x", ms=4),
    "Optiland": dict(color="#2ca02c", lw=1.2, ls="-.", marker="s", ms=3),
    "OSLO enp": dict(color="#9467bd", lw=1.2, ls="-", marker="^", ms=3),
    "OSLO crr": dict(color="#8c564b", lw=1.2, ls="-", marker="v", ms=3),
}
TAN_POINTS = [-1.0, -0.75, -0.5, -0.25, 0.0, 0.25, 0.5, 0.75, 1.0]
SAG_POINTS = [0.25, 0.5, 0.75, 1.0]


def read_table(path: Path):
    """{field: (columns, [(px, py, {column: value or None})])} and the header notes."""
    text = path.read_text(encoding="utf-8")
    notes = [l[2:] for l in text.splitlines() if l.startswith("- **")]
    fields = {}
    for block in re.split(r"^## Field ", text, flags=re.M)[1:]:
        lines = block.splitlines()
        field = lines[0].strip()
        rows = [l for l in lines if l.startswith("|")]
        cols = [c.strip() for c in rows[0].strip("|").split("|")]
        data = []
        for r in rows[2:]:
            cells = [c.strip() for c in r.strip("|").split("|")]
            px, py = float(cells[0]), float(cells[1])
            vals = {}
            for name, c in zip(cols[2:], cells[2:]):
                vals[name] = None if c in ("–", "") else float(c)
            data.append((px, py, vals))
        fields[field] = (cols[2:], data)
    return notes, fields


def line(data, axis):
    """The points on the tangential (Px = 0) or sagittal (Py = 0, Px >= 0) line, sorted."""
    if axis == "tan":
        pts = [(py, v) for px, py, v in data if abs(px) < 1e-9]
    else:
        pts = [(px, v) for px, py, v in data if abs(py) < 1e-9 and px >= -1e-9]
    return sorted(pts, key=lambda t: t[0])


def plot(cols, data, title):
    fig, axes = plt.subplots(2, 2, figsize=(10, 5.6), sharex="col",
                             gridspec_kw={"height_ratios": [3, 2], "width_ratios": [2, 1]})
    for c, (axis, label) in enumerate([("tan", "Py (Px = 0)"), ("sag", "Px (Py = 0)")]):
        pts = line(data, axis)
        x = [p for p, _ in pts]
        for name in cols:
            y = [v[name] for _, v in pts]
            if all(v is None for v in y):
                continue
            st = STYLE.get(name, {})
            axes[0][c].plot(x, [float("nan") if v is None else v for v in y], label=name, **st)
            if name != "WEC":
                d = [float("nan") if (v[name] is None or v["WEC"] is None) else v[name] - v["WEC"] for _, v in pts]
                axes[1][c].plot(x, d, label=name, **st)
        axes[0][c].set_title("tangential" if axis == "tan" else "sagittal", fontsize=10)
        axes[1][c].set_xlabel(label)
        axes[0][c].grid(alpha=0.3)
        axes[1][c].grid(alpha=0.3)
        axes[1][c].axhline(0, color="black", lw=0.8)
    axes[0][0].set_ylabel("OPD (waves)")
    axes[1][0].set_ylabel("program − WEC (waves)")
    axes[0][1].legend(fontsize=8, loc="best")
    fig.suptitle(title, fontsize=11)
    fig.tight_layout()
    buf = io.StringIO()
    fig.savefig(buf, format="svg")
    plt.close(fig)
    svg = buf.getvalue()
    return svg[svg.index("<svg"):]


def value_table(cols, data):
    def find(px, py):
        for x, y, v in data:
            if abs(x - px) < 1e-6 and abs(y - py) < 1e-6:
                return v
        return None

    rows = [("tangential", 0.0, py) for py in TAN_POINTS] + [("sagittal", px, 0.0) for px in SAG_POINTS]
    out = ["<table><tr><th>Line</th><th>Px</th><th>Py</th>" + "".join(f"<th>{html.escape(c)}</th>" for c in cols) + "</tr>"]
    for lab, px, py in rows:
        v = find(px, py)
        if v is None:
            continue
        # A value that rounds to zero prints as 0, not -0.000000.
        fmt = lambda x: '–' if x is None else f'{(0.0 if abs(x) < 5e-7 else x):.6f}'
        cells = "".join(f"<td>{fmt(v[c])}</td>" for c in cols)
        out.append(f"<tr><td>{lab}</td><td>{px:g}</td><td>{py:g}</td>{cells}</tr>")
    out.append("</table>")
    return "\n".join(out)


def readme_section(title: str) -> str:
    """A section of README.md as simple HTML (paragraphs, bullets, tables)."""
    text = (HERE / "README.md").read_text(encoding="utf-8")
    m = re.search(rf"^## {re.escape(title)}\n(.*?)(?=^## |\Z)", text, flags=re.M | re.S)
    return md_to_html(m.group(1)) if m else ""


def inline(s: str) -> str:
    s = html.escape(s)
    s = re.sub(r"\*\*(.+?)\*\*", r"<b>\1</b>", s)
    s = re.sub(r"`(.+?)`", r"<code>\1</code>", s)
    s = re.sub(r"\[(.+?)\]\((.+?)\)", r"\1", s)
    return s


def md_to_html(md: str) -> str:
    out, table, items = [], [], []

    def flush():
        nonlocal table, items
        if table:
            rows = [r for r in table if not re.match(r"^\|[-:| ]+\|$", r)]
            h = ["<table>"]
            for i, r in enumerate(rows):
                cells = [inline(c.strip()) for c in r.strip().strip("|").split("|")]
                tag = "th" if i == 0 else "td"
                h.append("<tr>" + "".join(f"<{tag}>{c}</{tag}>" for c in cells) + "</tr>")
            h.append("</table>")
            out.append("\n".join(h))
            table = []
        if items:
            out.append("<ul>" + "".join(items) + "</ul>")
            items = []

    for raw in md.splitlines():
        l = raw.rstrip()
        if l.startswith("|"):
            if items:
                flush()
            table.append(l)
            continue
        if table:
            flush()
        if re.match(r"^\s*- ", l):
            depth = (len(l) - len(l.lstrip())) // 2
            items.append(f"<li class='d{depth}'>{inline(l.strip()[2:])}</li>")
        elif l.strip():
            flush()
            out.append(f"<p>{inline(l)}</p>")
        else:
            flush()
    flush()
    return "\n".join(out)


def main(out: str) -> None:
    parts = []
    parts.append("<h1>OPD with respect to the chief ray: five programs, point by point</h1>")
    parts.append("<p class='sub'>WavefrontErrorCalculator (WEC), Zemax OpticStudio, LensHH-LT 1.0.161, Optiland and OSLO, "
                 "on the same refractive indices.</p>")
    coverage = ["<table><tr><th>Lens</th><th>Conjugate</th><th>Fields</th><th>Aiming off</th><th>Aiming real</th></tr>"]
    for key, name, conj, unit in LENSES:
        _, f = read_table(HERE / f"{key}_AimingOff.md")
        coverage.append(f"<tr><td>{html.escape(name)}</td><td>{conj}</td><td>{', '.join(f.keys())}{html.escape(unit)}</td>"
                        "<td>WEC, Zemax, LensHH-LT, Optiland, OSLO enp, OSLO crr</td><td>WEC, Zemax, LensHH-LT</td></tr>")
    coverage.append("</table>")
    parts.append("<h2>Coverage</h2>" + "\n".join(coverage))
    parts.append("<p>Every program is evaluated at the same 857 pupil points per field (a grid of spacing 1/16 and 60 points on the rim), "
                 "at the primary wavelength. The first field of each lens is on axis. This report shows the tangential and sagittal "
                 "lines; the full tables are in docs/opd/compare/*.md.</p>")
    intro = (HERE / "README.md").read_text(encoding="utf-8").split("## ")[0]
    prog = re.search(r"(\| Column \|.*?)(?=\n\n)", intro, flags=re.S)
    if prog:
        parts.append("<h2>Programs</h2>" + md_to_html(prog.group(1)))
    glass = re.search(r"(\*\*WEC's W\*\*.*?)(?=\nGenerated by)", intro, flags=re.S)
    if glass:
        parts.append("<h2>Definitions and glass</h2>" + md_to_html(glass.group(1)))
    parts.append("<h2>LensHH-LT and Zemax, ray by ray</h2>" + readme_section("LensHH-LT and Zemax, ray by ray"))
    parts.append("<h2>The WEC column, checked by integration</h2>" + readme_section("The WEC column, checked by integration"))
    parts.append("<h2>Why the columns differ</h2>" + readme_section("Why the columns differ"))
    parts.append("<p>The reference sphere is the whole of the WEC–Zemax difference at the primary wavelength: with WEC's "
                 "<code>ExitPupil</code> set to <code>ParaxialChiefIntersect</code> (Zemax's sphere) and the primary wavelength's "
                 "sphere for every wavelength, WEC equals Zemax and LensHH-LT to about 10⁻⁸ wave.</p>")

    for key, name, conj, unit in LENSES:
        parts.append(f"<h2 class='lens'>{html.escape(name)} <span class='sub'>({conj} conjugate)</span></h2>")
        for aim, label in [("Off", "ray aiming Off (unaimed)"), ("Real", "ray aiming Real")]:
            notes, fields = read_table(HERE / f"{key}_Aiming{aim}.md")
            parts.append(f"<h3>{html.escape(label)}</h3>")
            parts.append("<ul class='notes'>" + "".join(f"<li>{inline(n)}</li>" for n in notes) + "</ul>")
            for field, (cols, data) in fields.items():
                where = "on axis" if float(field) == 0 else "off axis"
                title = f"{name}: field {field}{unit} ({where}), {label}"
                parts.append(f"<div class='block'><h4>Field {html.escape(field)}{html.escape(unit)}, {where}</h4>")
                parts.append(plot(cols, data, title))
                parts.append(value_table(cols, data))
                parts.append("</div>")

    css = """
    body { font-family: 'Segoe UI', Arial, sans-serif; font-size: 10.5pt; color: #111; margin: 0; }
    h1 { font-size: 18pt; margin: 0 0 4px 0; }
    h2 { font-size: 14pt; margin: 18px 0 6px 0; border-bottom: 1px solid #999; }
    h2.lens { page-break-before: always; }
    h3 { font-size: 12pt; margin: 12px 0 4px 0; }
    h4 { font-size: 11pt; margin: 8px 0 2px 0; }
    .sub { color: #555; font-weight: normal; font-size: 10pt; }
    table { border-collapse: collapse; margin: 6px 0 10px 0; font-size: 8.5pt; }
    th, td { border: 1px solid #bbb; padding: 2px 6px; text-align: right; }
    th { background: #eee; }
    td:first-child, th:first-child { text-align: left; }
    code { font-family: Consolas, monospace; font-size: 9pt; }
    ul { margin: 4px 0; } li.d1 { margin-left: 18px; } li.d2 { margin-left: 36px; }
    ul.notes { font-size: 8.5pt; color: #333; }
    .block { page-break-inside: avoid; }
    svg { width: 100%; height: auto; }
    @page { size: A4; margin: 14mm 12mm; }
    """
    doc = f"<!doctype html><html><head><meta charset='utf-8'><title>OPD comparison</title><style>{css}</style></head><body>{''.join(parts)}</body></html>"
    Path(out).write_text(doc, encoding="utf-8")
    print(f"wrote {out}")


if __name__ == "__main__":
    main(sys.argv[1])
