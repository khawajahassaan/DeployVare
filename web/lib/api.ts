import axios from 'axios';

// API Base URL. A server install writes it into /config.js at deploy time (window.__DEPLOYVARE_CONFIG__.apiUrl),
// so one static build works under any host name; otherwise the build-time variable, then local development.
const BUILD_API_URL = process.env.NEXT_PUBLIC_API_URL || 'http://localhost:5233/api';
const resolveApiBaseUrl = () =>
  (typeof window !== 'undefined'
    ? (window as Window & { __DEPLOYVARE_CONFIG__?: { apiUrl?: string } }).__DEPLOYVARE_CONFIG__?.apiUrl
    : undefined) || BUILD_API_URL;
const API_BASE_URL = resolveApiBaseUrl();

// Log the API URL for debugging (only in development)
if (typeof window !== 'undefined' && process.env.NODE_ENV === 'development') {
  console.log('🚀 DeployVare API URL:', API_BASE_URL);
}

// Create axios instance
const apiClient = axios.create({
  baseURL: API_BASE_URL,
  headers: {
    'Content-Type': 'application/json',
  },
});

// Request interceptor to add auth token
apiClient.interceptors.request.use(
  (config) => {
    // Read per request, so /config.js is honoured however early this module was evaluated
    config.baseURL = resolveApiBaseUrl();

    const token = localStorage.getItem('token');
    if (token) {
      config.headers.Authorization = `Bearer ${token}`;
    }
    
    // If sending FormData, remove Content-Type to let axios set it with boundary
    if (config.data instanceof FormData) {
      delete config.headers['Content-Type'];
    }
    
    return config;
  },
  (error) => Promise.reject(error)
);

// Response interceptor to handle errors
apiClient.interceptors.response.use(
  (response) => response,
  (error) => {
    // Only redirect on 401 if NOT a login request (to avoid reload on wrong credentials)
    const isLoginRequest = error.config?.url?.includes('/auth/login');
    
    if (error.response?.status === 401 && !isLoginRequest) {
      // Unauthorized - clear token and redirect to login
      localStorage.removeItem('token');
      localStorage.removeItem('user');
      localStorage.removeItem('auth-storage');
      if (typeof window !== 'undefined') {
        window.location.href = '/login';
      }
    }
    return Promise.reject(error);
  }
);

// Types
export interface LoginRequest {
  username: string;
  password: string;
}

export interface LoginResponse {
  token: string;
  username: string;
  fullName?: string;
  role: string;
  permissions: string[];
  expiresAt: string;
}

export interface Client {
  id: number;
  name: string;
  clientCode: string;
  licenseKey: string;
  isActive: boolean;
  createdAt: string;
  createdBy: string;
  updatedAt?: string;
  applicationCount: number;
}

export interface Application {
  id: number;
  clientId: number;
  clientName: string;
  name: string;
  description?: string;
  createdAt: string;
  createdBy: string;
  instanceCount: number;
}

export interface DatabaseInstance {
  id: number;
  applicationId: number;
  applicationName: string;
  instanceName: string;
  currentPatchNumber: number;
  isActive: boolean;
  createdAt: string;
  createdBy: string;
  updatedAt?: string;
  updatedBy?: string;
}

export interface Deployment {
  id: number;
  databaseInstanceId: number;
  instanceName: string;
  applicationName: string;
  deployedBy: string;
  deployedAt: string;
  status: string;
  fromPatchNumber: number;
  toPatchNumber: number;
  backupFilePath?: string;
  errorMessage?: string;
  durationSeconds?: number;
  patches?: DeploymentPatch[];
  objects?: DeploymentObject[];
}

export interface DeploymentPatch {
  patchNumber: number;
  patchFileName: string;
  description?: string;
  author?: string;
  executedAt: string;
  status: string;
  errorMessage?: string;
}

export interface DeploymentObject {
  objectType: string;
  objectName: string;
  backedUp: boolean;
  executedAt: string;
}

export interface SchemaComparison {
  source: DatabaseSchemaInfo;
  target: DatabaseSchemaInfo;
  tableDifferences: SchemaObjectDiff[];
  procedureDifferences: SchemaObjectDiff[];
  functionDifferences: SchemaObjectDiff[];
}

export interface DatabaseSchemaInfo {
  instanceName: string;
  applicationName: string;
  currentPatchNumber: number;
}

export interface SchemaObjectDiff {
  objectName: string;
  diffType: string;
  sourceDefinition?: string;
  targetDefinition?: string;
  changes: string[];
}

export interface ProcedureDetail {
  procedureName: string;
  schema: string;
  sourceDefinition: string;
  targetDefinition: string;
  diff: string;
  status: string; // "Modified", "Added", "Removed"
}

export interface TransferResult {
  deploymentId: number;
  success: boolean;
  message: string;
  errorMessage?: string;
}

// API Deployment Types
export interface ApplicationServer {
  id: number;
  applicationId: number;
  applicationName: string;
  serverName: string;
  hostName: string;
  port: number;
  username: string;
  deploymentPath: string;
  appPoolName: string;
  websiteName: string;
  backupPath: string;
  serverType: 'Api' | 'Frontend';
  isActive: boolean;
  createdAt: string;
  updatedAt?: string;
  totalDeployments: number;
  lastDeploymentDate?: string;
}

