using RMC.TotalRisk.Api.Configuration;
using RMC.TotalRisk.Api.DTOs;
using RMC.TotalRisk.Core.Enums;
using RMC.TotalRisk.Core.Interfaces;
using RMC.TotalRisk.Systems.Components;

namespace RMC.TotalRisk.Api.Mappers
{
    /// <summary>
    /// Maps component DTOs to <see cref="SystemComponent"/> instances through the model
    /// library's chain-style authoring surface: the hazard function on the graph root, one
    /// failure mode per response/consequence pair, and the non-fail consequences on the
    /// component's single response-free path.
    /// </summary>
    public static class ComponentMapper
    {
        /// <summary>
        /// Maps a component DTO to a <see cref="SystemComponent"/>.
        /// </summary>
        /// <param name="dto">The component DTO.</param>
        /// <param name="index">The component's index in the request, used in issue paths.</param>
        /// <param name="limits">The host limits.</param>
        /// <param name="declaredTypes">The analysis's declared consequence types (label, unit), entry 0 the primary.</param>
        /// <param name="issues">The issue collector.</param>
        /// <returns>The component, or null when the DTO fails request-shape checks.</returns>
        public static SystemComponent? ToComponent(ComponentDto dto, int index, ApiOptions limits,
            IReadOnlyList<(string Label, string Unit)> declaredTypes, List<ValidationIssueDto> issues)
        {
            ArgumentNullException.ThrowIfNull(dto);
            ArgumentNullException.ThrowIfNull(limits);
            ArgumentNullException.ThrowIfNull(declaredTypes);
            ArgumentNullException.ThrowIfNull(issues);

            string path = $"components[{index}]";
            bool ok = true;

            if (string.IsNullOrWhiteSpace(dto.Name))
            {
                issues.Add(ValidationIssueDto.ApiError("API_NAME_REQUIRED",
                    "The component name is required.", $"{path}.name"));
                ok = false;
            }
            if (dto.FailureModes.Count == 0 && dto.NonFailConsequences.Count == 0)
            {
                issues.Add(ValidationIssueDto.ApiError("API_COMPONENT_EMPTY",
                    "A component must define at least one failure mode or one non-fail consequence.", path));
                ok = false;
            }

            var hazard = FunctionMapper.ToHazard(dto.Hazard, $"{path}.hazard", limits, issues);
            if (hazard == null || !ok) return null;
            var hazardLabels = (hazard.SpecifiedHazard, hazard.HazardUnit);

            var component = new SystemComponent { Name = dto.Name };
            component.HazardFunction = hazard;

            for (int m = 0; m < dto.FailureModes.Count; m++)
            {
                var mode = dto.FailureModes[m];
                string modePath = $"{path}.failureModes[{m}]";
                if (mode == null)
                {
                    issues.Add(ValidationIssueDto.ApiError("API_FAILURE_MODE_REQUIRED",
                        "The failure mode entry is null.", modePath));
                    ok = false;
                    continue;
                }
                if (string.IsNullOrWhiteSpace(mode.Name))
                {
                    issues.Add(ValidationIssueDto.ApiError("API_NAME_REQUIRED",
                        "The failure mode name is required.", $"{modePath}.name"));
                    ok = false;
                    continue;
                }

                var response = FunctionMapper.ToResponse(mode.Response, $"{modePath}.response", limits,
                    hazardLabels, $"{mode.Name} Response", issues);
                var consequences = MapConsequenceList(mode.Consequences, $"{modePath}.consequences", limits,
                    hazardLabels, declaredTypes, mode.Name, requireOnePerType: true, issues);
                if (response == null || consequences == null)
                {
                    ok = false;
                    continue;
                }

                component.AddFailureMode(new FailureMode(
                    new List<ResponseStage> { new ResponseStage(new List<ITransformFunction>(), response) },
                    null, consequences));
            }

            if (dto.NonFailConsequences.Count > 0)
            {
                var nonFail = MapConsequenceList(dto.NonFailConsequences, $"{path}.nonFailConsequences", limits,
                    hazardLabels, declaredTypes, "Non-Failure", requireOnePerType: true, issues);
                if (nonFail == null)
                {
                    ok = false;
                }
                else
                {
                    component.AddFailureMode(new FailureMode(
                        new List<ResponseStage> { new ResponseStage(new List<ITransformFunction>(), null) },
                        null, nonFail));
                }
            }

            // Dependency before method: the common-cause and mutually-exclusive methods carry no
            // dependence model, and the method setter coerces the dependency to independent —
            // matching the model's own invariant. A discarded explicit dependency is reported.
            if (dto.FailureModeDependency.HasValue) component.FailureModeDependency = dto.FailureModeDependency.Value;
            if (dto.FailureModeMethod.HasValue) component.FailureModeMethod = dto.FailureModeMethod.Value;
            if (dto.FailureModeDependency.HasValue && dto.FailureModeDependency.Value != DependencyType.Independent
                && component.FailureModeDependency == DependencyType.Independent
                && dto.FailureModeDependency.Value != component.FailureModeDependency)
            {
                issues.Add(new ValidationIssueDto
                {
                    Code = "API_DEPENDENCY_COERCED",
                    Severity = DiagnosticSeverity.Warning,
                    Message = $"The '{component.FailureModeMethod}' method carries no dependence model; the requested failure-mode dependency '{dto.FailureModeDependency.Value}' was coerced to independent.",
                    ObjectPath = $"{path}.failureModeDependency",
                });
            }
            if (dto.JointConsequences.HasValue) component.JointConsequences = dto.JointConsequences.Value;
            if (dto.HazardThreshold.HasValue) component.HazardThreshold = dto.HazardThreshold.Value;

            if (dto.FailureModeCorrelationMatrix != null)
            {
                if (component.FailureModeDependency == DependencyType.CorrelationMatrix)
                {
                    var matrix = ToMatrix(dto.FailureModeCorrelationMatrix, $"{path}.failureModeCorrelationMatrix", issues);
                    if (matrix == null) ok = false;
                    else component.CorrelationMatrix = matrix;
                }
                else
                {
                    issues.Add(new ValidationIssueDto
                    {
                        Code = "API_MATRIX_IGNORED",
                        Severity = DiagnosticSeverity.Warning,
                        Message = "A failure-mode correlation matrix was supplied but the failure-mode dependency is not 'correlationMatrix'; the matrix was ignored.",
                        ObjectPath = $"{path}.failureModeCorrelationMatrix",
                    });
                }
            }

            return ok ? component : null;
        }

