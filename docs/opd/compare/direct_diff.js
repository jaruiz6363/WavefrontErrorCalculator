// Largest |LensHH-LT - OpticStudio| OPDC per lens, aiming and field, over every pupil point and wavelength
// both programs trace (the same ray in each, by name). Usage:
//   node docs/opd/compare/direct_diff.js tests/TestData [lenshh dir] [file suffix] [lens,lens...]
// A suffix picks an alternative gather, e.g. _Schott: the double Gauss with LensHH-LT given only
// the Schott catalog, so its F4 is OpticStudio's.
const fs = require('fs'), path = require('path');
const dir = process.argv[2], lhDir = process.argv[3] || 'lenshh-lt-opdc';
const suffix = process.argv[4] || '';
const lenses = process.argv[5] ? process.argv[5].split(',') : ['KingslakeDG', 'Cooke_40deg_FC', 'US8264785_Ex4', 'Relay_1to1', 'Objective_NA03_5x'];
function load(f) { return JSON.parse(fs.readFileSync(path.join(dir, f), 'utf8')); }
function pts(field) {
  const out = new Map();
  for (const part of [field.map, field.fans && field.fans.rim]) {
    if (!part) continue;
    for (let i = 0; i < part.px.length; i++) {
      if (part.vignetted && part.vignetted[i]) continue;
      const v = part.opd[i];
      if (v === null || v === undefined || !isFinite(v)) continue;
      out.set(part.px[i].toFixed(6) + ',' + part.py[i].toFixed(6), v);
    }
  }
  return out;
}
console.log('| Lens | Aiming | Field | points | largest abs(LensHH-LT - OpticStudio) | at (px, py, wave) |');
console.log('|---|---|---|---|---|---|');
for (const lens of lenses) for (const aim of ['Off', 'Real']) {
  const L = load(`${lhDir}/${lens}_OPDC_${aim}${suffix}.json`), Z = load(`zemax-opdc/${lens}_OPDC_${aim}.json`);
  const nf = Z.wavelengths[0].fields.length;
  for (let f = 0; f < nf; f++) {
    let worst = 0, at = '', n = 0;
    for (let w = 0; w < Z.wavelengths.length; w++) {
      const lw = L.wavelengths.find(x => Math.abs(x.wavelength_um - Z.wavelengths[w].wavelength_um) < 1e-6);
      if (!lw) continue;
      const lp = pts(lw.fields[f]), zp = pts(Z.wavelengths[w].fields[f]);
      for (const [k, zv] of zp) {
        if (!lp.has(k)) continue;
        n++;
        const d = Math.abs(lp.get(k) - zv);
        if (d > worst) { worst = d; at = `${k}, ${Z.wavelengths[w].wavelength_um}`; }
      }
    }
    console.log(`| ${lens} | ${aim} | ${Z.wavelengths[0].fields[f].value} | ${n} | ${worst.toExponential(1)} | ${at} |`);
  }
}