export interface ApiDeployment {
  id: number;
  applicationServerId: number;
  serverName: string;
  applicationName: string;
  deployedBy: string;
  deploymentDate: string;
  version?: string;
  status: string;
  deploymentType: 'Api' | 'Frontend';
  licensingMode: 'WithoutLicense' | 'WithLicense';
  licenseId?: string;
  artifactEnvironment?: string;
  azureBuildId?: number;
  azureBuildNumber?: string;
  azureArtifactName?: string;
  sourceBranch?: string;
  sourceVersion?: string;
  backupFileName?: string;
  errorMessage?: string;
  deploymentDurationSeconds?: number;
  startedAt?: string;
  completedAt?: string;
  files: ApiDeploymentFile[];
}

export interface ApiDeploymentFile {
  id: number;
  fileName: string;
  fileSize: number;
  fileHash?: string;
  uploadedAt: string;
}

export interface SigningKey {
  exists: boolean;
  keyId?: string | null;
  algorithm?: string | null;
  /** Public key to embed in the licensed application so it can verify signatures offline. */
  publicKey?: string | null;
  createdAt?: string | null;
  createdBy?: string | null;
  location: string;
  protection?: string | null;
}

export interface LicenseActivation {
  id: number;
  machineFingerprint: string;
  machineName?: string | null;
  serverIdentity?: string | null;
  status: string;
  activatedAt: string;
  deactivatedAt?: string | null;
  lastIssuedAt?: string | null;
  activatedBy: string;
  deactivationReason?: string | null;
}

export interface License {
  id: number;
  clientId: number;
  clientName: string;
  customerId: string;
  licenseId: string;
  product: string;
  status: string;
  issuedAt: string;
  expiresAt: string;
  isExpired: boolean;
  maxActivations: number;
  features: string[];
  notes?: string | null;
  keyId: string;
  createdBy: string;
  createdAt: string;
  updatedAt?: string | null;
  currentActivation?: LicenseActivation | null;
  previousActivations: LicenseActivation[];
  fileName: string;
}

export interface LicensingFile {
  fileName: string;
  content: string;
}

export interface ActivationReview {
  canApprove: boolean;
  outcome: string;
  message: string;
  licenseId: string;
  product: string;
  customerId: string;
  clientName?: string | null;
  machineFingerprint: string;
  machineName: string;
  serverIdentity?: string | null;
  requestedAt: string;
  licenseExpiresAt?: string | null;
  currentActivation?: LicenseActivation | null;
}

export interface ActivationApproval {
  review: ActivationReview;
  responseFile: LicensingFile;
  license: License;
}

export type DeploymentStageState = 'Pending' | 'Running' | 'Succeeded' | 'Warning' | 'Skipped' | 'Failed';

export interface DeploymentStage {
  key: string;
  label: string;
  state: DeploymentStageState;
  detail?: string | null;
  startedAt?: string | null;
  completedAt?: string | null;
}

export type BackgroundOperationStatus = 'Running' | 'Succeeded' | 'Failed';

/** A long action running on the server outside the request that started it. */
export interface BackgroundOperation {
  operationId: string;
  kind: 'deployment' | 'package' | string;
  status: BackgroundOperationStatus;
  error?: string | null;
  startedAt: string;
  completedAt?: string | null;
  result?: unknown;
  fileName?: string | null;
  fileSizeBytes?: number | null;
  downloadToken?: string | null;
  /** Every zip of a package: one per tier for two- and three-tier downloads (download with operationDownloadUrl(..., index)). */
  files?: BackgroundOperationFile[];
}

export interface BackgroundOperationFile {
  index: number;
  fileName: string;
  sizeBytes: number;
  tier?: 'server' | 'database' | 'api' | 'frontend' | string | null;
}

/** thisServer: one zip. twoTier: API (+ database) and frontend zips. threeTier: database, API and frontend zips. */
export type PackageLayout = 'ThisServer' | 'TwoTier' | 'ThreeTier';

export interface AzureArtifactDeployRequest {
  applicationServerId: number;
  environment: ArtifactEnvironment;
  buildId: number;
  artifactName: string;
  version?: string;
  progressId?: string;
  // Omitted means WithoutLicense on the server, so an older caller deploys exactly as before
  licensingMode?: 'WithoutLicense' | 'WithLicense';
  licenseId?: number;
}

export interface PackageRequest {
  applicationServerId: number;
  environment: ArtifactEnvironment;
  buildId: number;
  artifactName: string;
  licensingMode?: 'WithoutLicense' | 'WithLicense';
  licenseId?: number;
  freshDatabase?: boolean;
  certificateMode?: 'Default' | 'UserProvided';
  siteHostName?: string;
  sqlInstanceName?: string;
  databaseName?: string;
  certificateFile?: File | null;
  layout?: PackageLayout;
  counterpartServerId?: number;
  counterpartSiteHostName?: string;
  /** The server reports its build stages under this ID (getDeploymentProgress) */
  progressId?: string;
  /** Off leaves the offline installers out (servers that already have the prerequisites). Default on. */
  includeInstallers?: boolean;
}

