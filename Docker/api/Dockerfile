# Build stage
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

# Copy csproj and restore dependencies
COPY API/API.csproj API/
RUN dotnet restore API/API.csproj

# Copy everything else and build
COPY API/ API/
WORKDIR /src/API
RUN dotnet build -c Release --no-restore
RUN dotnet publish -c Release -o /app/publish --no-build

# Runtime stage
FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS runtime
WORKDIR /app

# Install required packages for SSH and other operations
RUN apt-get update && apt-get install -y \
    openssh-client \
    && rm -rf /var/lib/apt/lists/*

# Create non-root user
RUN groupadd -r appuser && useradd -r -g appuser appuser

# Create directories for uploads and backups
RUN mkdir -p /app/wwwroot/uploads /app/wwwroot/backups /app/logs && \
    chown -R appuser:appuser /app

# Copy published app
COPY --from=build /app/publish .

# Switch to non-root user
USER appuser

# Expose port
EXPOSE 80
EXPOSE 443

# Set the entry point
ENTRYPOINT ["dotnet", "API.dll"]
