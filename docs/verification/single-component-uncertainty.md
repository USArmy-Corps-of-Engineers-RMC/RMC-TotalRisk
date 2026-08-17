# Single-Component Knowledge Uncertainty

**Test class:** `SingleComponentUncertaintyVerification` · **Tests:** 4 · **Run of record:** 2026-07-23, isolated run, ✅ all passed

A NEW family with no legacy counterpart: the full-uncertainty **two-loop** simulation (outer
knowledge realizations, inner risk integral — the Uncertainty Analysis technical note's [26]
framework) verified against an independent two-loop oracle whose **inner integral is exact**.
The legacy Bucket-1 suite never covered knowledge uncertainty at the analysis level; this
family anchors the tabular co-monotonic percentile contract, the fail/non-fail consequence
coupling draw, the ensemble percentile surfaces, scheme agreement, and the `ParametricResponse`
posterior-injection lifecycle.

## Scenario A — tabular knowledge uncertainty

Hazard tabulated from Normal(100, 20) quantiles (±8 z-grid, step 0.25, deterministic); one
failure mode under the competing method (a single mode short-circuits to its raw response
probability — the per-mode path that carries the coupling pairing):

| Input | Knots (stage → distribution) |
|---|---|
| Fragility | 100 → Triangular(0, 0.05, 0.1); 200 → Triangular(0.6, 0.8, 1.0) |
| Failure consequence | 60 → Triangular(0, 10, 20); 200 → Triangular(400, 1000, 1600) |
| Non-failure consequence | 60 → Triangular(0, 5, 10); 200 → Triangular(200, 500, 800) |

Every knot sits on the hazard grid, so within each grid interval the sampled curves are linear
in stage and the stage is linear in probability — the oracle's per-interval Simpson rule
integrates the piecewise-quadratic inner integrands **exactly**. Oracle: 20,000 outer
realizations with independent streams (fragility 12345; the mode's coupling percentile
45678 driving BOTH the failure consequence and its paired non-failure; the non-failure mode's
own percentile 78910; the decoupled counter-pin draws 13579). Engine: 500 knowledge
realizations, Latin hypercube (Monte Carlo for the scheme-agreement run).

### Results (oracle / engine)

| Measure | Oracle | Engine |
|---|---|---|
| Grand mean — failure risk | 42.9458 | 43.1169 |
| Grand mean — excess risk (coupling-paired) | 21.4729 | 21.5584 |
| Grand mean — background risk | 146.625 | 147.04 |
| Grand mean — non-failure risk | 125.149 | 125.533 |
| Mean-only failure risk (linearity cross-check) | — | 43.0389 |
| 90% confidence exceedance band at consequence 300 | percentiles of the realization ensemble | [0.02144, 0.1142] |

Every comparison within its combined outer-sampling k·SE (k = 4; the engine side charged at
the Monte Carlo rate — conservative for Latin hypercube). The ensemble 5th/95th percentiles of
the per-realization failure mean and the lower/upper 90% confidence exceedance ordinates at
the probe agree at the density-scaled quantile error. The Monte Carlo scheme reproduces the
Latin hypercube grand mean (scheme agreement; variance-reduction quantification is the
[LHS variance-reduction family](lhs-variance-reduction.md)).

### The coupling pin and counter-pin

Under quantile dominance the coupled excess never clamps, and its ensemble dispersion is the
dispersion of the **quantile difference** — far smaller than under independent percentiles:

| Ensemble σ of the excess mean | Value |
|---|---|
| Coupled oracle (the coupling contract) | 6.80038 |
| **Engine** | **6.98694** |
| Decoupled oracle (the counter-hypothesis) | 12.5632 |

The engine sits on the coupled side within k·SE and rejects the decoupled hypothesis by a wide
margin (1.85× separation) — the fail/non-failure consequence pairing is real, exercised, and
this test would detect its loss.

## Scenario B — parametric posterior injection

The same hazard behind a `ParametricResponse` whose Normal(140, 30) parent receives a
deterministic injected posterior of 500 parameter sets (a fixed formula — no randomness), with
deterministic consequences. The engine's full-uncertainty pass looks the posterior up by
realization index (the v1.0 D = 0 semantics), so the ensemble is a deterministic walk of the
injected sets and the oracle compares **realization for realization** — every engine
realization mean against a dense-trapezoid integral of the same parameter set at 0.1% relative.

| Measure | Oracle | Engine |
|---|---|---|
| Posterior grand mean — failure risk | 56.331416 | 56.327676 |
| Mean-only (the posterior MEAN CURVE, v1.0 semantics) | — | 55.543279 |

All 500 realization-for-realization pins passed. The recorded **Jensen gap** — mean-only
55.543 versus ensemble grand 56.331 (≈ 1.4%) — is "pitfall 5" of the Uncertainty Analysis
technical note [26] made quantitative: the v1.0 mean-only pass reads the posterior mean curve (a
quantile-space mean), which legitimately differs from the mean of the per-set risk integrals.
This family is the `ParametricResponse` verification anchor in the Ported Types Matrix.

## Notes

- Engine runs use 500 knowledge realizations and a 1e-6 inner quadrature tolerance
  (documented in the test: the ensemble comparisons are outer-sampling statistics at 0.1%+
  scales; the 1e-8 default across hundreds of realizations only added runtime).
- Full-ensemble bit-identity reproducibility is pinned by the engine-reproducibility family;
  this family adds no duplicate pins.