function buildPackageForm(data: PackageRequest): FormData {
  const form = new FormData();
  form.append('applicationServerId', String(data.applicationServerId));
  form.append('environment', data.environment);
  form.append('buildId', String(data.buildId));
  form.append('artifactName', data.artifactName);
  form.append('licensingMode', data.licensingMode ?? 'WithoutLicense');
  if (data.licenseId) form.append('licenseId', String(data.licenseId));
  form.append('freshDatabase', data.freshDatabase ? 'true' : 'false');
  form.append('certificateMode', data.certificateMode ?? 'Default');
  if (data.siteHostName) form.append('siteHostName', data.siteHostName);
  if (data.sqlInstanceName) form.append('sqlInstanceName', data.sqlInstanceName);
  if (data.databaseName) form.append('databaseName', data.databaseName);
  if (data.certificateFile) form.append('certificateFile', data.certificateFile);
  form.append('layout', data.layout ?? 'ThisServer');
  form.append('includeInstallers', data.includeInstallers === false ? 'false' : 'true');
  if (data.counterpartServerId) form.append('counterpartServerId', String(data.counterpartServerId));
  if (data.counterpartSiteHostName) form.append('counterpartSiteHostName', data.counterpartSiteHostName);
  if (data.progressId) form.append('progressId', data.progressId);
  return form;
}

export interface DeploymentProgress {
  progressId: string;
  deploymentId: number;
  startedAt: string;
  completedAt?: string | null;
  isCompleted: boolean;
  succeeded?: boolean | null;
  errorMessage?: string | null;
  stages: DeploymentStage[];
}

export interface ApiDeploymentResult {
  deploymentId: number;
  status: string;
  message: string;
  backupFileName?: string;
  deploymentDurationSeconds: number;
  deploymentSteps: string[];
  errorMessage?: string;
  startedAt: string;
  completedAt: string;
  deployedFiles: ApiDeploymentFile[];
}

// Azure DevOps Types
export type ArtifactEnvironment = 'Dev' | 'UAT';

export interface AzureDevOpsConnection {
  id: number;
  clientId: number;
  organization: string;
  project: string;
  hasPersonalAccessToken: boolean;
  createdAt: string;
  createdBy: string;
  updatedAt?: string;
  updatedBy?: string;
  lastTestedAt?: string;
  lastTestSucceeded?: boolean;
}

export interface AzureDevOpsTestResult {
  success: boolean;
  message: string;
}

export interface ArtifactSource {
  id: number;
  applicationId: number;
  applicationName: string;
  environment: ArtifactEnvironment;
  serverType: 'Api' | 'Frontend';
  repository: string;
  branch: string;
  pipelineDefinitionId?: number;
  artifactName?: string;
  artifactSubfolder?: string;
  preserveWebConfig: boolean;
  keepOnServerPaths?: string | null;
  runDatabaseMigrations: boolean;
  sqlScriptsArtifactName?: string | null;
  createdAt: string;
  createdBy: string;
  updatedAt?: string;
}

export interface SaveArtifactSourceRequest {
  applicationId: number;
  environment: ArtifactEnvironment;
  serverType: 'Api' | 'Frontend';
  repository: string;
  branch: string;
  pipelineDefinitionId?: number | null;
  artifactName?: string | null;
  artifactSubfolder?: string | null;
  preserveWebConfig: boolean;
  keepOnServerPaths?: string | null;
  runDatabaseMigrations: boolean;
  sqlScriptsArtifactName?: string | null;
}

export interface AzureBuildArtifact {
  name: string;
  resourceType: string;
  sizeBytes?: number;
  isDownloadable: boolean;
}

export interface AzureBuild {
  id: number;
  buildNumber: string;
  status: string;
  result: string;
  pipelineId: number;
  pipelineName: string;
  repository: string;
  sourceBranch: string;
  sourceVersion: string;
  startTime?: string;
  finishTime?: string;
  requestedBy?: string;
  webUrl?: string;
  artifacts: AzureBuildArtifact[];
}

export interface LatestBuildResult {
  source: ArtifactSource;
  build: AzureBuild;
  selectedArtifactName?: string;
}

// Server Setup Types
export type ReadinessStatus = 'Pass' | 'Fail' | 'Warning' | 'Skipped';
export type ServerComponent = 'HostingBundle' | 'DotNetSdk' | 'Redis' | 'UrlRewrite';
export type ReadinessFix =
  | 'InstallIis'
  | 'CreateSite'
  | 'InstallComponent'
  | 'ConfigureCertificate'
  | 'ConfigureBindings'
  | 'UpdateDeploymentPath'
  | 'ConfigureDatabase'
  | 'GrantDatabaseAccess'
  | 'ConfigureRuntimeSettings';

export interface DatabaseAccess {
  databaseInstanceId?: number | null;
  instanceName?: string | null;
  dataSource?: string | null;
  databaseName?: string | null;
  loginName: string;
  configurationError?: string | null;
  sqlOnSameServer?: boolean | null;
  connected?: boolean | null;
  connectionError?: string | null;
  connectedAs?: string | null;
  isSysadmin?: boolean | null;
  databaseExists?: boolean | null;
  loginExists?: boolean | null;
  userExists?: boolean | null;
  isDbOwner?: boolean | null;
}

export interface ReadinessItem {
  key: string;
  name: string;
  status: ReadinessStatus;
  detail: string;
  required: boolean;
  fix?: ReadinessFix | null;
  component?: ServerComponent | null;
  installerAvailable?: boolean | null;
}

