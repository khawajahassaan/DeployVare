'use client';

import { useEffect } from 'react';
import { useRouter } from 'next/navigation';
import Link from 'next/link';
import { useAuthStore } from '@/store/authStore';
import { useClientStore } from '@/store/clientStore';
import { useUiStore } from '@/store/uiStore';
import { Button } from '@/components/ui/button';
import { EmptyState } from '@/components/ui/empty-state';
import { LoadingState } from '@/components/ui/loading-state';
import { Building2 } from 'lucide-react';
import { cn } from '@/lib/utils';
import Navigation from './Navigation';

interface ProtectedLayoutProps {
  children: React.ReactNode;
  // When true, the page content is only shown once a client is selected
  requireClient?: boolean;
}

export default function ProtectedLayout({ children, requireClient = false }: ProtectedLayoutProps) {
  const router = useRouter();
  const { isAuthenticated, checkAuth } = useAuthStore();
  const { selectedClientId, loaded, loadClients } = useClientStore();
  const sidebarCollapsed = useUiStore((state) => state.sidebarCollapsed);

  useEffect(() => {
    checkAuth();
    const token = localStorage.getItem('token');
    if (!token) {
      router.push('/login');
    }
  }, [checkAuth, router]);

  useEffect(() => {
    if (isAuthenticated && !loaded) {
      loadClients();
    }
  }, [isAuthenticated, loaded, loadClients]);

  if (!isAuthenticated) {
    return null;
  }

  const renderContent = () => {
    if (!requireClient || selectedClientId) return children;

    if (!loaded) {
      return <LoadingState label="Loading clients..." />;
    }

    return (
      <EmptyState
        icon={Building2}
        title="No client onboarded yet"
        description="Applications, instances, servers and deployments are all managed per client. Onboard your first client to get started."
        action={
          <Link href="/clients">
            <Button>
              <Building2 className="h-4 w-4" />
              Go to Client Onboarding
            </Button>
          </Link>
        }
      />
    );
  };

  return (
    <div className="min-h-screen bg-background">
      <Navigation />
      {/* Content always sits beside the sidebar, with the same width and alignment on every page */}
      <main
        className={cn(
          'pt-16 transition-[padding] duration-300',
          sidebarCollapsed ? 'lg:pl-[4.5rem]' : 'lg:pl-64'
        )}
      >
        <div className="mx-auto w-full max-w-7xl px-4 py-6 sm:px-6 lg:px-8 lg:py-8">
          {renderContent()}
        </div>
      </main>
    </div>
  );
}
