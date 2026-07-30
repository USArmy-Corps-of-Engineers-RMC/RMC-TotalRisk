# Canonical Hashing, Content-Based Seeding, and Sampling Schemes

> Technical reference for the seed-identity kernel in `RMC.TotalRisk.Core`:
> `CanonicalContentHasher`, `CanonicalizationRules`, `SeedHelpers`, `SamplerSeedMap`, and the
> `SamplingScheme` catalog. The normative specification is
> [../requirements/MODEL_LIBRARY_ARCHITECTURE.md](../requirements/MODEL_LIBRARY_ARCHITECTURE.md)
> §5.5 (canonical hashing and content-based seeding; §5.5.8 the seed-stable perturbation mode)
> and §5.8 (sampling) — this page summarizes the shipped mechanism and defers every contract
> detail to that document.

## The contract in one sentence

Monte Carlo seeds derive from **SHA-256 canonical content hashes** of the model objects plus
**occurrence indices** — never from names, canvas positions, list order, or wall clocks — so
renaming, moving, or reordering model elements can never change results, while any
compute-relevant edit deliberately re-rolls exactly the streams it touches. The same inputs and
seed produce bit-identical results at any thread count.

## `CanonicalContentHasher`

`Hash(persistedForm, rules)` maps a model type's `ToXElement()` output to a 32-byte SHA-256 hash:
deep-copy the persisted form → apply the rule set's structural rewrites → strip
identity/display/presentation attributes and elements → emit self-describing canonical bytes →
SHA-256. The byte emission is a length-prefixed binary encoding (never `XElement.ToString()`), so
the hash is independent of XML formatting and writer behavior: attributes sort ordinally by name
(attribute order is not semantic), child nodes keep document order (owned-collection order **is**
semantic), and node counts plus type markers make the encoding injective. Doubles participate as
their persisted `"G17"` invariant-culture text, which is bit-faithful — distinct IEEE-754 values,
including negative zero, NaN, and infinities, always produce distinct text. Hashing is intended
for analysis-setup time only, never inside per-realization loops. `ToTokenHex` renders a short
display token.

Because `ToXElement()` is the identity surface, serialized attribute names and owned-child order
are **append-only contract**: every new model property is classified compute-relevant (hashed) or
metadata (added to the strip rules), and existing serialized attributes are never renamed or
reordered.

## `CanonicalizationRules`

An immutable rule set — stripped attributes, stripped elements, and structural rewriters —
applied before hashing. `CanonicalizationRules.ModelRules` is the audited library-wide rule set.
Its stripped attributes are: identity and display metadata (`Id`, `Name`, `Description` — two
objects with identical content and different ids must hash identically); the expanded
response-branch persistence addresses (`SelectedBranchId`/`SelectedBranchName`, the
source/secondary/hazard branch id-and-name pairs — persistence identity and migration metadata,
while the projected `SelectedBranchIdentity` token remains compute content); the axis labels
(`SpecifiedHazard`, `HazardUnit`, `TransformedHazard`, `TransformedHazardUnit`,
`SpecifiedConsequence`, `ConsequenceUnit` — units and type names, not math); the UI-envelope
attributes stripped defensively (`NameOnDisk`, `Guid`, canvas positions, chart settings — never
written by the model library); and `UseDefaults`, which only records who wrote the integration
settings while the settings themselves stay hashed. These fields serialize but never hash. Two container types deliberately
hash a **projected identity form** instead of their persisted element graph (`SystemComponent`
and `CompositeConsequence`): their persisted forms carry link Guids, names, or
`FunctionReference` markers that must never be a hash surface, so `RiskFunctionBase.CanonicalHash()`
is virtual for exactly these overrides. `EventTreeResponse` likewise projects its recursive
occurrence content (see [event-trees.md](event-trees.md)).

## `SeedHelpers` — the derivation primitives

