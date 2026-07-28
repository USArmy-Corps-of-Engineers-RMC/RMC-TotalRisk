# Technical Reference

Per-family mathematical documentation, added as each roadmap phase lands, grounded in the RMC technical reports kept under [../reports/](../reports/) (RMC-TR-2022-XX *Quantitative Risk Analysis with RMC-TotalRisk* and the 2024 *Verification of the RMC-TotalRisk Software*). Every page describes the family's mathematics in full, and its API code blocks are kept in lock-step with the model library's public surface.

Pages (mirroring the model-library namespaces; phase numbers per [../ROADMAP.md](../ROADMAP.md)):

| Page | Contents | Status |
|---|---|---|
| [hazard-functions.md](hazard-functions.md) | Hazard (frequency) functions: tabular uncertainty modes, parametric bootstrap, posterior import | Phase 2 (landed 2026-07-20); nonparametric/RFA/composite (9), bivariate (11) |
| [transform-functions.md](transform-functions.md) | Transform (composition) functions: tabular; linear/power documented ahead of Phase 7 | Phase 2 (landed 2026-07-20); composites + rating-curve import (9, 11) |
| [response-functions.md](response-functions.md) | System response (fragility) functions: R-S formulation, tabular, parametric, non-fail sentinel; links to the normative event/fault-tree design | Phase 2 (landed 2026-07-20); composites (9), event trees (10A), fault trees (10B), bivariate (11) |
| [consequence-functions.md](consequence-functions.md) | Consequence (damage) functions: tabular, incremental-consequence coupling | Phase 2 (landed 2026-07-20); parametric (7), composites (9), LifeSim (11) |
| [risk-integration.md](risk-integration.md) | The risk engine's numerical integration: Adaptive Gauss–Kronrod (1D), the `RiskIntegrand` refinement-objective enum, VEGAS with power-transform tail focus | Phase 4 / 4b (spec landed 2026-07-21) |
| [loss-exceedance-curves.md](loss-exceedance-curves.md) | LEC / F-N construction, probability-mass derivation, stable weighted moments, the full risk-measure catalog (VaR/CVaR/assurance), FFT system convolution | Phase 4 / 4b (spec landed 2026-07-21) |
| [risk-contribution.md](risk-contribution.md) | % contribution to risk: the Shapley probability split + consequence-proportional risk split over the exclusive failure events of all four combination methods, the additive-system Poisson-binomial closed form, exact sum identities, two percentage bases | Phase 6.6 (landed 2026-07-24) |
| [composite-functions.md](composite-functions.md) | Composite functions across all four clusters: mixture versus competing risks, the aleatory/epistemic decision rule for the weights, why `CompositeTransform` is weighted-average only (with the Jensen-bias worked example), and what is deferred | Phase 9 (landed 2026-07-25) |
| [cascading-end-states.md](cascading-end-states.md) | Cascading response end states (the event tree in the risk diagram): the polarity-product leaf algebra, final-polarity classification, the claimed complement mixture, sibling excess pairing, across-unit combination with the narrow competing gate, and the knowledge-sampling contract | Phase 6.7 (landed 2026-07-24) |
| support/ | Canonical hashing + content-based seeding, sampling schemes | planned (Phase 3–4 write-up alongside the components/engine) |
| risk-analysis/ | Components, occurrence-index seeding, engine integration methods | Phases 3–6 |

Phase 10A note (2026-07-28): `response-functions.md` documents the foundation, independent-link, and controlled authoring/topology slices, including transactional fragment operations, deterministic graph inspection, occurrence sampling, two-mode references, cycle diagnostics, and projected identity. The remaining scope is explicit; Phase 10A is not complete.
