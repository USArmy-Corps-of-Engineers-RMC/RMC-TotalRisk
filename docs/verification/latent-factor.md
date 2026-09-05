# Latent-factor capacity dependence

**Test class:** `LatentFactorVerification` · **Tests:** 5 · **Run of record:** 2026-09-05, isolated run, ✅ all passed

## Scope and status

`LatentFactorVerification` verifies the `DependencyType.LatentFactors` failure-mode dependency
mode: named latent factors with one loading per combination unit inducing the between-unit
correlation ρij = Σf λif·λjf (unit diagonal assigned exactly, off-diagonals accumulated in
declared factor order), back-filled into the correlation-matrix field and consumed by the
existing Gaussian combination kernels — the product-of-conditional-marginals joint-failures
path, the competing-risks cumulative-incidence pre-processing, and the common-cause
adjustment — with zero changes to any kernel. The loadings serialize and hash as a conditional
child only under the latent-factors mode, so every other component keeps a byte-identical
form, canonical identity, and seed.

Run in isolation:

```powershell
dotnet test src/RMC.TotalRisk.Verification -- --filter "ClassName~LatentFactorVerification"
```

Observed 2026-09-05: **5/5 passed** (≈ 11 s).

## The dense-equivalence contract

A latent-factors model and a correlation-matrix twin authored with the identically computed
induced matrix feed the same kernels the same doubles. The primary oracle is therefore
**bit-exact rather than statistical**: on the deterministic three-mode Bucket-1 joint model
(the twins' canonical hashes deliberately differ — selecting the mode is the hash event — but
deterministic functions are seed-free, and the published mean pass consumes the mean
surfaces), the summary means, standard deviations, tail measures, and every mean
loss-exceedance ordinate must agree bit for bit.

## The engine's dependent-union approximation, measured

The joint-failures dependent path evaluates the Gaussian-copula union through the
product-of-conditional-marginals (PCM) recursion over pairwise bivariate normal probabilities,
not the exact orthant, and the lazy exclusive enumeration converges within its documented 1e-4
tolerance. The exchangeable test isolates the dependence model with constant
(hazard-independent) failure probabilities — the annualized failure probability then equals
the between-mode union exactly — and anchors three quantities on the pinned fixture (four
units, common loading λ = 0.7, p = {0.05, 0.10, 0.15, 0.08}):

| Anchor | Comparison | Allowance |
|---|---|---|
| Exact vs exact | test-local Simpson factor integral vs `Probability.UnionSingleFactor(p, λ²)` | 5e-8 relative (two independent exact evaluations) |
| Engine vs PCM | engine union vs the directly evaluated `Probability.UnionPCM` | 2e-4 absolute (the lazy enumeration's convergence tolerance) |
| Engine vs exact | engine union vs the factor integral | 2.5e-3 relative — headroom over the measured PCM error of 8.4e-4 relative (exact 0.259005, PCM 0.258787), the honest statement that the shipped dependent-union kernel is an approximation of the exact orthant |

## Results by test

| Test | Anchor | Result |
|---|---|---|
| Dense equivalence | bit-identity against the correlation-matrix twin on the deterministic Bucket-1 model | bit-equal on every compared scalar and both LEC surfaces |
| Exchangeable union | the exact one-factor integral (test-local quadrature, cross-anchored to `UnionSingleFactor`) | engine within the measured PCM allowance; see the table above |
| Multi-factor Monte Carlo | 1,000,000 trials of U_i = Σf λif·z_f + √(1 − Σf λif²)·ε_i from one MersenneTwister(12345) stream (factor draws first, then one idiosyncratic draw per unit), three units under two factors | engine union within 4·SE + the PCM allowance |
| Competing at three units | 1,000,000-trial weakest-exceeded-wins oracle (the legacy competing convention with factor-model capacity draws); D = 3 routes the engine's cumulative-incidence pre-processing through the seeded Genz lattice — the dimension at which a seeding regression is detectable | failure union and mean loss streams within 4·SE + the competing family's 0.3% cumulative-incidence allowance |
| Reproducibility and inertness | full-uncertainty runs (the mean-only default switched off) | repeated runs and component/factor renames bit-identical; a factor reorder re-rolls the content-derived component seed and moves the sampled ensemble (declared order is hashed content) |

## Boundaries recorded

Cross-component capacity factors (dependence between components still enters only through
their hazards), the conditional-independence evaluation path that would exploit the factor
structure directly (`Probability.UnionSingleFactor` is its upstream seed), and the Vanmarcke
correlation-length derivation of loadings from geometry are recorded future work — see
[failure-mode-combination.md](../technical-reference/failure-mode-combination.md) §5.1.
