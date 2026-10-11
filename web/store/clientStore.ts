import { create } from 'zustand';
import { persist } from 'zustand/middleware';
import api, { Client } from '@/lib/api';

interface ClientState {
  clients: Client[];
  selectedClientId: number | null;
  loaded: boolean;
  loadClients: () => Promise<void>;
  selectClient: (id: number | null) => void;
}

// Everything in the app (applications, instances, servers, deployments) is scoped
// to the client selected here. Only the selection is persisted across reloads.
export const useClientStore = create<ClientState>()(
  persist(
    (set, get) => ({
      clients: [],
      selectedClientId: null,
      loaded: false,

      loadClients: async () => {
        try {
          const clients = await api.getClients();
          const { selectedClientId } = get();
          const stillExists = clients.some((c) => c.id === selectedClientId);
          set({
            clients,
            loaded: true,
            selectedClientId: stillExists ? selectedClientId : clients[0]?.id ?? null,
          });
        } catch (error) {
          console.error('Failed to load clients:', error);
          set({ loaded: true });
        }
      },

      selectClient: (id) => set({ selectedClientId: id }),
    }),
    {
      name: 'client-storage',
      partialize: (state) => ({ selectedClientId: state.selectedClientId }),
    }
  )
);
