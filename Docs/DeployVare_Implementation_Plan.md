# DeployVare - SQL Deployment Manager
## Implementation Plan & Architecture

---

## 🎯 **Project Overview**

**DeployVare** is a SQL deployment management system that:
- Manages database schema changes across multiple applications and instances
- Tracks SQL patches, stored procedures, and functions
- Provides schema comparison between databases
- Maintains deployment history and audit trails
- Securely stores and manages database connections

---

## 📋 **Core Requirements**

### 1. **Database Connection Management**
- Store connection strings encrypted in DeployVare DB
- Support multiple Applications (e.g., AuditVare, InventoryVare)
- Support multiple Instances per Application (e.g., Stage, Prod, Instance1, Instance2)
- Track current patch number for each Application+Instance combination
- Secure access - only authorized users can view/use connections

### 2. **SQL Patch Management**
- Upload ZIP file containing SQL scripts
- ZIP Structure:
  ```
  SQLScripts.zip
  ├── SQLPatch/
  │   ├── 106_Add_Column_UserName.sql
  │   ├── 107_Create_Table_Audit.sql
  ├── StoreProcedure/
  │   ├── usp_GetExpenses.sql
  │   ├── usp_SaveExpense.sql
  └── SQLFunction/
      ├── fn_CalculateTotal.sql
  ```
- Patch naming convention: `{PatchNumber}_{Description}_{Author}.sql`
  - Example: `111_Alter_Expenses_Table_MuhammadSaad.sql`
  - Split by `_`: First = Patch Number, Last = Author, Middle = Description

### 3. **Deployment Process**
- **SQLPatch (DDL Scripts):**
  - Apply all patches from current patch number to latest sequentially
  - Update patch number in DeployVare DB after successful deployment
  - Track which patches were applied in deployment history

- **StoreProcedure & SQLFunction:**
  - Backup existing procedures/functions before replacement
  - Apply new procedures/functions
  - Compress backup and return as downloadable file
  - Delete temporary backup files after compression

### 4. **Schema Comparison**
- Compare any 2 databases (same or different applications)
- Compare:
  - Tables (columns, data types, constraints, indexes)
  - Stored Procedures (definition)
  - Functions (definition)
- Display results in **Git-like diff format**

### 5. **Deployment History**
- Track:
  - Deployment ID
  - Application + Instance
  - Deployed By (username)
  - Deployed At (timestamp)
  - Patches Applied (list)
  - Procedures/Functions Updated (list)
  - Status (Success/Failed)
  - Error Message (if failed)
  - Backup File Path

### 6. **Security**
- Authentication & Authorization
- Encrypted connection strings
- Audit trail for all operations
- Role-based access control

---

## 🏗️ **Database Schema - DeployVare DB**

