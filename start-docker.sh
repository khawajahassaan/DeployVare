#!/bin/bash

# DeployVare Docker Startup Script

set -e

echo "🚀 Starting DeployVare with Docker Compose..."

# Check if Docker is running
if ! docker info > /dev/null 2>&1; then
    echo "❌ Docker is not running. Please start Docker first."
    exit 1
fi

# Check if Docker Compose is available
if ! command -v docker-compose &> /dev/null; then
    echo "❌ Docker Compose is not installed."
    exit 1
fi

# Create environment files if they don't exist
if [ ! -f ".env" ]; then
    echo "📋 Creating .env file from template..."
    cp docker-compose.env.example .env
fi

if [ ! -f "Docker/api/.env" ]; then
    echo "📋 Creating API environment file..."
    cp Docker/api/env.example Docker/api/.env
fi

if [ ! -f "Docker/web/.env" ]; then
    echo "📋 Creating web environment file..."
    cp Docker/web/env.example Docker/web/.env
fi

# Stop any existing containers
echo "🛑 Stopping existing containers..."
docker-compose down

# Build and start services
echo "🔨 Building and starting services..."
docker-compose up -d --build

# Wait for services to be healthy
echo "⏳ Waiting for services to start..."
echo "   - Database (may take 30-60 seconds)..."

# Wait for database
timeout=60
counter=0
while [ $counter -lt $timeout ]; do
    if docker-compose exec -T db /opt/mssql-tools/bin/sqlcmd -S localhost -U sa -P "YourStrong!Passw0rd" -Q "SELECT 1" > /dev/null 2>&1; then
        break
    fi
    counter=$((counter + 5))
    sleep 5
    echo "   Still waiting... (${counter}s/${timeout}s)"
done

if [ $counter -ge $timeout ]; then
    echo "❌ Database failed to start within ${timeout} seconds"
    echo "📋 Check logs with: docker-compose logs db"
    exit 1
fi

# Wait a bit more for API to initialize
echo "⏳ Waiting for API to initialize..."
sleep 10

echo "✅ DeployVare is now running!"
echo ""
echo "🌐 Frontend: http://localhost:3000"
echo "🔌 API: http://localhost:8080"
echo "📊 API Docs: http://localhost:8080/swagger"
echo "🗄️  Database: localhost:1433"
echo ""
echo "📋 Default admin credentials:"
echo "   Username: admin"
echo "   Password: Admin123!"
echo ""
echo "🛑 To stop: docker-compose down"
echo "📜 View logs: docker-compose logs -f"
echo "🔄 Restart: docker-compose restart"
