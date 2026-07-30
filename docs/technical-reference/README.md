# Technical Reference

Per-family mathematical documentation for the model library, grounded in the published RMC reports — RMC-TR-2022-XX [*Quantitative Risk Analysis with RMC-TotalRisk*](https://usace-rmc.github.io/RMC-Software-Documentation/source-documents/desktop-applications/rmc-totalrisk/technical-reference-manual/RMC-TotalRisk-Technical-Reference-Manual.pdf) and *Verification of the RMC-TotalRisk Software* (2024). Every page describes the family's mathematics in full, and its API code blocks are kept in lock-step with the model library's public surface.

Pages (mirroring the model-library namespaces):

| Page | Contents |
|---|---|
| [hazard-functions.md](hazard-functions.md) | Hazard (frequency) functions: tabular uncertainty modes, parametric bootstrap, posterior import, composite mixtures |
| [transform-functions.md](transform-functions.md) | Transform (composition) functions: tabular, closed-form linear and power, composite weighted average |
| [response-functions.md](response-functions.md) | System response (fragility) functions: R-S formulation, tabular, parametric, non-fail sentinel, composites, event-tree responses; links to the normative event/fault-tree design |
| [consequence-functions.md](consequence-functions.md) | Consequence (damage) functions: tabular, parametric power model, composite exposure mixtures, incremental-consequence coupling |
| [risk-integration.md](risk-integration.md) | The risk engine's numerical integration: Adaptive Gauss–Kronrod (1D), the `RiskIntegrand` refinement-objective enum, VEGAS with power-transform tail focus |
| [loss-exceedance-curves.md](loss-exceedance-curves.md) | LEC / F-N construction, probability-mass accounting, stable weighted moments, the full risk-measure catalog (VaR/CVaR/assurance), FFT system convolution |
| [risk-contribution.md](risk-contribution.md) | % contribution to risk: the Shapley probability split + consequence-proportional risk split over the exclusive failure events of all four combination methods, the additive-system Poisson-binomial closed form, exact sum identities, two percentage bases |
| [composite-functions.md](composite-functions.md) | Composite functions across all four clusters: mixture versus competing risks, the aleatory/epistemic decision rule for the weights, why `CompositeTransform` is weighted-average only (with the Jensen-bias worked example), and what is deferred |
| [cascading-end-states.md](cascading-end-states.md) | Cascading response end states (the event tree in the risk diagram): the polarity-product leaf algebra, final-polarity classification, the claimed complement mixture, sibling excess pairing, across-unit combination with the narrow competing gate, and the knowledge-sampling contract |
| [sensitivity-analysis.md](sensitivity-analysis.md) | The unified sensitivity engine: stored-results correlation with bit-exact seed rederivation, the input-column sampler walk, the association-measure catalog, and the hazard-level tornado |

Planned pages: event trees, canonical hashing / content-based seeding / sampling schemes, and the results catalog.
