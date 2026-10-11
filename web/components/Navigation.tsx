'use client';

import Link from 'next/link';
import { usePathname, useRouter } from 'next/navigation';
import { useMemo } from 'react';
import type { LucideIcon } from 'lucide-react';
import {
  Home,
  Building2,
  Cloud,
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
  Users,
  Settings,
  PanelLeftClose,
  PanelLeftOpen,
  ShieldCheck,
} from 'lucide-react';
import { useAuthStore } from '@/store/authStore';
import { useClientStore } from '@/store/clientStore';
import { useUiStore } from '@/store/uiStore';
import { Button } from '@/components/ui/button';
import { Select } from '@/components/ui/select';
import { cn } from '@/lib/utils';

interface NavItem {
  name: string;
  href: string;
  icon: LucideIcon;
  requiredPermission?: string; // Permission needed to see this item
  matches?: string[]; // Other routes that belong to this item
}

interface NavGroup {
  label: string;
  items: NavItem[];
}

const navGroups: NavGroup[] = [
  {
    label: 'Overview',
    items: [
      { name: 'Dashboard', href: '/dashboard', icon: Home },
      { name: 'Client Onboarding', href: '/clients', icon: Building2, requiredPermission: 'ManageClients' },
      { name: 'Licensing', href: '/licensing', icon: ShieldCheck, requiredPermission: 'ManageClients' },
    ],
  },
  {
    label: 'Database',
    items: [
      { name: 'Applications', href: '/applications', icon: FolderKanban, requiredPermission: 'ManageApplications' },
      { name: 'Instances', href: '/instances', icon: Database, requiredPermission: 'ManageDatabaseInstances' },
      { name: 'Deploy SQL', href: '/deploy', icon: Rocket, requiredPermission: 'DeploySql' },
      { name: 'SQL History', href: '/history', icon: History, requiredPermission: 'ViewSqlHistory' },
      { name: 'Compare', href: '/compare', icon: GitCompare, requiredPermission: 'CompareSchemas' },
    ],
  },
  {
    label: 'App Deployment',
    items: [
      { name: 'Servers', href: '/servers', icon: Server, requiredPermission: 'ManageServers', matches: ['/server-setup'] },
      { name: 'Azure DevOps', href: '/azure-devops', icon: Cloud, requiredPermission: 'ManageAzureDevOps' },
      { name: 'Deploy App', href: '/app-deploy', icon: Upload, requiredPermission: 'DeployApi', matches: ['/api-deploy'] },
      { name: 'App History', href: '/app-history', icon: FileCode, requiredPermission: 'ViewApiHistory', matches: ['/api-history'] },
    ],
  },
  {
    label: 'Administration',
    items: [
      { name: 'Users', href: '/users', icon: Users, requiredPermission: 'ManageUsers' },
      { name: 'Settings', href: '/settings', icon: Settings, requiredPermission: 'ManageUsers' },
    ],
  },
];

const isRouteActive = (pathname: string, route: string) => pathname === route || pathname.startsWith(`${route}/`);

const initialsOf = (name: string) =>
  name
    .split(/\s+/)
    .filter(Boolean)
    .slice(0, 2)
    .map((part) => part[0]?.toUpperCase())
    .join('') || '?';

