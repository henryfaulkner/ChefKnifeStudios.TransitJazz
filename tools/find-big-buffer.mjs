// Identify the single ~80MB ArrayBuffer and what points at it (1 hop up).
import { readFileSync } from 'node:fs';
const snap = JSON.parse(readFileSync(process.argv[2], 'utf8'));
const meta = snap.snapshot.meta;
const NF = meta.node_fields, EF = meta.edge_fields;
const nodeTypeEnum = meta.node_types[0], edgeTypeEnum = meta.edge_types[0];
const F = NF.length, EW = EF.length;
const nodes = snap.nodes, edges = snap.edges, strings = snap.strings;
const N_TYPE = NF.indexOf('type'), N_NAME = NF.indexOf('name'), N_SELF = NF.indexOf('self_size'), N_EDGES = NF.indexOf('edge_count');
const E_TYPE = EF.indexOf('type'), E_NAME = EF.indexOf('name_or_index'), E_TO = EF.indexOf('to_node');
const nodeCount = nodes.length / F;
const mb = (b) => (b / 1048576).toFixed(2) + ' MB';
const nm = (n) => strings[nodes[n * F + N_NAME]] ?? '';
const ty = (n) => nodeTypeEnum[nodes[n * F + N_TYPE]];
const self = (n) => nodes[n * F + N_SELF];

const firstEdge = new Int32Array(nodeCount);
let c = 0; for (let n = 0; n < nodeCount; n++) { firstEdge[n] = c; c += nodes[n * F + N_EDGES]; }
const offToIdx = new Map(); for (let n = 0; n < nodeCount; n++) offToIdx.set(n * F, n);

// biggest self_size nodes overall
const all = [];
for (let n = 0; n < nodeCount; n++) all.push(n);
all.sort((a, b) => self(b) - self(a));
console.log('=== top 15 nodes by self_size ===');
for (let i = 0; i < 15; i++) { const n = all[i]; console.log(`${mb(self(n)).padStart(12)}  ${ty(n).padEnd(10)} ${nm(n).slice(0,50)}  id=${nodes[n*F+2]}`); }

// for the biggest, list incoming edges (who retains it) and its outgoing edge names
const big = all[0];
console.log(`\n=== incoming edges to biggest node (${mb(self(big))}) ===`);
let found = 0;
for (let src = 0; src < nodeCount && found < 25; src++) {
  const ec = nodes[src * F + N_EDGES]; let off = firstEdge[src] * EW;
  for (let e = 0; e < ec; e++, off += EW) {
    if (offToIdx.get(edges[off + E_TO]) === big) {
      const etype = edgeTypeEnum[edges[off + E_TYPE]];
      const ename = (etype === 'element' || etype === 'hidden') ? '[' + edges[off + E_NAME] + ']' : (strings[edges[off + E_NAME]] ?? edges[off + E_NAME]);
      console.log(`  <- ${ty(src).padEnd(10)} ${nm(src).slice(0,40).padEnd(40)} via ${etype}:${ename}`);
      found++;
    }
  }
}