```sql
-- Applications
CREATE TABLE Applications (
    Id INT PRIMARY KEY IDENTITY(1,1),
    Name NVARCHAR(100) NOT NULL UNIQUE,
    Description NVARCHAR(500),
    CreatedAt DATETIME2 NOT NULL DEFAULT GETUTCDATE(),
    CreatedBy NVARCHAR(100) NOT NULL
);

-- Database Instances
CREATE TABLE DatabaseInstances (
    Id INT PRIMARY KEY IDENTITY(1,1),
    ApplicationId INT NOT NULL,
    InstanceName NVARCHAR(100) NOT NULL,
    ConnectionStringEncrypted NVARCHAR(MAX) NOT NULL, -- Encrypted
    CurrentPatchNumber INT NOT NULL DEFAULT 0,
    IsActive BIT NOT NULL DEFAULT 1,
    CreatedAt DATETIME2 NOT NULL DEFAULT GETUTCDATE(),
    CreatedBy NVARCHAR(100) NOT NULL,
    UpdatedAt DATETIME2,
    UpdatedBy NVARCHAR(100),
    CONSTRAINT FK_DatabaseInstances_Applications FOREIGN KEY (ApplicationId) 
        REFERENCES Applications(Id),
    CONSTRAINT UQ_Application_Instance UNIQUE (ApplicationId, InstanceName)
);

-- Deployments
CREATE TABLE Deployments (
    Id INT PRIMARY KEY IDENTITY(1,1),
    DatabaseInstanceId INT NOT NULL,
    DeployedBy NVARCHAR(100) NOT NULL,
    DeployedAt DATETIME2 NOT NULL DEFAULT GETUTCDATE(),
    Status NVARCHAR(20) NOT NULL, -- Pending, InProgress, Success, Failed
    FromPatchNumber INT NOT NULL,
    ToPatchNumber INT NOT NULL,
    BackupFilePath NVARCHAR(500),
    ErrorMessage NVARCHAR(MAX),
    DurationSeconds INT,
    CONSTRAINT FK_Deployments_DatabaseInstances FOREIGN KEY (DatabaseInstanceId) 
        REFERENCES DatabaseInstances(Id),
    CONSTRAINT CK_Deployment_Status CHECK (Status IN ('Pending', 'InProgress', 'Success', 'Failed'))
);

-- Deployment Patches (which patches were applied)
CREATE TABLE DeploymentPatches (
    Id INT PRIMARY KEY IDENTITY(1,1),
    DeploymentId INT NOT NULL,
    PatchNumber INT NOT NULL,
    PatchFileName NVARCHAR(255) NOT NULL,
    Description NVARCHAR(500),
    Author NVARCHAR(100),
    ExecutedAt DATETIME2 NOT NULL DEFAULT GETUTCDATE(),
    Status NVARCHAR(20) NOT NULL, -- Success, Failed
    ErrorMessage NVARCHAR(MAX),
    CONSTRAINT FK_DeploymentPatches_Deployments FOREIGN KEY (DeploymentId) 
        REFERENCES Deployments(Id) ON DELETE CASCADE
);

-- Deployment Objects (stored procedures/functions updated)
CREATE TABLE DeploymentObjects (
    Id INT PRIMARY KEY IDENTITY(1,1),
    DeploymentId INT NOT NULL,
    ObjectType NVARCHAR(50) NOT NULL, -- StoredProcedure, Function
    ObjectName NVARCHAR(255) NOT NULL,
    BackedUp BIT NOT NULL DEFAULT 0,
    ExecutedAt DATETIME2 NOT NULL DEFAULT GETUTCDATE(),
    CONSTRAINT FK_DeploymentObjects_Deployments FOREIGN KEY (DeploymentId) 
        REFERENCES Deployments(Id) ON DELETE CASCADE
);

-- Audit Log
CREATE TABLE AuditLogs (
    Id INT PRIMARY KEY IDENTITY(1,1),
    UserId NVARCHAR(100) NOT NULL,
    Action NVARCHAR(100) NOT NULL, -- ViewConnection, Deploy, Compare, etc.
    EntityType NVARCHAR(50), -- Application, DatabaseInstance, Deployment
    EntityId INT,
    Details NVARCHAR(MAX),
    Timestamp DATETIME2 NOT NULL DEFAULT GETUTCDATE(),
    IpAddress NVARCHAR(50)
);

-- Users (simple user management)
CREATE TABLE Users (
    Id INT PRIMARY KEY IDENTITY(1,1),
    Username NVARCHAR(100) NOT NULL UNIQUE,
    PasswordHash NVARCHAR(255) NOT NULL,
    FullName NVARCHAR(200),
    Email NVARCHAR(255),
    Role NVARCHAR(50) NOT NULL DEFAULT 'User', -- Admin, User
    IsActive BIT NOT NULL DEFAULT 1,
    CreatedAt DATETIME2 NOT NULL DEFAULT GETUTCDATE(),
    LastLoginAt DATETIME2
);

-- Indexes
CREATE INDEX IX_DatabaseInstances_ApplicationId ON DatabaseInstances(ApplicationId);
CREATE INDEX IX_Deployments_DatabaseInstanceId ON Deployments(DatabaseInstanceId);
CREATE INDEX IX_Deployments_DeployedAt ON Deployments(DeployedAt DESC);
CREATE INDEX IX_DeploymentPatches_DeploymentId ON DeploymentPatches(DeploymentId);
CREATE INDEX IX_DeploymentObjects_DeploymentId ON DeploymentObjects(DeploymentId);
CREATE INDEX IX_AuditLogs_Timestamp ON AuditLogs(Timestamp DESC);
```

