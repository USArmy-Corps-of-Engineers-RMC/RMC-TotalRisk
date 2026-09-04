using Numerics.Data;
using Numerics.Distributions;
using RMC.TotalRisk.Api.Configuration;
using RMC.TotalRisk.Api.DTOs;
using RMC.TotalRisk.Core.Enums;
using RMC.TotalRisk.Core.Interfaces;
using RMC.TotalRisk.RiskFunctions.Consequences;
using RMC.TotalRisk.RiskFunctions.Hazards;
using RMC.TotalRisk.RiskFunctions.Responses;

namespace RMC.TotalRisk.Api.Mappers
{
    /// <summary>
    /// Maps input-function DTOs to model-library function instances, reporting request-shape
    /// problems as structured issues that echo the offending values. Nothing is silently
    /// reordered, defaulted past an error, or clamped.
    /// </summary>
    public static class FunctionMapper
    {
        /// <summary>
        /// The maximum composite nesting depth accepted from a request — deep enough for any
        /// realistic exposure model, shallow enough to bound recursion.
        /// </summary>
        private const int MaxCompositeDepth = 8;

        /// <summary>
        /// Maps a tabular hazard DTO to a deterministic <see cref="TabularHazard"/>.
        /// </summary>
        /// <param name="dto">The hazard DTO.</param>
        /// <param name="path">The request object path for issue reporting.</param>
        /// <param name="limits">The host limits.</param>
        /// <param name="issues">The issue collector.</param>
        /// <returns>The hazard function, or null when the DTO fails request-shape checks.</returns>
        public static TabularHazard? ToHazard(TabularHazardDto dto, string path, ApiOptions limits, List<ValidationIssueDto> issues)
        {
            ArgumentNullException.ThrowIfNull(dto);
            ArgumentNullException.ThrowIfNull(limits);
            ArgumentNullException.ThrowIfNull(issues);

            if (!CheckType(dto.Type, FunctionTypeNames.TabularHazard, $"{path}.type", issues)) return null;

            bool tableOk = CheckTable(dto.ExceedanceProbabilities, dto.HazardValues,
                "exceedanceProbabilities", "hazardValues", path, limits, issues);
            if (tableOk)
            {
                tableOk &= CheckStrictOrder(dto.ExceedanceProbabilities, descending: true, "exceedanceProbabilities", path, issues);
                tableOk &= CheckStrictOrder(dto.HazardValues, descending: false, "hazardValues", path, issues);
                tableOk &= CheckRange01(dto.ExceedanceProbabilities, "exceedanceProbabilities", path, issues);
            }
            if (!tableOk) return null;

            var ordinates = new UncertainOrdinate[dto.ExceedanceProbabilities.Count];
            for (int i = 0; i < ordinates.Length; i++)
            {
                ordinates[i] = new UncertainOrdinate(dto.ExceedanceProbabilities[i], new Deterministic(dto.HazardValues[i]));
            }

            var hazard = new TabularHazard
            {
                Name = string.IsNullOrWhiteSpace(dto.Name) ? "Hazard" : dto.Name!,
                SpecifiedHazard = dto.SpecifiedHazard,
                HazardUnit = dto.HazardUnit,
                UncertaintyValue = FunctionUncertainty.None,
                NoUncertaintyFunction = new UncertainOrderedPairedData(ordinates,
                    true, SortOrder.Descending, true, SortOrder.Ascending, UnivariateDistributionType.Deterministic),
            };
            if (dto.HazardTransform.HasValue) hazard.HazardTransform = dto.HazardTransform.Value;
            if (dto.ProbabilityTransform.HasValue) hazard.ProbabilityTransform = dto.ProbabilityTransform.Value;
            if (dto.Extrapolation.HasValue) hazard.Extrapolation = dto.Extrapolation.Value;
            return hazard;
        }

