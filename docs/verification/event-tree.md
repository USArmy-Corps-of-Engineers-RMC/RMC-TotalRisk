# Event-tree response

**Test classes:** `EventTreeVerification` (8) · `EventTreeLegacyConversionVerification` (3) · `EventTreePhaseCloseVerification` (3) · **Tests:** 14 · **Run of record:** 2026-07-28, isolated run, ✅ all passed

## Scope and status

`EventTreeVerification` verifies the complete numerically observable event-tree response
capability: controlled scalar/tabular event trees, legacy conditional-probability algebra,
aggregate failure, exhaustive terminal outputs, indexed LHS table sampling, internal/external
`IndependentClone` links, direct and multi-level nested `EventTreeResponse` probability sources,
both serialization modes, occurrence reproducibility, recursive v1.0 XML conversion and shipped
templates, graph-connected arbitrary n-way per-leaf consequences, an independent branch-routing
Monte Carlo oracle, aggregate LHS variance reduction, and thread-count bit identity. It also covers the
immutable compiled occurrence/evaluation plan, complete dependency invalidation and rollback, and
the F5 large repeated-link performance fixture. Fixed-seed generated fast tests supply deterministic
property coverage and minimized serialized counterexamples. At the run of record the isolated
family was **14/14**, the fast suite **781/781**, and unit-only library line coverage
**90.40% (12,257/13,584)**. The exact static fault-tree capability is future work and is not part
of this verification family.

The response computes conditional fragility `P(F|h)` only. Hazard probability, annualization,
consequences, and risk remain outside the event tree.

## Probability oracle

For explicit siblings with raw conditional probabilities `q_i`, let `S` be their compensated sum.
The implemented and verified legacy rule is

```text
p_i = q_i                  when S <= 1
p_i = q_i / S              when S > 1
p_remainder = 1 - S        when S <= 1
p_remainder = 0            when S > 1
```

The probability of terminal path `L` is the product of its conditional branch probabilities.
Aggregate failure is the compensated sum of the path probabilities whose terminal descriptors are
classified `IsFailure = true`. When no authored remainder exists, unassigned mass is emitted as a
stable implicit non-failure branch so terminal probabilities remain exhaustive.

## Legacy authority and conversion boundary

The conversion was audited against these Dev-repository authorities:

- partial C#: `RMC.TotalRisk.IO/Project/Elements/Response Function/EventTreeResponse.cs` and its
  `Event Nodes` types;
- released VB: `RMC.TotalRisk/Project/Elements/Response Function/EventTreeResponse.vb` and its
  `Event Nodes` types;
- shipped templates: `RMC-TotalRisk/Resources/TreeTemplates.xml`; and
- legacy smoke test: `Test_TotalRisk/Test_EventTree.vb::TestIO`.

The shipped template audit found 29 roots, 466 total nodes, and 219 chance nodes: 208 use
`MultiValue`, 11 use `SingleValue`, and none uses `ResponseFunction`, `EventNode`, or structural
links. The committed [fixture manifest](../../src/RMC.TotalRisk.Verification/Data/MANIFEST.md)
records the exact `Basic` and `Concrete Dam Gate Failure` roots copied for verification.

The import-only adapter accepts a direct recursive `Node`, an `EventTreeResponse/Node` envelope,
or an `EventTreeResponse/EventTree/Node` envelope. It accepts both `HazardLevels` and
`HazardIntervals`, both `NodeGuid` and `NodeGUID`, legacy `SingleValue` and `MultiValue` encodings,
and name-only `ResponseFunction` sources. Resolver-backed imports repair the response ID on the
next current-format write. It preserves valid IDs, derives deterministic path IDs when absent,
adds the legacy automatic remainder when omitted, and emits only the explicit-node/edge v1.1 form.

A legacy `EventNode` probability source means reuse of another node's probability source, not
reuse of a subtree. Mapping it to the v1.1 `IndependentClone` would change semantics, so the
converter rejects it with a deterministic path diagnostic. The commented `SecondaryHazardNode`
remains excluded, and `WeightedHazardLevel` remains future bivariate-response work.

No value from legacy `Test_EventTree.Test_Product` is used as an oracle: that method has no
assertion and does not construct an event tree.

## Fixtures and results

Run in isolation:

