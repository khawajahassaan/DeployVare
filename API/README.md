# DeployVare API - SQL Deployment Management System

## Overview

DeployVare is a comprehensive SQL deployment management system that helps manage database schema changes, stored procedures, and functions across multiple applications and instances.

## Features

✅ **Application & Instance Management**
- Manage multiple applications (e.g., AuditVare, InventoryVare)
- Multiple database instances per application (Stage, Prod, etc.)
- Securely encrypted connection strings

✅ **SQL Patch Management**
- Track SQL patches with sequential numbering
- Apply patches automatically from ZIP files
- Parse patch filenames: `{PatchNumber}_{Description}_{Author}.sql`
- Example: `111_Alter_Expenses_Table_MuhammadSaad.sql`

✅ **Stored Procedures & Functions**
- Automatic backup before replacement
- Apply from ZIP file (StoreProcedure and SQLFunction folders)
- Download backup files

✅ **Schema Comparison**
- Compare any two database instances
- Git-like diff format for easy visualization
- Compare tables, procedures, and functions

✅ **Deployment History**
- Complete audit trail of all deployments
- Track patches applied, objects updated
- View deployment duration and status

✅ **Security**
- JWT authentication
- Role-based authorization (Admin/User)
- Encrypted connection strings (AES-256)

---

## Getting Started

### Prerequisites

- .NET 8 SDK
- SQL Server (LocalDB or full instance)
- Visual Studio Code or Visual Studio 2022

### Setup

1. **Update Connection String**

Edit `appsettings.json`:

```json
{
  "ConnectionStrings": {
    "DeployVareDb": "Server=localhost;Database=DeployVareDb;Trusted_Connection=True;TrustServerCertificate=True;MultipleActiveResultSets=true"
  }
}
```

2. **Create Database**

```bash
dotnet ef migrations add InitialCreate
dotnet ef database update
```

3. **Run the Application**

```bash
dotnet run
```

The API will start at: `https://localhost:7xxx` (check console output)

4. **Access Swagger Documentation**

Navigate to: `https://localhost:7xxx/swagger`

---

## API Endpoints

### Authentication

| Method | Endpoint | Description | Auth |
|--------|----------|-------------|------|
| POST | `/api/auth/login` | Login | None |
| POST | `/api/auth/register` | Register new user | Admin |
| GET | `/api/auth/me` | Get current user | User |

### Applications

| Method | Endpoint | Description | Auth |
|--------|----------|-------------|------|
| GET | `/api/applications` | Get all applications | User |
| GET | `/api/applications/{id}` | Get application by ID | User |
| POST | `/api/applications` | Create application | Admin |
| DELETE | `/api/applications/{id}` | Delete application | Admin |

### Database Instances

| Method | Endpoint | Description | Auth |
|--------|----------|-------------|------|
| GET | `/api/databaseinstances` | Get all instances | User |
| GET | `/api/databaseinstances/by-application/{id}` | Get instances by app | User |
| GET | `/api/databaseinstances/{id}` | Get instance by ID | User |
| POST | `/api/databaseinstances` | Create instance | Admin |
| PUT | `/api/databaseinstances/{id}` | Update instance | Admin |
| DELETE | `/api/databaseinstances/{id}` | Delete instance | Admin |
| POST | `/api/databaseinstances/{id}/test-connection` | Test connection | User |

### Deployments

| Method | Endpoint | Description | Auth |
|--------|----------|-------------|------|
| GET | `/api/deployments` | Get all deployments | User |
| GET | `/api/deployments/{id}` | Get deployment by ID | User |
| GET | `/api/deployments/by-instance/{id}` | Get by instance | User |
| POST | `/api/deployments/deploy` | Deploy SQL scripts | Admin |
| GET | `/api/deployments/{id}/download-backup` | Download backup | User |

### Schema Comparison

| Method | Endpoint | Description | Auth |
|--------|----------|-------------|------|
| POST | `/api/schemacomparison/compare` | Compare two databases | User |

---

## Deployment ZIP Structure

When deploying, upload a ZIP file with this structure:

```
SQLScripts.zip
├── SQLPatch/
│   ├── 106_Add_Column_UserName.sql
│   ├── 107_Create_Table_Audit.sql
│   └── 108_Add_Constraint_FK_User.sql
├── StoreProcedure/
│   ├── usp_GetExpenses.sql
│   └── usp_SaveExpense.sql
└── SQLFunction/
    └── fn_CalculateTotal.sql
```

### Patch Naming Convention

Format: `{PatchNumber}_{Description}_{Author}.sql`

