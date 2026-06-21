// Trace retainers upward from the biggest JSArrayBufferData by a few hops.
import { readFileSync } from 'node:fs';
const snap = JSON.parse(readFileSync(process.argv[2], 'utf8'));
const meta = snap.snapshot.meta;
const NF = meta.node_fields, EF = meta.edge_fields;
const nodeTypeEnum = meta.node_types[0], edgeTypeEnum = meta.edge_types[0];
const F = NF.length, EW = EF.length;
const nodes = snap.nodes, edges = snap.edges, strings = snap.strings;
const N_NAME = NF.indexOf('name'), N_SELF = NF.indexOf('self_size'), N_EDGES = NF.indexOf('edge_count'), N_TYPE = NF.indexOf('type');
const E_TYPE = EF.indexOf('type'), E_NAME = EF.indexOf('name_or_index'), E_TO = EF.indexOf('to_node');
const nodeCount = nodes.length / F;
const mb = (b) => (b / 1048576).toFixed(2) + ' MB';
const nm = (n) => strings[nodes[n * F + N_NAME]] ?? '';
const ty = (n) => nodeTypeEnum[nodes[n * F + N_TYPE]];
const self = (n) => nodes[n * F + N_SELF];

const firstEdge = new Int32Array(nodeCount);
let c = 0; for (let n = 0; n < nodeCount; n++) { firstEdge[n] = c; c += nodes[n * F + N_EDGES]; }
const offToIdx = new Map(); for (let n = 0; n < nodeCount; n++) offToIdx.set(n * F, n);

// Build reverse index: to_node-index -> list of {src, edgeName}
function incoming(target) {
  const res = [];
  for (let src = 0; src < nodeCount; src++) {
    const ec = nodes[src * F + N_EDGES]; let off = firstEdge[src] * EW;
    for (let e = 0; e < ec; e++, off += EW) {
      if (offToIdx.get(edges[off + E_TO]) === target) {
        const etype = edgeTypeEnum[edges[off + E_TYPE]];
        const ename = (etype === 'element' || etype === 'hidden') ? '[' + edges[off + E_NAME] + ']' : (strings[edges[off + E_NAME]] ?? String(edges[off + E_NAME]));
        res.push({ src, etype, ename });
      }
    }
    if (res.length > 6) break;
  }
  return res;
}

// biggest node
let big = 0; for (let n = 0; n < nodeCount; n++) if (self(n) > self(big)) big = n;
let frontier = [{ n: big, path: `${ty(big)} ${nm(big)} (${mb(self(big))})` }];
const seen = new Set([big]);
for (let depth = 0; depth < 5; depth++) {
  console.log(`\n--- depth ${depth} ---`);
  const next = [];
  for (const { n, path } of frontier) {
    console.log(path);
    for (const inc of incoming(n)) {
      if (seen.has(inc.src)) continue;
      seen.add(inc.src);
      next.push({ n: inc.src, path: `  <- ${ty(inc.src)} "${nm(inc.src).slice(0,40)}" via ${inc.etype}:${inc.ename}` });
    }
  }
  if (next.length === 0) break;
  frontier = next.slice(0, 8);
}