```powershell
dotnet test src/RMC.TotalRisk.Verification -- --filter "ClassName~EventTreeVerification"
```

Observed 2026-07-28: **14/14 passed**.

| Fixture | Independent expectation | Result |
|---|---|---|
| Deep/wide tree | `0.1 + 0.6 x 0.25 + 0.6 x 0.15 = 0.34`; every hazard's branch mass = 1 | Exact within `1e-14` |
| Graph-connected n-way leaves | Five terminal consequences independently expect `0.10`, `0.15`, `0.09`, `0.36`, and `0.30`; failure leaves sum to aggregate `0.34` | Exact within `1e-14` per leaf and aggregate |
| Over-allocated siblings | failure = `0.8/(0.8+0.7)`; remainder = 0 | Exact within `1e-14` |
| Indexed uncertainty | 256 realization outputs equal the aligned `UncertainOrderedPairedData.CurveSample(p)` at the recorded LHS percentile | Bit-equal |
| Linked subtree parity | Internal and external links after self-contained/by-reference round trip equal an explicitly cloned tree | Bit-equal branches, aggregate curves, and canonical hashes |
| Linked uncertainty reproducibility | Two independent occurrences reproduce across XML modes and sibling reordering | Bit-equal for 256 realizations; occurrences remain distinct |
| Multi-level nested analytic response | Deepest `0.2` to `0.6` response is Normal-Z interpolated at caller hazard `h=1` and multiplied by the explicit `0.6` path | Exact within `1e-14` at all three caller hazards |
| Multi-level nested LHS | 256 indexed results equal direct deepest-table samples followed by independent Normal-Z caller-hazard interpolation | Exact within `1e-14` realization-for-realization |
| Legacy `TestIO` Basic shape | Recursive direct-root XML converts to canonical v1.1 XML and matches an independent terminal path-product oracle | Exact terminal and aggregate parity |
| Shipped `Basic` and `Concrete Dam Gate Failure` | Every converted terminal and aggregate equals an independent recursive oracle with compensated sibling sums and residual/normalization rules | Exact within `1e-14` |
| Legacy nested response | Name-only response resolution, ID repair, caller-hazard interpolation, 256 LHS realizations, and both XML modes match an independent oracle | Exact within `1e-14` realization-for-realization |
| Independent branch routing | Eleven terminal categories plus aggregate failure, including nested siblings, both remainder forms, normalized over-allocation, and repeated internal/external clones | Every comparison within the finite-sample simultaneous Hoeffding bound; maximum error `0.001039`, bound `0.00291492404603552` |
| Aggregate SRS versus LHS | Two independent affine uniform failure branches, analytic expectation `0.4`, equal `N=256`, `R=12` paired-seed replicates | Both unbiased; variance ratio `37,670.5922`; every LHS stratum covered once per dimension/replicate |
| Thread-count reproducibility | One expanded five-port event-tree graph, 100 LHS realizations, sequential/four-worker/two default runs | Bit-identical aggregate/per-leaf curves, full JSON, hashes, seeds, IDs, and ports |

## Fixed-seed generated properties

`EventTreePropertyTests` uses four fixed structural seeds (`0x10A20261`, `0x10A20262`,
`0x10A20263`, and `0x10A20264`) and 32 cases per seed: **128 deterministic valid trees**. The
corpus forces shallow, deep, and wide forms; sibling sums below/equal/above one; explicit and
implicit remainders; failure/non-failure terminals; scalar and aligned uncertain-table sources;
internal/external repeated independent clones; nested event-tree sources; and both XML modes.

For each case an independent authored-tree recursive oracle (not the compiled evaluator) checks
terminal mass conservation, aggregate failure versus failure leaves, and every compiled terminal.
The test also checks link expansion against controlled explicit materialization; self-contained and
resolver-backed by-reference round trips; metadata/GUID/name/order invariance; and compute-source
sensitivity. Failure handling is deterministic: at most 32 accepted reductions first attempt to
remove branches/subtrees, then simplify chance sources to scalar `0.5`; a candidate is retained only
when it remains valid and still fails. The assertion reports seed, case index, feature ledger, the
original exception, and the minimized self-contained XML. This is bounded minimization rather than
a proof of globally minimal structure.

## Independent branch-routing Monte Carlo

