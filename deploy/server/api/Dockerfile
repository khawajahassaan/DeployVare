# Packs the API published by deploy.sh (app/); nothing is compiled on the server.
FROM mcr.microsoft.com/dotnet/aspnet:10.0
WORKDIR /app
COPY app/ .
RUN mkdir -p /app/wwwroot/uploads /app/wwwroot/backups /app/logs && chown -R app:app /app
USER app
EXPOSE 8080
ENTRYPOINT ["dotnet", "API.dll"]