        /// <summary>
        /// Maps a tabular response DTO to a deterministic <see cref="TabularResponse"/>.
        /// </summary>
        /// <param name="dto">The response DTO.</param>
        /// <param name="path">The request object path for issue reporting.</param>
        /// <param name="limits">The host limits.</param>
        /// <param name="hazardLabels">The component hazard's (label, unit) pair, inherited by blank labels.</param>
        /// <param name="defaultName">The name used when the DTO carries none.</param>
        /// <param name="issues">The issue collector.</param>
        /// <returns>The response function, or null when the DTO fails request-shape checks.</returns>
        public static TabularResponse? ToResponse(TabularResponseDto dto, string path, ApiOptions limits,
            (string Label, string Unit) hazardLabels, string defaultName, List<ValidationIssueDto> issues)
        {
            ArgumentNullException.ThrowIfNull(dto);
            ArgumentNullException.ThrowIfNull(limits);
            ArgumentNullException.ThrowIfNull(issues);

            if (!CheckType(dto.Type, FunctionTypeNames.TabularResponse, $"{path}.type", issues)) return null;

            bool tableOk = CheckTable(dto.HazardValues, dto.ResponseProbabilities,
                "hazardValues", "responseProbabilities", path, limits, issues);
            if (tableOk)
            {
                tableOk &= CheckStrictOrder(dto.HazardValues, descending: false, "hazardValues", path, issues);
                tableOk &= CheckRange01(dto.ResponseProbabilities, "responseProbabilities", path, issues);
            }
            if (!tableOk) return null;

            var ordinates = new UncertainOrdinate[dto.HazardValues.Count];
            for (int i = 0; i < ordinates.Length; i++)
            {
                ordinates[i] = new UncertainOrdinate(dto.HazardValues[i], new Deterministic(dto.ResponseProbabilities[i]));
            }

            var response = new TabularResponse
            {
                Name = string.IsNullOrWhiteSpace(dto.Name) ? defaultName : dto.Name!,
                SpecifiedHazard = string.IsNullOrWhiteSpace(dto.SpecifiedHazard) ? hazardLabels.Label : dto.SpecifiedHazard!,
                HazardUnit = string.IsNullOrWhiteSpace(dto.HazardUnit) ? hazardLabels.Unit : dto.HazardUnit!,
                // Non-strict Y (SortOrder.None) is mandatory: fragility curves legitimately
                // plateau at 0 and 1.
                UncertainOrderedPairedData = new UncertainOrderedPairedData(ordinates,
                    true, SortOrder.Ascending, false, SortOrder.None, UnivariateDistributionType.Deterministic),
            };
            if (dto.HazardTransform.HasValue) response.HazardTransform = dto.HazardTransform.Value;
            if (dto.ProbabilityTransform.HasValue) response.ProbabilityTransform = dto.ProbabilityTransform.Value;
            if (dto.Extrapolation.HasValue) response.Extrapolation = dto.Extrapolation.Value;
            return response;
        }

        /// <summary>
        /// Maps a consequence DTO — tabular or composite mixture — to a model consequence
        /// function.
        /// </summary>
        /// <param name="dto">The consequence DTO.</param>
        /// <param name="path">The request object path for issue reporting.</param>
        /// <param name="limits">The host limits.</param>
        /// <param name="hazardLabels">The component hazard's (label, unit) pair, inherited by blank labels.</param>
        /// <param name="declaredType">The declared consequence (label, unit) pair at the position this function fills, inherited by blank labels.</param>
        /// <param name="defaultName">The name used when the DTO carries none.</param>
        /// <param name="issues">The issue collector.</param>
        /// <returns>The consequence function, or null when the DTO fails request-shape checks.</returns>
        public static IConsequenceFunction? ToConsequence(ConsequenceFunctionDto dto, string path, ApiOptions limits,
            (string Label, string Unit) hazardLabels, (string Label, string Unit) declaredType, string defaultName,
            List<ValidationIssueDto> issues)
        {
            return ToConsequenceCore(dto, path, limits, hazardLabels, declaredType, defaultName, depth: 0, issues);
        }