The routing fixture has root probability `0.5`; within that scenario it routes direct failure
`0.2`, two independent occurrences of a local sequence at `0.3` each, and route B at `0.2`. Each
local sequence fails at `0.4` and otherwise contributes implicit residual mass. Route B contains two
external clone occurrences whose raw sibling probabilities are `0.6 + 0.6`; the approved
normalization therefore gives each `0.5`, followed by external failure `0.3` or survival `0.7`.
Explicit zero remainders remain terminal categories. The independent analytic partition is:

```text
root survival                         0.500
direct failure                        0.100
local failures (two occurrences)      0.060 each
external failures (two occurrences)   0.015 each
external survivals (two occurrences)  0.035 each
scenario and route-B remainders        0.000 each
combined implicit local residual       0.180
aggregate failure                      0.250
```

A separate BCL `Random` router uses seed **10,202,671** and **N = 1,000,000**; it never calls the
production evaluator for expected values. Eleven terminals plus aggregate failure give `K=12`
simultaneous comparisons. The two-sided finite-sample Hoeffding inequality and a union allowance
at familywise `alpha=1e-6` give the common verification-only absolute bound

```text
epsilon = sqrt(log(2K/alpha) / (2N)) = 0.00291492404603552
```

because `P(|p_hat-p| >= epsilon) <= 2 exp(-2N epsilon^2)` for every Bernoulli category.
The maximum observed absolute error was **0.001039**; aggregate failure observed **0.251039**.
This bound is finite-sample and requires only independent routed realizations; it does not use a
normal approximation or category-specific estimated variance. Zero-probability remainders are
deterministic under the fixture. The test verifies routing probabilities, not the quality of the
BCL generator or temporal dependence.

## Aggregate LHS variance reduction

The LHS fixture has two independent terminal failure probabilities `U(0.05,0.25)` and
`U(0.15,0.35)` plus an explicit survival remainder. Aggregate failure is affine with exact mean
`0.4` and per-draw variance `V = 2 * 0.2^2/12`. Twelve paired-seed replicates use seeds
**10,202,731 through 10,202,742**, equal **N=256** samples, and the same seed for SRS and LHS in
each replicate. The exact variance of a replicate mean is `V/N` for SRS and `V/N^3` for one
jittered draw per LHS stratum. Three simultaneous mean assertions use 25 pooled standard errors:
Chebyshev plus the union bound limits their familywise failure allowance to at most
`3/25^2 = 0.0048`. Acceptance bounds are `0.03682847818679925` (SRS),
`0.0001438612429171850` (LHS), and the dependence-safe sum `0.03697233942971650` for the
paired scheme difference. The variance-ratio gate is a deliberately conservative
`var(SRS)/var(LHS) >= 100`.

Measured pooled means were **0.3986132443167511** (SRS) and **0.39999719858236121** (LHS).

| Replicate | Seed | SRS aggregate mean | LHS aggregate mean |
|---:|---:|---:|---:|
| 0 | 10202731 | 0.39601443792507773 | 0.40001826709065119 |
| 1 | 10202732 | 0.40489462462774112 | 0.39999696067303048 |
| 2 | 10202733 | 0.39986242807317446 | 0.39997725287011943 |
| 3 | 10202734 | 0.40339009470026210 | 0.39999607590577713 |
| 4 | 10202735 | 0.39524235528806484 | 0.40000578515018642 |
| 5 | 10202736 | 0.40305789845206175 | 0.40001857918947487 |
| 6 | 10202737 | 0.39776205653688534 | 0.39997042304311298 |
| 7 | 10202738 | 0.39567803250683936 | 0.39996232048062808 |
| 8 | 10202739 | 0.40019346882490936 | 0.40001188353099415 |
| 9 | 10202740 | 0.39692085471979216 | 0.40000195070675088 |
| 10 | 10202741 | 0.39551790658588326 | 0.39999630132617975 |
| 11 | 10202742 | 0.39482477356032203 | 0.40001058302142944 |

Replicate sample variances were **1.2758276684807033e-5** and **3.3868001385987057e-10**, giving
ratio **37,670.592189374926**. All 12 LHS means and first samples differed by seed. Recovered
uniform percentiles proved exactly one sample in every one of 256 strata, for both dimensions in
every replicate. The proof applies to this analytically affine fixture; it does not claim the same
ratio for nonlinear or strongly interacting event-tree uncertainty.

