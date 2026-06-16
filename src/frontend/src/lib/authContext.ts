import { createContext, useContext } from "react";

export interface AuthState {
  enabled: boolean;
  username: string | null;
  email: string | null;
  logout: () => Promise<void>;
}

export const AuthContext = createContext<AuthState>({
  enabled: false,
  username: null,
  email: null,
  logout: async () => {},
});

export function useAuth(): AuthState {
  return useContext(AuthContext);
}