Examples:
- `111_Alter_Expenses_Table_MuhammadSaad.sql`
- `112_Create_Index_UserId_AhmedAli.sql`

**Important:**
- Patches are applied sequentially from current patch number to latest
- Current patch number is tracked per database instance
- Only patches greater than current number are applied

---

## Deployment Process

1. **Select Database Instance** - Choose which database to deploy to
2. **Upload ZIP** - ZIP file containing SQLPatch, StoreProcedure, SQLFunction folders
3. **Automatic Backup** - System backs up existing procedures/functions
4. **Apply Patches** - Sequential execution from current to latest patch
5. **Update Objects** - Apply stored procedures and functions
6. **Update Patch Number** - Instance's current patch number is updated
7. **Download Backup** - Backup ZIP is available for download

---

## Configuration

### appsettings.json

```json
{
  "ConnectionStrings": {
    "DeployVareDb": "Your connection string"
  },
  "JwtSettings": {
    "SecretKey": "YourSuperSecretKeyThatIsAtLeast32CharactersLongForHS256!",
    "Issuer": "DeployVareAPI",
    "Audience": "DeployVareClient",
    "ExpirationMinutes": 480
  },
  "EncryptionSettings": {
    "EncryptionKey": "32CharacterLongEncryptionKey12"
  },
  "FileUploadSettings": {
    "MaxFileSizeMB": 50,
    "UploadPath": "wwwroot/uploads",
    "BackupPath": "wwwroot/backups"
  }
}
```

**⚠️ Important:** Change the `SecretKey` and `EncryptionKey` to your own secure values in production!

---

## Example Usage

### 1. Register/Login

```bash
# Login
curl -X POST https://localhost:7001/api/auth/login \
  -H "Content-Type: application/json" \
  -d '{"username":"admin","password":"password"}'
```

### 2. Create Application

```bash
curl -X POST https://localhost:7001/api/applications \
  -H "Authorization: Bearer YOUR_TOKEN" \
  -H "Content-Type: application/json" \
  -d '{"name":"AuditVare","description":"Audit Management System"}'
```

### 3. Create Database Instance

```bash
curl -X POST https://localhost:7001/api/databaseinstances \
  -H "Authorization: Bearer YOUR_TOKEN" \
  -H "Content-Type: application/json" \
  -d '{
    "applicationId": 1,
    "instanceName": "Stage",
    "connectionString": "Server=localhost;Database=AuditVareStage;...",
    "currentPatchNumber": 105
  }'
```

### 4. Deploy

```bash
curl -X POST https://localhost:7001/api/deployments/deploy \
  -H "Authorization: Bearer YOUR_TOKEN" \
  -F "databaseInstanceId=1" \
  -F "zipFile=@SQLScripts.zip"
```

### 5. Compare Schemas

```bash
curl -X POST https://localhost:7001/api/schemacomparison/compare \
  -H "Authorization: Bearer YOUR_TOKEN" \
  -H "Content-Type: application/json" \
  -d '{"sourceInstanceId":1,"targetInstanceId":2}'
```

---

## Technology Stack

- **Framework:** ASP.NET Core 8
- **Database:** SQL Server with Entity Framework Core
- **Authentication:** JWT Bearer
- **Security:** AES-256 encryption for connection strings, BCrypt for passwords
- **Logging:** Serilog
- **API Documentation:** Swagger/OpenAPI

---

## Security Best Practices

1. ✅ Change default JWT secret key
2. ✅ Change default encryption key
3. ✅ Use HTTPS in production
4. ✅ Keep connection strings encrypted
5. ✅ Implement proper role-based access control
6. ✅ Regular backup of DeployVare database
7. ✅ Audit trail for all actions

---

## Troubleshooting

### Migration Issues

```bash
# Reset migrations
dotnet ef database drop -f
dotnet ef migrations remove
dotnet ef migrations add InitialCreate
dotnet ef database update
```

### Connection Issues

- Ensure SQL Server is running
- Verify connection string
- Check firewall rules
- Test connection from SQL Server Management Studio

### File Upload Issues

- Check file size limits in `appsettings.json`
- Ensure `wwwroot/uploads` and `wwwroot/backups` folders exist
- Verify write permissions

---

## Future Enhancements

- [ ] Rollback functionality with down scripts
- [ ] Email notifications
- [ ] Scheduled deployments
- [ ] Multi-environment support
- [ ] Real-time deployment logs (SignalR)
- [ ] Deployment approval workflow
- [ ] Frontend (Next.js) application

---

## License

Internal use only.

## Contact

For questions or support, contact your system administrator.

