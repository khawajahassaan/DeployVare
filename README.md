# 🚀 DeployVare

**SQL Deployment Management System**

A comprehensive solution for managing SQL deployments across multiple applications and database instances with automatic backups, schema comparison, and detailed audit trails.

---

## 📦 Project Overview

```
DeployVare/
├── API/              # .NET 8 Web API Backend ✅
├── web/              # Next.js 14 Frontend ✅
├── Docs/             # Documentation
└── README.md         # This file
```

---

## ✨ Features

### 🔐 Authentication & Security
- JWT-based authentication
- Role-based authorization (Admin/User)
- AES-256 encrypted connection strings
- Secure password hashing with BCrypt

### 📱 Application Management
- Create and manage multiple applications
- Group database instances by application
- Track instance count and status

### 🗄️ Database Instance Management
- Add unlimited database instances
- Encrypted connection string storage
- Test database connections
- Track current patch level per instance

### 🚀 SQL Deployment
- Upload ZIP files with SQL patches, procedures, and functions
- Sequential patch management
- Automatic backups before deployment
- Real-time deployment feedback
- Download backup files

### 📊 Deployment History
- Complete audit trail of all deployments
- View detailed patch and object information
- Track who deployed, when, and what changed
- Filter by application/instance
- Paginated results

### 🔍 Schema Comparison
- Compare any two database instances
- Git-like diff viewer for changes
- Compare tables, procedures, and functions
- Visual indicators for added, removed, modified objects
- Export-ready results

---

## 🛠️ Tech Stack

### Backend
- **.NET 8** - Web API
- **Entity Framework Core 8** - ORM
- **Dapper** - Direct SQL execution
- **SQL Server** - Database
- **JWT** - Authentication
- **Serilog** - Logging
- **Swagger** - API documentation

### Frontend
- **Next.js 14** - React framework (App Router)
- **TypeScript** - Type safety
- **Tailwind CSS v4** - Styling
- **Zustand** - State management
- **Axios** - HTTP client
- **Lucide React** - Icons

---

## 🖥️ Desktop App (Tauri)

DeployVare ships as a self-contained Linux desktop app (`.deb`) that bundles the .NET API as a sidecar process — no separate server needed.

### Build & Install

```bash
cd ~/Projects/DeployVare/web

# 1. Build the sidecar (.NET API) + Tauri frontend and package as .deb
npm run tauri:full-build

# 2. Install (overwrites any existing version)
sudo dpkg -i src-tauri/target/release/bundle/deb/DeployVare_1.0.0_amd64.deb

# 3. Launch
deployvare
```

> **What `tauri:full-build` does:**
> 1. `dotnet publish` — compiles the API as a self-contained Linux x64 binary
> 2. Copies the binary to `src-tauri/binaries/` as the Tauri sidecar
> 3. `tauri build` — compiles the frontend, bundles everything, and produces the `.deb`

### Re-installing / Upgrading

`dpkg -i` with `sudo` will overwrite the currently installed version automatically. No need to uninstall first.

### Uninstall

```bash
sudo dpkg -r deployvare
```

---

## ⚡ Quick Start

### Prerequisites
- .NET 8 SDK
- Node.js 18+
- SQL Server (LocalDB or full instance)

### 1. Start Backend

```bash
cd API

# Update appsettings.json with your SQL Server connection string

# Create database
dotnet ef database update

# Run API
dotnet run

# API: https://localhost:7001
# Swagger: https://localhost:7001/swagger
```

### 2. Create Admin User

Use Swagger or Postman:

**POST** `https://localhost:7001/api/auth/register`
```json
{
  "username": "admin",
  "password": "Admin@123",
  "fullName": "System Administrator",
  "role": "Admin"
}
```

### 3. Start Frontend

```bash
cd web

# Create environment file
echo "NEXT_PUBLIC_API_URL=https://localhost:7001/api" > .env.local

# Run frontend
npm run dev

# Frontend: http://localhost:3000
```

### 4. Login & Use

1. Open `http://localhost:3000`
2. Login with: `admin` / `Admin@123`
3. Start managing your SQL deployments!

---

## 📖 Documentation

- **[START.md](START.md)** - Quick start guide
- **[COMPLETE_SETUP_GUIDE.md](COMPLETE_SETUP_GUIDE.md)** - Detailed setup
- **[FRONTEND_COMPLETE.md](FRONTEND_COMPLETE.md)** - Frontend documentation
- **[API/README.md](API/README.md)** - Backend API documentation
- **[API/QUICKSTART.md](API/QUICKSTART.md)** - Backend quick start
- **[web/README.md](web/README.md)** - Frontend guide
- **[Docs/DeployVare_Implementation_Plan.md](Docs/DeployVare_Implementation_Plan.md)** - Architecture

---

## 📸 Screenshots

### Dashboard
- Overview statistics
- Recent deployments
- Quick actions

