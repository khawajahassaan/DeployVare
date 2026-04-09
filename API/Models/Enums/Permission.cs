namespace API.Models.Enums;

public enum Permission
{
    // User Management
    ManageUsers,
    
    // Application & Instance Management
    ManageApplications,
    ManageDatabaseInstances,
    
    // SQL Deployment
    DeploySql,
    ViewSqlHistory,
    CompareSchemas,
    
    // API Deployment
    ManageServers,
    DeployApi,
    ViewApiHistory
}




