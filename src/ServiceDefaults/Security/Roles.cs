namespace ServiceDefaults.Security;

/// <summary>
/// Single source of truth for role names. Identity seeds and assigns these; every other service
/// authorizes against them, so a role referenced in one service can never silently drift from the
/// roles Identity can actually issue.
/// </summary>
public static class Roles
{
    public const string SystemAdmin = "SystemAdmin";
    public const string OrganizationAdmin = "OrganizationAdmin";
    public const string ComplianceOfficer = "ComplianceOfficer";
    public const string SecurityAuditor = "SecurityAuditor";
    public const string Attorney = "Attorney";
    public const string Paralegal = "Paralegal";
    public const string Client = "Client";
    public const string User = "User";

    /// <summary>Comma-separated form accepted by <c>AuthorizeAttribute.Roles</c> (caller needs any one of them).</summary>
    public const string Admins = SystemAdmin + "," + OrganizationAdmin;

    /// <summary>Every role Identity seeds. Workflow templates may only assign steps to one of these.</summary>
    public static readonly IReadOnlyList<string> All =
    [
        SystemAdmin, OrganizationAdmin, ComplianceOfficer, SecurityAuditor,
        Attorney, Paralegal, Client, User
    ];

    public static bool IsKnown(string? role) => role is not null && All.Contains(role, StringComparer.Ordinal);
}
