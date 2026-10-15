'use client';

import Link from 'next/link';
import { usePathname, useRouter } from 'next/navigation';
import { useEffect, useMemo, useRef, useState } from 'react';
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
  X,
  Users,
  Settings,
  ShieldCheck,
  Terminal,
} from 'lucide-react';
import { useAuthStore } from '@/store/authStore';
import { useClientStore } from '@/store/clientStore';
import { useUiStore } from '@/store/uiStore';
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
    label: 'Linux',
    items: [
      { name: 'Linux Deployment', href: '/linux-deploy', icon: Terminal, requiredPermission: 'DeployApi' },
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

const isItemActive = (pathname: string, item: NavItem) =>
  isRouteActive(pathname, item.href) || (item.matches ?? []).some((route) => isRouteActive(pathname, route));

const initialsOf = (name: string) =>
  name
    .split(/\s+/)
    .filter(Boolean)
    .slice(0, 2)
    .map((part) => part[0]?.toUpperCase())
    .join('') || '?';

// Hidden gradient that active navigation glyphs and page-title icons stroke with —
// the purple → blue ramp AuditVare's active sidebar icons are drawn in
function BrandGradientDefs() {
  return (
    <svg width="0" height="0" className="absolute" aria-hidden focusable="false">
      <defs>
        <linearGradient id="dv-brand-gradient" x1="3" y1="21" x2="21" y2="3" gradientUnits="userSpaceOnUse">
          <stop stopColor="#520B4D" />
          <stop offset="1" stopColor="#0F6CBD" />
        </linearGradient>
      </defs>
    </svg>
  );
}

interface RailTip {
  label: string;
  top: number;
}

export default function Navigation() {
  const pathname = usePathname() ?? '';
  const router = useRouter();
  const { user, logout, hasPermission } = useAuthStore();
  const { clients, selectedClientId, selectClient } = useClientStore();
  const { mobileNavOpen, setMobileNavOpen } = useUiStore();
  const [tip, setTip] = useState<RailTip | null>(null);
  const [userMenuOpen, setUserMenuOpen] = useState(false);
  const userMenuRef = useRef<HTMLDivElement>(null);
  const userButtonRef = useRef<HTMLButtonElement>(null);

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

  const activeItem = useMemo(
    () => navGroups.flatMap((group) => group.items).find((item) => isItemActive(pathname, item)),
    [pathname]
  );

  // Close the profile menu on an outside click or Escape
  useEffect(() => {
    if (!userMenuOpen) return;

    const onPointerDown = (event: PointerEvent) => {
      const target = event.target as Node;
      if (!userMenuRef.current?.contains(target) && !userButtonRef.current?.contains(target)) setUserMenuOpen(false);
    };
    const onKeyDown = (event: KeyboardEvent) => {
      if (event.key === 'Escape') setUserMenuOpen(false);
    };

    document.addEventListener('pointerdown', onPointerDown);
    document.addEventListener('keydown', onKeyDown);
    return () => {
      document.removeEventListener('pointerdown', onPointerDown);
      document.removeEventListener('keydown', onKeyDown);
    };
  }, [userMenuOpen]);

  const handleLogout = () => {
    logout();
    router.push('/login');
  };

  const showTip = (label: string, element: HTMLElement) => {
    const rect = element.getBoundingClientRect();
    setTip({ label, top: rect.top + rect.height / 2 });
  };

  const displayName = user?.fullName || user?.username || '';

  return (
    <>
      <BrandGradientDefs />

      {/* Top bar — AuditVare's translucent 36px header */}
      <header className="fixed inset-x-0 top-0 z-[80] flex h-[var(--dv-header-h)] min-w-0 items-center gap-1 bg-[#FFFFFF33] px-2 backdrop-blur-[50px]">
        <button
          type="button"
          onClick={() => setMobileNavOpen(!mobileNavOpen)}
          className="flex size-8 shrink-0 items-center justify-center rounded-[6px] text-black lg:hidden"
          aria-label={mobileNavOpen ? 'Close navigation' : 'Open navigation'}
          aria-expanded={mobileNavOpen}
        >
          {mobileNavOpen ? <X className="size-4" /> : <Menu className="size-4" />}
        </button>

        <Link href="/dashboard" className="shrink-0" aria-label="DeployVare home">
          {/* eslint-disable-next-line @next/next/no-img-element */}
          <img src="/brand/v-mark.png" alt="" className="size-[28px] object-contain" />
        </Link>

        {activeItem && (
          <p className="ml-2 truncate text-[14px] font-normal text-ink md:text-[16px]">{activeItem.name}</p>
        )}

        <div className="ml-auto flex min-w-0 shrink-0 items-center gap-2 sm:gap-3">
          {user && clients.length > 0 && (
            <label className="flex min-w-0 items-center gap-1.5">
              <Building2 className="hidden size-4 shrink-0 text-[#5B5C5E] md:block" strokeWidth={1.5} />
              <span className="sr-only">Active client</span>
              <select
                aria-label="Active client"
                className="h-[28px] w-36 cursor-pointer appearance-none truncate rounded-[8px] border border-white dv-select-chevron dv-select-chevron-sm bg-white/70 pl-2.5 pr-6 text-[13px] text-ink transition-colors hover:border-[#CECECE] focus-visible:border-[#333333] focus-visible:outline-none sm:w-56"
                value={selectedClientId ?? ''}
                onChange={(e) => selectClient(Number(e.target.value))}
              >
                {clients.map((client) => (
                  <option key={client.id} value={client.id}>
                    {client.name} ({client.clientCode})
                  </option>
                ))}
              </select>
            </label>
          )}
        </div>
      </header>

      {/* Icon rail (desktop) — AuditVare's sidebar: 35px tiles, grey outline glyphs,
          the active page on a white tile with a gradient glyph */}
      <aside
        className="fixed bottom-0 left-0 top-[var(--dv-header-h)] z-[70] hidden w-[50px] flex-col justify-between overflow-hidden border border-l-0 border-[#F6F6F65C] bg-[#FFFFFF33] backdrop-blur-[50px] lg:flex"
        aria-label="Main"
      >
        <nav
          className="no-scrollbar flex flex-1 flex-col items-center gap-2 overflow-y-auto py-2"
          onScroll={() => setTip(null)}
        >
          {groups.map((group, index) => (
            <div key={group.label} className="flex flex-col items-center gap-2">
              {index > 0 && <div className="my-0.5 h-px w-[24px] rounded-[1px] bg-[#9DABB9]/40" aria-hidden />}
              {group.items.map((item) => {
                const Icon = item.icon;
                const active = isItemActive(pathname, item);

                return (
                  <Link
                    key={item.name}
                    href={item.href}
                    aria-label={item.name}
                    aria-current={active ? 'page' : undefined}
                    onMouseEnter={(e) => showTip(item.name, e.currentTarget)}
                    onMouseLeave={() => setTip(null)}
                    onFocus={(e) => showTip(item.name, e.currentTarget)}
                    onBlur={() => setTip(null)}
                    className="group flex size-[35px] items-center justify-center rounded-[8px] focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-ring/40"
                  >
                    {active ? (
                      <span className="flex size-[35px] items-center justify-center rounded-[8px] bg-white">
                        <Icon className="size-[22px]" stroke="url(#dv-brand-gradient)" strokeWidth={1.75} />
                      </span>
                    ) : (
                      // Grey outline and gradient glyph stacked and cross-faded on hover,
                      // like AuditVare's NavIconSwap
                      <span className="relative inline-flex size-[22px]">
                        <Icon
                          className="size-[22px] text-nav-idle opacity-70 transition-opacity duration-150 ease-out group-hover:opacity-0"
                          strokeWidth={1.25}
                        />
                        <Icon
                          className="absolute inset-0 size-[22px] opacity-0 transition-opacity duration-150 ease-out group-hover:opacity-100"
                          stroke="url(#dv-brand-gradient)"
                          strokeWidth={1.75}
                        />
                      </span>
                    )}
                  </Link>
                );
              })}
            </div>
          ))}
        </nav>

        {/* Profile — AuditVare keeps the user menu at the foot of the rail */}
        {user && (
          <div className="flex flex-col items-center pb-2 pt-1">
            <button
              ref={userButtonRef}
              type="button"
              onClick={() => {
                setTip(null);
                setUserMenuOpen((open) => !open);
              }}
              onMouseEnter={(e) => !userMenuOpen && showTip(displayName, e.currentTarget)}
              onMouseLeave={() => setTip(null)}
              className="flex size-[30px] items-center justify-center rounded-full bg-gradient-to-tr from-brand-purple to-brand-blue text-[11px] font-semibold text-white ring-2 ring-white"
              aria-label="Account menu"
              aria-expanded={userMenuOpen}
            >
              {initialsOf(displayName)}
            </button>
          </div>
        )}
      </aside>

      {/* Profile menu — outside the rail, whose backdrop blur would otherwise trap and
          clip this fixed panel inside the 50px column */}
      {user && userMenuOpen && (
        <div
          ref={userMenuRef}
          className="fixed bottom-2 left-[58px] z-[90] w-[220px] rounded-[14px] bg-white p-2 shadow-[0_6px_20px_rgba(1,39,78,0.16)] animate-fade-in"
        >
          <div className="flex items-center gap-2.5 px-2 pb-2 pt-1">
            <span className="flex size-9 shrink-0 items-center justify-center rounded-full bg-gradient-to-tr from-brand-purple to-brand-blue text-[12px] font-semibold text-white">
              {initialsOf(displayName)}
            </span>
            <div className="min-w-0">
              <p className="truncate text-[14px] text-ink">{displayName}</p>
              <p className="truncate text-[12px] text-label">{user.role}</p>
            </div>
          </div>
          <div className="mx-1 h-px bg-[#EEEEEE]" />
          <button
            type="button"
            onClick={handleLogout}
            className="mt-1 flex w-full items-center gap-2 rounded-[8px] px-2 py-1.5 text-left text-[13px] text-[#C90000] transition-colors hover:bg-[#FDECEC]"
          >
            <LogOut className="size-4" />
            Sign out
          </button>
        </div>
      )}

      {/* Rail tooltip — rendered outside the scrolling rail so it is never clipped */}
      {tip && (
        <div
          className="pointer-events-none fixed left-[56px] z-[90] -translate-y-1/2 whitespace-nowrap rounded-[6px] bg-[rgba(0,0,0,0.85)] px-2 py-1.5 text-[13px] text-white shadow-md animate-fade-in"
          style={{ top: tip.top }}
          role="tooltip"
        >
          {tip.label}
        </div>
      )}

      {/* Mobile drawer — AuditVare's list-style navigation */}
      <aside
        className={cn(
          'fixed bottom-0 left-0 top-[var(--dv-header-h)] z-[70] flex w-[240px] flex-col bg-white transition-transform duration-300 ease-[cubic-bezier(0.22,1,0.36,1)] lg:hidden',
          mobileNavOpen ? 'translate-x-0 shadow-xl' : 'pointer-events-none -translate-x-full'
        )}
        aria-label="Main"
        aria-hidden={!mobileNavOpen}
      >
        <nav className="no-scrollbar flex flex-1 flex-col gap-1 overflow-y-auto p-2">
          {groups.map((group, index) => (
            <div key={group.label} className="flex flex-col gap-1">
              {index > 0 && <div className="mx-1 my-2 h-px bg-[#EEEEEE]" />}
              <p className="px-3 pb-0.5 text-[12px] text-label">{group.label}</p>
              {group.items.map((item) => {
                const Icon = item.icon;
                const active = isItemActive(pathname, item);

                return (
                  <Link
                    key={item.name}
                    href={item.href}
                    aria-current={active ? 'page' : undefined}
                    onClick={() => setMobileNavOpen(false)}
                    className={cn(
                      'flex w-full items-center gap-3 rounded-[8px] px-3 py-2.5 text-left text-[14px] transition-colors',
                      active ? 'bg-accent text-accent-foreground' : 'text-ink hover:bg-[#F5F8FB]'
                    )}
                  >
                    <Icon
                      className={cn('size-[20px] shrink-0', !active && 'text-nav-idle')}
                      {...(active ? { stroke: 'url(#dv-brand-gradient)' } : {})}
                      strokeWidth={1.5}
                    />
                    <span className="truncate">{item.name}</span>
                  </Link>
                );
              })}
            </div>
          ))}
        </nav>

        {user && (
          <div className="flex shrink-0 items-center gap-2 border-t border-[#EEEEEE] p-3">
            <span className="flex size-8 shrink-0 items-center justify-center rounded-full bg-gradient-to-tr from-brand-purple to-brand-blue text-[11px] font-semibold text-white">
              {initialsOf(displayName)}
            </span>
            <div className="min-w-0 flex-1">
              <p className="truncate text-[14px] text-ink">{displayName}</p>
              <p className="truncate text-[12px] text-label">{user.role}</p>
            </div>
            <button
              type="button"
              onClick={handleLogout}
              className="flex size-8 items-center justify-center rounded-[8px] text-[#C90000] hover:bg-[#FDECEC]"
              aria-label="Sign out"
              title="Sign out"
            >
              <LogOut className="size-4" />
            </button>
          </div>
        )}
      </aside>

      {/* Overlay for mobile */}
      {mobileNavOpen && (
        <div
          className="fixed inset-0 top-[var(--dv-header-h)] z-[65] bg-black/30 animate-fade-in lg:hidden"
          onClick={() => setMobileNavOpen(false)}
        />
      )}
    </>
  );
}
