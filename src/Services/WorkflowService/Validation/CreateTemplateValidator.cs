using ServiceDefaults.Security;
using WorkflowService.Endpoints;

namespace WorkflowService.Validation;

/// <summary>
/// Checks a workflow template definition before it is stored. A step assigned to a role that Identity can
/// never issue would be a step nobody can ever approve, so roles are validated against <see cref="Roles"/>.
/// </summary>
internal static class CreateTemplateValidator
{
    /// <returns>Field-name to messages map; empty when the request is valid.</returns>
    public static Dictionary<string, string[]> Validate(WorkflowEndpoints.CreateTemplateRequest request)
    {
        var errors = new Dictionary<string, string[]>();

        if (string.IsNullOrWhiteSpace(request.Name))
        {
            errors["name"] = ["Name is required."];
        }

        if (request.Steps is null || request.Steps.Count == 0)
        {
            errors["steps"] = ["At least one step is required."];
            return errors;
        }

        for (var i = 0; i < request.Steps.Count; i++)
        {
            var step = request.Steps[i];
            var stepErrors = new List<string>();

            if (string.IsNullOrWhiteSpace(step.StepName))
            {
                stepErrors.Add("StepName is required.");
            }

            if (!Roles.IsKnown(step.AssignedRole))
            {
                stepErrors.Add($"AssignedRole '{step.AssignedRole}' is not a known role. Valid roles: {string.Join(", ", Roles.All)}.");
            }

            if (step.DueInDays < 0)
            {
                stepErrors.Add("DueInDays cannot be negative.");
            }

            if (stepErrors.Count > 0)
            {
                errors[$"steps[{i}]"] = stepErrors.ToArray();
            }
        }

        return errors;
    }
}