---

## 📁 **Project Structure - Monolithic Web API**

```
DeployVare/
│
├── Docs/
│   ├── ClaudeChat.md
│   └── DeployVare_Implementation_Plan.md
│
├── Web/                                # Next.js Frontend (Future)
│
└── API/                                # .NET 8 Web API
    ├── Controllers/
    │   ├── ApplicationsController.cs
    │   ├── DatabaseInstancesController.cs
    │   ├── DeploymentsController.cs
    │   ├── SchemaComparisonController.cs
    │   └── AuthController.cs
    │
    ├── Models/
    │   ├── Entities/
    │   │   ├── Application.cs
    │   │   ├── DatabaseInstance.cs
    │   │   ├── Deployment.cs
    │   │   ├── DeploymentPatch.cs
    │   │   ├── DeploymentObject.cs
    │   │   ├── AuditLog.cs
    │   │   └── User.cs
    │   └── Enums/
    │       ├── DeploymentStatus.cs
    │       └── UserRole.cs
    │
    ├── DTOs/
    │   ├── Requests/
    │   │   ├── CreateApplicationRequest.cs
    │   │   ├── CreateDatabaseInstanceRequest.cs
    │   │   ├── DeployRequest.cs
    │   │   ├── CompareSchemaRequest.cs
    │   │   └── LoginRequest.cs
    │   └── Responses/
    │       ├── ApplicationDto.cs
    │       ├── DatabaseInstanceDto.cs
    │       ├── DeploymentDto.cs
    │       ├── DeploymentResultDto.cs
    │       ├── SchemaComparisonDto.cs
    │       └── LoginResponse.cs
    │
    ├── Services/
    │   ├── Interfaces/
    │   │   ├── IApplicationService.cs
    │   │   ├── IDatabaseInstanceService.cs
    │   │   ├── IDeploymentService.cs
    │   │   ├── ISchemaComparisonService.cs
    │   │   ├── ISqlExecutionService.cs
    │   │   ├── IEncryptionService.cs
    │   │   ├── IBackupService.cs
    │   │   ├── IAuthService.cs
    │   │   └── IAuditService.cs
    │   └── Implementations/
    │       ├── ApplicationService.cs
    │       ├── DatabaseInstanceService.cs
    │       ├── DeploymentService.cs
    │       ├── SchemaComparisonService.cs
    │       ├── SqlExecutionService.cs
    │       ├── EncryptionService.cs
    │       ├── BackupService.cs
    │       ├── AuthService.cs
    │       └── AuditService.cs
    │
    ├── Data/
    │   ├── DeployVareDbContext.cs
    │   └── Migrations/
    │
    ├── Middleware/
    │   ├── ErrorHandlingMiddleware.cs
    │   └── AuditMiddleware.cs
    │
    ├── Utilities/
    │   ├── FileHelper.cs
    │   ├── SqlParser.cs
    │   └── DiffGenerator.cs
    │
    ├── wwwroot/
    │   └── uploads/          # Temporary storage for ZIP files
    │
    ├── Program.cs
    ├── appsettings.json
    └── appsettings.Development.json
```

---

## 🔐 **Security Implementation**

### Connection String Encryption
```csharp
// Use AES encryption with a master key stored in Azure Key Vault or appsettings
public class EncryptionService : IEncryptionService
{
    public string Encrypt(string plainText);
    public string Decrypt(string cipherText);
}
```

### Authentication & Authorization
- JWT-based authentication
- Role-based authorization (Admin, User)
- Admins can: Create/Edit/Delete connections, Deploy, Compare
- Users can: View connections (limited), Deploy (if granted), Compare

