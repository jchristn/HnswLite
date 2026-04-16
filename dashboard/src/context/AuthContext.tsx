import { createContext, useCallback, useContext, useEffect, useState } from 'react';
import type { ReactNode } from 'react';
import { setApiKey, setOnUnauthorized, validateApiKey } from '../api/client';

const STORAGE_KEY = 'hnswlite_api_key';
const STORAGE_HEADER = 'hnswlite_api_key_header';

interface AuthContextValue {
  apiKey: string | null;
  apiKeyHeader: string;
  isAuthenticated: boolean;
  loading: boolean;
  login: (key: string, header?: string) => Promise<boolean>;
  logout: () => void;
}

const AuthContext = createContext<AuthContextValue | null>(null);

export function AuthProvider({ children }: { children: ReactNode }) {
  const [apiKey, setApiKeyState] = useState<string | null>(() => localStorage.getItem(STORAGE_KEY));
  const [apiKeyHeader, setApiKeyHeaderState] = useState<string>(
    () => localStorage.getItem(STORAGE_HEADER) ?? 'x-api-key',
  );
  const [isAuthenticated, setIsAuthenticated] = useState<boolean>(false);
  const [loading, setLoading] = useState<boolean>(true);

  const logout = useCallback(() => {
    localStorage.removeItem(STORAGE_KEY);
    setApiKey(null);
    setApiKeyState(null);
    setIsAuthenticated(false);
  }, []);

  useEffect(() => {
    setOnUnauthorized(logout);
  }, [logout]);

  useEffect(() => {
    let cancelled = false;
    async function restore() {
      if (!apiKey) {
        setLoading(false);
        return;
      }
      setApiKey(apiKey, apiKeyHeader);
      try {
        const ok = await validateApiKey();
        if (!cancelled) {
          setIsAuthenticated(ok);
          if (!ok) logout();
        }
      } catch {
        if (!cancelled) {
          setIsAuthenticated(false);
          logout();
        }
      } finally {
        if (!cancelled) setLoading(false);
      }
    }
    void restore();
    return () => {
      cancelled = true;
    };
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, []);

  const login = useCallback(async (key: string, header: string = 'x-api-key'): Promise<boolean> => {
    setApiKey(key, header);
    const ok = await validateApiKey();
    if (ok) {
      localStorage.setItem(STORAGE_KEY, key);
      localStorage.setItem(STORAGE_HEADER, header);
      setApiKeyState(key);
      setApiKeyHeaderState(header);
      setIsAuthenticated(true);
    } else {
      setApiKey(null);
    }
    return ok;
  }, []);

  return (
    <AuthContext.Provider value={{ apiKey, apiKeyHeader, isAuthenticated, loading, login, logout }}>
      {children}
    </AuthContext.Provider>
  );
}

export function useAuth(): AuthContextValue {
  const ctx = useContext(AuthContext);
  if (!ctx) throw new Error('useAuth must be used within AuthProvider');
  return ctx;
}
