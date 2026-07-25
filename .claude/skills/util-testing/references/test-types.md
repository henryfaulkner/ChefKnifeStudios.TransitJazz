# Test Types Catalog

Distilled from the Geotab Software Test Plan Template. Use this to classify and select the right test type for a given scenario.

---

## Functional Tests

Tests that validate *what* the system does (feature requirements).

| Type | Scope | Key Characteristics |
|---|---|---|
| **Unit** | Single unit of behaviour (a method, a function) | All dependencies mocked/doubled; one logical assertion; fast; 100% consistent; run every commit |
| **Component** | Multiple units/classes within one system boundary | No external dependencies; nearly as fast as unit; covers gaps not easily unit-testable |
| **Integration** | Interaction between components or systems | Broadly covers component, contract, and system interop; real or controlled external dependencies |
| **Contract** | Interfaces and boundaries with external/internal services | Tests edge classes (egress calls, REST/gRPC endpoints, SDK clients) in isolation |
| **Sanity / Smoke** | Critical happy paths; exit criteria for code-complete | Subset of system tests; gates "testing can start" |
| **Functional** | Coverage between integration and system | Catch-all for tests not cleanly fitting other layers; matrix-style coverage |
| **Interface** | GUI, CLI, API, SDK surfaces | Explicit tests for each interface type |
| **System (Greybox)** | Full solution without *uncontrolled* external dependencies | Black-box inputs; critical feature workflows; run in core CI/CD |
| **End-to-End (E2E)** | Full solution with all real external dependencies and infrastructure | Non-functional focus; rarely fails for functional reasons; nightly or gate-triggered |
| **Installation / Upgrade / Downgrade** | Deployment lifecycle | Greenfield, brownfield, migration scenarios |
| **Beta / Acceptance (UAT)** | Real users in simulated or production environment | Confirms user requirements; automate results as regression |

---

## Non-Functional Tests

Tests that validate *how* the system operates (quality attributes).

| Type | Focus | Typical Duration |
|---|---|---|
| **Stress / Break'n'Destroy** | System resilience beyond specifications; recovery | Hours |
| **Feature Interoperability / 3rd Party Integration** | Behaviour with external/partner systems outside our control | Varies |
| **Reliability / SOAK** | Long-running stability; memory leaks, degradation trends | 24 h – 1 week |
| **Performance / Load** | Vertical throughput at peak load; bottleneck identification | Minutes–hours |
| **Horizontal Scalability** | How the system scales out; diminishing returns | Hours |
| **Dimensioning** | Quantitative model for field sizing and sales | On demand |
| **Usability (UX)** | UX expert review of interfaces | On demand |
| **Security** | DevSecOps toolchain; threat modelling; colour-team exercises | On demand |
| **Exploratory** | Unscripted, creative investigation of unknown/risky areas | On demand |
| **Documentation** | Explicit tests for docs completeness and accuracy | On demand |
| **Localization** | Geo-redundancy, multi-timezone, regional correctness | On demand |

---

## SMURF Trade-off Dimensions

When choosing a test type, weigh these five dimensions:

| Dimension | Question |
|---|---|
| **Speed** | How fast does this test run? |
| **Maintainability** | How costly is it to keep this test working over time? |
| **Utilisation** | What resources does it consume (CPU, network, hardware)? |
| **Reliability** | Will it fail only when there is a real problem (no flakiness)? |
| **Fidelity** | How closely does it mimic real operating conditions? |

Lower test layers (unit, component) score high on Speed/Reliability/Maintainability but low on Fidelity. Higher layers (E2E, Perf) score high on Fidelity but low on Speed/Reliability. Push coverage *as low as practical* to maximise efficiency.

---

## Execution Tiers
| Tier | Layers | Frequency |
|---|---|---|
| **0 (Always)** | Unit, fast component | Every commit everywhere |
| **1 (Core)** | Broader integration, some system | Often locally; always in CI smoke |
| **2 (Extended)** | Contract, system, E2E, basic perf | By trigger/rule in CI |
| **3 (Perf/Scale)** | Performance, scalability, stress, slow soak | On demand / scheduled |
| **4 (Edge)** | Destructive, statistical reliability, extreme edge cases | Rarely, only on demand | 