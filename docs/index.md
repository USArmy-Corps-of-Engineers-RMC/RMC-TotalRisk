# RMC-TotalRisk Documentation Map

Development documentation for the v1.1 effort. The v1.0 end-user documentation (User's Guide, Technical Reference Manual, Verification Report) is linked from the repository [README](../README.md).

| Document | Purpose |
|---|---|
| [ROADMAP.md](ROADMAP.md) | The phased development roadmap — single source of truth for phases and exit gates |
| [PROGRESS.md](PROGRESS.md) | Per-session progress log (newest first) — read this first when resuming work |
| [verification.md](verification.md) | Legacy-oracle conversion strategy + Monte Carlo tolerance policy |
| [verification/](verification/README.md) | Per-family verification results (the living v1.1 counterpart of the 2024 Word report) |
| [references.md](references.md) | Consolidated IEEE-numbered bibliography |
| [requirements/MODEL_LIBRARY_ARCHITECTURE.md](requirements/MODEL_LIBRARY_ARCHITECTURE.md) | Normative model-library architecture spec (layout, contracts, seeding, sampling, clusters, engine) |
| [requirements/SHARED_FUNCTIONS_STRATEGY.md](requirements/SHARED_FUNCTIONS_STRATEGY.md) | Cross-repo strategy: shared function math in Numerics; BestFit import contract |
| [technical-reference/](technical-reference/README.md) | Per-family math documentation (grows per phase) |
| verification-requests/ | Specs for user-executed reference runs that produce committed benchmark data (created as needed) |

Process rules (quality gates, contracts, workflows) live in [CLAUDE.md](../CLAUDE.md) at the repo root (AGENTS.md is generated from it).