export interface ServerCertificate {
  thumbprint: string;
  subject: string;
  friendlyName?: string | null;
  notAfter?: string | null;
  isExpired: boolean;
}

export interface SiteBinding {
  protocol: string;
  bindingInformation: string;
  hostName?: string | null;
  port?: number | null;
  certificateThumbprint?: string | null;
}

export interface ServerReadinessReport {
  serverId: number;
  serverName: string;
  serverType: 'Api' | 'Frontend';
  checkedAt: string;
  checkedBy?: string | null;
  isReady: boolean;
  rebootPending: boolean;
  operatingSystem?: string | null;
  sitePhysicalPath?: string | null;
  items: ReadinessItem[];
  certificates: ServerCertificate[];
  bindings: SiteBinding[];
  database?: DatabaseAccess | null;
}

export interface InstallerFile {
  component: ServerComponent;
  fileName?: string | null;
  version?: string | null;
  sizeBytes?: number | null;
  available: boolean;
  pattern: string;
  directory: string;
}

export interface SqlServerInstance {
  name: string;
  serviceName: string;
  dataSource: string;
  running: boolean;
  version?: string | null;
  edition?: string | null;
  mixedAuthentication: boolean;
  connected: boolean;
  connectionError?: string | null;
  connectedAs?: string | null;
  canManageLogins: boolean;
  databases: string[];
}

export interface SqlDatabaseUser {
  userName: string;
  userType: string;
  loginName?: string | null;
  loginDisabled: boolean;
  loginLocked: boolean;
  roles: string[];
}

/** How DeployVare connects to SQL Server. Never stored. */
export interface SqlServerConnection {
  useWindowsAuthentication: boolean;
  adminLogin?: string;
  adminPassword?: string;
}

export interface SqlServerInfo {
  installed: boolean;
  instances: SqlServerInstance[];
}

export interface RuntimeSetting {
  key: string;
  value: string;
}

export interface FrontendRuntimeConfig {
  serverId: number;
  serverType: 'Api' | 'Frontend';
  settings: RuntimeSetting[];
  standardKeys: string[];
  suggestedKeys: string[];
  configJsPreview?: string | null;
}

export interface ServerSetupActionResult {
  success: boolean;
  message: string;
  rebootRequired: boolean;
  steps: string[];
}

// User Management Types
export interface User {
  id: number;
  username: string;
  fullName?: string;
  email?: string;
  role: string;
  isActive: boolean;
  createdAt: string;
  lastLoginAt?: string;
  permissions: string[];
}

export interface CreateUserRequest {
  username: string;
  password: string;
  fullName?: string;
  email?: string;
  role: 'Admin' | 'User';
  permissions: string[];
}

export interface UpdateUserRequest {
  fullName?: string;
  email?: string;
  isActive?: boolean;
  role?: 'Admin' | 'User';
  newPassword?: string;
}

