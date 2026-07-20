using Microsoft.VisualStudio.TestTools.UnitTesting;

// Shared by RMC.TotalRisk.Verification and (via a linked Compile item) RMC.TotalRisk.Tests.
// Method-level parallelization: tests must be independent of execution order and share no
// mutable static state.
[assembly: Parallelize(Scope = ExecutionScope.MethodLevel)]
