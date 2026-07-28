# Legal Platform Infrastructure

An enterprise-grade Legal Workflow and Document Management platform architecture orchestrated using **.NET Aspire**, backing microservices via event-driven pub/sub on **RabbitMQ**, search indexing on **Elasticsearch**, object storage on **MinIO S3**, and an administrative portal in **Angular 18 & Tailwind CSS**.

---

## Architecture Topology

```
                         +----------------------------------+
                         |       Gateway (YARP Proxy)       |
                         |          localhost:5000          |
                         +-----------------+----------------+
                                           |
    +-------------------+------------------+------------------+-------------------+
    |                   |                  |                  |                   |
  IdentityService    CaseService       DocumentService    WorkflowService      AuditService
    |                   |                  |                  |                   |
  Identity DB         Case DB          Document DB + MinIO   Workflow DB         Audit DB
    |                   |                  |                  |                   |
    +-------------------+------------------+------------------+-------------------+
                                           |
                                       RabbitMQ
                                     (Event Bus)
                                           |
             +-----------------------------+-----------------------------+
             |                             |                             |
     SearchService                 ReportingService             NotificationService
             |                             |                             |
       Elasticsearch                 Reporting DB                     Mailpit
```

---

## Key Features

### 1. Unified Gateway Routing & Security
- **YARP Reverse Proxy**: Exposes all services on a single port (`5000`) with CORS allowed for client development.
- **Centralized Rate Limiting**: Protects backend services using a `FixedWindowPolicy` mapping to max 100 requests per minute.
- **Zero-Trust JWT**: Microservices decode and validate JWT signatures locally using a shared cryptographical secret.

### 2. Document & Case Management
- **Hybrid Versioning**: DB metadata matches PostgreSQL while physical files store immutably in a local S3-compatible **MinIO** container.
- **Timeline Forensics**: Cases maintain a chronological note feed capturing legal actions with user stamps.

### 3. Asynchronous Workflow Engine
- Approving a sequential workflow step updates statuses and emits RabbitMQ integration events.
- **Notification Routing**: Resolves events and dispatches database alerts, simulated emails (via **Mailpit**), or mock Slack hooks.

### 4. Search & Analytics
- **Fuzzy Search**: Real-time multi-match queries powered by **Elasticsearch 8.11.1** (basic authentication enabled).
- **Read-Model Dashboard**: Eventual consistency metric aggregates (assigned vs completed workflow stats) queried from reporting DB.

### 5. Centralized Audit Trail
- **Immutable DbContext**: Database context rejects all updates/deletes in the application layer, guaranteeing write-once persistence.
- **Tamper Detection**: Computes a SHA-256 integrity hash of audit fields on insertion.
- **Forensic Access**: Server-side tenant-filtering checks caller role claims (`SystemAdmin` gets global scope; `ComplianceOfficer` scopes to own tenant). Export actions are themselves audited.

---

## Scaffolding Layout

```
d:\Study\LegalPlatefomInfrastucture\
 ├── src/
 │    ├── AppHost/               # .NET Aspire Orchestration Host
 │    ├── ServiceDefaults/       # Shared Resilience, Diagnostics, Exception Handler
 │    ├── Gateway/               # YARP Proxy and rate limiting configuration
 │    ├── BuildingBlocks/
 │    │    ├── Contracts/        # Integration Event definitions
 │    │    ├── EventBus/         # MassTransit RabbitMQ wrappers
 │    │    └── SharedKernel/     # AggregateRoot, Entity, and ValueObject patterns
 │    ├── Services/
 │    │    ├── IdentityService/  # Users Authentication (Identity, JWT tokens)
 │    │    ├── UserService/      # User context registry
 │    │    ├── OrganizationService/ # Tenants configuration
 │    │    ├── DocumentService/  # S3 File upload and version archiving
 │    │    ├── CaseService/      # Legal cases timelines
 │    │    ├── WorkflowService/  # Approval state stepping
 │    │    ├── NotificationService/# Extensible Slack/SMTP mail simulation
 │    │    ├── SearchService/    # Elasticsearch fuzzy queries
 │    │    ├── ReportingService/ # Event-driven dashboard statistics
 │    │    └── AuditService/     # Append-only forensic database logs
 │    └── WebPortal/             # Angular 18 + Tailwind CSS Admin Portal
 └── tests/
      ├── Identity.Tests/        # JWT Authentication unit tests
      └── IntegrationTests/      # WebApplicationFactory gateway routing tests
```

---

## Local Setup & Run Instructions

### Prerequisites
- **Node.js**: v20.x installed.
- **Docker / Podman**: Required for hosting .NET Aspire containers (PostgreSQL, Redis, RabbitMQ, MinIO, Mailpit, Elasticsearch).
- **.NET SDK**: .NET 10 installed.

### Step 1: Run the Backend Orchestrator
1. Open a terminal at the root directory.
2. Build the solution:
   ```powershell
   dotnet build
   ```
3. Run the AppHost project:
   ```powershell
   dotnet run --project src/AppHost/AppHost.csproj
   ```
4. This launches the Aspire Developer Dashboard listing all containers and endpoints.

### Step 2: Run the Web Portal
1. Navigate to the portal directory:
   ```powershell
   cd src/WebPortal
   ```
2. Install dependencies (if needed):
   ```powershell
   npm install
   ```
3. Launch the development server:
   ```powershell
   npm start
   ```
4. Open [http://localhost:4200](http://localhost:4200) in your browser. Use the Simulated Profiles panel to test different user access contexts.

### Step 3: Running Tests
Run all unit and integration tests:
```powershell
dotnet test
```