        /// <summary>
        /// Maps an ordered consequence-function list against the declared consequence-type axis.
        /// </summary>
        /// <param name="dtos">The consequence DTOs, index-aligned with the declared types.</param>
        /// <param name="path">The list's request object path.</param>
        /// <param name="limits">The host limits.</param>
        /// <param name="hazardLabels">The inherited hazard (label, unit) pair.</param>
        /// <param name="declaredTypes">The declared consequence types, entry 0 the primary.</param>
        /// <param name="ownerName">The owning failure mode's (or non-fail path's) name, used for default function names.</param>
        /// <param name="requireOnePerType">Whether the list length must equal the declared type count.</param>
        /// <param name="issues">The issue collector.</param>
        /// <returns>The mapped list, or null when any entry fails request-shape checks.</returns>
        private static List<IConsequenceFunction>? MapConsequenceList(List<ConsequenceFunctionDto> dtos, string path,
            ApiOptions limits, (string Label, string Unit) hazardLabels,
            IReadOnlyList<(string Label, string Unit)> declaredTypes, string ownerName, bool requireOnePerType,
            List<ValidationIssueDto> issues)
        {
            if (requireOnePerType && dtos.Count != declaredTypes.Count)
            {
                issues.Add(ValidationIssueDto.ApiError("API_CONSEQUENCE_COUNT",
                    $"Expected one consequence function per declared consequence type ({declaredTypes.Count}); got {dtos.Count}.",
                    path));
                return null;
            }

            bool ok = true;
            var functions = new List<IConsequenceFunction>(dtos.Count);
            for (int i = 0; i < dtos.Count; i++)
            {
                string entryPath = $"{path}[{i}]";
                if (dtos[i] == null)
                {
                    issues.Add(ValidationIssueDto.ApiError("API_CONSEQUENCE_REQUIRED",
                        "The consequence entry is null.", entryPath));
                    ok = false;
                    continue;
                }
                var declared = i < declaredTypes.Count ? declaredTypes[i] : (string.Empty, string.Empty);
                string defaultName = i == 0 ? ownerName
                    : string.IsNullOrWhiteSpace(declared.Item1) ? $"{ownerName} {i + 1}" : $"{ownerName} - {declared.Item1}";
                var function = FunctionMapper.ToConsequence(dtos[i], entryPath, limits, hazardLabels, declared,
                    defaultName, issues);
                if (function == null)
                {
                    ok = false;
                    continue;
                }
                functions.Add(function);
            }
            return ok ? functions : null;
        }

        /// <summary>
        /// Converts a row-list matrix payload to a rectangular array, reporting ragged rows.
        /// </summary>
        /// <param name="rows">The matrix rows.</param>
        /// <param name="path">The matrix's request object path.</param>
        /// <param name="issues">The issue collector.</param>
        /// <returns>The rectangular matrix, or null when the payload is empty or ragged.</returns>
        public static double[,]? ToMatrix(List<List<double>> rows, string path, List<ValidationIssueDto> issues)
        {
            ArgumentNullException.ThrowIfNull(rows);
            ArgumentNullException.ThrowIfNull(issues);

            if (rows.Count == 0)
            {
                issues.Add(ValidationIssueDto.ApiError("API_MATRIX_EMPTY", "The matrix has no rows.", path));
                return null;
            }
            int columns = rows[0]?.Count ?? 0;
            for (int r = 0; r < rows.Count; r++)
            {
                if (rows[r] == null || rows[r].Count != columns)
                {
                    issues.Add(ValidationIssueDto.ApiError("API_MATRIX_RAGGED",
                        $"Matrix row {r} has {rows[r]?.Count ?? 0} entries; expected {columns}.", path));
                    return null;
                }
            }
            var matrix = new double[rows.Count, columns];
            for (int r = 0; r < rows.Count; r++)
            {
                for (int c = 0; c < columns; c++)
                {
                    matrix[r, c] = rows[r][c];
                }
            }
            return matrix;
        }
    }
}