        /// <summary>
        /// The recursive body behind <see cref="ToConsequence"/>, carrying the nesting depth.
        /// </summary>
        /// <param name="dto">The consequence DTO.</param>
        /// <param name="path">The request object path for issue reporting.</param>
        /// <param name="limits">The host limits.</param>
        /// <param name="hazardLabels">The inherited hazard (label, unit) pair.</param>
        /// <param name="declaredType">The inherited consequence (label, unit) pair.</param>
        /// <param name="defaultName">The name used when the DTO carries none.</param>
        /// <param name="depth">The current composite nesting depth.</param>
        /// <param name="issues">The issue collector.</param>
        /// <returns>The consequence function, or null on request-shape failure.</returns>
        private static IConsequenceFunction? ToConsequenceCore(ConsequenceFunctionDto dto, string path, ApiOptions limits,
            (string Label, string Unit) hazardLabels, (string Label, string Unit) declaredType, string defaultName,
            int depth, List<ValidationIssueDto> issues)
        {
            ArgumentNullException.ThrowIfNull(dto);
            ArgumentNullException.ThrowIfNull(limits);
            ArgumentNullException.ThrowIfNull(issues);

            string type = string.IsNullOrWhiteSpace(dto.Type) ? FunctionTypeNames.TabularConsequence : dto.Type!;
            if (string.Equals(type, FunctionTypeNames.TabularConsequence, StringComparison.Ordinal))
            {
                return ToTabularConsequence(dto, path, limits, hazardLabels, declaredType, defaultName, issues);
            }
            if (string.Equals(type, FunctionTypeNames.CompositeMixture, StringComparison.Ordinal))
            {
                return ToCompositeMixture(dto, path, limits, hazardLabels, declaredType, defaultName, depth, issues);
            }

            issues.Add(ValidationIssueDto.ApiError("API_FUNCTION_TYPE_UNSUPPORTED",
                $"'{dto.Type}' is not a supported consequence function type. Accepted values: {FunctionTypeNames.TabularConsequence}, {FunctionTypeNames.CompositeMixture}.",
                $"{path}.type"));
            return null;
        }

        /// <summary>
        /// Maps the tabular branch of a consequence DTO.
        /// </summary>
        /// <param name="dto">The consequence DTO (tabular kind).</param>
        /// <param name="path">The request object path.</param>
        /// <param name="limits">The host limits.</param>
        /// <param name="hazardLabels">The inherited hazard (label, unit) pair.</param>
        /// <param name="declaredType">The inherited consequence (label, unit) pair.</param>
        /// <param name="defaultName">The name used when the DTO carries none.</param>
        /// <param name="issues">The issue collector.</param>
        /// <returns>The tabular consequence, or null on request-shape failure.</returns>
        private static TabularConsequence? ToTabularConsequence(ConsequenceFunctionDto dto, string path, ApiOptions limits,
            (string Label, string Unit) hazardLabels, (string Label, string Unit) declaredType, string defaultName,
            List<ValidationIssueDto> issues)
        {
            bool tableOk = CheckTable(dto.HazardValues, dto.ConsequenceValues,
                "hazardValues", "consequenceValues", path, limits, issues);
            if (tableOk)
            {
                tableOk &= CheckStrictOrder(dto.HazardValues!, descending: false, "hazardValues", path, issues);
            }
            if (!tableOk) return null;

            var ordinates = new UncertainOrdinate[dto.HazardValues!.Count];
            for (int i = 0; i < ordinates.Length; i++)
            {
                ordinates[i] = new UncertainOrdinate(dto.HazardValues[i], new Deterministic(dto.ConsequenceValues![i]));
            }

            var consequence = new TabularConsequence
            {
                Name = string.IsNullOrWhiteSpace(dto.Name) ? defaultName : dto.Name!,
                SpecifiedHazard = string.IsNullOrWhiteSpace(dto.SpecifiedHazard) ? hazardLabels.Label : dto.SpecifiedHazard!,
                HazardUnit = string.IsNullOrWhiteSpace(dto.HazardUnit) ? hazardLabels.Unit : dto.HazardUnit!,
                SpecifiedConsequence = string.IsNullOrWhiteSpace(dto.SpecifiedConsequence) ? declaredType.Label : dto.SpecifiedConsequence!,
                ConsequenceUnit = string.IsNullOrWhiteSpace(dto.ConsequenceUnit) ? declaredType.Unit : dto.ConsequenceUnit!,
                UncertainOrderedPairedData = new UncertainOrderedPairedData(ordinates,
                    true, SortOrder.Ascending, false, SortOrder.None, UnivariateDistributionType.Deterministic),
            };
            if (dto.HazardTransform.HasValue) consequence.HazardTransform = dto.HazardTransform.Value;
            if (dto.ConsequenceTransform.HasValue) consequence.ConsequenceTransform = dto.ConsequenceTransform.Value;
            if (dto.Extrapolation.HasValue) consequence.Extrapolation = dto.Extrapolation.Value;
            return consequence;
        }

