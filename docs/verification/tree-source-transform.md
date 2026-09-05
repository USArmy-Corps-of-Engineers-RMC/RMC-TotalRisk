# Transform- and axis-mapped tree probability sources

**Test class:** `TreeSourceTransformVerification` · **Tests:** 6 · **Run of record:** 2026-09-05, isolated run, ✅ all passed

## Scope and status

`TreeSourceTransformVerification` verifies the hazard-transform seat on `ProbabilitySource` —
the optional ordered chain that lets an event-tree chance node or fault-tree basic event be
keyed on a derived hazard axis (overtopping depth, duration, warning time) rather than on the
tree's driving hazard — and the bivariate surface axis it enables: with a declared
`BivariateSourceAxis` and a chain supplying the other coordinate, a bivariate response surface
is evaluated as the slice `p(h) = S(h, t(h))` (or `S(t(h), h)`), retiring the former
bivariate-source refusal without the silent weight collapse it guarded against. The chain and
the axis are conditional serialized content: absent, every existing source keeps a
byte-identical form, canonical identity, and seed (the eight perf byte gates carry the
full-scale proof, F5/F7 the tree tripwires).

Run in isolation:

```powershell
dotnet test src/RMC.TotalRisk.Verification -- --filter "ClassName~TreeSourceTransformVerification"
```

Observed 2026-09-05: **6/6 passed**.

## The oracles

| Test | Independent expectation | Result |
|---|---|---|
| Event-tree chain reconstruction (N = 1,024) | A two-transform uncertain chain over an uncertain t-space table is re-derived from independently constructed samplers at the documented content-seed recipe — the class consumes one child-stream ordinal, chain entry *i* forks from that base with the transform's content hash and position — and every branch probability equals the manual `CurveSample(p).GetYFromX(t₂(t₁(h)))` composition | Bit-exact at every (realization × hazard) |
| Pre-transformed equivalence (dyadic) | A table authored at t-knots `{2, 3}` with probabilities `{0.25, 0.75}` behind the affine map `t(h) = 2 + 0.5·h` equals a direct table authored at the composed ordinates `{0.25, 0.5, 0.75}` — at the response surface and behind mean-only engine runs with identical hazards and consequences | Bit-exact (the dyadic fixture makes both interpolation paths compute identical doubles) |
| Fault-tree inclusion–exclusion (N = 512) | `OR(AND(transformed, 0.3), 0.15)` matches the independently composed `p₃ + p₁p₂ − p₁p₂p₃` with `p₁` re-derived through the live map at the parent matrix's copied chain percentile (bit-equal to the clone stream by the flattening contract) | Within `1e-14` absolute (operation-order roundoff between the decision diagram and the composed formula) |
| Bivariate-axis slice engine twin | A surface source with the tree hazard on the primary axis and an affine chain supplying the secondary coordinate equals a twin authored directly as the univariate slice at the same tree levels, behind mean-only engine runs | Bit-exact on annual failure probability and expected consequences |
| Full-run reproducibility + configuration reach | Two re-authored full-uncertainty twins (a transformed event-tree source referencing a house-event fault tree) publish byte-identical results JSON; `MeasureConfigurationRisk` reaches the house event through the transformed source, bit-equal to the re-authored configured twin | Byte-identical JSON; bit-equal configured probability |
| Conditional-presence pin | A transform-free tree component's serialized form carries no `HazardTransforms` child and no `BivariateAxis` attribute anywhere | Confirmed (the byte gates prove the full-scale identity) |

The fast suite adds the seat contracts: constructor guards, dimension and determinism folds,
two-mode round trips with resolver repair and the unresolved-reference sink, identity movement
(chain content, chain order, and the axis move the token; renames and serialization mode never
do), the validation matrix (scalar-with-chain, misplaced axis, bivariate-in-chain, the
axis-without-chain and axis-on-univariate refusals, the transformed table's freedom from
tree-axis alignment, label-continuity warnings), the aligned-realization refusal, fragment
snapshots, kitchen-sink hash-invariance registration, live-edit plan invalidation through the
new transform fingerprint bucket, and the epistemic containment gates (a chain-carried
epistemic composite transform and an external-transfer-carried epistemic composite are both
refused by the exact logic-tree enumerator and the mean-only blend gate).

## Conventions and limitations

- **Chain application splits by evaluation mode.** Mean and percentile evaluation applies the
  live chain inside the source (mean curves, or one consistent percentile — the established
  co-monotonic convention); realization mode applies the sampler-bound transform clones at the
  tree dispatch seats, and the aligned-index realization lookup throws for a transformed source
  because `t(h)` is off-axis by construction.
- **A transformed table always interpolates.** The aligned fast path exists only for
  untransformed tables bit-aligned to the owner axis; a transformed table is authored freely on
  the derived axis and every lookup runs through the curve-sample interpolation, inheriting the
  established off-axis endpoint behavior.
- **Seeds move only for transformed sources.** The chain enters the source's projected identity
  as ordered transform content hashes, so configuring, editing, or reordering a chain
  deliberately re-rolls the owning tree's streams, while every chainless source and model is
  byte-identical — pinned by the conditional-presence tests and the eight perf gates.
- **One chain per source.** A bivariate-axis source maps one coordinate from the tree hazard
  and one through the chain; a surface with both axes independently transformed is a recorded
  boundary, not a supported shape.
- **The fault inclusion–exclusion comparison is tolerance-grade by design** — the frozen
  decision diagram orders its floating-point operations differently from the composed formula;
  the transformed probability inside it is still reconstructed bit-exactly.
