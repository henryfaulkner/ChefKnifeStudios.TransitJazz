// Find who retains the big JSArrayBufferData nodes (the 133 MB bucket).
// We walk edges backward: for each ArrayBuffer-ish node, record its incoming edges'
// source node names, and also bucket the buffers by self_size to see the size profile.
import { readFileSync } from 'node:fs';

const file = process.argv[2];
const snap = JSON.parse(readFileSync(file, 'utf8'));
const meta = snap.snapshot.meta;
const NF = meta.node_fields, EF = meta.edge_fields;
const nodeTypeEnum = meta.node_types[0];
const edgeTypeEnum = meta.edge_types[0];
const F = NF.length, EW = EF.length;
const nodes = snap.nodes, edges = snap.edges, strings = snap.strings;

const N_TYPE = NF.indexOf('type'), N_NAME = NF.indexOf('name'),
      N_SELF = NF.indexOf('self_size'), N_EDGES = NF.indexOf('edge_count');
const E_TYPE = EF.indexOf('type'), E_NAME = EF.indexOf('name_or_index'), E_TO = EF.indexOf('to_node');

const nodeCount = nodes.length / F;
const mb = (b) => (b / 1048576).toFixed(2) + ' MB';

// Precompute, per node, the starting edge offset (edges are stored in node order).
const firstEdge = new Int32Array(nodeCount);
let eCursor = 0;
for (let n = 0; n < nodeCount; n++) {
  firstEdge[n] = eCursor;
  eCursor += nodes[n * F + N_EDGES];
}

// node-byte-offset (to_node value) -> node index
const offsetToIndex = new Map();
for (let n = 0; n < nodeCount; n++) offsetToIndex.set(n * F, n);

const nodeName = (n) => strings[nodes[n * F + N_NAME]] ?? '';
const nodeType = (n) => nodeTypeEnum[nodes[n * F + N_TYPE]];
const nodeSelf = (n) => nodes[n * F + N_SELF];

// 1) Identify target buffer nodes and bucket by size.
const targets = new Set();
const sizeBuckets = new Map(); // rounded-KB -> {count,size}
let bufTotal = 0, bufCount = 0;
for (let n = 0; n < nodeCount; n++) {
  const name = nodeName(n);
  if (name === 'system / JSArrayBufferData' || name === 'ArrayBuffer') {
    targets.add(n);
    const s = nodeSelf(n);
    bufTotal += s; bufCount++;
    const kb = s < 1024 ? '<1KB' : s < 16384 ? '1-16KB' : s < 65536 ? '16-64KB' : s < 262144 ? '64-256KB' : s < 1048576 ? '256KB-1MB' : '>1MB';
    const b = sizeBuckets.get(kb) || { count: 0, size: 0 };
    b.count++; b.size += s; sizeBuckets.set(kb, b);
  }
}
console.log(`\nArrayBuffer/JSArrayBufferData nodes: ${bufCount}, total ${mb(bufTotal)}`);
console.log('\nsize profile:');
for (const [k, v] of [...sizeBuckets.entries()].sort((a, b) => b[1].size - a[1].size))
  console.log(`  ${mb(v.size).padStart(12)}  ${String(v.count).padStart(6)}x  ${k}`);

// 2) Walk ALL edges; whenever an edge points at a target, credit the SOURCE node name.
const retainerByName = new Map(); // source node name -> {count,size(of pointed buffers)}
for (let src = 0; src < nodeCount; src++) {
  const ec = nodes[src * F + N_EDGES];
  let off = firstEdge[src] * EW;
  for (let e = 0; e < ec; e++, off += EW) {
    const toOffset = edges[off + E_TO];
    const ti = offsetToIndex.get(toOffset);
    if (ti !== undefined && targets.has(ti)) {
      const sname = nodeType(src) + ': ' + nodeName(src);
      const r = retainerByName.get(sname) || { count: 0, size: 0 };
      r.count++; r.size += nodeSelf(ti);
      retainerByName.set(sname, r);
    }
  }
}

console.log('\n=== retainers of those buffers (by direct incoming edge source) ===');
[...retainerByName.entries()]
  .sort((a, b) => b[1].size - a[1].size)
  .slice(0, 30)
  .forEach(([name, r]) => console.log(`${mb(r.size).padStart(12)}  ${String(r.count).padStart(6)}x  ${name.slice(0, 70)}`));
