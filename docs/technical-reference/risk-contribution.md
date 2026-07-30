# % Contribution to Risk

> The attribution mathematics behind `RiskContribution`: how each
> failure mode's share of a component's risk — and each component's share of the system's — is
> computed **generally** across all four failure-mode combination methods and both system
> aggregation methods, with exact sum identities. Companion pages:
> [risk-integration.md](risk-integration.md) (where the recorded evaluations come from) and the
> executable evidence in [../verification/contribution.md](../verification/contribution.md).
> Grounding: *Failure Mode Combination Methods in RMC-TotalRisk* (Smith, 2026) — its Eqs. 8–19
> define the exclusive decompositions this page attributes.

## 1. The problem

Practitioners ask two attribution questions of every quantitative risk model:

1. **Which failure mode drives this component's risk?** (a % of the component's annualized
   failure probability, and a % of its mean loss)
2. **Which component drives the system's risk?** (the same two percentages at system scope)

Other tools answer only the easy case: under common-cause adjustment the adjusted marginals are
already exclusive, each mode's mean excess loss simply adds to the component total, and % 
contribution is a plain ratio. The v1.1 requirement is a scheme that works **identically in
concept** across Mutually Exclusive, Common Cause Adjustment, Competing Failures, and Joint
Failures (all four `FailureModeMethod`s), under every joint consequence rule (Sum / Average /
Max / Min), in both analysis modes (risk and reliability), and for both system methods
(additive and joint) — with the shares always summing to exactly what they decompose.

## 2. The exclusive-event foundation

Every combination method the engine implements produces, at each recorded hazard evaluation, an
**exclusive decomposition**: a set of disjoint failure events e with probabilities p_e summing to
the failure union at that hazard level.

| Method | Exclusive events at hazard h | Participants per event |
|---|---|---|
| Mutually Exclusive | the normalized marginals p_j · a(h) | one mode |
| Common Cause Adjustment | the adjusted marginals p_j · c(h), c = union/Σp | one mode |
| Competing Failures | the cumulative-incidence increments CIF_j | one mode |
| Joint Failures | the 2ⁿ − 1 inclusion–exclusion events "exactly the modes in T fail" | the set T |

The first three are the |T| = 1 degenerate case: every event has a single participant, and the
scheme below reduces to "adjusted marginal × its own consequence" — precisely the CCA ratio other
tools report. Joint Failures is the general case that forced a design decision: a pathway event
with participants T = {j, k} has one probability and one combined consequence (per the joint
rule), and some of its mass must be attributed to j and some to k.

## 3. The attribution scheme

Within each exclusive failure event e with participant set T, event probability p_e, per-mode
marginal consequences c_j (j ∈ T), and event consequence C_e (the joint rule applied to the c_j):

- **Probability attribution — equal split (the Shapley value).**

  φ_j(e) = p_e / |T|

  This is not a heuristic. For the coalitional game v(S) = P(∪_{j∈S} F_j) (the failure union over
  mode subsets), the Shapley value of mode j is exactly the sum over exclusive events of
  p_e / |T| for the events j participates in: symmetric modes split evenly, dummy modes get
  zero, and Σ_j φ_j = v(all) = the union — efficiency is the sum identity.

- **Risk attribution — consequence-proportional split.**

  r_j(e) = p_e · C_e · c_j / Σ_{k∈T} c_k   (equal split when Σ_{k∈T} c_k = 0)

  The weights sum to one, so Σ_j r_j(e) = p_e · C_e under **every** joint consequence rule —
  including Max and Min, where no additive per-mode decomposition of C_e exists. The zero-sum
  fallback also covers reliability mode, where the consequence surface is degenerate at zero and
  only the probability attribution is meaningful.

Accumulating over all recorded evaluations (with the same probability-mass treatment the risk
integral itself uses — §5) gives each mode's three raw values, stored per consequence type in
`RiskContribution`:

| Field | Meaning | Sums to |
|---|---|---|
| `FailureProbability` | Σ_e φ_j(e) over the hazard domain | the component Fail `MassBalance` |
| `FailureMean` | Σ_e r_j(e) with C_e = the fail consequence | the component Fail `Mean` |
| `ExcessMean` | Σ_e r_j(e) with C_e = the excess (incremental) entries | the component Excess `Mean` |