---

## 🚀 **API Endpoints**

### Applications
- `GET /api/applications` - List all applications
- `POST /api/applications` - Create new application
- `GET /api/applications/{id}` - Get application by ID
- `PUT /api/applications/{id}` - Update application
- `DELETE /api/applications/{id}` - Delete application

### Database Instances
- `GET /api/database-instances` - List all instances
- `GET /api/database-instances/by-application/{applicationId}` - Get instances by application
- `POST /api/database-instances` - Create new instance
- `GET /api/database-instances/{id}` - Get instance by ID
- `PUT /api/database-instances/{id}` - Update instance
- `DELETE /api/database-instances/{id}` - Delete instance
- `POST /api/database-instances/{id}/test-connection` - Test database connection

### Deployments
- `GET /api/deployments` - List all deployments (with pagination)
- `GET /api/deployments/{id}` - Get deployment details
- `POST /api/deployments/deploy` - Start new deployment
  - Request: `multipart/form-data` (ZIP file + DatabaseInstanceId)
  - Response: DeploymentResultDto + Backup ZIP file download
- `GET /api/deployments/by-instance/{instanceId}` - Get deployments by instance

### Schema Comparison
- `POST /api/schema-comparison/compare` - Compare two databases
  - Request: `{ sourceInstanceId, targetInstanceId }`
  - Response: Git-like diff format

### Authentication
- `POST /api/auth/login` - Login
- `POST /api/auth/register` - Register (Admin only)
- `GET /api/auth/me` - Get current user

---

## 🎨 **Frontend UI Flow (Next.js - Future)**

### Pages
1. **Dashboard**
   - Overview of applications and instances
   - Recent deployments
   - Deployment status

2. **Applications**
   - List/Create/Edit applications

3. **Database Instances**
   - List instances grouped by application
   - Create/Edit instances
   - Test connection
   - View current patch number

4. **Deploy**
   - Select database instance
   - Upload ZIP file
   - Preview patches to be applied
   - Deploy button
   - Real-time deployment logs
   - Download backup file

5. **Schema Compare**
   - Select source database
   - Select target database
   - View diff (Git-like UI with syntax highlighting)
   - Filter by: Tables, Procedures, Functions

6. **History**
   - Deployment history table
   - Filter by application, instance, date, user
   - View deployment details
   - Download backup files

---

## 📝 **Implementation Phases**

### ✅ **Phase 1: Foundation** (Current Focus)
- [x] Project setup
- [x] Database schema
- [x] Entity models
- [x] DTOs
- [ ] DeployVare DbContext
- [ ] Initial migration

### 📋 **Phase 2: Core Services**
- [ ] EncryptionService (AES encryption for connection strings)
- [ ] SqlExecutionService (execute scripts on target databases)
- [ ] ApplicationService
- [ ] DatabaseInstanceService
- [ ] BackupService (backup procedures/functions)

### 📋 **Phase 3: Deployment Engine**
- [ ] DeploymentService
  - [ ] Parse ZIP file
  - [ ] Extract and validate SQL scripts
  - [ ] Apply patches sequentially
  - [ ] Update procedures/functions
  - [ ] Track deployment progress
  - [ ] Handle errors and rollback

### 📋 **Phase 4: Schema Comparison**
- [ ] SchemaComparisonService
  - [ ] Extract schema from database
  - [ ] Compare tables, procedures, functions
  - [ ] Generate Git-like diff

### 📋 **Phase 5: Authentication & Security**
- [ ] AuthService (JWT authentication)
- [ ] AuditService (audit logging)
- [ ] Middleware (error handling, audit)

### 📋 **Phase 6: API Controllers**
- [ ] Implement all controllers
- [ ] Add validation
- [ ] Add Swagger documentation

### 📋 **Phase 7: Testing**
- [ ] Unit tests
- [ ] Integration tests
- [ ] Manual testing

### 📋 **Phase 8: Frontend (Next.js)**
- [ ] Setup Next.js project
- [ ] Implement UI pages
- [ ] Real-time deployment logs (SignalR)
- [ ] Git-like diff viewer

