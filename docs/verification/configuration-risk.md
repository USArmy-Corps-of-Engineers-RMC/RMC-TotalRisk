# Configuration risk

**Test class:** `ConfigurationRiskVerification` · **Tests:** 4 · **Run of record:** 2026-09-03, isolated run, ✅ all passed

## Scope and status

`ConfigurationRiskVerification` verifies `RiskAnalysis.MeasureConfigurationRisk` — the
runtime-only query answering "what is the risk of this system with named fault-tree house
events held at specified states," such as a spillway gate out of service. The query clones the
components self-contained, applies `HouseEventState(functionId, nodeId, state)` overrides
through a containment walk (element-assigned functions, external transfer targets,
tree-referenced responses, structural links, and composite-response children), refuses any
override that matches no house event, and runs two mean-only quantifications — the unmodified
baseline and the configured system — publishing unpersisted system and per-component rows of
annual failure probability and per-consequence-type expected annual consequences with their
changes and ratios (the ratio is NaN at a zero baseline, the established convention). The
authored model, its published results, and its estimated state are never touched, and nothing
is serialized, hashed, or seed-affecting.

Run in isolation:

```powershell
dotnet test src/RMC.TotalRisk.Verification -- --filter "ClassName~ConfigurationRiskVerification"
```

Observed 2026-09-03: **4/4 passed**.

## The structural oracle

A configured clone is content-identical to a model authored directly with the same house-event
states, and both quantifications are mean-only and therefore deterministic. The primary oracle
is consequently **bit-exact rather than statistical**: the query's baseline numbers must equal
an independent mean-only run of an unmodified twin, and its configured numbers an independent
mean-only run of a re-authored configured twin, at system and component scope, for both the
annual failure probability and the expected annual consequences.

| Test | Independent expectation | Result |
|---|---|---|
| Two-component re-authored parity | Baseline and configured system/component failure probabilities and expected consequences equal independent runs of re-authored twins; the component without the house event is unchanged | Bit-equal on every slot; the untouched component's changes are exactly zero |
| Flat-response closed form | `OR(AND(house, 0.9), 0.2)`: baseline probability `0.2`, configured `1 − (1 − 0.9)(1 − 0.2) = 0.92`, change `0.72`, ratio `4.6`; the identical `4.6` on the consequence ratio because a flat response factors out of the consequence integral | Exact within `1e-12` (ratio `1e-10`) |
| Event-tree-carried reach | A house event inside a fault tree referenced as an event-tree chance probability source is reconfigured; the configured value equals the re-authored nested variant | Bit-equal; the applied-override label names the carried event |
| Author full-run inertness | After a 200-realization full run, the query leaves the published ensemble JSON, the estimated state, the component canonical hash, and the authored house state untouched | Byte-identical |

The fast suite adds the input-record and container contracts, the refusal matrix (null, empty,
duplicate, and unmatched overrides — an unreachable override throws rather than being silently
ignored), the no-op configuration reproducing the baseline exactly, the external-transfer
nested reach, the unestimated-author path, and the applied-override display labels.

## Conventions and limitations

- The query is deliberately **mean-only**: a configured house state is compute content, so a
  configured realization ensemble would re-roll every content-derived seed and mix stream noise
  into the difference. The deterministic re-quantification is the "risk right now" answer; the
  configured-ensemble extension is recorded future work.
- Every live instance of one function id receives the override, so a stored function reused
  across components is reconfigured everywhere — the physical reading of one piece of
  equipment.
- The verified fixtures use flat conditional responses so the closed forms are exact; the
  bit-exact re-authored parity does not depend on that flatness.
- The engine rejects the exact certain-failure response (a flat system response probability of
  one) loudly at integration for any model, independent of this query; a configuration forcing
  certain failure therefore fails the configured run with the engine's integration diagnostic
  rather than returning a number. The observation is recorded in the remaining-work map.
- The verification fixtures declare the primary consequence type only; the per-type output
  loop is exercised structurally and reads the same published mean surfaces.