**Shares are derived on read, never stored** (`ShareOf(total)`), so a 0/0 (an all-zero scope)
returns NaN at the API without ever writing NaN into a persisted field, and the raw values stay
testable against the sum identities. Two first-class percentage bases:

- **% of APF** = FailureProbability_j / Σ_k FailureProbability_k — consequence-free by
  construction, so **reliability mode reports it fully**;
- **% of mean loss** = FailureMean_j / Σ_k FailureMean_k (or the Excess analog) — the ratio risk
  mode reports alongside it.

## 4. Why the sum identities are exact

The identities pin against the component's **recorded** aggregates:

- Σ_j FailureProbability_j ≡ Fail `MassBalance` ≡ `TotalProbability`. The lazy exclusive
  decomposition is clipped once at the approved remaining-budget boundary before the contribution
  accumulators consume it, so every downstream identity sees exactly the same accepted cells.
- Under the Joint **Additive** rule, r_j(e) = p_e · c_j exactly (the proportional weights cancel),
  so a mode's `FailureMean` contribution equals its marginal Fail mean — the oracle identity the
  verification family pins.
- Background/NonFail streams are owned by the non-failure mode and receive no per-FM attribution
  (documented; the Total-share view derives as FailureMean / Total `Mean` at the API).

## 5. Accumulation mechanics (the bit-identity guard)

Attribution rides **separate accumulators** (`ContributionAccumulator`, runtime-only): per
(mode, type) a list of `readonly struct ContributionRow` appended once per recording evaluation,
gated on `recordOutput` so probes and warm-ups pay nothing, and **never touching the existing
floating-point chains** — introducing attribution moved zero pinned verification constants. Two
finalize modes mirror the engine's two mass regimes:

- `FinalizeFromLedger(ledger)` — the 1D quadrature path. Each row's probability coordinate is an
  integration abscissa, and its mass is read from the **same** `QuadratureMassLedger` the recorded
  curves read, under the same credit-first-occurrence rule. Because the accumulator therefore sees
  the identical mass multiset the component curves see, the sum identities hold to ~1e-12 relative.
- `FinalizeDirect(scale)` — the joint path, where VEGAS rows carry their mass directly and every
  recorded quantity is scaled by the same self-normalization at the `ScaleRecordedMass` hook.

ME/CCA/Competing accumulation stores per-mode products the kernels already compute (one array
store per mode per evaluation — zero new arithmetic in the hot path); Joint accumulation is
O(|T|) per emitted pathway × branch tuple.

## 6. System scope

- **Additive method (independent components).** Component means add exactly under convolution, so
  `FailureMean`/`ExcessMean` system contributions are the component means themselves. The union
  APF split is the Shapley value again, now of the independent union game — with a closed form:

  φ_i = p_i · E[ 1 / (1 + K_i) ]

  where K_i is the Poisson-binomial count of *other* components failing. The engine evaluates the
  expectation with an exact O(D²) dynamic program over the other components' failure
  probabilities (no 2^D enumeration; hand-rolled with documented remarks — attribution
  bookkeeping, not replaceable numerics), and Σ_i φ_i ≡ the independent union to floating-point
  roundoff. A single-component system reports a 100% share.
- **Joint method.** The VEGAS integrand already enumerates exclusive component
  failure/non-failure combinations per evaluation, so per-component scalar accumulators apply the
  same equal-probability / consequence-proportional split per combination, under the same mass
  self-normalization; sums pin against the recorded system aggregates.

## 7. Containers and availability

`FailureModeRealization.Contribution` + `AdditionalContributions` (per declared type),
`ComponentRealization.SystemContribution` + `AdditionalSystemContributions`, with summary capture
on `FailureModeResults`/`ComponentResults`/`ConsequenceResults` — all results-JSON append-only
(an earlier payload loads with null blocks = "not computed"). Because the per-realization scalar
trees persist in `EnsembleResults`, contribution shares are available in mean-only runs, per
realization, and as ensemble confidence intervals through `EnsembleSummary` — no re-simulation.
