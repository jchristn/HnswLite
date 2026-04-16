import { NavLink, Outlet } from 'react-router-dom';
import { useAuth } from '../context/AuthContext';
import { useTheme } from '../context/ThemeContext';
import { GitHubIcon, LogoutIcon, MoonIcon, SunIcon } from './shared/Icons';

declare const __APP_VERSION__: string;

const GITHUB_URL = 'https://github.com/jchristn/HnswIndex';

export default function Layout() {
  const { logout } = useAuth();
  const { darkMode, toggle } = useTheme();

  return (
    <div style={{ display: 'flex', flexDirection: 'column', height: '100vh', overflow: 'hidden' }}>
      {/* Top bar — logo upper-left, icon buttons upper-right */}
      <header
        style={{
          display: 'flex',
          alignItems: 'center',
          justifyContent: 'space-between',
          padding: '0 16px',
          height: 52,
          background: 'var(--bg-primary)',
          borderBottom: '1px solid var(--border-color)',
          flexShrink: 0,
        }}
      >
        <div style={{ display: 'flex', alignItems: 'center', gap: 10 }}>
          <img src="/dashboard/logo.png" alt="HnswLite" style={{ height: 32, width: 'auto' }} />
          <div style={{ fontWeight: 700, fontSize: 16, color: 'var(--color-primary)', letterSpacing: '0.01em' }}>
            HnswLite
          </div>
        </div>

        <div style={{ display: 'flex', alignItems: 'center', gap: 6 }}>
          <a
            className="btn-icon"
            href={GITHUB_URL}
            target="_blank"
            rel="noopener noreferrer"
            title="View on GitHub"
            style={{ textDecoration: 'none' }}
          >
            <GitHubIcon size={18} />
          </a>
          <button
            className="btn-icon"
            onClick={toggle}
            title={darkMode ? 'Switch to light mode' : 'Switch to dark mode'}
            aria-label="Toggle theme"
          >
            {darkMode ? <SunIcon /> : <MoonIcon />}
          </button>
          <button className="btn-icon" onClick={logout} title="Sign out" aria-label="Sign out">
            <LogoutIcon />
          </button>
        </div>
      </header>

      {/* Sidebar + workspace */}
      <div style={{ display: 'grid', gridTemplateColumns: '220px 1fr', flex: 1, minHeight: 0 }}>
        <aside
          style={{
            background: 'var(--bg-primary)',
            borderRight: '1px solid var(--border-color)',
            display: 'flex',
            flexDirection: 'column',
            minHeight: 0,
            overflow: 'hidden',
          }}
        >
          <nav
            style={{
              flex: 1,
              padding: '16px 12px',
              display: 'flex',
              flexDirection: 'column',
              gap: 16,
              overflowY: 'auto',
              minHeight: 0,
            }}
          >
            <NavSection title="Navigation">
              <NavItem to="/dashboard" label="Dashboard" />
              <NavItem to="/indexes" label="Indices" />
              <NavItem to="/vectors" label="Vectors" />
              <NavItem to="/search" label="Search" />
            </NavSection>
            <NavSection title="Observability">
              <NavItem to="/history" label="Request History" />
              <NavItem to="/explorer" label="API Explorer" />
            </NavSection>
            <NavSection title="System">
              <NavItem to="/server" label="Server Info" />
              <NavItem to="/settings" label="Settings" />
            </NavSection>
          </nav>

          <div
            style={{
              padding: '12px 16px',
              borderTop: '1px solid var(--border-color)',
              display: 'flex',
              flexDirection: 'column',
              gap: 2,
              fontSize: '0.72rem',
              color: 'var(--text-muted)',
            }}
          >
            <div>Dashboard</div>
            <div className="mono">v{__APP_VERSION__}</div>
          </div>
        </aside>

        <main className="workspace">
          <Outlet />
        </main>
      </div>
    </div>
  );
}

function NavSection({ title, children }: { title: string; children: React.ReactNode }) {
  return (
    <div>
      <div
        style={{
          fontSize: '0.72rem',
          fontWeight: 600,
          textTransform: 'uppercase',
          letterSpacing: '0.05em',
          color: 'var(--text-muted)',
          padding: '0 12px',
          marginBottom: 6,
        }}
      >
        {title}
      </div>
      <div style={{ display: 'flex', flexDirection: 'column', gap: 2 }}>{children}</div>
    </div>
  );
}

function NavItem({ to, label }: { to: string; label: string }) {
  return (
    <NavLink
      to={to}
      style={({ isActive }) => ({
        display: 'flex',
        alignItems: 'center',
        padding: '6px 12px',
        borderRadius: 6,
        fontSize: '0.875rem',
        fontWeight: isActive ? 600 : 500,
        color: isActive ? 'white' : 'var(--text-primary)',
        background: isActive ? 'var(--color-primary)' : 'transparent',
        textDecoration: 'none',
        transition: 'background 0.15s ease',
      })}
    >
      {label}
    </NavLink>
  );
}
