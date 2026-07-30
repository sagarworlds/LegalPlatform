var builder = DistributedApplication.CreateBuilder(args);

// Core infrastructure containers
var redis = builder.AddRedis("redis");
var rabbitmq = builder.AddRabbitMQ("rabbitmq");
var postgres = builder.AddPostgres("postgres")
    .WithPgAdmin();

// MinIO S3 Object Storage container defined as a generic container
var minio = builder.AddContainer("minio", "minio/minio")
    .WithImageTag("latest")
    .WithArgs("server", "/data", "--console-address", ":9001")
    .WithHttpEndpoint(targetPort: 9000, port: 9000, name: "api")
    .WithHttpEndpoint(targetPort: 9001, port: 9001, name: "console")
    .WithEnvironment("MINIO_ROOT_USER", "minioadmin")
    .WithEnvironment("MINIO_ROOT_PASSWORD", "minioadmin");

// Mailpit SMTP container
var mailpit = builder.AddContainer("mailpit", "axllent/mailpit")
    .WithImageTag("latest")
    .WithHttpEndpoint(targetPort: 8025, port: 8025, name: "web")
    .WithHttpEndpoint(targetPort: 1025, port: 1025, name: "smtp");

// Elasticsearch container with basic security enabled
var elasticsearch = builder.AddContainer("elasticsearch", "docker.elastic.co/elasticsearch/elasticsearch")
    .WithImageTag("8.11.1")
    .WithHttpEndpoint(targetPort: 9200, port: 9200, name: "http")
    .WithEnvironment("discovery.type", "single-node")
    .WithEnvironment("xpack.security.enabled", "true")
    .WithEnvironment("ELASTIC_PASSWORD", "devpassword123");

// Individual PostgreSQL databases per microservice
var identityDb = postgres.AddDatabase("identity-db", "identity_db");
var userDb = postgres.AddDatabase("user-db", "user_db");
var organizationDb = postgres.AddDatabase("organization-db", "organization_db");
var documentDb = postgres.AddDatabase("document-db", "document_db");
var caseDb = postgres.AddDatabase("case-db", "case_db");
var workflowDb = postgres.AddDatabase("workflow-db", "workflow_db");
var notificationDb = postgres.AddDatabase("notification-db", "notification_db");
var reportingDb = postgres.AddDatabase("reporting-db", "reporting_db");
var auditDb = postgres.AddDatabase("audit-db", "audit_db");

// Service Declarations
var identityService = builder.AddProject<Projects.IdentityService>("identityservice")
    .WithReference(identityDb)
    .WaitFor(identityDb)
    .WithReference(redis)
    .WaitFor(redis)
    .WithReference(rabbitmq)
    .WaitFor(rabbitmq);

var userService = builder.AddProject<Projects.UserService>("userservice")
    .WithReference(userDb)
    .WaitFor(userDb)
    .WithReference(redis)
    .WaitFor(redis)
    .WithReference(rabbitmq)
    .WaitFor(rabbitmq);

var organizationService = builder.AddProject<Projects.OrganizationService>("organizationservice")
    .WithReference(organizationDb)
    .WaitFor(organizationDb)
    .WithReference(redis)
    .WaitFor(redis)
    .WithReference(rabbitmq)
    .WaitFor(rabbitmq);

var documentService = builder.AddProject<Projects.DocumentService>("documentservice")
    .WithReference(documentDb)
    .WaitFor(documentDb)
    .WithReference(minio.GetEndpoint("api"))
    .WithReference(redis)
    .WaitFor(redis)
    .WithReference(rabbitmq)
    .WaitFor(rabbitmq);

var caseService = builder.AddProject<Projects.CaseService>("caseservice")
    .WithReference(caseDb)
    .WaitFor(caseDb)
    .WithReference(redis)
    .WaitFor(redis)
    .WithReference(rabbitmq)
    .WaitFor(rabbitmq);

var workflowService = builder.AddProject<Projects.WorkflowService>("workflowservice")
    .WithReference(workflowDb)
    .WaitFor(workflowDb)
    .WithReference(redis)
    .WaitFor(redis)
    .WithReference(rabbitmq)
    .WaitFor(rabbitmq);

var notificationService = builder.AddProject<Projects.NotificationService>("notificationservice")
    .WithReference(notificationDb)
    .WaitFor(notificationDb)
    .WithReference(mailpit.GetEndpoint("smtp"))
    .WithReference(redis)
    .WaitFor(redis)
    .WithReference(rabbitmq)
    .WaitFor(rabbitmq);

var searchService = builder.AddProject<Projects.SearchService>("searchservice")
    .WithReference(elasticsearch.GetEndpoint("http"))
    .WithReference(redis)
    .WaitFor(redis)
    .WithReference(rabbitmq)
    .WaitFor(rabbitmq);

var reportingService = builder.AddProject<Projects.ReportingService>("reportingservice")
    .WithReference(reportingDb)
    .WaitFor(reportingDb)
    .WithReference(redis)
    .WaitFor(redis)
    .WithReference(rabbitmq)
    .WaitFor(rabbitmq);

var auditService = builder.AddProject<Projects.AuditService>("auditservice")
    .WithReference(auditDb)
    .WaitFor(auditDb)
    .WithReference(redis)
    .WaitFor(redis)
    .WithReference(rabbitmq)
    .WaitFor(rabbitmq);

// Gateway (YARP Reverse Proxy)
builder.AddProject<Projects.Gateway>("gateway")
    .WithReference(identityService)
    .WithReference(userService)
    .WithReference(organizationService)
    .WithReference(documentService)
    .WithReference(caseService)
    .WithReference(workflowService)
    .WithReference(notificationService)
    .WithReference(searchService)
    .WithReference(reportingService)
    .WithReference(auditService)
    .WithExternalHttpEndpoints();

builder.Build().Run();