export default function Navigation() {
  const pathname = usePathname() ?? '';
  const router = useRouter();
  const { user, logout, hasPermission } = useAuthStore();
  const { clients, selectedClientId, selectClient } = useClientStore();
  const { sidebarCollapsed, mobileNavOpen, toggleSidebarCollapsed, setMobileNavOpen } = useUiStore();

  // Only show items (and groups) the user has permission for
  const groups = useMemo(() => {
    if (!user) return [];

    return navGroups
      .map((group) => ({
        ...group,
        items: group.items.filter((item) => !item.requiredPermission || hasPermission(item.requiredPermission)),
      }))
      .filter((group) => group.items.length > 0);
  }, [user, hasPermission]);

  const handleLogout = () => {
    logout();
    router.push('/login');
  };

  const handleMenuClick = () => {
    if (window.innerWidth >= 1024) {
      toggleSidebarCollapsed();
    } else {
      setMobileNavOpen(!mobileNavOpen);
    }
  };

  const displayName = user?.fullName || user?.username || '';

  return (
    <>
      {/* Top App Bar */}
      <header className="fixed inset-x-0 top-0 z-50 h-16 border-b border-border bg-card/95 backdrop-blur supports-[backdrop-filter]:bg-card/80">
        <div className="flex h-full items-center justify-between gap-3 px-3 sm:px-4">
          <div className="flex min-w-0 items-center gap-2">
            <Button variant="ghost" size="icon" onClick={handleMenuClick} aria-label="Toggle navigation">
              <Menu className="h-5 w-5" />
            </Button>

            <Link href="/dashboard" className="flex items-center gap-2.5 rounded-md px-1 py-1">
              <span className="flex h-8 w-8 items-center justify-center rounded-lg bg-primary text-primary-foreground shadow-sm">
                <Rocket className="h-4 w-4" />
              </span>
              <span className="hidden text-lg font-semibold tracking-tight sm:inline">DeployVare</span>
            </Link>
          </div>

          <div className="flex min-w-0 items-center gap-2 sm:gap-4">
            {user && clients.length > 0 && (
              <div className="flex min-w-0 items-center gap-2">
                <Building2 className="hidden h-4 w-4 shrink-0 text-muted-foreground md:block" />
                <Select
                  aria-label="Active client"
                  className="w-40 sm:w-60"
                  value={selectedClientId ?? ''}
                  onChange={(e) => selectClient(Number(e.target.value))}
                >
                  {clients.map((client) => (
                    <option key={client.id} value={client.id}>
                      {client.name} ({client.clientCode})
                    </option>
                  ))}
                </Select>
              </div>
            )}

            {user && (
              <div className="flex items-center gap-2 sm:border-l sm:border-border sm:pl-4">
                <div className="hidden text-right md:block">
                  <p className="text-sm font-medium leading-tight">{displayName}</p>
                  <p className="text-xs text-muted-foreground">{user.role}</p>
                </div>
                <span
                  className="hidden h-8 w-8 shrink-0 items-center justify-center rounded-full bg-primary/10 text-xs font-semibold text-primary sm:flex"
                  aria-hidden
                >
                  {initialsOf(displayName)}
                </span>
                <Button variant="ghost" size="icon" onClick={handleLogout} aria-label="Sign out" title="Sign out">
                  <LogOut className="h-4 w-4" />
                </Button>
              </div>
            )}
          </div>
        </div>
      </header>

      {/* Sidebar */}
      <aside
        className={cn(
          'fixed left-0 top-16 z-40 flex h-[calc(100vh-4rem)] w-64 flex-col border-r border-border bg-card transition-[transform,width] duration-300',
          mobileNavOpen ? 'translate-x-0 shadow-xl' : '-translate-x-full',
          'lg:translate-x-0 lg:shadow-none',
          sidebarCollapsed && 'lg:w-[4.5rem]'
        )}
      >
        <nav className="scrollbar-thin flex-1 overflow-y-auto px-3 py-4" aria-label="Main">
          {groups.map((group, index) => (
            <div key={group.label} className={cn(index > 0 && 'mt-5')}>
              <p
                className={cn(
                  'mb-1.5 px-3 text-[11px] font-semibold uppercase tracking-wider text-muted-foreground/80',
                  sidebarCollapsed && 'lg:hidden'
                )}
              >
                {group.label}
              </p>
              {sidebarCollapsed && index > 0 && <div className="mx-2 mb-3 hidden border-t border-border lg:block" />}

              <div className="flex flex-col gap-0.5">
                {group.items.map((item) => {
                  const Icon = item.icon;
                  const active =
                    isRouteActive(pathname, item.href) || (item.matches ?? []).some((route) => isRouteActive(pathname, route));

                  return (
                    <Link
                      key={item.name}
                      href={item.href}
                      title={sidebarCollapsed ? item.name : undefined}
                      aria-current={active ? 'page' : undefined}
                      onClick={() => setMobileNavOpen(false)}
                      className={cn(
                        'group relative flex h-9 items-center gap-3 rounded-md px-3 text-sm font-medium transition-colors',
                        active
                          ? 'bg-primary/10 text-primary'
                          : 'text-slate-600 hover:bg-muted hover:text-foreground',
                        sidebarCollapsed && 'lg:justify-center lg:px-0'
                      )}
                    >
                      {active && <span className="absolute inset-y-1.5 left-0 w-0.5 rounded-full bg-primary" aria-hidden />}
                      <Icon className={cn('h-[18px] w-[18px] shrink-0', !active && 'text-slate-500 group-hover:text-foreground')} />
                      <span className={cn('truncate', sidebarCollapsed && 'lg:hidden')}>{item.name}</span>
                    </Link>
                  );
                })}
              </div>
            </div>
          ))}
        </nav>

        <div className="hidden border-t border-border p-3 lg:block">
          <button
            type="button"
            onClick={toggleSidebarCollapsed}
            className={cn(
              'flex h-9 w-full items-center gap-3 rounded-md px-3 text-sm font-medium text-slate-600 transition-colors hover:bg-muted hover:text-foreground',
              sidebarCollapsed && 'justify-center px-0'
            )}
            title={sidebarCollapsed ? 'Expand sidebar' : 'Collapse sidebar'}
          >
            {sidebarCollapsed ? <PanelLeftOpen className="h-[18px] w-[18px]" /> : <PanelLeftClose className="h-[18px] w-[18px]" />}
            {!sidebarCollapsed && <span>Collapse</span>}
          </button>
        </div>
      </aside>

      {/* Overlay for mobile */}
      {mobileNavOpen && (
        <div
          className="fixed inset-0 top-16 z-30 bg-slate-900/40 animate-fade-in lg:hidden"
          onClick={() => setMobileNavOpen(false)}
        />
      )}
    </>
  );
}
