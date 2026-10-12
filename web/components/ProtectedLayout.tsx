'use client';

import { useEffect } from 'react';
import { useRouter } from 'next/navigation';
import Link from 'next/link';
import { useAuthStore } from '@/store/authStore';
import { useClientStore } from '@/store/clientStore';
import { Button } from '@/components/ui/button';
import { EmptyState } from '@/components/ui/empty-state';
import { LoadingState } from '@/components/ui/loading-state';
import { Building2 } from 'lucide-react';
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
    <div className="dv-canvas min-h-screen">
      <Navigation />
      {/* Content sits on the canvas beside the 50px icon rail and under the 36px bar */}
      <main className="pt-[var(--dv-header-h)] lg:pl-[50px]">
        <div className="mx-auto w-full max-w-[1600px] px-3 py-4 sm:px-4 lg:px-5 lg:py-5">
          {renderContent()}
        </div>
      </main>
    </div>
  );
}
