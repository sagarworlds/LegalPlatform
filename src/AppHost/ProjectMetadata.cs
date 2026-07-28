using System;
using System.IO;
using Aspire.Hosting;

namespace Projects;

public class IdentityService : IProjectMetadata
{
    public string ProjectPath => Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../Services/IdentityService/IdentityService.csproj"));
}

public class UserService : IProjectMetadata
{
    public string ProjectPath => Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../Services/UserService/UserService.csproj"));
}

public class OrganizationService : IProjectMetadata
{
    public string ProjectPath => Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../Services/OrganizationService/OrganizationService.csproj"));
}

public class DocumentService : IProjectMetadata
{
    public string ProjectPath => Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../Services/DocumentService/DocumentService.csproj"));
}

public class CaseService : IProjectMetadata
{
    public string ProjectPath => Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../Services/CaseService/CaseService.csproj"));
}

public class WorkflowService : IProjectMetadata
{
    public string ProjectPath => Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../Services/WorkflowService/WorkflowService.csproj"));
}

public class NotificationService : IProjectMetadata
{
    public string ProjectPath => Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../Services/NotificationService/NotificationService.csproj"));
}

public class SearchService : IProjectMetadata
{
    public string ProjectPath => Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../Services/SearchService/SearchService.csproj"));
}

public class ReportingService : IProjectMetadata
{
    public string ProjectPath => Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../Services/ReportingService/ReportingService.csproj"));
}

public class AuditService : IProjectMetadata
{
    public string ProjectPath => Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../Services/AuditService/AuditService.csproj"));
}

public class Gateway : IProjectMetadata
{
    public string ProjectPath => Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../Gateway/Gateway.csproj"));
}