## Production thread-count reproducibility

The production `RiskAnalysis` fixture contains an uncertain nested event tree with expanded output
connections to all five stable branch ports. At seed **10,202,791** and **100 LHS realizations**, the
same analysis instance ran with maximum degree 1, maximum degree 4, and twice with the production
default. The internal observer recorded 1 unique executing thread for the sequential run, 5 over the
lifetime of the capped run, and 23/26 over the two default runs (the cap limits concurrency, not the
number of thread-pool identities used over time). Every run produced bit-identical aggregate failure
curves, serialized per-leaf curves, `EnsembleResults` JSON, mean-results JSON, response/component/
options hashes, manifest hashes, complete sampler-seed maps, and branch IDs/names/classifications/
ports. The seed-map hash was `0B2B6277147F7563728EFEFB2B7916BBED9398501881EE5F57F2246B91B4FD97`;
the analysis-content hash was `EEE025DBAFC6DF8A306356439B4D1632A4B8219F87BB8BB16CE08AFB8C308049`.
The test controls scheduling only; it does not alter index ownership, reductions, seeds, or default behavior.


## Fast-suite coverage

The fast suite additionally pins direct and multi-level analytic and percentile parity; exact
recursive `SamplingDimensions`; independent repeated occurrences; nested indexed/LHS
reproducibility; metadata/GUID/name/order/serialization/reference-wrapper hash and seed invariance;
both XML modes with resolver ID/name fallback and repair; complete structural/source/mixed cycle
paths; and exact sampler-state rollback. Authoring tests cover immutable fragments, fresh-ID paste
with local-reference remapping, replace/materialize/delete-materialize/prune operations,
deterministic expanded topology/reference inspection, and failed-mutation rollback of topology,
IDs, output ports, hashes, and configured samplers. Graph tests additionally cover opt-in output
discovery for arbitrary n-way splits/remainders, branch-ID/name/port migration, both graph XML
modes and live resolver reuse, rename/metadata/reorder/copy-paste/materialize/prune stability, every
connection slot, stale diagnostics, all three deletion policies, observer-failure rollback, exact
per-leaf mean/indexed projection and grouping, plus canonical hash/seed invariance and selected-path sensitivity.
Compiled-plan tests add every mutation category, metadata retention, silent-table fingerprints,
direct/nested/internal/external/resolver dependencies, unrelated-tree isolation, and exact rollback.

`LegacyEventTreeConversionTests` adds the exact `TestIO` direct-factory import; both wrapper forms;
both hazard/GUID spellings; scalar, compact/table, ordinary-response, and nested-response sources;
current-only self-contained/by-reference writes with repaired IDs; metadata, ID, order, hash, and
seed invariance; deterministic malformed/missing/excluded/unsupported diagnostics; and proof that
failed conversion or converted-source cycle detection leaves an already configured live sampler
unchanged.

## Compiled-plan parity and performance

Repeated reads share one published plan, including concurrent first publication on a 259-node
deep/wide fast fixture. Fresh/self-contained/by-reference equivalents retain bit-identical
canonical hashes, fixed-seed indexed aggregate curves, every per-leaf ordinate, and stable ports.
Failed mutation and sampler setup preserve the prior plan identity/build count and every public
sampler-observable value. Existing `EventTreeVerification` expectations therefore remain unchanged.

The isolated Release F5 fixture expands 24 external independent links into 1,105 instructions,
1,104 edges, and 745 branches over 33 hazards. Median-of-three setup changed from 0.086353 s /
51.70 MB to 0.031395 s / 20.55 MB; 32 indexed reads changed from 1.157486 s / 1,695.75 MB to
0.239866 s / 99.77 MB in the original paired cache characterization. The phase-close

`dotnet run -c Release --project scripts/perf/PerfHarness -- --reps 3 F5`

run measured **0.035477 s / 20.55 MB** setup and **0.283314 s / 99.79 MB** for 32 reads, with
exactly one published plan. Every run produces byte gate
`2ae3925bfb7488cbfa4bd516cc2d4eb7d71c6f84bfff9f4891873a2bfd811349`. These are measured
characteristics, not a newly invented threshold. Wall-clock movement is ordinary workstation
variation; the deterministic plan count, allocation shape, and hash remained stable. Full details are in the
[performance results](../../scripts/perf/RESULTS.md).
