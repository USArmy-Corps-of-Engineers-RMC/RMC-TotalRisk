# Parametric common-cause failure groups

**Test class:** `FaultTreeCcfVerification` · **Tests:** 4 · **Run of record:** 2026-09-05, isolated run, ✅ all passed

## Scope and status

`FaultTreeCcfVerification` verifies the fault-tree common-cause failure groups
(`FaultTreeCcfGroup`): beta-factor, multiple Greek letter, and alpha-factor parameterizations
mapped through one per-multiplicity factor kernel, expanded at compile time into derived
independent and common-cause events over one shared exchangeable basis draw, and evaluated by
the existing exact decision diagram unchanged. Groups are conditional serialized and identity
content — the eight perf byte gates (F7 the fault tripwire) prove every group-free tree
byte-identical.

Run in isolation:

```powershell
dotnet test src/RMC.TotalRisk.Verification -- --filter "ClassName~FaultTreeCcfVerification"
```

Observed 2026-09-05: **4/4 passed**.

## The oracles

| Test | Independent expectation | Result |
|---|---|---|
| Alpha-factor exhaustive enumeration | A three-member group under `OR(AND(A, B), C, D)` with the multiplicity split re-derived independently from the published non-staggered formulas (`Q_k = k·α_k/(C(n−1,k−1)·α_t)·Q`), every state of the eight-event derived product space enumerated exactly | Within `1e-14` at every hazard (operation-order roundoff between the enumeration and the decision diagram) |
| Greek-letter union closed form + rare-event doctrine | The exact three-member union `1 − (1−Q₁)³(1−Q₂)³(1−Q₃)` with the independently computed split, and at `Q = 10⁻⁸` the first-order coefficient `3f₁ + 3f₂ + f₃ = 2.825` — each common event fires the union once, so the grouped union sits below three independent totals | Closed form within `1e-14`; the coefficient within `1e-6`; the common-cause discount confirmed |
| Beta/Greek-letter facade identity | For a two-member group both facades compute identical factor doubles, so their expanded responses agree curve for curve | Bit-identical |
| Engine closed form, importance, reproducibility | The mean-only annual failure probability of a flat grouped And pair equals `βQ + (1 − βQ)((1−β)Q)²` exactly; the node-importance sweep reports exactly one uncertain entry (the shared basis); two re-authored full-uncertainty twins publish byte-identical results | Closed form within `1e-12`; one knowledge quantity; byte-identical JSON |

The fast suite adds the seat contracts: the factor kernel's closed forms and the exact
per-member identity `Σ C(n−1,k−1)·f_k = 1` for all three models, the loud invalid-configuration
throws, the group configuration-error matrix (member count/resolution/kind, duplicate and
cross-group membership, per-model parameter rules, the content-identical exchangeability
requirement), the exact beta And/Or closed forms through the diagram, the one-shared-draw
sampling pin (`SamplingDimensions` = 1 with realization-exact manual composition), the hash
lifecycle (add moves, remove restores bit-exactly, metadata inert, parameters compute
content), conditional serialized presence with grouped round trips, derived-event cut-set
naming, live-edit plan invalidation, the blocking invalid-group diagnostics, and the
partial-context coverage refusal.

## Conventions and limitations

- **The exact engine computes the exact Boolean union of the derived events**, not the
  rare-event sum the published parameter estimates are usually quoted against; the two agree
  to first order (pinned) and the union is never larger.
- **One shared basis draw per group per independent context** — declaring the group is the
  exchangeability statement, so member sources must be content-identical and the group
  registers as a single knowledge quantity in importance sweeps. A member set split across
  independent contexts (through independent-clone transfers) cannot carry the coupling and is
  refused loudly.
- **The alpha-factor mapping is the non-staggered convention**; the staggered-testing variant
  is a recorded boundary for a future append-only model member.
- Published worked-example tables can be pinned as additional constants when a reference
  document is put in the repository's hands; the exhaustive enumeration and the independent
  closed forms carry the correctness claim in the meantime.
