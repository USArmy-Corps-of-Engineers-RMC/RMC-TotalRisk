# Legacy event-tree verification fixtures

`LegacyEventTreeTemplates.xml` contains two exact-content recursive `<Node>` roots extracted from the
shipped template collection at:

`C:\GIT\RMC-TotalRisk-Dev\RMC-TotalRisk\RMC-TotalRisk\Resources\TreeTemplates.xml`

The source was audited on 2026-07-28. The copied roots are:

- `Basic`: the exact compact `Ordinate`/`Uniform` shape used by
  `Test_TotalRisk\Test_EventTree.vb::TestIO` through `InitiatingNode.TemplateBasic()`.
- `Concrete Dam Gate Failure`: a recursive template containing the shipped `SingleValue`
  all-hazards distribution plus modern-form `MultiValue` triangular tables and explicit
  remainders.

Whitespace was reformatted only. Attribute names, values, node IDs, topology, source content, and
template metadata are unchanged. Tests load this file from the verification output directory; the
model library never performs file I/O.
