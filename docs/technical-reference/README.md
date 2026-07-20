# Technical Reference

Per-family mathematical documentation, added as each roadmap phase lands. Every page follows the six-section structure: 1. Mathematical Formulation (LaTeX) · 2. Numerical Implementation · 3. Parameters · 4. Assumptions and Limitations · 5. Verification (benchmark, tolerance, test link) · 6. References (IEEE numbers from [../references.md](../references.md)).

Planned family tree (mirrors the model-library namespaces):

```
technical-reference/
├── support/            ← canonical hashing + seeding, sampling schemes (Phase 1)
├── hazard/             ← tabular + parametric hazards (Phase 2); nonparametric/RFA/composite (10); bivariate (12)
├── transform/          ← linear, power, tabular (Phase 3); composites + rating-curve import (10, 12)
├── response/           ← tabular, parametric, non-fail (Phase 4); composites (10); event trees (11); bivariate (12)
├── consequence/        ← tabular, parametric (Phase 4); composites (10); LifeSim (12)
└── risk-analysis/      ← components, occurrence-index seeding, engine integration methods (Phases 5–6)
```
