namespace API.Services.Interfaces;

public interface IBackupService
{
    Task<string> BackupDatabaseObjectsAsync(string connectionString, List<string> procedureNames, List<string> functionNames, int deploymentId);
    string GetBackupFilePath(int deploymentId);
}