        /// <summary>
        /// Maps the composite-mixture branch of a consequence DTO (e.g., day/night exposure
        /// weights) to a <see cref="CompositeConsequence"/> in Mixture mode.
        /// </summary>
        /// <param name="dto">The consequence DTO (composite kind).</param>
        /// <param name="path">The request object path.</param>
        /// <param name="limits">The host limits.</param>
        /// <param name="hazardLabels">The inherited hazard (label, unit) pair.</param>
        /// <param name="declaredType">The inherited consequence (label, unit) pair.</param>
        /// <param name="defaultName">The name used when the DTO carries none.</param>
        /// <param name="depth">The current composite nesting depth.</param>
        /// <param name="issues">The issue collector.</param>
        /// <returns>The composite consequence, or null on request-shape failure.</returns>
        private static CompositeConsequence? ToCompositeMixture(ConsequenceFunctionDto dto, string path, ApiOptions limits,
            (string Label, string Unit) hazardLabels, (string Label, string Unit) declaredType, string defaultName,
            int depth, List<ValidationIssueDto> issues)
        {
            if (depth >= MaxCompositeDepth)
            {
                issues.Add(ValidationIssueDto.ApiError("API_COMPOSITE_TOO_DEEP",
                    $"Composite consequence nesting exceeds the maximum depth of {MaxCompositeDepth}.", path));
                return null;
            }
            if (dto.Branches == null || dto.Branches.Count == 0)
            {
                issues.Add(ValidationIssueDto.ApiError("API_COMPOSITE_BRANCHES_REQUIRED",
                    "A compositeMixture consequence requires at least one weighted branch.", $"{path}.branches"));
                return null;
            }

            string name = string.IsNullOrWhiteSpace(dto.Name) ? defaultName : dto.Name!;
            string specifiedHazard = string.IsNullOrWhiteSpace(dto.SpecifiedHazard) ? hazardLabels.Label : dto.SpecifiedHazard!;
            string hazardUnit = string.IsNullOrWhiteSpace(dto.HazardUnit) ? hazardLabels.Unit : dto.HazardUnit!;
            string specifiedConsequence = string.IsNullOrWhiteSpace(dto.SpecifiedConsequence) ? declaredType.Label : dto.SpecifiedConsequence!;
            string consequenceUnit = string.IsNullOrWhiteSpace(dto.ConsequenceUnit) ? declaredType.Unit : dto.ConsequenceUnit!;

            double weightSum = 0d;
            bool branchesOk = true;
            var entries = new List<WeightedConsequenceFunction>(dto.Branches.Count);
            for (int i = 0; i < dto.Branches.Count; i++)
            {
                var branch = dto.Branches[i];
                string branchPath = $"{path}.branches[{i}]";
                if (branch == null)
                {
                    issues.Add(ValidationIssueDto.ApiError("API_COMPOSITE_BRANCH_REQUIRED",
                        "The branch entry is null.", branchPath));
                    branchesOk = false;
                    continue;
                }
                if (double.IsNaN(branch.Weight) || branch.Weight < 0d || branch.Weight > 1d)
                {
                    issues.Add(ValidationIssueDto.ApiError("API_MIXTURE_WEIGHT_RANGE",
                        $"The branch weight must be in [0, 1] (got {branch.Weight}).", $"{branchPath}.weight"));
                    branchesOk = false;
                }
                weightSum += branch.Weight;

                var childLabels = (specifiedConsequence, consequenceUnit);
                var child = ToConsequenceCore(branch.Function, $"{branchPath}.function", limits,
                    (specifiedHazard, hazardUnit), childLabels, $"{name} Branch {i + 1}", depth + 1, issues);
                if (child == null)
                {
                    branchesOk = false;
                    continue;
                }
                entries.Add(new WeightedConsequenceFunction(child, branch.Weight));
            }

            if (branchesOk && Math.Abs(weightSum - 1d) > 1e-6)
            {
                issues.Add(ValidationIssueDto.ApiError("API_MIXTURE_WEIGHTS_SUM",
                    $"The branch weights must sum to 1 (got {weightSum}).", $"{path}.branches"));
                branchesOk = false;
            }
            if (!branchesOk) return null;

            return new CompositeConsequence(entries)
            {
                Name = name,
                SpecifiedHazard = specifiedHazard,
                HazardUnit = hazardUnit,
                SpecifiedConsequence = specifiedConsequence,
                ConsequenceUnit = consequenceUnit,
                CompositeFunctionType = CompositeFunctionType.Mixture,
            };
        }

