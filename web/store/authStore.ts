import { create } from 'zustand';
import { persist } from 'zustand/middleware';
import api, { LoginRequest } from '@/lib/api';

interface User {
  username: string;
  fullName?: string;
  role: string;
  permissions: string[];
}

export interface AuthState {
  user: User | null;
  token: string | null;
  isAuthenticated: boolean;
  isLoading: boolean;
  login: (credentials: LoginRequest) => Promise<void>;
  logout: () => void;
  checkAuth: () => void;
  hasPermission: (permission: string) => boolean;
}

export const useAuthStore = create<AuthState>()(
  persist(
    (set) => ({
      user: null,
      token: null,
      isAuthenticated: false,
      isLoading: false,

      login: async (credentials) => {
        set({ isLoading: true });
        try {
          const response = await api.login(credentials);
          
          const user: User = {
            username: response.username,
            fullName: response.fullName,
            role: response.role,
            permissions: response.permissions || [],
          };

          // Store token in localStorage
          if (typeof window !== 'undefined') {
            localStorage.setItem('token', response.token);
          }

          set({
            user,
            token: response.token,
            isAuthenticated: true,
            isLoading: false,
          });
        } catch (error) {
          set({ isLoading: false });
          throw error;
        }
      },

      logout: () => {
        if (typeof window !== 'undefined') {
          localStorage.removeItem('token');
          localStorage.removeItem('user');
        }
        set({
          user: null,
          token: null,
          isAuthenticated: false,
        });
      },

      checkAuth: () => {
        if (typeof window !== 'undefined') {
          const token = localStorage.getItem('token');
          const userStr = localStorage.getItem('auth-storage');
          
          if (token && userStr) {
            try {
              const storage = JSON.parse(userStr);
              if (storage.state?.user) {
                set({
                  user: storage.state.user,
                  token,
                  isAuthenticated: true,
                });
              }
            } catch (error) {
              console.error('Failed to parse stored auth:', error);
            }
          }
        }
      },

      hasPermission: (permission: string): boolean => {
        const { user } = useAuthStore.getState() as AuthState;
        if (!user) return false;
        
        // Admins have all permissions
        if (user.role === 'Admin') return true;
        
        return user.permissions.includes(permission);
      },
    }),
    {
      name: 'auth-storage',
    }
  )
);

