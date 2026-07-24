# Cascading Response End States

The mathematics of Phase 6.7 (arch doc §7.9, ratified 2026-07-23/24): multi-stage response
chains as chance-node paths, consequence terminals as end states, mutually-exclusive state
groups, and the across-group combination semantics. This page is the compute-side companion to
the arch doc's design section; the executable evidence is
[verification/cascade-end-states.md](../verification/cascade-end-states.md).

## 1. The leaf algebra

A component's risk diagram is an event tree: each response element is a chance node whose branch
probability is hazard-dependent — the sampled fragility `pᵢ(sᵢ)` on the **Fail** port (output
port 0, the implied v1.0 port) and `1 − pᵢ(sᵢ)` on the **Non-Fail** port (output port 1) — where
`sᵢ` is the stage-transformed signal (transforms between stages remap it; responses are
signal-transparent). Every consequence terminal projects one failure mode = one **end state**
whose weight is the polarity product along its path,

```
w_s(h) = ∏ᵢ ( πᵢ = Fail ? pᵢ(sᵢ(h)) : 1 − pᵢ(sᵢ(h)) ),
```

with each stage's `BranchPolarity` read from the exit port the path uses (serialized
resolved-on-write; a single-stage Fail mode reproduces the pre-6.7 arithmetic bit-identically —
the product's lone factor multiplies 1.0 exactly).

The **leaf signature** is the ordered (response occurrence ordinal, polarity) pair sequence.
Terminals sharing their first response element with *distinct* signatures diverge at a shared
chance node via opposite ports, so their events are disjoint by construction: they form one
**mutually-exclusive state group** whose total mass is an exact sum (no inclusion–exclusion).
Duplicate and prefix-nested signatures leave the partition and combine as standalone units under
the ambient method (the Q2 ruling — exactly the legacy fan-out semantics, with an advisory
warning and the mass-balance witness reporting any double count honestly). The structural
derivation lives in `EndStateGroupLayout`; every pre-6.7 model produces the trivial layout, whose
kernels are byte-for-byte the pre-cascade paths (proven by the bit-identical results byte gates).

## 2. Classification — final polarity

An end state is a **failure state** iff its final stage polarity is Fail (§7.9.2, user-ratified):
the failure union, APF, Fail stream, f-N surface, and contribution diagnostics count Fail-final
states only, so the breach convention is preserved — in the progression example the union is
`p₁p₂`, and wiring a partial-damage terminal never changes it. A Non-Fail-final terminal is a
**claimed non-failure state**: the direct wiring of branch-scoped non-failure consequences.

## 3. The claimed complement (§7.9.5)

With `U(h)` the across-group combined failure union and `C(h) = 1 − U(h)` the complement, a
claimed state `s′` in group `g` carries the conditional share

```
q_{s′}(h) = w_{s′}(h) / (1 − P_g(h)),      P_g = Σ_{s ∈ g, failure} w_s ,
```

exact under independent groups (`C·q = w_{s′} · ∏_{g′≠g}(1 − P_{g′})`) and the documented
convention under the mutually-exclusive, common-cause, and competing adjustments. The
**complement mixture** — remainder-scaled background branches plus q-scaled claimed branches —
is one distribution with three consumers: the non-failure scalar (the conditional mean), the
joint method's excess pair baseline, and the Background/NonFail/Total recording (Total stays
exhaustive at one). Claimed states are scoped to **one group per component** (validation error
otherwise): with two claiming groups the exact decomposition needs a cross-product complement
enumeration plus a non-failure combination rule — deliberately deferred.

## 4. Excess pairing (§7.9.4)