        /// <summary>
        /// Checks the type discriminator against the single accepted value, reporting a
        /// structured issue on mismatch. A null or blank discriminator is accepted as the
        /// default.
        /// </summary>
        /// <param name="actual">The discriminator carried by the DTO.</param>
        /// <param name="expected">The accepted discriminator.</param>
        /// <param name="path">The request object path of the type field.</param>
        /// <param name="issues">The issue collector.</param>
        /// <returns>True when accepted.</returns>
        private static bool CheckType(string? actual, string expected, string path, List<ValidationIssueDto> issues)
        {
            if (string.IsNullOrWhiteSpace(actual) || string.Equals(actual, expected, StringComparison.Ordinal)) return true;
            issues.Add(ValidationIssueDto.ApiError("API_FUNCTION_TYPE_UNSUPPORTED",
                $"'{actual}' is not a supported function type here. Accepted value: {expected}.", path));
            return false;
        }

        /// <summary>
        /// Checks the presence, alignment, minimum length, host cap, and finiteness of a paired
        /// table, reporting structured issues that echo the offending values.
        /// </summary>
        /// <param name="xValues">The X array.</param>
        /// <param name="yValues">The Y array.</param>
        /// <param name="xName">The X field's wire name.</param>
        /// <param name="yName">The Y field's wire name.</param>
        /// <param name="path">The owning function's request object path.</param>
        /// <param name="limits">The host limits.</param>
        /// <param name="issues">The issue collector.</param>
        /// <returns>True when the table passes every check.</returns>
        private static bool CheckTable(List<double>? xValues, List<double>? yValues, string xName, string yName,
            string path, ApiOptions limits, List<ValidationIssueDto> issues)
        {
            bool ok = true;
            if (xValues == null || xValues.Count == 0)
            {
                issues.Add(ValidationIssueDto.ApiError("API_TABLE_REQUIRED", $"The {xName} array is required.", $"{path}.{xName}"));
                ok = false;
            }
            if (yValues == null || yValues.Count == 0)
            {
                issues.Add(ValidationIssueDto.ApiError("API_TABLE_REQUIRED", $"The {yName} array is required.", $"{path}.{yName}"));
                ok = false;
            }
            if (!ok) return false;

            if (xValues!.Count != yValues!.Count)
            {
                issues.Add(ValidationIssueDto.ApiError("API_TABLE_LENGTH_MISMATCH",
                    $"The {xName} and {yName} arrays must be the same length (got {xValues.Count} and {yValues.Count}).", path));
                return false;
            }
            if (xValues.Count < 2)
            {
                issues.Add(ValidationIssueDto.ApiError("API_TABLE_TOO_SHORT",
                    $"A table requires at least two ordinates (got {xValues.Count}).", path));
                return false;
            }
            if (xValues.Count > limits.MaxOrdinatesPerTable)
            {
                issues.Add(ValidationIssueDto.ApiError("API_TABLE_TOO_LONG",
                    $"The table exceeds the host limit of {limits.MaxOrdinatesPerTable} ordinates (got {xValues.Count}).", path));
                return false;
            }
            for (int i = 0; i < xValues.Count; i++)
            {
                if (!double.IsFinite(xValues[i]))
                {
                    issues.Add(ValidationIssueDto.ApiError("API_TABLE_NONFINITE",
                        $"{xName}[{i}] is not finite (got {xValues[i]}).", $"{path}.{xName}"));
                    ok = false;
                }
                if (!double.IsFinite(yValues[i]))
                {
                    issues.Add(ValidationIssueDto.ApiError("API_TABLE_NONFINITE",
                        $"{yName}[{i}] is not finite (got {yValues[i]}).", $"{path}.{yName}"));
                    ok = false;
                }
            }
            return ok;
        }

