// Ad-hoc Chrome .heapsnapshot analyzer for the RAM investigation.
// Usage: node --max-old-space-size=4096 analyze-heapsnapshot.mjs <file>
// See docs/BROWSER_MEMORY_INVESTIGATION_DESIGN_DOCUMENT.md.
import { readFileSync } from 'node:fs';

const file = process.argv[2];
if (!file) { console.error('pass a .heapsnapshot path'); process.exit(1); }

console.error(`reading ${file} ...`);
const snap = JSON.parse(readFileSync(file, 'utf8'));

const meta = snap.snapshot.meta;
const nodeFields = meta.node_fields;          // [type,name,id,self_size,edge_count,detachedness]
const nodeTypeEnum = meta.node_types[0];      // ["hidden","array","string",...]
const F = nodeFields.length;                  // stride
const nodes = snap.nodes;
const strings = snap.strings;

const TYPE = nodeFields.indexOf('type');
const NAME = nodeFields.indexOf('name');
const SELF = nodeFields.indexOf('self_size');

const nodeCount = nodes.length / F;
console.error(`nodes=${nodeCount.toLocaleString()} strings=${strings.length.toLocaleString()}`);

const mb = (b) => (b / 1048576).toFixed(2) + ' MB';

// --- 1) total self_size, and by node type ---
let total = 0;
const byType = new Map();
// --- 2) by constructor/name (the useful one: groups objects/strings/arrays by name) ---
const byName = new Map();      // name -> { size, count }

for (let i = 0; i < nodes.length; i += F) {
  const t = nodeTypeEnum[nodes[i + TYPE]];
  const self = nodes[i + SELF];
  total += self;
  byType.set(t, (byType.get(t) || 0) + self);

  const name = strings[nodes[i + NAME]] ?? '';
  // Bucket key: type + name keeps "object: MyClass" distinct from a same-named string.
  const key = t + '\t' + name;
  const e = byName.get(key);
  if (e) { e.size += self; e.count++; } else { byName.set(key, { size: self, count: 1 }); }
}

console.log('\n=== TOTAL self_size ===');
console.log(mb(total), `(this is the sum of node self sizes; retained-size totals differ)`);

console.log('\n=== by node type ===');
[...byType.entries()].sort((a, b) => b[1] - a[1]).forEach(([t, s]) =>
  console.log(`${mb(s).padStart(12)}  ${(100 * s / total).toFixed(1).padStart(5)}%  ${t}`));

console.log('\n=== top 40 by (type, name) ===');
[...byName.entries()]
  .sort((a, b) => b[1].size - a[1].size)
  .slice(0, 40)
  .forEach(([key, e]) => {
    const [t, name] = key.split('\t');
    const label = name === '' ? '(blank)' : name;
    console.log(`${mb(e.size).padStart(12)}  ${String(e.count).padStart(8)}x  ${t.padEnd(12)} ${label.slice(0, 60)}`);
  });

// --- 3) Targeted: anything that looks like ours / MapLibre / route data ---
console.log('\n=== suspects (substring match on name) ===');
const suspectRe = /(ChefMap|Animator|route|Route|vehicle|Vehicle|tile|Tile|geojson|GeoJSON|maplibre|MapLibre|Feature|coordinates|cumDist|Float64|Float32|Tone|Sampler|AudioBuffer|WebGL|Texture|Buffer)/;
const suspectAgg = new Map();
for (let i = 0; i < nodes.length; i += F) {
  const name = strings[nodes[i + NAME]] ?? '';
  if (!suspectRe.test(name)) continue;
  const t = nodeTypeEnum[nodes[i + TYPE]];
  const key = t + '\t' + name;
  const e = suspectAgg.get(key);
  const self = nodes[i + SELF];
  if (e) { e.size += self; e.count++; } else { suspectAgg.set(key, { size: self, count: 1 }); }
}
[...suspectAgg.entries()]
  .sort((a, b) => b[1].size - a[1].size)
  .slice(0, 40)
  .forEach(([key, e]) => {
    const [t, name] = key.split('\t');
    console.log(`${mb(e.size).padStart(12)}  ${String(e.count).padStart(8)}x  ${t.padEnd(12)} ${name.slice(0, 60)}`);
  });