// API Methods
export const api = {
  // Auth
  async login(data: LoginRequest): Promise<LoginResponse> {
    const response = await apiClient.post<LoginResponse>('/auth/login', data);
    return response.data;
  },

  async getCurrentUser(): Promise<any> {
    const response = await apiClient.get('/auth/me');
    return response.data;
  },

  // Clients
  async getClients(): Promise<Client[]> {
    const response = await apiClient.get<Client[]>('/clients');
    return response.data;
  },

  async createClient(data: { name: string; clientCode: string }): Promise<Client> {
    const response = await apiClient.post<Client>('/clients', data);
    return response.data;
  },

  async updateClient(id: number, data: { name?: string; clientCode?: string; isActive?: boolean }): Promise<Client> {
    const response = await apiClient.put<Client>(`/clients/${id}`, data);
    return response.data;
  },

  async regenerateClientLicense(id: number): Promise<Client> {
    const response = await apiClient.post<Client>(`/clients/${id}/regenerate-license`);
    return response.data;
  },

  async deleteClient(id: number): Promise<void> {
    await apiClient.delete(`/clients/${id}`);
  },

  // Applications
  async getApplications(clientId?: number | null): Promise<Application[]> {
    const response = await apiClient.get<Application[]>('/applications', {
      params: { clientId },
    });
    return response.data;
  },

  async getApplication(id: number): Promise<Application> {
    const response = await apiClient.get<Application>(`/applications/${id}`);
    return response.data;
  },

  async createApplication(data: { clientId: number; name: string; description?: string }): Promise<Application> {
    const response = await apiClient.post<Application>('/applications', data);
    return response.data;
  },

  async deleteApplication(id: number): Promise<void> {
    await apiClient.delete(`/applications/${id}`);
  },

  // Database Instances
  async getDatabaseInstances(clientId?: number | null): Promise<DatabaseInstance[]> {
    const response = await apiClient.get<DatabaseInstance[]>('/databaseinstances', {
      params: { clientId },
    });
    return response.data;
  },

  async getDatabaseInstancesByApp(appId: number): Promise<DatabaseInstance[]> {
    const response = await apiClient.get<DatabaseInstance[]>(`/databaseinstances/by-application/${appId}`);
    return response.data;
  },

  async getDatabaseInstance(id: number): Promise<DatabaseInstance> {
    const response = await apiClient.get<DatabaseInstance>(`/databaseinstances/${id}`);
    return response.data;
  },

  async createDatabaseInstance(data: {
    applicationId: number;
    instanceName: string;
    connectionString: string;
    currentPatchNumber?: number;
  }): Promise<DatabaseInstance> {
    const response = await apiClient.post<DatabaseInstance>('/databaseinstances', data);
    return response.data;
  },

  async testConnection(id: number): Promise<{ success: boolean; message: string }> {
    const response = await apiClient.post<{ success: boolean; message: string }>(
      `/databaseinstances/${id}/test-connection`
    );
    return response.data;
  },

  async deleteDatabaseInstance(id: number): Promise<void> {
    await apiClient.delete(`/databaseinstances/${id}`);
  },

  // Deployments
  async getDeployments(page = 1, pageSize = 20, clientId?: number | null): Promise<{
    data: Deployment[];
    page: number;
    pageSize: number;
    totalCount: number;
    totalPages: number;
  }> {
    const response = await apiClient.get('/deployments', {
      params: { page, pageSize, clientId },
    });
    return response.data;
  },

  async getDeployment(id: number): Promise<Deployment> {
    const response = await apiClient.get<Deployment>(`/deployments/${id}`);
    return response.data;
  },

  async getDeploymentsByInstance(instanceId: number): Promise<Deployment[]> {
    const response = await apiClient.get<Deployment[]>(`/deployments/by-instance/${instanceId}`);
    return response.data;
  },

  async deploy(databaseInstanceId: number, zipFile: File): Promise<any> {
    const formData = new FormData();
    formData.append('databaseInstanceId', databaseInstanceId.toString());
    formData.append('zipFile', zipFile);

    // Don't manually set Content-Type for FormData - axios will set it automatically with boundary
    // This prevents overriding the Authorization header
    const response = await apiClient.post('/deployments/deploy', formData);
    return response.data;
  },

  async downloadBackup(deploymentId: number): Promise<Blob> {
    const response = await apiClient.get(`/deployments/${deploymentId}/download-backup`, {
      responseType: 'blob',
    });
    return response.data;
  },

  // Schema Comparison
  async compareSchemas(sourceInstanceId: number, targetInstanceId: number): Promise<SchemaComparison> {
    const response = await apiClient.post<SchemaComparison>('/schemacomparison/compare', {
      sourceInstanceId,
      targetInstanceId,
    });
    return response.data;
  },

  async compareWithSnap(
    sourceInstanceId: number | null,
    sourceFile: File | null,
    targetInstanceId: number | null,
    targetFile: File | null
  ): Promise<SchemaComparison> {
    const form = new FormData();
    if (sourceFile) form.append('sourceFile', sourceFile);
    if (targetFile) form.append('targetFile', targetFile);
    if (sourceInstanceId) form.append('sourceInstanceId', String(sourceInstanceId));
    if (targetInstanceId) form.append('targetInstanceId', String(targetInstanceId));
    const response = await apiClient.post<SchemaComparison>('/schemacomparison/compare-with-snap', form, {
      headers: { 'Content-Type': 'multipart/form-data' },
    });
    return response.data;
  },

  async getProcedureDetail(
    sourceInstanceId: number,
    targetInstanceId: number,
    procedureName: string,
    schema: string = 'dbo'
  ): Promise<ProcedureDetail> {
    const response = await apiClient.get<ProcedureDetail>('/schemacomparison/procedure-detail', {
      params: { sourceInstanceId, targetInstanceId, procedureName, schema },
    });
    return response.data;
  },

  async transferObject(
    sourceInstanceId: number,
    targetInstanceId: number,
    objectType: string,
    objectName: string,
    schema: string,
    direction: 'SourceToTarget' | 'TargetToSource'
  ): Promise<TransferResult> {
    const response = await apiClient.post<TransferResult>('/schemacomparison/transfer-object', {
      sourceInstanceId,
      targetInstanceId,
      objectType,
      objectName,
      schema,
      direction,
    });
    return response.data;
  },

  // Application Servers
  async getApplicationServers(clientId?: number | null): Promise<ApplicationServer[]> {
    const response = await apiClient.get<ApplicationServer[]>('/applicationservers', {
      params: { clientId },
    });
    return response.data;
  },

  async getApplicationServersByApp(appId: number): Promise<ApplicationServer[]> {
    const response = await apiClient.get<ApplicationServer[]>(`/applicationservers/application/${appId}`);
    return response.data;
  },

  async getApplicationServer(id: number): Promise<ApplicationServer> {
    const response = await apiClient.get<ApplicationServer>(`/applicationservers/${id}`);
    return response.data;
  },

  async createApplicationServer(data: {
    applicationId: number;
    serverName: string;
    hostName: string;
    port?: number;
    username: string;
    password?: string;
    sshPrivateKey?: string;
    deploymentPath: string;
    appPoolName: string;
    websiteName: string;
    backupPath?: string;
    serverType?: 'Api' | 'Frontend';
  }): Promise<ApplicationServer> {
    const response = await apiClient.post<ApplicationServer>('/applicationservers', data);
    return response.data;
  },

  async updateApplicationServer(id: number, data: {
    serverName?: string;
    hostName?: string;
    port?: number;
    username?: string;
    password?: string;
    sshPrivateKey?: string;
    deploymentPath?: string;
    appPoolName?: string;
    websiteName?: string;
    backupPath?: string;
    isActive?: boolean;
    serverType?: 'Api' | 'Frontend';
  }): Promise<ApplicationServer> {
    const response = await apiClient.put<ApplicationServer>(`/applicationservers/${id}`, data);
    return response.data;
  },

  async deleteApplicationServer(id: number): Promise<void> {
    await apiClient.delete(`/applicationservers/${id}`);
  },

  async testServerConnection(id: number): Promise<{ message: string }> {
    const response = await apiClient.post<{ message: string }>(`/applicationservers/${id}/test-connection`);
    return response.data;
  },

  // API Deployments
  async deployApi(applicationServerId: number, files: File[], version?: string): Promise<ApiDeploymentResult> {
    const formData = new FormData();
    formData.append('applicationServerId', applicationServerId.toString());
    if (version) {
      formData.append('version', version);
    }
    files.forEach(file => {
      formData.append('files', file);
    });

    // Don't manually set Content-Type for FormData - axios will set it automatically with boundary
    // This prevents overriding the Authorization header
    const response = await apiClient.post<ApiDeploymentResult>('/apideployments', formData);
    return response.data;
  },

  async getApiDeploymentHistory(applicationServerId: number, limit = 50): Promise<ApiDeployment[]> {
    const response = await apiClient.get<ApiDeployment[]>(`/apideployments/server/${applicationServerId}`, {
      params: { limit },
    });
    return response.data;
  },

  async getApiDeployment(deploymentId: number): Promise<ApiDeployment> {
    const response = await apiClient.get<ApiDeployment>(`/apideployments/${deploymentId}`);
    return response.data;
  },

  async downloadApiBackup(deploymentId: number): Promise<Blob> {
    const response = await apiClient.get(`/apideployments/${deploymentId}/backup`, {
      responseType: 'blob',
    });
    return response.data;
  },

  async deployFrontend(applicationServerId: number, zipFile: File, version?: string): Promise<ApiDeploymentResult> {
    const formData = new FormData();
    formData.append('applicationServerId', applicationServerId.toString());
    if (version) {
      formData.append('version', version);
    }
    formData.append('zipFile', zipFile);
    const response = await apiClient.post<ApiDeploymentResult>('/apideployments/frontend', formData);
    return response.data;
  },

  // Azure DevOps
  async getAzureDevOpsConnection(clientId: number): Promise<AzureDevOpsConnection | null> {
    const response = await apiClient.get<AzureDevOpsConnection | ''>('/azuredevops/connection', {
      params: { clientId },
    });
    return response.data || null;
  },

  async saveAzureDevOpsConnection(data: {
    clientId: number;
    organization: string;
    project: string;
    personalAccessToken?: string;
  }): Promise<AzureDevOpsConnection> {
    const response = await apiClient.put<AzureDevOpsConnection>('/azuredevops/connection', data);
    return response.data;
  },

  async deleteAzureDevOpsConnection(clientId: number): Promise<void> {
    await apiClient.delete(`/azuredevops/connection/${clientId}`);
  },

  async testAzureDevOpsConnection(data: {
    clientId: number;
    organization?: string;
    project?: string;
    personalAccessToken?: string;
  }): Promise<AzureDevOpsTestResult> {
    const response = await apiClient.post<AzureDevOpsTestResult>('/azuredevops/connection/test', data);
    return response.data;
  },

  async getArtifactSources(clientId: number): Promise<ArtifactSource[]> {
    const response = await apiClient.get<ArtifactSource[]>('/azuredevops/artifact-sources', {
      params: { clientId },
    });
    return response.data;
  },

  async createArtifactSource(data: SaveArtifactSourceRequest): Promise<ArtifactSource> {
    const response = await apiClient.post<ArtifactSource>('/azuredevops/artifact-sources', data);
    return response.data;
  },

  async updateArtifactSource(id: number, data: SaveArtifactSourceRequest): Promise<ArtifactSource> {
    const response = await apiClient.put<ArtifactSource>(`/azuredevops/artifact-sources/${id}`, data);
    return response.data;
  },

  async deleteArtifactSource(id: number): Promise<void> {
    await apiClient.delete(`/azuredevops/artifact-sources/${id}`);
  },

  async testArtifactSource(id: number): Promise<AzureDevOpsTestResult> {
    const response = await apiClient.post<AzureDevOpsTestResult>(`/azuredevops/artifact-sources/${id}/test`);
    return response.data;
  },

  async getLatestAzureBuild(applicationServerId: number, environment: ArtifactEnvironment): Promise<LatestBuildResult> {
    const response = await apiClient.get<LatestBuildResult>('/azuredevops/latest-build', {
      params: { applicationServerId, environment },
    });
    return response.data;
  },

  async deployAzureArtifact(data: AzureArtifactDeployRequest): Promise<ApiDeploymentResult> {
    const response = await apiClient.post<ApiDeploymentResult>('/apideployments/azure-artifact', data);
    return response.data;
  },

  // Licensing (only an installation holding the signing key can issue or approve)
  async getSigningKey(): Promise<SigningKey> {
    const response = await apiClient.get<SigningKey>('/licensing/signing-key');
    return response.data;
  },

  async createSigningKey(): Promise<SigningKey> {
    const response = await apiClient.post<SigningKey>('/licensing/signing-key');
    return response.data;
  },

  async getLicenses(clientId?: number | null): Promise<License[]> {
    const response = await apiClient.get<License[]>('/licensing/licenses', { params: { clientId } });
    return response.data;
  },

  async issueLicense(data: {
    clientId: number;
    product?: string;
    licenseId?: string;
    expiresAt?: string;
    features?: string[];
    maxActivations?: number;
    notes?: string;
  }): Promise<License> {
    const response = await apiClient.post<License>('/licensing/licenses', data);
    return response.data;
  },

  async getLicenseFile(id: number): Promise<LicensingFile> {
    const response = await apiClient.get<LicensingFile>(`/licensing/licenses/${id}/file`);
    return response.data;
  },

  async renewLicense(id: number, expiresAt: string, reason?: string): Promise<License> {
    const response = await apiClient.post<License>(`/licensing/licenses/${id}/renew`, { expiresAt, reason });
    return response.data;
  },

  /** Checks an activation request file without changing anything. */
  async reviewActivation(requestFile: string): Promise<ActivationReview> {
    const response = await apiClient.post<ActivationReview>('/licensing/activations/review', { requestFile });
    return response.data;
  },

  /** Binds the licence to the requesting machine and returns the response file to send back. */
  async approveActivation(requestFile: string): Promise<ActivationApproval> {
    const response = await apiClient.post<ActivationApproval>('/licensing/activations/approve', { requestFile });
    return response.data;
  },

  async transferLicense(id: number, reason?: string): Promise<License> {
    const response = await apiClient.post<License>(`/licensing/licenses/${id}/transfer`, { reason });
    return response.data;
  },

  async revokeLicense(id: number, reason?: string): Promise<License> {
    const response = await apiClient.post<License>(`/licensing/licenses/${id}/revoke`, { reason });
    return response.data;
  },

  async downloadDeploymentPackage(data: PackageRequest, onTransfer?: (loaded: number, total?: number) => void): Promise<{ blob: Blob; fileName: string }> {
    const response = await apiClient.post('/apideployments/azure-artifact/package', buildPackageForm(data), {
      responseType: 'blob',
      // Bytes of the finished zip arriving from the server (after the build stages)
      onDownloadProgress: onTransfer ? (event) => onTransfer(event.loaded, event.total ?? undefined) : undefined,
    });
    const disposition = String(response.headers['content-disposition'] ?? '');
    const match = /filename\*?=(?:UTF-8'')?"?([^";]+)"?/i.exec(disposition);
    return { blob: response.data as Blob, fileName: match ? decodeURIComponent(match[1]) : 'manual-deploy.zip' };
  },

  /**
   * Starts a package build in the background and returns at once. Follow it with getDeploymentProgress(progressId)
   * and getOperation(progressId); when it succeeds, download it from operationDownloadUrl(...).
   */
  async startPackageBuild(data: PackageRequest & { progressId: string }): Promise<string> {
    const response = await apiClient.post<{ operationId: string }>('/apideployments/azure-artifact/package/start', buildPackageForm(data));
    return response.data.operationId;
  },

  /** Starts an Azure DevOps deployment in the background (a closed tab or a proxy timeout cannot cancel it). */
  async startAzureDeployment(data: AzureArtifactDeployRequest & { progressId: string }): Promise<string> {
    const response = await apiClient.post<{ operationId: string }>('/apideployments/azure-artifact/start', data);
    return response.data.operationId;
  },

  /** Status and result of a background deployment or package build. */
  async getOperation(operationId: string): Promise<BackgroundOperation> {
    const response = await apiClient.get<BackgroundOperation>(`/apideployments/operations/${encodeURIComponent(operationId)}`);
    return response.data;
  },

  /** Plain link to a finished package zip (no login header needed; the token is the key). */
  operationDownloadUrl(operationId: string, token: string, fileIndex = 0): string {
    return `${resolveApiBaseUrl()}/apideployments/operations/${encodeURIComponent(operationId)}/download?token=${encodeURIComponent(token)}&file=${fileIndex}`;
  },

  /** Live progress of an Azure DevOps deployment, or null until the API has started tracking it. */
  async getDeploymentProgress(progressId: string): Promise<DeploymentProgress | null> {
    const response = await apiClient.get<DeploymentProgress | ''>(`/apideployments/progress/${encodeURIComponent(progressId)}`);
    return response.data || null;
  },

  // Server Setup
  /** Latest saved readiness report, or null when the server has never been checked. */
  async getServerReadiness(serverId: number): Promise<ServerReadinessReport | null> {
    const response = await apiClient.get<ServerReadinessReport | ''>(`/serversetup/${serverId}/readiness`);
    return response.data || null;
  },

  /** Runs the readiness check on the server and saves the result. */
  async runServerReadinessCheck(serverId: number): Promise<ServerReadinessReport> {
    const response = await apiClient.post<ServerReadinessReport>(`/serversetup/${serverId}/readiness`);
    return response.data;
  },

  async getServerInstallers(): Promise<InstallerFile[]> {
    const response = await apiClient.get<InstallerFile[]>('/serversetup/installers');
    return response.data;
  },

  async installIis(serverId: number): Promise<ServerSetupActionResult> {
    const response = await apiClient.post<ServerSetupActionResult>(`/serversetup/${serverId}/install-iis`);
    return response.data;
  },

  async installServerComponent(serverId: number, component: ServerComponent): Promise<ServerSetupActionResult> {
    const response = await apiClient.post<ServerSetupActionResult>(`/serversetup/${serverId}/install-component`, { component });
    return response.data;
  },

  async createServerSite(serverId: number, data: {
    physicalPath: string;
    hostName: string;
    useAsDeploymentPath: boolean;
  }): Promise<ServerSetupActionResult> {
    const response = await apiClient.post<ServerSetupActionResult>(`/serversetup/${serverId}/create-site`, data);
    return response.data;
  },

  async installServerCertificate(serverId: number, certificateFile: File, password?: string): Promise<ServerSetupActionResult> {
    const formData = new FormData();
    formData.append('certificateFile', certificateFile);
    if (password) {
      formData.append('password', password);
    }
    const response = await apiClient.post<ServerSetupActionResult>(`/serversetup/${serverId}/certificates`, formData);
    return response.data;
  },

  async configureServerBindings(serverId: number, data: {
    hostName: string;
    certificateThumbprint: string;
    requireSni: boolean;
  }): Promise<ServerSetupActionResult> {
    const response = await apiClient.post<ServerSetupActionResult>(`/serversetup/${serverId}/bindings`, data);
    return response.data;
  },

  async linkServerDatabaseInstance(serverId: number, databaseInstanceId: number | null): Promise<ServerSetupActionResult> {
    const response = await apiClient.post<ServerSetupActionResult>(`/serversetup/${serverId}/database-instance`, {
      databaseInstanceId,
    });
    return response.data;
  },

  async grantServerDatabaseAccess(serverId: number): Promise<ServerSetupActionResult> {
    const response = await apiClient.post<ServerSetupActionResult>(`/serversetup/${serverId}/grant-database-access`);
    return response.data;
  },

  /** Without a connection only detects instances; with one, also connects and lists databases. */
  async checkServerSqlServer(serverId: number, connection: SqlServerConnection | null): Promise<SqlServerInfo> {
    const response = await apiClient.post<SqlServerInfo>(`/serversetup/${serverId}/sql-server/check`, connection ?? {});
    return response.data;
  },

  async getServerSqlDatabaseUsers(serverId: number, data: SqlServerConnection & {
    instanceName: string;
    databaseName: string;
  }): Promise<SqlDatabaseUser[]> {
    const response = await apiClient.post<SqlDatabaseUser[]>(`/serversetup/${serverId}/sql-server/database-users`, data);
    return response.data;
  },

  async createServerSqlLogin(serverId: number, data: SqlServerConnection & {
    instanceName: string;
    databaseName: string;
    loginName: string;
    password: string;
    resetPasswordIfExists: boolean;
  }): Promise<ServerSetupActionResult> {
    const response = await apiClient.post<ServerSetupActionResult>(`/serversetup/${serverId}/sql-server/logins`, data);
    return response.data;
  },

  async enableServerSqlAuthentication(serverId: number, instanceName: string): Promise<ServerSetupActionResult> {
    const response = await apiClient.post<ServerSetupActionResult>(
      `/serversetup/${serverId}/sql-server/enable-sql-authentication`,
      { instanceName }
    );
    return response.data;
  },

  async getServerRuntimeConfig(serverId: number): Promise<FrontendRuntimeConfig> {
    const response = await apiClient.get<FrontendRuntimeConfig>(`/serversetup/${serverId}/runtime-config`);
    return response.data;
  },

  async saveServerRuntimeConfig(serverId: number, settings: RuntimeSetting[]): Promise<FrontendRuntimeConfig> {
    const response = await apiClient.put<FrontendRuntimeConfig>(`/serversetup/${serverId}/runtime-config`, { settings });
    return response.data;
  },

  async applyServerRuntimeConfig(serverId: number): Promise<ServerSetupActionResult> {
    const response = await apiClient.post<ServerSetupActionResult>(`/serversetup/${serverId}/runtime-config/apply`);
    return response.data;
  },

  async syncServerDeploymentPath(serverId: number): Promise<ServerSetupActionResult> {
    const response = await apiClient.post<ServerSetupActionResult>(`/serversetup/${serverId}/sync-deployment-path`);
    return response.data;
  },

  // User Management
  async getUsers(): Promise<User[]> {
    const response = await apiClient.get<User[]>('/users');
    return response.data;
  },

  async getUserById(userId: number): Promise<User> {
    const response = await apiClient.get<User>(`/users/${userId}`);
    return response.data;
  },

  async createUser(user: CreateUserRequest): Promise<User> {
    const response = await apiClient.post<User>('/users', user);
    return response.data;
  },

  async updateUser(userId: number, user: UpdateUserRequest): Promise<User> {
    const response = await apiClient.put<User>(`/users/${userId}`, user);
    return response.data;
  },

  async deleteUser(userId: number): Promise<void> {
    await apiClient.delete(`/users/${userId}`);
  },

  async getUserPermissions(userId: number): Promise<{ permissions: string[] }> {
    const response = await apiClient.get<{ permissions: string[] }>(`/users/${userId}/permissions`);
    return response.data;
  },

  async updateUserPermissions(userId: number, permissions: string[]): Promise<void> {
    await apiClient.put(`/users/${userId}/permissions`, { permissions });
  },

  async getAvailablePermissions(): Promise<{ value: string; label: string }[]> {
    const response = await apiClient.get<{ value: string; label: string }[]>('/users/available-permissions');
    return response.data;
  },
};

export default api;

