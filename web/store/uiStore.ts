import { create } from 'zustand';
import { persist } from 'zustand/middleware';

interface UiState {
  // Desktop: sidebar shrinks to an icon rail (remembered per browser)
  sidebarCollapsed: boolean;
  // Mobile: sidebar slides over the page
  mobileNavOpen: boolean;
  toggleSidebarCollapsed: () => void;
  setMobileNavOpen: (open: boolean) => void;
}

export const useUiStore = create<UiState>()(
  persist(
    (set) => ({
      sidebarCollapsed: false,
      mobileNavOpen: false,
      toggleSidebarCollapsed: () => set((state) => ({ sidebarCollapsed: !state.sidebarCollapsed })),
      setMobileNavOpen: (open) => set({ mobileNavOpen: open }),
    }),
    {
      name: 'deployvare-ui',
      partialize: (state) => ({ sidebarCollapsed: state.sidebarCollapsed }),
    }
  )
);
