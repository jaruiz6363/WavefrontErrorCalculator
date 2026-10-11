// The afocal test lenses of docs/verification.md, as OpticStudio .zmx (UTF-16LE, CRLF), with
// OpticStudio's "Afocal Image Space" set. Run from the repository root:
//   node tests/TestData/make_afocal_lenses.js tests/TestData
// Each telescope's air gap is solved paraxially (d-line indices) so that a ray from an object at
// infinity leaves parallel; the Keplerian's image surface is at its paraxial exit pupil.
const fs = require('fs');
const path = require('path');
const out = process.argv[2] || __dirname;

const nd = { 'N-BK7': 1.5168, 'N-SF5': 1.67271 };

// Paraxial y-nu trace; surfaces { c, t, glass }; returns per-surface (y, nu) after refraction.
function trace(surfaces, y, nu) {
  let n = 1.0;
  const rays = [];
  for (const s of surfaces) {
    const n2 = s.glass ? nd[s.glass] : 1.0;
    nu = nu - y * (s.c || 0) * (n2 - n);
    n = n2;
    rays.push({ y, nu, n });
    y = y + (s.t || 0) * nu / n;
  }
  return rays;
}

// The gap after surface `gap` that makes the marginal ray from infinity leave parallel (bisection).
function solveGap(surfaces, gap) {
  const exitSlope = (t) => { const s = surfaces.map((x, i) => i === gap ? { ...x, t } : x); return trace(s, 1, 0).at(-1).nu; };
  let lo = 1, hi = 1000;
  const sLo = Math.sign(exitSlope(lo));
  for (let k = 0; k < 200; k++) {
    const mid = 0.5 * (lo + hi);
    if (Math.sign(exitSlope(mid)) === sLo) lo = mid; else hi = mid;
  }
  return 0.5 * (lo + hi);
}

// The paraxial exit pupil: where the chief ray, from the centre of the stop (surface 1), crosses
// the axis after the last surface, measured from it.
function exitPupil(surfaces) {
  const r = trace(surfaces.slice(1), 0, 0.01);    // from the stop centre, angle 0.01
  const last = r.at(-1);
  return -last.y / last.nu;
}

function zmx(name, epd, fields, surfaces) {
  const n = fields.length;
  const L = [];
  L.push('VERS 221026 6 20120530 20120530', 'MODE SEQ', `NAME ${name}`, 'UNIT MM X W X CM MR CPMM',
         `ENPD ${epd}`, 'GCAT SCHOTT', 'RAIM 0 0 1 1 0 0 0 0 0 1', `FTYP 0 0 ${n} 3 0 0 1 ${n}`,
         'ROPD 2', 'XFLN ' + fields.map(() => 0).join(' '), 'YFLN ' + fields.join(' '),
         'FWGN ' + fields.map(() => 1).join(' '),
         'VDXN ' + fields.map(() => 0).join(' '), 'VDYN ' + fields.map(() => 0).join(' '),
         'VCXN ' + fields.map(() => 0).join(' '), 'VCYN ' + fields.map(() => 0).join(' '),
         'VANN ' + fields.map(() => 0).join(' '),
         'WAVM 1 0.48613270000000002 1', 'WAVM 2 0.58756180000000004 1', 'WAVM 3 0.65627250000000004 1', 'PWAV 2');
  surfaces.forEach((s, i) => {
    L.push(`SURF ${i}`, '  TYPE STANDARD', `  CURV ${s.c ?? 0} 0 0 0 0 ""`);
    if (s.stop) L.push('  STOP');
    L.push(`  DISZ ${s.t}`);
    if (s.glass) L.push(`  GLAS ${s.glass} 0 0 0 0 0 0 0 0 0 0 `);
    L.push(`  DIAM ${s.d ?? 0} 0 0 0 1 ""`);
  });
  const text = L.join('\r\n') + '\r\n';
  const body = Buffer.from(text, 'utf16le');
  fs.writeFileSync(path.join(out, name + '.zmx'), Buffer.concat([Buffer.from([0xff, 0xfe]), body]));
}

function telescope(name, epd, fields, objective, eyepiece, eye = null) {
  // Object, objective (stop on its first surface), the gap, eyepiece, the eye's distance, image.
  const lens = [{ t: 'INFINITY' }, ...objective, ...eyepiece, { t: 0 }];
  lens[1].stop = true;
  const gap = objective.length;                      // the objective's last surface
  lens[gap].t = solveGap(lens.slice(1, -1).map((s) => ({ ...s, t: s.t === 'INFINITY' ? 0 : s.t })), gap - 1);
  const optics = lens.slice(1, -1);
  const relief = eye ?? exitPupil(optics);
  optics.at(-1).t = relief;
  lens[lens.length - 2].t = relief;
  zmx(name, epd, fields, lens);
  console.log(`wrote ${name}.zmx: gap ${lens[gap].t.toFixed(6)} mm, image surface ${relief.toFixed(6)} mm after the eyepiece, exit pupil ${exitPupil(optics).toFixed(6)}`);
}

// A cemented achromat, N-BK7 then N-SF5: f = 150 mm (thin-lens design).
const achromat150 = () => [
  { c: 1 / 77.1, t: 6, glass: 'N-BK7' },
  { c: -1 / 77.1, t: 3, glass: 'N-SF5' },
  { c: -1 / 338.0, t: 0 },
];
// The same achromat for f = 100 mm.
const achromat100 = () => [
  { c: 1 / 51.4, t: 5, glass: 'N-BK7' },
  { c: -1 / 51.4, t: 2.5, glass: 'N-SF5' },
  { c: -1 / 225.3, t: 0 },
];

// Keplerian, 5x: the f = 150 objective and an f = 30 achromat eyepiece, flint towards the
// intermediate image; real exit pupil behind the eyepiece.
telescope('Keplerian_5x', 30, [0, 1, 1.5], achromat150(), [
  { c: 1 / 67.5, t: 2.5, glass: 'N-SF5' },
  { c: 1 / 15.42, t: 6, glass: 'N-BK7' },
  { c: -1 / 15.42, t: 0 },
]);

// Galilean, 4x: the f = 100 objective and an f = -25 N-BK7 eye lens; virtual exit pupil, so the
// image surface is put where an eye would be, 12 mm behind the eye lens, not at the pupil.
telescope('Galilean_4x', 25, [0, 1, 1.5], achromat100(), [
  { c: -1 / 25.84, t: 2, glass: 'N-BK7' },
  { c: 1 / 25.84, t: 0 },
], 12);
