import { DeploymentStep } from '@/components/DeploymentStepsPanel';

export function getDeploymentSteps(serverType: 'Api' | 'Frontend' = 'Api'): DeploymentStep[] {
  return [
    {
      id: 'extract',
      name: 'Extract Package',
      status: 'pending',
      estimatedMinutes: 1,
      folder: 'C:\\Deploy\\',
      command: 'unzip -r AuditVare-package.zip',
      instructions: 'Once extracted, navigate to the package folder: cd AuditVare',
    },
    {
      id: 'navigate',
      name: 'Navigate to Package',
      status: 'pending',
      estimatedMinutes: 0,
      folder: 'C:\\',
      command: 'cd Deploy\\AuditVare',
      instructions: 'You are now in the deployment package directory',
    },
    {
      id: 'read-instructions',
      name: 'Read Deployment Instructions',
      status: 'pending',
      estimatedMinutes: 1,
      folder: 'C:\\Deploy\\AuditVare\\',
      command: 'Get-Content .\\README.txt',
      instructions: 'Review the deployment steps and prerequisites',
    },
    {
      id: 'run-deploy',
      name: 'Run Deployment Script',
      status: 'pending',
      estimatedMinutes: serverType === 'Api' ? 15 : 10,
      folder: 'C:\\Deploy\\AuditVare\\',
      command: 'powershell -ExecutionPolicy Bypass -File .\\Deploy.ps1',
      instructions: serverType === 'Api'
        ? 'The script will install IIS, SQL Server, configure database, and deploy the application. Monitor the output for any errors.'
        : 'The script will install IIS, download .NET runtime, and deploy the frontend. Monitor the output for any errors.',
    },
    {
      id: 'verify-services',
      name: 'Verify Services are Running',
      status: 'pending',
      estimatedMinutes: 2,
      folder: 'C:\\',
      command: 'Get-Service | Where-Object {$_.Name -like "*AuditVare*" -or $_.Name -like "*IIS*"}',
      instructions: 'Check that all services are in "Running" state',
    },
    {
      id: 'test-site',
      name: 'Test Site Access',
      status: 'pending',
      estimatedMinutes: 1,
      folder: 'Browser',
      command: serverType === 'Api'
        ? 'https://your-server-name/api/health'
        : 'https://your-server-name/',
      instructions: 'Open your browser and navigate to your server. You should see the application responding without errors.',
    },
    {
      id: 'check-logs',
      name: 'Check Application Logs',
      status: 'pending',
      estimatedMinutes: 2,
      folder: serverType === 'Api'
        ? 'C:\\inetpub\\wwwroot\\AuditVareBackend\\logs\\'
        : 'C:\\inetpub\\wwwroot\\AuditVarieFrontend\\logs\\',
      command: 'Get-ChildItem -Path . -Filter "*.log" | Get-Content | Select-Object -Last 50',
      instructions: 'Review recent log entries for any warnings or errors',
    },
    {
      id: 'deployment-complete',
      name: 'Deployment Complete',
      status: 'pending',
      estimatedMinutes: 0,
      folder: '-',
      command: 'All steps completed successfully!',
      instructions: 'Your application is now deployed and running. Contact support if you encounter any issues.',
    },
  ];
}