A failure state's mode-scope excess partner is the terminal whose signature matches with the
final polarity flipped — the exact "last response held" counterfactual (`R2-Fail` excess
= `C_full − C_partial`) — falling back to the component background mode when the sibling is
unwired or the branch continues (v1.0 parity). The partner's consequences sample at the failure
state's Q-N coupling percentile: the pairing rides the existing shared-draw construct unchanged.
The joint method's component-scope excess pairs the complement mixture (the v1.0 "no-failure
world" baseline, generalized); the per-mode methods' component excess is the sibling-paired
mode excess.

## 5. Across-group combination (§7.9.6)

The `FailureModeMethod` operates on the combination-unit failure-mass vector `{P_g}` — exclusive
groups plus standalone failure states; the combination caches, the multivariate normal, and the
correlation matrix take the **unit count** as their dimension (the failure-path count for every
pre-6.7 layout):

- **Joint failures:** the pathway decomposition (independent / perfectly-positive /
  `ExclusivePCM` under the Gaussian copula) runs over the unit masses; within a pathway each
  participating unit's odometer digit iterates its concatenated (state, branch) entries at
  conditional weight `w_s·branchWeight / P_g`, so exclusive members stay disjoint within a unit
  while weights multiply across units. The Shapley probability split lands on each unit's picked
  state — summed over tuples a unit's share distributes across members by conditional mass, so
  every Σ-identity holds at every scope.
- **Mutually exclusive / common cause:** the normalization and the common-cause factor compute
  over `{P_g}`; the adjusted unit mass distributes back to member states conditionally.
- **Competing (weak-link):** the cumulative-incidence preprocessing builds per-unit mass curves
  `P_g(h)` over the 200 stratified levels. Legal iff every failure state carries an all-Fail
  signature — a product of non-decreasing fragilities is non-decreasing, so the ascending curve
  construction holds; an **else-chain** failure state (a Fail-final state riding a Non-Fail
  branch, weight e.g. `(1 − p₁)p₂`) is not monotone in the hazard, no weak-link ordering exists,
  and validation errors (§7.9.6). The telescoping-union relaxation (claimed leaf sets whose union
  is an increasing event) is documented, not implemented.

Guardrails count within-unit branches **additively** and across-unit branches
**multiplicatively** (`Π_g (1 + Σ_{s∈g} bᵢ) − 1` for the joint entry estimate).

## 6. Knowledge sampling (user directive 2026-07-24)

Knowledge uncertainty samples **independently across all functions** — each cascade stage's
transforms and response are seeded at their own walk ordinals with their own content seeds, so
equal-content stages draw independent curves like any other functions. The two deliberate
exceptions: (1) the Q-N failure/non-failure **consequence** pairing (one shared coupling
percentile per mode per type — the §7.9.4 sibling resolution rides it unchanged), and (2) one
**shared response instance** wired into sibling end states is one knowledge quantity — its Fail
and Non-Fail branches must ride the same sampled curve so the partition `p + (1 − p) = 1` holds
realization by realization. Pinned by
`SampledFailureModeTests.Test_MultiStage_ResponseKnowledge_IndependentPerStage`.

## 7. Non-monotone state weights

Individual end-state weights (and else-chain group masses) are legitimately non-monotone in the
hazard under Joint/ME/CCA — the pointwise algebra needs no monotonicity, only the competing
method does. Consequently `SampledFailureMode.InverseSRP` stays exact for single-stage
Fail-polarity modes only and throws for cascades (no engine path consumes it), and a cascade
state's cumulative failure profile can rise and fall — expected behavior, not a defect.

## 8. What a cascade changes in the results surface

One `FailureModeRealization` per terminal was already the end-state scope; Phase 6.7 adds the
stamped end-state `Name` (terminal-first label chain) and the append-only `PathLabel` branch
descriptor (`"Initiation[Fail] → Progression[NonFail]"`) on the realization and summary mode
scopes. A claimed state's mode-scope curves record its conditional complement entries into its
**NonFail** stream (Fail/Excess stay empty — it is not a failure); the component Background
stream becomes the complement-conditional mixture when claimed states exist (bit-identical to
the raw background otherwise).
