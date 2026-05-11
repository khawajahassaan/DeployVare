import axios from 'axios';

// API Base URL - update this to match your API
const API_BASE_URL = process.env.NEXT_PUBLIC_API_URL || 'http://localhost:5233/api';

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

export interface Application {
  id: number;
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

  // Applications
  async getApplications(): Promise<Application[]> {
    const response = await apiClient.get<Application[]>('/applications');
    return response.data;
  },

  async getApplication(id: number): Promise<Application> {
    const response = await apiClient.get<Application>(`/applications/${id}`);
    return response.data;
  },

  async createApplication(data: { name: string; description?: string }): Promise<Application> {
    const response = await apiClient.post<Application>('/applications', data);
    return response.data;
  },

  async deleteApplication(id: number): Promise<void> {
    await apiClient.delete(`/applications/${id}`);
  },

  // Database Instances
  async getDatabaseInstances(): Promise<DatabaseInstance[]> {
    const response = await apiClient.get<DatabaseInstance[]>('/databaseinstances');
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
  async getDeployments(page = 1, pageSize = 20): Promise<{
    data: Deployment[];
    page: number;
    pageSize: number;
    totalCount: number;
    totalPages: number;
  }> {
    const response = await apiClient.get('/deployments', {
      params: { page, pageSize },
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
  async getApplicationServers(): Promise<ApplicationServer[]> {
    const response = await apiClient.get<ApplicationServer[]>('/applicationservers');
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

