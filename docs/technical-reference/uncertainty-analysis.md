# Uncertainty Analysis

> Technical reference for the full-uncertainty simulation in `RMC.TotalRisk.Analyses.RiskAnalysis`
> and its results surface (`EnsembleResults`, `EnsembleSummary`, `ConvergenceDiagnostics`,
> `SensitivityResults`). Grounding: the *Uncertainty Analysis* technical note [26], whose structure
> this page follows, with the two-loop foundations in [7] App. C and the diagnostics of [7] §7.4–7.6.
> Executable evidence:
> [../verification/single-component-uncertainty.md](../verification/single-component-uncertainty.md),
> [../verification/scalar-uncertainty.md](../verification/scalar-uncertainty.md),
> [../verification/lhs-variance-reduction.md](../verification/lhs-variance-reduction.md), and
> [../verification/sensitivity.md](../verification/sensitivity.md). Companion pages:
> [hashing-and-seeding.md](hashing-and-seeding.md) (the deterministic sampling contract),
> [risk-integration.md](risk-integration.md) (the inner loop),
> [sensitivity-analysis.md](sensitivity-analysis.md) (the implemented sensitivity engine).

## 1. Why uncertainty matters

Risk estimates for dams and levees rest on limited data, expert judgment, and simplified models —
every input, from the flood-frequency curve to the response probabilities to the consequence
estimates, carries uncertainty. Unless that uncertainty is quantified and propagated, a decision
maker cannot say how confident to be in the results, whether risk exceeds tolerable guidelines at a
stated confidence level, or where additional data collection would reduce uncertainty most
effectively [26].

## 2. Two kinds of randomness

**Natural variability (aleatory).** The inherent randomness of the system — irreducible by further
study [38]. It is what the *input functions themselves* model: the flow-frequency curve's annual
maximum, the range of consequence outcomes across depths, durations, and warning times. The risk
integral integrates over natural variability to produce expected annual consequences.

**Knowledge uncertainty (epistemic).** The lack of knowledge about parameters and processes —
reducible with more data [37]. It is what the *uncertainty bounds around* the input functions
model: parameter uncertainty in the frequency distribution, elicited fragility uncertainty, warning
-time and evacuation-rate uncertainty in consequences. Its two primary sources are sampling
uncertainty (small effective record lengths) and model uncertainty (the chosen mathematical form
never fully explains the variable) [46].

The separation is structural: input functions carry natural variability; their uncertainty
distributions carry knowledge uncertainty. That clean split is what makes the two-loop simulation
possible.

## 3. Simulation options

**Mean risk only.** Risk is computed once from the expected value of every input function:

```
E[C] = f(NV, E[KU]).                                                              (1)
```