---

## 🔧 **Technologies & Libraries**

### Backend
- .NET 8 Web API
- Entity Framework Core 8
- SQL Server
- Dapper (for dynamic SQL execution on target databases)
- BCrypt.Net (password hashing)
- System.Security.Cryptography (AES encryption)
- JWT Authentication
- Serilog (logging)
- FluentValidation (input validation)
- Swashbuckle (Swagger/OpenAPI)

### Frontend (Future)
- Next.js 14
- TypeScript
- Tailwind CSS
- React Diff Viewer (for schema comparison)
- Zustand (state management)
- Axios (HTTP client)

---

## 📊 **Deployment Flow Diagram**

```
┌─────────────────────────────────────────────────────────────┐
│  1. User selects Database Instance                          │
│     (e.g., AuditVare - Prod, Current Patch: 105)           │
└────────────────┬────────────────────────────────────────────┘
                 │
                 ▼
┌─────────────────────────────────────────────────────────────┐
│  2. User uploads SQLScripts.zip                             │
│     - SQLPatch: 106.sql, 107.sql, 108.sql                  │
│     - StoreProcedure: usp_GetExpenses.sql                  │
│     - SQLFunction: fn_CalculateTotal.sql                   │
└────────────────┬────────────────────────────────────────────┘
                 │
                 ▼
┌─────────────────────────────────────────────────────────────┐
│  3. DeployVare extracts and validates ZIP                   │
│     - Parse patch filenames                                 │
│     - Identify patches to apply (106, 107, 108)            │
│     - Validate SQL syntax (optional)                        │
└────────────────┬────────────────────────────────────────────┘
                 │
                 ▼
┌─────────────────────────────────────────────────────────────┐
│  4. Create Deployment record (Status: InProgress)           │
│     - FromPatchNumber: 105                                  │
│     - ToPatchNumber: 108                                    │
└────────────────┬────────────────────────────────────────────┘
                 │
                 ▼
┌─────────────────────────────────────────────────────────────┐
│  5. Backup existing Stored Procedures & Functions           │
│     - Query target DB for existing definitions              │
│     - Save to temp folder                                   │
│     - Compress to Backup_{Timestamp}.zip                    │
└────────────────┬────────────────────────────────────────────┘
                 │
                 ▼
┌─────────────────────────────────────────────────────────────┐
│  6. Apply SQL Patches (in order: 106, 107, 108)            │
│     - Execute each patch in transaction                     │
│     - Track success/failure                                 │
│     - If failed, stop and mark deployment as Failed         │
└────────────────┬────────────────────────────────────────────┘
                 │
                 ▼
┌─────────────────────────────────────────────────────────────┐
│  7. Apply Stored Procedures & Functions                     │
│     - Execute CREATE OR ALTER statements                    │
│     - Track updated objects                                 │
└────────────────┬────────────────────────────────────────────┘
                 │
                 ▼
┌─────────────────────────────────────────────────────────────┐
│  8. Update DatabaseInstance.CurrentPatchNumber = 108        │
│     - Mark Deployment as Success                            │
│     - Save backup file path                                 │
└────────────────┬────────────────────────────────────────────┘
                 │
                 ▼
┌─────────────────────────────────────────────────────────────┐
│  9. Return DeploymentResult + Backup ZIP download           │
│     - Clean up temp files                                   │
│     - Log audit trail                                       │
└─────────────────────────────────────────────────────────────┘
```

---

## 🎯 **Success Criteria**

- ✅ Securely store and manage database connections
- ✅ Upload ZIP and automatically apply patches
- ✅ Track current patch number per application/instance
- ✅ Backup procedures/functions before replacement
- ✅ Compare database schemas with git-like diff
- ✅ Complete deployment history and audit trail
- ✅ Authentication and authorization
- ✅ Error handling and rollback (future)

---

**Document Version:** 1.0  
**Last Updated:** November 18, 2025  
**Author:** DeployVare Team

