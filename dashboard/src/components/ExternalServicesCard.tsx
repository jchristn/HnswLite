import { useEffect, useState } from 'react';
import CopyButton from './shared/CopyButton';

declare const __HNSWLITE_GRAFANA_URL__: string;
declare const __HNSWLITE_PROMETHEUS_URL__: string;
declare const __HNSWLITE_TEMPO_URL__: string;

type ProbeState = 'checking' | 'reachable' | 'unreachable';

interface ExternalService {
  name: string;
  purpose: string;
  url: string;
  probePath: string;
  credentials: string | null;
  extraLink?: { label: string; href: string };
}

// Browser-reachable URLs for the bundled observability tools. Defaults use the host the dashboard was
// loaded from with the compose stack's published ports; override at build time with HNSWLITE_GRAFANA_URL,
// HNSWLITE_PROMETHEUS_URL, and HNSWLITE_TEMPO_URL when the ports or hosts differ.
function defaultUrl(port: number): string {
  const { protocol, hostname } = window.location;
  return `${protocol}//${hostname}:${port}`;
}

function buildServices(): ExternalService[] {
  const grafana = __HNSWLITE_GRAFANA_URL__ || defaultUrl(3000);
  const prometheus = __HNSWLITE_PROMETHEUS_URL__ || defaultUrl(9090);
  const tempo = __HNSWLITE_TEMPO_URL__ || defaultUrl(3200);
  return [
    {
      name: 'Grafana',
      purpose: 'Dashboards (HnswLite folder) and trace search',
      url: grafana,
      probePath: '/api/health',
      credentials: 'admin / admin',
      extraLink: { label: 'Overview dashboard', href: `${grafana}/d/hnswlite-overview` },
    },
    {
      name: 'Prometheus',
      purpose: 'Metrics store and PromQL queries',
      url: prometheus,
      probePath: '/-/healthy',
      credentials: null,
    },
    {
      name: 'Tempo',
      purpose: 'Trace store API (browse traces through Grafana)',
      url: tempo,
      probePath: '/ready',
      credentials: null,
    },
  ];
}

// An opaque no-cors response still proves the service answered; a network error or timeout means it is
// not part of this deployment or not reachable from this browser.
async function probe(url: string): Promise<ProbeState> {
  const controller = new AbortController();
  const timer = window.setTimeout(() => controller.abort(), 3000);
  try {
    await fetch(url, { mode: 'no-cors', cache: 'no-store', signal: controller.signal });
    return 'reachable';
  } catch {
    return 'unreachable';
  } finally {
    window.clearTimeout(timer);
  }
}

export default function ExternalServicesCard() {
  const [services] = useState<ExternalService[]>(() => buildServices());
  const [states, setStates] = useState<Record<string, ProbeState>>({});

  useEffect(() => {
    let cancelled = false;
    services.forEach((s) => {
      probe(s.url + s.probePath).then((state) => {
        if (!cancelled) setStates((prev) => ({ ...prev, [s.name]: state }));
      });
    });
    return () => {
      cancelled = true;
    };
  }, [services]);

  return (
    <div className="workspace-card" style={{ marginTop: 16 }}>
      <div className="workspace-card-header">
        <h3>External services</h3>
      </div>
      <div className="workspace-card-body tight">
        <table className="data-table">
          <thead>
            <tr>
              <th>Service</th>
              <th>URL</th>
              <th>Default credentials</th>
              <th>Status</th>
            </tr>
          </thead>
          <tbody>
            {services.map((s) => {
              const state = states[s.name] ?? 'checking';
              return (
                <tr key={s.name}>
                  <td>
                    <div style={{ fontWeight: 600 }}>{s.name}</div>
                    <div className="stat-card-sub">{s.purpose}</div>
                  </td>
                  <td>
                    <div style={{ display: 'flex', alignItems: 'center', gap: 8, flexWrap: 'wrap' }}>
                      <a href={s.url} target="_blank" rel="noopener noreferrer" className="mono">
                        {s.url}
                      </a>
                      <CopyButton text={s.url} />
                    </div>
                    {s.extraLink && (
                      <a href={s.extraLink.href} target="_blank" rel="noopener noreferrer" className="stat-card-sub">
                        {s.extraLink.label}
                      </a>
                    )}
                  </td>
                  <td>
                    {s.credentials ? (
                      <div style={{ display: 'flex', alignItems: 'center', gap: 8 }}>
                        <code className="mono">{s.credentials}</code>
                        <CopyButton text={s.credentials} />
                      </div>
                    ) : (
                      <span className="stat-card-sub">None</span>
                    )}
                  </td>
                  <td>
                    {state === 'checking' && <span className="status-pill muted">Checking</span>}
                    {state === 'reachable' && <span className="status-pill success">Reachable</span>}
                    {state === 'unreachable' && (
                      <span className="status-pill warn" title="Not part of this deployment, or not reachable from this browser.">
                        Not reachable
                      </span>
                    )}
                  </td>
                </tr>
              );
            })}
          </tbody>
        </table>
        <div className="stat-card-sub" style={{ padding: '10px 16px' }}>
          Defaults are for local development. Change the Grafana admin password (GRAFANA_ADMIN_PASSWORD) for any shared or hosted deployment.
        </div>
      </div>
    </div>
  );
}
