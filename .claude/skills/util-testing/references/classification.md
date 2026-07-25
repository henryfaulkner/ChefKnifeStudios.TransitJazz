# Knowledge - Test Classification & Choosing the Layer

Load when picking the layer for a behaviour, deciding where coverage belongs.
Classifications are *guidelines*, not rigid law - the goal is minimizing risk and
cost while maximizing reliable coverage. Aim for "close enough," and when stuck,
fall through to the SMURF trade-offs below.

---

## Functional vs. non-functional

- **Functional** - *what* it does (from requirements). Unit, component,
  integration, system, regression, localization, install/upgrade. Fast, precise
  feedback; most run every commit.

- **Non-functional** - *how* it behaves. Performance, scalability, stress,
  reliability/soak, security, usability, E2E. Run less often; assert
  releasability.

## Classification layers

Ordered lowest (cheapest, fastest, most reliable) to highest. **Push coverage to
the lowest layer that can prove the behaviour.**

| Layer | Scope | Dependencies | Notes |
|---|---|---|
| **Unit** | One unit of behaviour (a method or small class) | All mocked/doubled | Fast, 100% reliable, ~1 logical assertion. Mockist style. Run constantly (red/green). |
| **Component** | Several units together within one component | No external deps (all doubled) | Nearly as fast as unit; asserts on state; may have >1 assertion. Classicist style. Smaller scope than system. |
| **Integration** | Interaction of components/systems | Some real | Broad umbrella term - prefer a more specific label below when you can. |
| **Contract** | An "edge" class against a real dependency boundary (HTTP/gRPC, upstream lib, SDK) | The specific dependency, in isolation | Verifies interfaces are used as specified. Where you test your own published clients/SDKs. |
| **System** | The whole solution, no **uncontrolled** external deps| Local test instance of deps | Black-box where practical. "Does it work as a system?" Slower/costlier; still run in core CI. |
| **E2E** | Full solution + real deps + deployed infra | All real | Targets non-functional/infra concerns, not functional bugs. Slowest, least reliable - often nightly or a merge gate, not a commit. |
| **Performance** | Throughput/latency of a component or system | Controlled env | Builds on performance results. |
| **Reliability (soak)** | Long-running stability | Real-ish | Finds leaks, drift, recovery issues over hours/days. |

The classic **test pyramid**: many unit tests at the base, fewer as you climb.
Complexity and matrix/combinatorial coverage should live as low as possible.

## SMURF - the trade-offs when the pyramid isn't enough

Every layer choice trades these off; improving one often costs another. If you
can improve one without harming the rest, do it.

- **S**peed - how fast tests run (lower layers win).
- **M**aintainability - aggregate cost to keep tests working.
- **U**tilization - resources consumed per run.
- **R**ealiability - fails only on real problems (see `reliability.md`).
- **F**idelity - how closely the test mirrors production (higher layers win).

## Execution tiers (when to run what)

A loose aggregation of layer + SMURF, useful for CI trigger rules:

| Tier | Contents | When |
| **0 (Always)** | Fast units, some fast component | Everywhere, every change |
| **1 (Core)** | Integration/component, some system | Locally + always in CI (smoke) |
| **2 (Extended)** | Contract, system, E2E, basic perf | By trigger/rules in CI |
| **3 (Perf/Scale)** | Dedicated perf/scale, stress, soak | On demand / scheduled |
| **4 (Edge)** | Rare edge, statistical, destructive | Only when needed |

## Choosing, in practice

1. Default to **units**. Can this behaviour be proven with a fast, isolated test?
2. If it needs to few collaborating units -> **component**.
3. If it crosses a real boundary you own or depend on -> **contract**.
4. Only escalate to **system/E2E** for behaviour that truly can't be proven
   lower - and keep those few and high-value.