'use client';

import { useState, useEffect } from 'react';
import ProtectedLayout from '@/components/ProtectedLayout';
import { Card, CardContent } from '@/components/ui/card';
import { Button } from '@/components/ui/button';
import { Input } from '@/components/ui/input';
import { Label } from '@/components/ui/label';
import { Badge } from '@/components/ui/badge';
import { Dialog, DialogContent, DialogDescription, DialogHeader, DialogTitle } from '@/components/ui/dialog';
import { Select } from '@/components/ui/select';
import api, { User, CreateUserRequest, UpdateUserRequest } from '@/lib/api';
import { PageHeader } from '@/components/ui/page-header';
import { LoadingState } from '@/components/ui/loading-state';
import { Plus, Edit, Trash2, Shield, User as UserIcon, Users } from 'lucide-react';
import { useAuthStore, AuthState } from '@/store/authStore';

export default function UsersPage() {
  const [users, setUsers] = useState<User[]>([]);
  const [availablePermissions, setAvailablePermissions] = useState<{ value: string; label: string }[]>([]);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState('');
  
  // Dialogs
  const [showCreateDialog, setShowCreateDialog] = useState(false);
  const [showEditDialog, setShowEditDialog] = useState(false);
  const [showPermissionsDialog, setShowPermissionsDialog] = useState(false);
  const [showDeleteDialog, setShowDeleteDialog] = useState(false);
  
  // Selected user
  const [selectedUser, setSelectedUser] = useState<User | null>(null);
  
  // Form data
  const [formData, setFormData] = useState<CreateUserRequest>({
    username: '',
    password: '',
    fullName: '',
    email: '',
    role: 'User',
    permissions: []
  });
  
  const [editFormData, setEditFormData] = useState<UpdateUserRequest>({
    fullName: '',
    email: '',
    role: 'User',
    isActive: true,
  });
  
  const [selectedPermissions, setSelectedPermissions] = useState<string[]>([]);
  
  const currentUser = useAuthStore((state: AuthState) => state.user);

  useEffect(() => {
    loadData();
  }, []);

  const loadData = async () => {
    try {
      setLoading(true);
      const [usersData, permissionsData] = await Promise.all([
        api.getUsers(),
        api.getAvailablePermissions()
      ]);
      setUsers(usersData);
      setAvailablePermissions(permissionsData);
    } catch (err: any) {
      setError(err.response?.data?.message || 'Failed to load data');
    } finally {
      setLoading(false);
    }
  };

  const handleCreateUser = async () => {
    try {
      setError('');
      await api.createUser(formData);
      setShowCreateDialog(false);
      setFormData({
        username: '',
        password: '',
        fullName: '',
        email: '',
        role: 'User',
        permissions: []
      });
      await loadData();
    } catch (err: any) {
      setError(err.response?.data?.message || 'Failed to create user');
    }
  };

  const handleEditUser = async () => {
    if (!selectedUser) return;
    
    try {
      setError('');
      await api.updateUser(selectedUser.id, editFormData);
      setShowEditDialog(false);
      setSelectedUser(null);
      await loadData();
    } catch (err: any) {
      setError(err.response?.data?.message || 'Failed to update user');
    }
  };

  const handleUpdatePermissions = async () => {
    if (!selectedUser) return;
    
    try {
      setError('');
      await api.updateUserPermissions(selectedUser.id, selectedPermissions);
      setShowPermissionsDialog(false);
      setSelectedUser(null);
      await loadData();
    } catch (err: any) {
      setError(err.response?.data?.message || 'Failed to update permissions');
    }
  };

  const handleDeleteUser = async () => {
    if (!selectedUser) return;
    
    try {
      setError('');
      await api.deleteUser(selectedUser.id);
      setShowDeleteDialog(false);
      setSelectedUser(null);
      await loadData();
    } catch (err: any) {
      setError(err.response?.data?.message || 'Failed to delete user');
    }
  };

  const openEditDialog = (user: User) => {
    setSelectedUser(user);
    setEditFormData({
      fullName: user.fullName || '',
      email: user.email || '',
      role: user.role as 'Admin' | 'User',
      isActive: user.isActive,
    });
    setShowEditDialog(true);
  };

  const openPermissionsDialog = async (user: User) => {
    setSelectedUser(user);
    setSelectedPermissions(user.permissions);
    setShowPermissionsDialog(true);
  };

  const openDeleteDialog = (user: User) => {
    setSelectedUser(user);
    setShowDeleteDialog(true);
  };

  const togglePermission = (permission: string) => {
    if (selectedPermissions.includes(permission)) {
      setSelectedPermissions(selectedPermissions.filter(p => p !== permission));
    } else {
      setSelectedPermissions([...selectedPermissions, permission]);
    }
  };

  if (loading) {
    return (
      <ProtectedLayout>
        <LoadingState label="Loading users..." />
      </ProtectedLayout>
    );
  }

  return (
    <ProtectedLayout>
      <div className="space-y-6">
        <PageHeader
          title="User Management"
          description="Manage users and their permissions"
          icon={Users}
          actions={
            <Button onClick={() => setShowCreateDialog(true)}>
              <Plus className="h-4 w-4" />
              Add User
            </Button>
          }
        />

        {error && (
          <div className="rounded-lg border border-red-200 bg-red-50 p-3 text-sm text-red-700">
            {error}
          </div>
        )}

        <div className="grid gap-4 sm:grid-cols-2 xl:grid-cols-3">
          {users.map((user) => (
            <Card key={user.id} className="transition-shadow hover:shadow-md">
              <CardContent className="pt-6">
                <div className="flex items-start justify-between gap-3">
                  <div className="flex min-w-0 flex-1 items-start gap-3">
                    <div className="shrink-0 rounded-full bg-primary/10 p-2.5">
                      {user.role === 'Admin' ? (
                        <Shield className="h-5 w-5 text-primary" />
                      ) : (
                        <UserIcon className="h-5 w-5 text-muted-foreground" />
                      )}
                    </div>

                    <div className="min-w-0 flex-1">
                      <div className="mb-1 flex flex-wrap items-center gap-2">
                        <h3 className="truncate text-base font-semibold text-foreground">{user.username}</h3>
                        <Badge variant={user.role === 'Admin' ? 'default' : 'secondary'}>
                          {user.role}
                        </Badge>
                        {!user.isActive && (
                          <Badge variant="destructive">Inactive</Badge>
                        )}
                      </div>
                      
                      {user.fullName && (
                        <p className="text-sm text-muted-foreground">{user.fullName}</p>
                      )}
                      
                      {user.email && (
                        <p className="truncate text-sm text-muted-foreground">{user.email}</p>
                      )}
                      
                      <div className="mt-3 flex flex-wrap gap-1">
                        {user.role === 'Admin' ? (
                          <Badge variant="outline" className="text-xs">All Permissions</Badge>
                        ) : user.permissions.length > 0 ? (
                          user.permissions.map((perm) => (
                            <Badge key={perm} variant="outline" className="text-xs">
                              {perm.replace(/([A-Z])/g, ' $1').trim()}
                            </Badge>
                          ))
                        ) : (
                          <Badge variant="outline" className="text-xs text-muted-foreground">
                            No permissions
                          </Badge>
                        )}
                      </div>
                    </div>
                  </div>

                  <div className="flex shrink-0 gap-1">
                    <Button
                      variant="ghost"
                      size="sm"
                      onClick={() => openEditDialog(user)}
                      disabled={user.username === currentUser?.username}
                    >
                      <Edit className="h-4 w-4" />
                    </Button>
                    <Button
                      variant="ghost"
                      size="sm"
                      onClick={() => openPermissionsDialog(user)}
                      disabled={user.role === 'Admin'}
                    >
                      <Shield className="h-4 w-4" />
                    </Button>
                    <Button
                      variant="ghost"
                      size="sm"
                      onClick={() => openDeleteDialog(user)}
                      disabled={user.username === currentUser?.username}
                    >
                      <Trash2 className="h-4 w-4 text-destructive" />
                    </Button>
                  </div>
                </div>
              </CardContent>
            </Card>
          ))}
        </div>

        {/* Create User Dialog */}
        <Dialog open={showCreateDialog} onOpenChange={setShowCreateDialog}>
          <DialogContent className="max-w-md">
            <DialogHeader>
              <DialogTitle>Create New User</DialogTitle>
              <DialogDescription>Add a new user to the system</DialogDescription>
            </DialogHeader>
            
            <div className="space-y-4">
              <div>
                <Label htmlFor="username">Username *</Label>
                <Input
                  id="username"
                  value={formData.username}
                  onChange={(e) => setFormData({ ...formData, username: e.target.value })}
                  placeholder="Enter username"
                />
              </div>

              <div>
                <Label htmlFor="password">Password *</Label>
                <Input
                  id="password"
                  type="password"
                  value={formData.password}
                  onChange={(e) => setFormData({ ...formData, password: e.target.value })}
                  placeholder="Enter password"
                />
              </div>

              <div>
                <Label htmlFor="fullName">Full Name</Label>
                <Input
                  id="fullName"
                  value={formData.fullName}
                  onChange={(e) => setFormData({ ...formData, fullName: e.target.value })}
                  placeholder="Enter full name"
                />
              </div>

              <div>
                <Label htmlFor="email">Email</Label>
                <Input
                  id="email"
                  type="email"
                  value={formData.email}
                  onChange={(e) => setFormData({ ...formData, email: e.target.value })}
                  placeholder="Enter email"
                />
              </div>

              <div>
                <Label htmlFor="role">Role</Label>
                <Select
                  id="role"
                  value={formData.role}
                  onChange={(e) => setFormData({ ...formData, role: e.target.value as 'Admin' | 'User' })}
                >
                  <option value="User">User</option>
                  <option value="Admin">Admin</option>
                </Select>
              </div>

              <div className="flex gap-2 justify-end mt-6">
                <Button variant="outline" onClick={() => setShowCreateDialog(false)}>
                  Cancel
                </Button>
                <Button onClick={handleCreateUser}>
                  Create User
                </Button>
              </div>
            </div>
          </DialogContent>
        </Dialog>

        {/* Edit User Dialog */}
        <Dialog open={showEditDialog} onOpenChange={setShowEditDialog}>
          <DialogContent className="max-w-md">
            <DialogHeader>
              <DialogTitle>Edit User</DialogTitle>
              <DialogDescription>Update user information</DialogDescription>
            </DialogHeader>
            
            <div className="space-y-4">
              <div>
                <Label htmlFor="edit-fullName">Full Name</Label>
                <Input
                  id="edit-fullName"
                  value={editFormData.fullName}
                  onChange={(e) => setEditFormData({ ...editFormData, fullName: e.target.value })}
                />
              </div>

              <div>
                <Label htmlFor="edit-email">Email</Label>
                <Input
                  id="edit-email"
                  type="email"
                  value={editFormData.email}
                  onChange={(e) => setEditFormData({ ...editFormData, email: e.target.value })}
                />
              </div>

              <div>
                <Label htmlFor="edit-role">Role</Label>
                <Select
                  id="edit-role"
                  value={editFormData.role}
                  onChange={(e) => setEditFormData({ ...editFormData, role: e.target.value as 'Admin' | 'User' })}
                >
                  <option value="User">User</option>
                  <option value="Admin">Admin</option>
                </Select>
              </div>

              <div>
                <Label htmlFor="edit-password">New Password (leave empty to keep current)</Label>
                <Input
                  id="edit-password"
                  type="password"
                  value={editFormData.newPassword || ''}
                  onChange={(e) => setEditFormData({ ...editFormData, newPassword: e.target.value })}
                  placeholder="Enter new password"
                />
              </div>

              <div className="flex items-center gap-2">
                <input
                  type="checkbox"
                  id="edit-isActive"
                  checked={editFormData.isActive}
                  onChange={(e) => setEditFormData({ ...editFormData, isActive: e.target.checked })}
                  className="h-4 w-4"
                />
                <Label htmlFor="edit-isActive">Account Active</Label>
              </div>

              <div className="flex gap-2 justify-end mt-6">
                <Button variant="outline" onClick={() => setShowEditDialog(false)}>
                  Cancel
                </Button>
                <Button onClick={handleEditUser}>
                  Save Changes
                </Button>
              </div>
            </div>
          </DialogContent>
        </Dialog>

        {/* Permissions Dialog */}
        <Dialog open={showPermissionsDialog} onOpenChange={setShowPermissionsDialog}>
          <DialogContent className="max-w-md">
            <DialogHeader>
              <DialogTitle>Manage Permissions</DialogTitle>
              <DialogDescription>
                Select permissions for {selectedUser?.username}
              </DialogDescription>
            </DialogHeader>
            
            <div className="space-y-2 max-h-96 overflow-y-auto">
              {availablePermissions.map((perm) => (
                <div key={perm.value} className="flex items-center gap-2">
                  <input
                    type="checkbox"
                    id={`perm-${perm.value}`}
                    checked={selectedPermissions.includes(perm.value)}
                    onChange={() => togglePermission(perm.value)}
                    className="h-4 w-4"
                  />
                  <Label htmlFor={`perm-${perm.value}`} className="cursor-pointer">
                    {perm.label}
                  </Label>
                </div>
              ))}
            </div>

            <div className="flex gap-2 justify-end mt-6">
              <Button variant="outline" onClick={() => setShowPermissionsDialog(false)}>
                Cancel
              </Button>
              <Button onClick={handleUpdatePermissions}>
                Save Permissions
              </Button>
            </div>
          </DialogContent>
        </Dialog>

        {/* Delete Confirmation Dialog */}
        <Dialog open={showDeleteDialog} onOpenChange={setShowDeleteDialog}>
          <DialogContent className="max-w-md">
            <DialogHeader>
              <DialogTitle>Delete User</DialogTitle>
              <DialogDescription>
                Are you sure you want to delete {selectedUser?.username}? This action cannot be undone.
              </DialogDescription>
            </DialogHeader>
            
            <div className="flex gap-2 justify-end mt-6">
              <Button variant="outline" onClick={() => setShowDeleteDialog(false)}>
                Cancel
              </Button>
              <Button variant="destructive" onClick={handleDeleteUser}>
                Delete User
              </Button>
            </div>
          </DialogContent>
        </Dialog>
      </div>
    </ProtectedLayout>
  );
}




