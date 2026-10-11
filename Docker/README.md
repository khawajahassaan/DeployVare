# DeployVare Docker Setup

This directory contains the Docker configuration for running DeployVare with all its components: SQL Server database, .NET API backend, and Next.js frontend.

## Architecture

The Docker setup includes three main services:

1. **Database (db)**: SQL Server 2022 Express running on port 1433
2. **API (api)**: .NET 8.0 Web API running on port 8080
3. **Web (web)**: Next.js frontend running on port 3000

## Quick Start

### Prerequisites

- Docker and Docker Compose installed
- At least 4GB of available RAM
- At least 10GB of available disk space

### Running the Application

1. **Clone the repository and navigate to the project root**

2. **Copy and configure environment files** (optional, defaults are provided):
   ```bash
   cp docker-compose.env.example .env
   cp Docker/api/env.example Docker/api/.env
   cp Docker/web/env.example Docker/web/.env
   ```

3. **Start all services**:
   ```bash
   docker-compose up -d
   ```

4. **Wait for services to be healthy**:
   - Database will be available on `localhost:1433`
   - API will be available on `http://localhost:8080`
   - Frontend will be available on `http://localhost:3000`

5. **Access the application**:
   - Frontend: http://localhost:3000
   - API Swagger: http://localhost:8080/swagger

### First Time Setup

The database will be automatically initialized with the required schema and an admin user will be created. The default admin credentials are:
- Username: `admin`
- Password: `Admin123!`

## Configuration

### Environment Variables

#### Database Configuration
- `SA_PASSWORD`: SQL Server SA password (default: `YourStrong!Passw0rd`)
- `DB_NAME`: Database name (default: `DeployVare`)

#### API Configuration
- `JWT_SECRET`: JWT signing key (must be at least 32 characters)
- `ENCRYPTION_KEY`: Encryption key for sensitive data (32 characters)
- `API_PORT`: API service port (default: 8080)

#### Frontend Configuration
- `NEXT_PUBLIC_API_URL`: API base URL for frontend (default: `http://localhost:8080/api`)
- `WEB_PORT`: Frontend service port (default: 3000)

### Volumes

The setup creates the following persistent volumes:
- `db_data`: SQL Server data
- `api_uploads`: File uploads directory
- `api_backups`: Database backup files
- `api_logs`: Application logs

## Development

### Building Images

To build all images:
```bash
docker-compose build
```

To build specific services:
```bash
docker-compose build api
docker-compose build web
```

### Logs

View logs for all services:
```bash
docker-compose logs -f
```

View logs for specific services:
```bash
docker-compose logs -f api
docker-compose logs -f web
docker-compose logs -f db
```

### Database Access

To connect to the SQL Server database directly:
```bash
docker-compose exec db /opt/mssql-tools/bin/sqlcmd -S localhost -U sa -P YourStrong!Passw0rd
```

### Resetting Data

To reset all data and start fresh:
```bash
docker-compose down -v
docker-compose up -d
```

## Troubleshooting

### Common Issues

1. **Port conflicts**: Ensure ports 1433, 3000, and 8080 are available
2. **Memory issues**: Ensure Docker has at least 4GB RAM allocated
3. **Database connection fails**: Wait for the database health check to pass
4. **File permissions**: The API runs as a non-root user, ensure proper permissions for uploads/backups

### Health Checks

All services include health checks. Monitor service health with:
```bash
docker-compose ps
```

### Database Initialization

If the database fails to initialize, check the logs:
```bash
docker-compose logs db
```

The API will automatically run database migrations and seed data on startup.

## Production Deployment

For production deployment:

1. Update all passwords and secrets in environment files
2. Use external databases for persistence
3. Configure proper SSL/TLS certificates
4. Set up reverse proxy (nginx, traefik, etc.)
5. Configure proper logging and monitoring
6. Use Docker secrets for sensitive data

## File Structure

```
Docker/
├── README.md                    # This file
├── api/
│   ├── Dockerfile              # .NET API build configuration
│   └── env.example            # API environment variables template
├── web/
│   ├── Dockerfile              # Next.js build configuration
│   └── env.example            # Frontend environment variables template
└── db/
    └── init/                  # Database initialization scripts (if needed)
```

## Support

For issues with the Docker setup, check:
1. Docker and Docker Compose versions
2. Available system resources
3. Port availability
4. Service logs using `docker-compose logs`