        /// <summary>
        /// Checks strict monotonic order, reporting the first offending adjacent pair.
        /// </summary>
        /// <param name="values">The array to check.</param>
        /// <param name="descending">True to require strictly descending order; false for strictly ascending.</param>
        /// <param name="fieldName">The field's wire name.</param>
        /// <param name="path">The owning function's request object path.</param>
        /// <param name="issues">The issue collector.</param>
        /// <returns>True when strictly ordered.</returns>
        private static bool CheckStrictOrder(List<double> values, bool descending, string fieldName,
            string path, List<ValidationIssueDto> issues)
        {
            for (int i = 1; i < values.Count; i++)
            {
                bool violates = descending ? values[i] >= values[i - 1] : values[i] <= values[i - 1];
                if (violates)
                {
                    string direction = descending ? "strictly descending" : "strictly ascending";
                    issues.Add(ValidationIssueDto.ApiError("API_TABLE_ORDER",
                        $"The {fieldName} array must be {direction}: {fieldName}[{i - 1}] = {values[i - 1]} then {fieldName}[{i}] = {values[i]}.",
                        $"{path}.{fieldName}"));
                    return false;
                }
            }
            return true;
        }

        /// <summary>
        /// Checks that every value lies in [0, 1], reporting the first offender.
        /// </summary>
        /// <param name="values">The array to check.</param>
        /// <param name="fieldName">The field's wire name.</param>
        /// <param name="path">The owning function's request object path.</param>
        /// <param name="issues">The issue collector.</param>
        /// <returns>True when every value is in range.</returns>
        private static bool CheckRange01(List<double> values, string fieldName, string path, List<ValidationIssueDto> issues)
        {
            for (int i = 0; i < values.Count; i++)
            {
                if (values[i] < 0d || values[i] > 1d)
                {
                    issues.Add(ValidationIssueDto.ApiError("API_TABLE_RANGE",
                        $"{fieldName}[{i}] must be in [0, 1] (got {values[i]}).", $"{path}.{fieldName}"));
                    return false;
                }
            }
            return true;
        }
    }
}
