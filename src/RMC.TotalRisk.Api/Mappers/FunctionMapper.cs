using Numerics.Data;
using Numerics.Distributions;
using RMC.TotalRisk.Api.Configuration;
using RMC.TotalRisk.Api.DTOs;
using RMC.TotalRisk.Core.Enums;
using RMC.TotalRisk.Core.Interfaces;
using RMC.TotalRisk.RiskFunctions.Consequences;
using RMC.TotalRisk.RiskFunctions.Hazards;
using RMC.TotalRisk.RiskFunctions.Responses;
using RMC.TotalRisk.RiskFunctions.Transforms;

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
        /// Maps a failure mode's ordered hazard-to-response transform chain, threading each
        /// entry's output labels into the next entry's blank input labels.
        /// </summary>
        /// <param name="dtos">The transform DTOs in chain order; null or empty maps to an empty chain.</param>
        /// <param name="path">The chain's request object path.</param>
        /// <param name="limits">The host limits.</param>
        /// <param name="hazardLabels">The component hazard's (label, unit) pair — the first entry's inherited input labels.</param>
        /// <param name="ownerName">The owning failure mode's name, used for default function names.</param>
        /// <param name="issues">The issue collector.</param>
        /// <returns>The mapped chain (empty for a null or empty DTO list), or null when any entry fails request-shape checks.</returns>
        public static List<ITransformFunction>? ToTransforms(List<TransformFunctionDto>? dtos, string path,
            ApiOptions limits, (string Label, string Unit) hazardLabels, string ownerName,
            List<ValidationIssueDto> issues)
        {
            ArgumentNullException.ThrowIfNull(limits);
            ArgumentNullException.ThrowIfNull(issues);

            var functions = new List<ITransformFunction>(dtos?.Count ?? 0);
            if (dtos == null || dtos.Count == 0) return functions;

            bool ok = true;
            var incoming = hazardLabels;
            for (int j = 0; j < dtos.Count; j++)
            {
                string entryPath = $"{path}[{j}]";
                if (dtos[j] == null)
                {
                    issues.Add(ValidationIssueDto.ApiError("API_TRANSFORM_REQUIRED",
                        "The transform entry is null.", entryPath));
                    ok = false;
                    continue;
                }
                var function = ToTransform(dtos[j], entryPath, limits, incoming,
                    $"{ownerName} Transform {j + 1}", issues);
                if (function == null)
                {
                    ok = false;
                    continue;
                }
                functions.Add(function);
                incoming = (function.TransformedHazard, function.TransformedHazardUnit);
            }
            return ok ? functions : null;
        }

        /// <summary>
        /// Maps one transform DTO — tabular, linear, or power — to a model transform function.
        /// </summary>
        /// <remarks>
        /// Request-shape checks (tables, required output labels, required clamp bounds) are
        /// reported here with request-relative paths; parameter-value rules (power alpha and
        /// beta ranges, non-finite coefficients, interpolation-space guards) and every
        /// label-continuity warning are deliberately left to the model's own validation, which
        /// flows through the analysis validation gate.
        /// </remarks>
        /// <param name="dto">The transform DTO.</param>
        /// <param name="path">The entry's request object path.</param>
        /// <param name="limits">The host limits.</param>
        /// <param name="incomingLabels">The incoming signal's (label, unit) pair, inherited by blank input labels.</param>
        /// <param name="defaultName">The name used when the DTO carries none.</param>
        /// <param name="issues">The issue collector.</param>
        /// <returns>The transform function, or null on request-shape failure.</returns>
        private static ITransformFunction? ToTransform(TransformFunctionDto dto, string path, ApiOptions limits,
            (string Label, string Unit) incomingLabels, string defaultName, List<ValidationIssueDto> issues)
        {
            string type = string.IsNullOrWhiteSpace(dto.Type) ? FunctionTypeNames.TabularTransform : dto.Type!;
            if (string.Equals(type, FunctionTypeNames.TabularTransform, StringComparison.Ordinal))
            {
                return ToTabularTransform(dto, path, limits, incomingLabels, defaultName, issues);
            }
            if (string.Equals(type, FunctionTypeNames.LinearTransform, StringComparison.Ordinal))
            {
                return ToLinearTransform(dto, path, incomingLabels, defaultName, issues);
            }
            if (string.Equals(type, FunctionTypeNames.PowerTransform, StringComparison.Ordinal))
            {
                return ToPowerTransform(dto, path, incomingLabels, defaultName, issues);
            }

            issues.Add(ValidationIssueDto.ApiError("API_FUNCTION_TYPE_UNSUPPORTED",
                $"'{dto.Type}' is not a supported transform function type. Accepted values: {FunctionTypeNames.TabularTransform}, {FunctionTypeNames.LinearTransform}, {FunctionTypeNames.PowerTransform}.",
                $"{path}.type"));
            return null;
        }

        /// <summary>
        /// Maps the tabular branch of a transform DTO to a deterministic
        /// <see cref="TabularTransform"/>.
        /// </summary>
        /// <param name="dto">The transform DTO (tabular kind).</param>
        /// <param name="path">The entry's request object path.</param>
        /// <param name="limits">The host limits.</param>
        /// <param name="incomingLabels">The incoming signal's (label, unit) pair, inherited by blank input labels.</param>
        /// <param name="defaultName">The name used when the DTO carries none.</param>
        /// <param name="issues">The issue collector.</param>
        /// <returns>The tabular transform, or null on request-shape failure.</returns>
        private static TabularTransform? ToTabularTransform(TransformFunctionDto dto, string path, ApiOptions limits,
            (string Label, string Unit) incomingLabels, string defaultName, List<ValidationIssueDto> issues)
        {
            bool ok = CheckTransformOutputLabels(dto, path, issues);
            bool tableOk = CheckTable(dto.HazardValues, dto.TransformedHazardValues,
                "hazardValues", "transformedHazardValues", path, limits, issues);
            if (tableOk)
            {
                tableOk &= CheckStrictOrder(dto.HazardValues!, descending: false, "hazardValues", path, issues);
            }
            if (!ok || !tableOk) return null;

            var ordinates = new UncertainOrdinate[dto.HazardValues!.Count];
            for (int i = 0; i < ordinates.Length; i++)
            {
                ordinates[i] = new UncertainOrdinate(dto.HazardValues[i], new Deterministic(dto.TransformedHazardValues![i]));
            }

            var transform = new TabularTransform
            {
                Name = string.IsNullOrWhiteSpace(dto.Name) ? defaultName : dto.Name!,
                SpecifiedHazard = string.IsNullOrWhiteSpace(dto.SpecifiedHazard) ? incomingLabels.Label : dto.SpecifiedHazard!,
                HazardUnit = string.IsNullOrWhiteSpace(dto.HazardUnit) ? incomingLabels.Unit : dto.HazardUnit!,
                TransformedHazard = dto.TransformedHazard!,
                TransformedHazardUnit = dto.TransformedHazardUnit!,
                // Non-strict Y (SortOrder.None) is mandatory: conversion tables legitimately
                // plateau (e.g., a gated rating curve).
                UncertainOrderedPairedData = new UncertainOrderedPairedData(ordinates,
                    true, SortOrder.Ascending, false, SortOrder.None, UnivariateDistributionType.Deterministic),
            };
            if (dto.HazardTransform.HasValue) transform.HazardTransform = dto.HazardTransform.Value;
            if (dto.TransformedHazardTransform.HasValue) transform.TransformTransform = dto.TransformedHazardTransform.Value;
            if (dto.Extrapolation.HasValue) transform.Extrapolation = dto.Extrapolation.Value;
            return transform;
        }

        /// <summary>
        /// Maps the linear branch of a transform DTO to a deterministic
        /// <see cref="LinearTransform"/> (Y = alpha + beta·X over the clamp range).
        /// </summary>
        /// <param name="dto">The transform DTO (linear kind).</param>
        /// <param name="path">The entry's request object path.</param>
        /// <param name="incomingLabels">The incoming signal's (label, unit) pair, inherited by blank input labels.</param>
        /// <param name="defaultName">The name used when the DTO carries none.</param>
        /// <param name="issues">The issue collector.</param>
        /// <returns>The linear transform, or null on request-shape failure.</returns>
        private static LinearTransform? ToLinearTransform(TransformFunctionDto dto, string path,
            (string Label, string Unit) incomingLabels, string defaultName, List<ValidationIssueDto> issues)
        {
            bool ok = CheckTransformOutputLabels(dto, path, issues);
            ok &= CheckTransformRange(dto.Minimum, dto.Maximum, path, issues);
            if (!ok) return null;

            return new LinearTransform
            {
                Name = string.IsNullOrWhiteSpace(dto.Name) ? defaultName : dto.Name!,
                SpecifiedHazard = string.IsNullOrWhiteSpace(dto.SpecifiedHazard) ? incomingLabels.Label : dto.SpecifiedHazard!,
                HazardUnit = string.IsNullOrWhiteSpace(dto.HazardUnit) ? incomingLabels.Unit : dto.HazardUnit!,
                TransformedHazard = dto.TransformedHazard!,
                TransformedHazardUnit = dto.TransformedHazardUnit!,
                Alpha = dto.Alpha ?? 0d,
                Beta = dto.Beta ?? 1d,
                IsUncertain = false,
                Minimum = dto.Minimum!.Value,
                Maximum = dto.Maximum!.Value,
            };
        }

        /// <summary>
        /// Maps the power branch of a transform DTO to a deterministic
        /// <see cref="PowerTransform"/> (Y = alpha·(X − xi)^beta over the clamp range).
        /// </summary>
        /// <param name="dto">The transform DTO (power kind).</param>
        /// <param name="path">The entry's request object path.</param>
        /// <param name="incomingLabels">The incoming signal's (label, unit) pair, inherited by blank input labels.</param>
        /// <param name="defaultName">The name used when the DTO carries none.</param>
        /// <param name="issues">The issue collector.</param>
        /// <returns>The power transform, or null on request-shape failure.</returns>
        private static PowerTransform? ToPowerTransform(TransformFunctionDto dto, string path,
            (string Label, string Unit) incomingLabels, string defaultName, List<ValidationIssueDto> issues)
        {
            bool ok = CheckTransformOutputLabels(dto, path, issues);
            ok &= CheckTransformRange(dto.Minimum, dto.Maximum, path, issues);
            if (!ok) return null;

            return new PowerTransform
            {
                Name = string.IsNullOrWhiteSpace(dto.Name) ? defaultName : dto.Name!,
                SpecifiedHazard = string.IsNullOrWhiteSpace(dto.SpecifiedHazard) ? incomingLabels.Label : dto.SpecifiedHazard!,
                HazardUnit = string.IsNullOrWhiteSpace(dto.HazardUnit) ? incomingLabels.Unit : dto.HazardUnit!,
                TransformedHazard = dto.TransformedHazard!,
                TransformedHazardUnit = dto.TransformedHazardUnit!,
                Alpha = dto.Alpha ?? 1d,
                Beta = dto.Beta ?? 1.5d,
                Xi = dto.Xi ?? 0d,
                IsInverse = dto.IsInverse ?? false,
                IsUncertain = false,
                Minimum = dto.Minimum!.Value,
                Maximum = dto.Maximum!.Value,
            };
        }

        /// <summary>
        /// Checks that a transform DTO declares both output-axis labels, reporting a structured
        /// issue per missing label. Output labels are required because they define the axis the
        /// next chain entry or the response reads.
        /// </summary>
        /// <param name="dto">The transform DTO.</param>
        /// <param name="path">The entry's request object path.</param>
        /// <param name="issues">The issue collector.</param>
        /// <returns>True when both output labels are present.</returns>
        private static bool CheckTransformOutputLabels(TransformFunctionDto dto, string path, List<ValidationIssueDto> issues)
        {
            bool ok = true;
            if (string.IsNullOrWhiteSpace(dto.TransformedHazard))
            {
                issues.Add(ValidationIssueDto.ApiError("API_LABEL_REQUIRED",
                    "The transform's output hazard type label (transformedHazard) is required; it defines the axis the next function reads.",
                    $"{path}.transformedHazard"));
                ok = false;
            }
            if (string.IsNullOrWhiteSpace(dto.TransformedHazardUnit))
            {
                issues.Add(ValidationIssueDto.ApiError("API_LABEL_REQUIRED",
                    "The transform's output hazard unit label (transformedHazardUnit) is required.",
                    $"{path}.transformedHazardUnit"));
                ok = false;
            }
            return ok;
        }

        /// <summary>
        /// Checks a linear or power transform's clamp bounds: both must be supplied (the model's
        /// own default range of [0, 100] would silently clamp realistic hazard domains, so the
        /// contract refuses to guess), finite, and correctly ordered.
        /// </summary>
        /// <param name="minimum">The requested lower bound.</param>
        /// <param name="maximum">The requested upper bound.</param>
        /// <param name="path">The entry's request object path.</param>
        /// <param name="issues">The issue collector.</param>
        /// <returns>True when the bounds pass every check.</returns>
        private static bool CheckTransformRange(double? minimum, double? maximum, string path, List<ValidationIssueDto> issues)
        {
            bool ok = true;
            if (!minimum.HasValue)
            {
                issues.Add(ValidationIssueDto.ApiError("API_TRANSFORM_RANGE_REQUIRED",
                    "The transform's minimum evaluation bound is required: inputs clamp to [minimum, maximum], and the model's own default range is [0, 100].",
                    $"{path}.minimum"));
                ok = false;
            }
            if (!maximum.HasValue)
            {
                issues.Add(ValidationIssueDto.ApiError("API_TRANSFORM_RANGE_REQUIRED",
                    "The transform's maximum evaluation bound is required: inputs clamp to [minimum, maximum], and the model's own default range is [0, 100].",
                    $"{path}.maximum"));
                ok = false;
            }
            if (!ok) return false;

            double min = minimum!.Value;
            double max = maximum!.Value;
            if (!double.IsFinite(min) || !double.IsFinite(max) || min >= max)
            {
                issues.Add(ValidationIssueDto.ApiError("API_TRANSFORM_RANGE_INVALID",
                    $"The transform's evaluation bounds must be finite with minimum < maximum (got {min} and {max}).",
                    path));
                return false;
            }
            return true;
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