| Member | Role |
|---|---|
| `HashCombine(seed, contentHash, index)` | Folds a base seed, a 32-byte canonical hash, and an occurrence/ordinal index into a derived seed — the one combining primitive every derivation uses |
| `ToPositiveSeed(seed)` | Maps a derived seed onto the strictly positive range Numerics samplers require (a Numerics `LatinHypercube` seed ≤ 0 would silently fall back to the wall clock) |
| `IndependentUniform(sampleSize, dimension, seed)` | The seeded independent-uniform matrix used by the plain Monte Carlo scheme |

The derivation walk: the analysis folds its `PRNGSeed` with each component's canonical hash and
**occurrence index** (identical-content components get distinct indices, so duplicates draw
independently); each component seeds its functions at their **sampler walk ordinals** (hazard,
then per mode the consequence-coupling positions and the chain functions); composite functions
seed each child from the child's own content hash and ordinal; the joint system's VEGAS stream
folds the system seed with a dedicated tag and the realization index. A shared live function
instance is one knowledge quantity — it is set up once and its stream reused wherever it is
wired.

## `SamplingScheme`

Every function pre-allocates an N×D percentile matrix in `SetupSampler(sampleSize, seed, scheme)`
(N realizations × the function's intrinsic sampling dimensions); the scheme controls how the
matrix is filled. Switching schemes is the only edit that changes Monte Carlo results without
changing the underlying math, so it is an explicit, hashed analysis option:

| Member | Behavior |
|---|---|
| `MonteCarlo` | Independent uniform draws — the legacy v1.0 behavior; SE ∝ 1/√N |
| `LatinHypercube` | Stratified with random placement within bins (unbiased) — the default; stratifying each marginal typically cuts variance 5–50× at the same realization count |
| `LatinHypercubeMedian` | Median bin centers; deterministic per seed, useful at very small realization counts |

## `SamplerSeedMap` — the seed-stable perturbation mode (§5.5.8)

Content-based seeding intentionally re-rolls a function's stream when its numeric content
changes — the right default for reproducibility, but noise on top of the signal in a perturbation
study. The seed-stable mode removes exactly that noise:

1. Run the baseline; `RiskAnalysis.CapturedSamplerSeeds` publishes a `SamplerSeedMap` — per
   component, the effective seed at every sampler walk ordinal, plus the joint VEGAS seed base.
2. Perturb the model, assign the captured map to `PinnedSamplerSeeds`, and re-run: the walk's
   seed scribe overrides each content-derived seed with the captured one, replaying identical
   Monte Carlo streams so result deltas are pure parameter effects.

Pinning operates at the **function-ordinal** level because a perturbed parameter moves that
function's hash — a pinned component seed alone would still re-roll its percentile matrix. A map
only fits the model shape it was captured from: a perturbation that changes the walk shape
(adding a mode, a function, or a consequence position) faults loudly. The map is runtime-only —
never serialized, never hashed.

**The pin removes seed noise only, never quadrature adaptivity.** The 1D adaptive mesh follows
the configured `RiskIntegrand` and the joint VEGAS integrand is inherently consequence-bearing,
so a perturbation that moves integrand values still shifts results within integration tolerance —
a deterministic parameter effect, not residual seed noise. Bit-identity fixtures pair the pin
with `RiskIntegrand.TotalProbabilityOfFailure` (1D, consequence perturbations) or a hashed-but-
integrand-inert edit.

## What the contract buys

- **Rename/reorder/metadata invariance**, pinned bit-exactly by
  [engine-reproducibility](../verification/engine-reproducibility.md) and the multi-component
  shuffle families.
- **Thread-count invariance** — same inputs + same seed → bit-identical results at any worker
  count, because every stream is pre-allocated from derived seeds and every reduction is
  deterministic.
- **Sensitivity without re-simulation** — the input columns of
  [sensitivity-analysis.md](sensitivity-analysis.md) re-derive bit-exactly from the same seeds.