### Deployment Interface
- Select database instance
- Upload ZIP file
- Real-time feedback
- Download backups

### Schema Comparison
- Git-like diff viewer
- Side-by-side comparison
- Color-coded changes

### History
- Complete deployment log
- Detailed patch information
- Backup downloads

---

## 🎯 Use Cases

### 1. Development to Staging to Production
- Maintain separate instances for each environment
- Track patch levels across all environments
- Deploy confidently knowing backups are automatic
- Compare schemas to ensure consistency

### 2. Multi-Application Management
- Manage multiple applications from one interface
- Each application can have multiple instances
- Centralized deployment history
- Consistent deployment process

### 3. Audit & Compliance
- Complete audit trail of all changes
- Track who deployed what and when
- Download backups for any deployment
- Error tracking and reporting

---

## 📋 SQL Script Structure

Upload ZIP files with this structure:

```
YourDeployment.zip
├── SQLPatch/                    # DDL queries (patches)
│   ├── 101_Create_Users_Table_JohnDoe.sql
│   ├── 102_Add_Email_Column_JaneSmith.sql
│   └── 103_Create_Index_Username_Admin.sql
├── StoreProcedure/              # Stored procedures
│   ├── usp_GetUsers.sql
│   ├── usp_CreateUser.sql
│   └── usp_DeleteUser.sql
└── SQLFunction/                 # SQL functions
    ├── fn_CalculateTotal.sql
    └── fn_FormatDate.sql
```

### Patch Naming Convention
`{PatchNumber}_{Description}_{Author}`

Example: `105_Alter_Expenses_Table_MuhammadSaad`

---

## 🔒 Security Features

- ✅ JWT token authentication
- ✅ Role-based authorization
- ✅ AES-256 connection string encryption
- ✅ BCrypt password hashing
- ✅ Protected API endpoints
- ✅ CORS configuration
- ✅ Input validation
- ✅ SQL injection prevention (parameterized queries)

---

## 📊 Status

### Backend API
- ✅ **100% Complete**
- ✅ All services implemented
- ✅ All controllers working
- ✅ Build successful
- ✅ No errors

### Frontend
- ✅ **100% Complete**
- ✅ All 7 pages implemented
- ✅ All UI components ready
- ✅ Build successful
- ✅ No errors

### Overall
- ✅ **Production Ready!**

---

## 🎓 Key Concepts

### Patch Management
- Each database instance tracks its current patch number
- Deployments apply all patches from current to latest
- Patches are applied sequentially
- Failed patches prevent further execution

### Backup Strategy
- Before deploying procedures/functions, old versions are backed up
- Backups are compressed and available for download
- Each deployment creates its own backup
- Temporary files are cleaned up automatically

### Schema Comparison
- Fetches schema definitions from both databases
- Generates line-by-line diffs
- Categorizes changes as Added, Removed, Modified, or Identical
- Presents results in git-like format

---

## 🚧 Troubleshooting

### Backend Won't Start
1. Check SQL Server is running
2. Verify connection string in `appsettings.json`
3. Run `dotnet ef database update`

### Frontend Won't Connect
1. Ensure backend is running at `https://localhost:7001`
2. Check `.env.local` has correct API URL
3. Try accepting the SSL certificate in browser

### Deployment Fails
1. Verify connection string is correct
2. Check patch numbers are sequential
3. Review error message in deployment history
4. Download backup to rollback if needed

---

## 📈 Future Enhancements (Optional)

- [ ] SignalR for real-time deployment progress
- [ ] Rollback functionality with down scripts
- [ ] Deployment scheduling
- [ ] Email notifications
- [ ] Slack/Teams integration
- [ ] Multi-database deployment (deploy to multiple instances at once)
- [ ] Deployment approval workflow
- [ ] Custom deployment scripts
- [ ] Database migration from one server to another
- [ ] Performance monitoring

---

## 👥 Contributing

This is a monolithic application designed for internal use. To extend:

1. **Add new API endpoint:**
   - Add method to appropriate service
   - Add controller action
   - Update API documentation

2. **Add new frontend page:**
   - Create page in `web/app/your-page/page.tsx`
   - Wrap with `<ProtectedLayout>`
   - Use existing UI components
   - Add route to navigation

3. **Add new feature:**
   - Follow existing patterns
   - Maintain type safety
   - Update documentation

---

## 📝 License

This is an internal tool. All rights reserved.

---

## 🎉 Credits

Built with modern web technologies and best practices.

**Backend:** ASP.NET Core 8  
**Frontend:** Next.js 14  
**Database:** SQL Server  
**Deployment:** IIS / Docker (future)

---

## 📞 Support

For questions or issues:
1. Check the documentation in `Docs/`
2. Review the README files in each directory
3. Check API documentation at `/swagger`
4. Review error logs in deployment history

---

**Happy Deploying! 🚀**

*DeployVare - Making SQL deployments simple, safe, and trackable.*




