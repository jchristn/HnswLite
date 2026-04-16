import { BrowserRouter, Navigate, Route, Routes } from 'react-router-dom';
import { AuthProvider, useAuth } from './context/AuthContext';
import { ThemeProvider } from './context/ThemeContext';
import Layout from './components/Layout';
import Login from './components/Login';
import Dashboard from './pages/Dashboard';
import Indexes from './pages/Indexes';
import Vectors from './pages/Vectors';
import Search from './pages/Search';
import RequestHistory from './pages/RequestHistory';
import ApiExplorer from './pages/ApiExplorer';
import Settings from './pages/Settings';
import ServerInfo from './pages/ServerInfo';
import type { ReactNode } from 'react';

function ProtectedRoute({ children }: { children: ReactNode }) {
  const { isAuthenticated, loading } = useAuth();
  if (loading) {
    return (
      <div style={{ minHeight: '100vh', display: 'flex', alignItems: 'center', justifyContent: 'center' }}>
        <span className="spinner" />
      </div>
    );
  }
  if (!isAuthenticated) return <Login />;
  return <>{children}</>;
}

export default function App() {
  return (
    <ThemeProvider>
      <AuthProvider>
        <BrowserRouter basename="/dashboard">
          <Routes>
            <Route
              element={
                <ProtectedRoute>
                  <Layout />
                </ProtectedRoute>
              }
            >
              <Route index element={<Navigate to="/dashboard" replace />} />
              <Route path="dashboard" element={<Dashboard />} />
              <Route path="indexes" element={<Indexes />} />
              <Route path="vectors" element={<Vectors />} />
              <Route path="search" element={<Search />} />
              <Route path="history" element={<RequestHistory />} />
              <Route path="explorer" element={<ApiExplorer />} />
              <Route path="settings" element={<Settings />} />
              <Route path="server" element={<ServerInfo />} />
              <Route path="*" element={<Navigate to="/dashboard" replace />} />
            </Route>
          </Routes>
        </BrowserRouter>
      </AuthProvider>
    </ThemeProvider>
  );
}
