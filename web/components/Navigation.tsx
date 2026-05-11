'use client';

import Link from 'next/link';
import { usePathname, useRouter } from 'next/navigation';
import { useAuthStore } from '@/store/authStore';
import { Button } from '@/components/ui/button';
import {
  Home,
  FolderKanban,
  Database,
  Rocket,
  History,
  GitCompare,
  LogOut,
  Server,
  Upload,
  FileCode,
  Menu,
  X,
  Users,
} from 'lucide-react';
import { cn } from '@/lib/utils';
import { useState, useMemo } from 'react';

interface NavItem {
  name: string;
  href: string;
  icon: any;
  requiredPermission?: string;  // Permission needed to see this item
}

const allNavItems: NavItem[] = [
  { name: 'Dashboard', href: '/dashboard', icon: Home },
  { name: 'Applications', href: '/applications', icon: FolderKanban, requiredPermission: 'ManageApplications' },
  { name: 'Instances', href: '/instances', icon: Database, requiredPermission: 'ManageDatabaseInstances' },
  { name: 'Deploy SQL', href: '/deploy', icon: Rocket, requiredPermission: 'DeploySql' },
  { name: 'SQL History', href: '/history', icon: History, requiredPermission: 'ViewSqlHistory' },
  { name: 'Compare', href: '/compare', icon: GitCompare, requiredPermission: 'CompareSchemas' },
  { name: 'Servers', href: '/servers', icon: Server, requiredPermission: 'ManageServers' },
  { name: 'Deploy App', href: '/app-deploy', icon: Upload, requiredPermission: 'DeployApi' },
  { name: 'App History', href: '/app-history', icon: FileCode, requiredPermission: 'ViewApiHistory' },
  { name: 'Users', href: '/users', icon: Users, requiredPermission: 'ManageUsers' },
];

export default function Navigation() {
  const pathname = usePathname();
  const router = useRouter();
  const { user, logout, hasPermission } = useAuthStore();
  const [sidebarOpen, setSidebarOpen] = useState(true);

  // Filter navigation items based on user permissions
  const navItems = useMemo(() => {
    if (!user) return [];
    
    return allNavItems.filter((item) => {
      // Items without required permission are always visible
      if (!item.requiredPermission) return true;
      
      // Check if user has the required permission
      return hasPermission(item.requiredPermission);
    });
  }, [user, hasPermission]);

  const handleLogout = () => {
    logout();
    router.push('/login');
  };

  return (
    <>
      {/* Top App Bar */}
      <nav className="fixed top-0 left-0 right-0 z-50 bg-card shadow-sm">
        <div className="flex h-16 items-center justify-between px-4">
          <div className="flex items-center gap-3">
            <Button
              variant="ghost"
              size="icon"
              onClick={() => setSidebarOpen(!sidebarOpen)}
            >
              <Menu className="h-5 w-5" />
            </Button>
            
            <Link href="/dashboard" className="flex items-center gap-2">
              <Rocket className="h-7 w-7 text-primary" />
              <span className="text-xl font-bold">DeployVare</span>
            </Link>
          </div>

          <div className="flex items-center gap-3">
            {user && (
              <>
                <div className="text-sm text-right hidden sm:block">
                  <p className="font-medium leading-none">{user.fullName || user.username}</p>
                  <p className="text-xs text-muted-foreground mt-1">{user.role}</p>
                </div>
                <Button variant="ghost" size="icon" onClick={handleLogout}>
                  <LogOut className="h-5 w-5" />
                </Button>
              </>
            )}
          </div>
        </div>
      </nav>

      {/* Sidebar */}
      <aside
        className={cn(
          'fixed top-16 left-0 z-40 h-[calc(100vh-4rem)] w-64 bg-card shadow-lg transition-transform duration-300 overflow-y-auto',
          sidebarOpen ? 'translate-x-0' : '-translate-x-full'
        )}
      >
        <div className="flex flex-col gap-1 p-4">
          {navItems.map((item) => {
            const Icon = item.icon;
            return (
              <Link
                key={item.name}
                href={item.href}
                onClick={() => {
                  // Close sidebar on mobile after navigation
                  if (window.innerWidth < 1024) {
                    setSidebarOpen(false);
                  }
                }}
                className={cn(
                  'flex items-center gap-3 px-4 py-3 rounded-lg text-sm font-medium transition-all',
                  pathname === item.href
                    ? 'bg-primary text-primary-foreground shadow-sm'
                    : 'text-muted-foreground hover:bg-muted hover:text-foreground'
                )}
              >
                <Icon className="h-5 w-5 flex-shrink-0" />
                <span>{item.name}</span>
              </Link>
            );
          })}
        </div>
      </aside>

      {/* Overlay for mobile */}
      {sidebarOpen && (
        <div
          className="fixed inset-0 top-16 z-30 bg-black/50 lg:hidden"
          onClick={() => setSidebarOpen(false)}
        />
      )}
    </>
  );
}


