// The stress lenses of docs/verification.md ("Extreme cases"), as OpticStudio .zmx (UTF-16LE, CRLF).
// Run from the repository root: node tests/TestData/make_stress_lenses.js tests/TestData
const fs = require('fs');
const path = require('path');
const out = process.argv[2] || __dirname;

function zmx(name, epd, fields, surfaces) {
  const L = [];
  L.push('VERS 221026 6 20120530 20120530', 'MODE SEQ', `NAME ${name}`, 'UNIT MM X W X CM MR CPMM',
         `ENPD ${epd}`, 'GCAT SCHOTT', 'RAIM 0 0 1 1 0 0 0 0 0 1', 'FTYP 0 0 ' + fields.length + ' 1 0 0 0 ' + fields.length,
         'ROPD 2', 'XFLN ' + fields.map(() => 0).join(' '), 'YFLN ' + fields.join(' '),
         'FWGN ' + fields.map(() => 1).join(' '),
         'VDXN ' + fields.map(() => 0).join(' '), 'VDYN ' + fields.map(() => 0).join(' '),
         'VCXN ' + fields.map(() => 0).join(' '), 'VCYN ' + fields.map(() => 0).join(' '),
         'VANN ' + fields.map(() => 0).join(' '),
         'WAVM 1 0.58756180000000004 1', 'PWAV 1');
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
  console.log('wrote', name + '.zmx');
}

// f = 50.08, back focal length 46.67 for N-BK7 (n = 1.5168), R = 50, -50, t = 10.
const bfl = 46.67;
const singlet = (back) => [
  { t: 'INFINITY' },
  { c: 1 / 50, t: 10, glass: 'N-BK7', stop: true, d: 22 },
  { c: -1 / 50, t: back, d: 22 },
  { t: 0 },
];
zmx('FastSinglet', 40, [0, 5, 10], singlet(bfl));
zmx('FastSinglet_Defocused', 40, [0, 5, 10], singlet(bfl + 1.0));
zmx('ShortPupilSinglet', 10, [0, 3], [
  { t: 'INFINITY' },
  { c: 1 / 50, t: 10, glass: 'N-BK7', d: 22 },
  { c: -1 / 50, t: 40, d: 22 },
  { t: bfl - 40, stop: true, d: 2 },
  { t: 0 },
]);