Fast, and sufficient when no confidence statement is needed. Formally it commutes the expectation
inside the risk integral (the TR's Eq. 94–95 independence assumption [7]) — legitimate for the
mean only when the integral is linear in the uncertain inputs. Where it is not, the mean-only
answer differs from the ensemble grand mean by a real, deterministic amount: the **Jensen gap**,
measured at ≈ 1.4% on the posterior-injection fixture in
[../verification/single-component-uncertainty.md](../verification/single-component-uncertainty.md)
(mean-only 55.543 versus ensemble grand mean 56.331). Neither number is wrong — they answer
different questions (§8, pitfall 5).

**Risk with full uncertainty.** The two-loop Monte Carlo propagates knowledge uncertainty through
the entire calculation, producing a distribution of every risk output. It is required when
confidence intervals are needed on the LEC, when risk is judged against tolerable-risk guidelines
at a confidence level, when sensitivity analysis must rank uncertainty drivers, and when the
standard deviation and tail risk must be estimated accurately.

## 4. The two-loop simulation framework

The outer loop samples knowledge uncertainty — for each realization i = 1…R, every input function
k of every component j samples a new curve `F*ᵢⱼₖ` from its uncertainty distribution at a
percentile rₖ. The inner loop computes risk with the sampled curves — the adaptive Gauss–Kronrod
integral per component and the VEGAS joint integral across components
([risk-integration.md](risk-integration.md)) — recording all five risk types and every configured
measure per failure mode, component, and system. Confidence intervals and the ensemble mean come
from the recorded realizations {θ*₁ … θ*_R}.

### 4.1 The v1.1 sampling design: a deterministic percentile matrix

The note's v1.0 machinery drew from a run-seeded subtractive PRNG during simulation. The v1.1
engine deliberately replaces that with a **deterministic, content-seeded percentile design**
generated before the realization loop ([hashing-and-seeding.md](hashing-and-seeding.md)):

- `SetupSamplers` gives each input function an N×D percentile matrix (N realizations × its own
  sampling dimensions), seeded from the component seed and the function's canonical content hash
  plus its sampler-walk ordinal. **No random number is generated inside the realization loop.**
- Same inputs + same seed → bit-identical results at any thread count; renaming, canvas moves, and
  reordering never change results — the content-based identity contract.
- `SamplingScheme` selects the design: `LatinHypercube` (the default) stratifies each function's
  percentile column, removing main-effect sampling variance; `MonteCarlo` preserves plain
  independent draws. On the deliberately linear discrimination scenario the replicate variance
  ratio measured 4.27×10⁴ in favor of LHS
  ([../verification/lhs-variance-reduction.md](../verification/lhs-variance-reduction.md)); on
  interaction-dominated models the benefit is smaller, since per-dimension stratification removes
  main-effect variance only.
- `PinnedSamplerSeeds` re-applies a captured seed map across model perturbations, removing seed
  noise from A/B comparisons while leaving quadrature adaptivity honest — a parameter effect then
  shifts results only within integration tolerance.

### 4.2 Integration discipline inside the loop

The mean pass, deterministic probes, and mean-only runs integrate at the tight production
tolerance; ensemble realizations integrate at the ensemble discipline (`EnsembleTolerance`,
default 1e-4) — a deliberate cost/accuracy split, since band statistics average across R
realizations while the mean pass stands alone. Tests comparing per-realization values against
exact quadrature pin the ensemble discipline explicitly.

### 4.3 Number of realizations

`Realizations` defaults to 1,000, which stabilizes means quickly (often within a few hundred
realizations); extreme percentiles (5th/95th) stabilize more slowly, so raise the count when tail
percentiles drive the decision [26]. The TR's App. C convergence formulas give the required N for
a target precision on a mean and on a percentile [7]. The v1.1 library imposes no file-format cap —
memory and runtime scale with R and are itemized before the run by the engine's resource estimate
— and `ConvergenceDiagnostics` reports the realization-adequacy indicators (SD/√N and confidence
half-widths on the headline scalars) with every ensemble run.

## 5. Sampling strategy by input-function family

All knowledge sampling is inverse-transform: a percentile r maps through the uncertainty
distribution's inverse CDF, `x = F⁻¹(r)` (Eq. 2). Per family:

- **Hazard functions.** Parametric hazards bootstrap their fitted distribution (or accept an
  externally fitted posterior through `Estimate(IList<ParameterSet>)` — the posterior-injection
  contract, sampled index-for-index), producing a new frequency curve per realization. Tabular
  hazards sample their per-ordinate uncertainty distributions co-monotonically along the curve;
  nonparametric hazards sample their derived log-quantile ladder. See
  [hazard-functions.md](hazard-functions.md).
- **Transform functions.** Tabular transforms sample per-ordinate uncertainty co-monotonically;
  the parametric transforms carry their fitted residual terms
  ([transform-functions.md](transform-functions.md)).
- **System response functions.** Parametric responses sample capacity-distribution parameters;
  tabular responses sample per-ordinate fragility uncertainty; event- and fault-tree responses
  sample each uncertain node source at its own content-seeded stream — sibling probabilities that
  sum above one normalize proportionally, exactly as in the deterministic algebra, and repeated
  link occurrences draw independent streams ([event-trees.md](event-trees.md),
  [fault-trees.md](fault-trees.md), [response-functions.md](response-functions.md)).
- **Consequence functions.** Tabular ordinates support deterministic, triangular, PERT, normal,
  log-normal, and truncated-normal uncertainty. Within a failure mode the failure and non-failure
  consequence functions sample **co-monotonically** (one percentile drives both), keeping the
  excess `C_Δ(x) = C_F(x) − C_NF(x)` consistent — both curves describe the same underlying
  scenario. The coupling is pinned, with a decoupled counter-pin proving the scenario
  discriminates, in
  [../verification/single-component-uncertainty.md](../verification/single-component-uncertainty.md).

Beyond the note's list, the composite families recurse — each child owns its dimensions and its
content-seeded stream — and a mixture's branch choice is **not** a knowledge dimension at all: the
engine enumerates exposure branches at every hazard level instead of drawing one
([composite-functions.md](composite-functions.md)).

## 6. Risk results with confidence intervals

**LEC bands.** Each realization constructs its exact loss-exceedance curve; the ensemble yields
percentile bands (e.g., the 90% band), the median, and the mean curve at each consequence level
([loss-exceedance-curves.md](loss-exceedance-curves.md)). Wide bands localize the hazard region
where knowledge uncertainty dominates.

**Conditional mean loss scatter.** Each realization contributes one (E[C|F], α) point; the R-point
cloud shows the joint spread of failure probability and conditional consequences — a tight cluster
is a robust estimate, a diffuse cloud a knowledge-limited one.

**Summary statistics.** Every configured risk measure is computed per realization;
`EnsembleSummary` reports the mean, standard deviation, and key percentiles of each measure's
distribution. Each measure reduces **independently** — percentile-consistent per measure,
deliberately not one coherent realization, because the 95th percentile of the value-at-risk is not
the value-at-risk of the 95th-percentile curve (the two orderings genuinely differ; see
[../verification/scalar-uncertainty.md](../verification/scalar-uncertainty.md)).

### 6.1 Weighted epistemic ensembles

The outer loop ordinarily treats its R realizations as equally credible: every band and summary
statistic gives each knowledge state weight 1/R. An optional per-realization weight vector
generalizes that — weight wᵢ states the **relative epistemic credibility** of realization i, and
every ensemble reduction becomes its weighted form:

- the mean of a measure is Σwᵢxᵢ/Σwᵢ;
- the percentile bands and summary percentiles use the symmetric weighted percentile — each
  positive-weight realization sits at plotting position p(i) = A(i)/(A(i) + B(i)) with A the
  weight strictly below and B strictly above, interpolating linearly between positions.
  Zero-weight realizations carry no mass (a refuted knowledge state drops out); equal weights
  reproduce the unweighted reduction;
- the convergence indicators report the weighted mean with standard error √(V/N_eff), where V is
  the reliability-weighted unbiased variance and N_eff = (Σw)²/Σw² is the Kish effective sample
  size — recorded on the summary as `EffectiveRealizationCount`, the honest statement of how many
  equally-weighted realizations the weighted ensemble is worth. A sharply concentrated weight
  vector means the ensemble carries less information than its raw count suggests, and the
  convergence check should be read against N_eff, not R.

Weights are **reliability (importance) weights**: only relative values carry meaning, they are
stored raw, and the reductions normalize internally. They are results-side state — never part of
a model's canonical hash and never an influence on sampling seeds, so a weighted run draws
bit-identically the realizations the unweighted run draws and only the reductions over them
change. Supplied as a run input (`RiskAnalysis.RealizationWeights`), weights shape the published
percentile bands and summary, are stamped into the stored ensemble, and are fingerprinted in the
run manifest; assigned to a finished result set (`EnsembleResults.SetRealizationWeights`), they
re-weight the scalar summary through `ComputeSummary` without re-simulation — the natural write
path for Bayesian likelihood re-weighting of an existing ensemble and for scenario or logic-tree
branch credibilities. The integrator effort diagnostics stay unweighted (they measure compute
actually spent), and a mean-only run refuses weights — there is no ensemble to weight.

## 7. Diagnostics

The library computes and serializes the diagnostic substance; plotting (kernel densities, tornado
charts, X-Y scatter, tabular explorers) is the consuming layer's presentation over the same
`EnsembleResults` JSON.

- **Integration diagnostics.** Per-realization integrator effort and reported error, aggregated in
  `ConvergenceDiagnostics` — the check that the inner loop converged across all realizations.
- **Risk-measure distributions.** The per-realization measure vectors behind `EnsembleSummary`
  give the full distribution of every measure, not just its mean.
- **Risk profiles.** Exceedance probabilities and conditional consequences against hazard level,
  filterable by component and risk type, with ensemble bands — the profile catalog on the results
  trees ([results-catalog.md](results-catalog.md)).
- **Sensitivity.** The engine ranks uncertainty drivers from the recorded realizations: Pearson
  and rank correlations between each sampled input stream and each risk output, and the squared
  correlation as the sensitivity index — for a single regressor exactly the regression variance
  share SIᵢ = (σ²_θᵢ/σ²_y)·βᵢ² of the note's Eqs. (3)–(4). Chance-node streams correlate
  positively with response probability, remainder streams negatively; a high index marks the input
  whose uncertainty reduction buys the most. The implemented surface
  (`MeasureSensitivity`, `MeasureSensitivityMatrix`, `HazardLevelSensitivity`) and its
  seed-rederivation contract are specified in
  [sensitivity-analysis.md](sensitivity-analysis.md); the tornado plot is the standard
  presentation of the ranked indices.

## 8. Practical guidance

Run full uncertainty whenever the decision is sensitive to confidence level: guideline comparisons
at a stated confidence, uncertainty-driver ranking, accurate σ and tail measures, and risk
communication that portrays the range rather than a point. The five pitfalls [26]:

1. **Running full uncertainty with no uncertainty defined.** Every realization is then identical
   and the bands collapse to a line — verify the key inputs carry uncertainty first.
2. **Under-specifying uncertainty.** Artificially tight bounds manufacture false confidence.
3. **Over-specifying uncertainty.** Arbitrary wide bounds drown the decision; the characterization
   must reflect the actual state of knowledge.
4. **Not checking convergence.** Tail percentiles from too few realizations mislead — confirm key
   statistics are stable against the `ConvergenceDiagnostics` indicators (or a doubled count).
5. **Confusing the ensemble mean with the "best estimate."** The ensemble grand mean incorporates
   knowledge uncertainty through a nonlinear integral and legitimately differs from the mean-only
   answer — the measured Jensen gap of §3. Report which one is being quoted.

Characterize uncertainty hardest where it matters most: the sensitivity ranking identifies the
inputs deserving the most careful elicitation or additional data.

## 9. Verification anchors

| Contract | Family |
|---|---|
| Two-loop grand means, percentile bands, coupling pin, posterior-injection parity | [single-component-uncertainty.md](../verification/single-component-uncertainty.md) |
| Closed-form ensemble-quantile targets; per-measure independent reductions | [scalar-uncertainty.md](../verification/scalar-uncertainty.md) |
| Weighted reductions vs an independent re-implementation; integer-weight replication; weight-inert sampling | [weighted-ensemble.md](../verification/weighted-ensemble.md) |
| LHS-versus-MC replicate variance ratio; unbiasedness | [lhs-variance-reduction.md](../verification/lhs-variance-reduction.md) |
| Correlation/rank sensitivity pins; inert-input null band | [sensitivity.md](../verification/sensitivity.md) |
| Bit-identical reproducibility at any thread count; metadata inertness | [engine-reproducibility.md](../verification/engine-reproducibility.md) |
