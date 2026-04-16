import type {
  IndexSummary,
  CreateIndexRequest,
  AddVectorRequest,
  AddVectorsRequest,
  EnumerationQuery,
  EnumerationResult,
  SearchRequest,
  SearchResponse,
  VectorEntry,
} from '../types/models';

declare const __HNSWLITE_SERVER_URL__: string;
const BASE_URL: string = __HNSWLITE_SERVER_URL__ || '';

let apiKey: string | null = null;
let apiKeyHeader: string = 'x-api-key';
let onUnauthorized: (() => void) | null = null;

export function setApiKey(key: string | null, header: string = 'x-api-key'): void {
  apiKey = key;
  apiKeyHeader = header;
}

export function getApiKey(): string | null {
  return apiKey;
}

export function setOnUnauthorized(cb: () => void): void {
  onUnauthorized = cb;
}

function keyToCamel(key: string): string {
  // Convert server PascalCase and SCREAMING_ACRONYMs correctly.
  //   "Name"           -> "name"
  //   "MaxM"           -> "maxM"
  //   "EfConstruction" -> "efConstruction"
  //   "GUID"           -> "guid"     (not "gUID")
  //   "URL"            -> "url"
  //   "URLPath"        -> "urlPath"  (acronym followed by a word)
  //   "name"           -> "name"     (already camelCase)
  if (key.length === 0) return key;

  // Find the end of the leading uppercase run.
  let i = 0;
  while (i < key.length) {
    const c = key[i];
    if (c >= 'A' && c <= 'Z') i++;
    else break;
  }

  if (i === 0) return key;
  if (i === key.length) return key.toLowerCase();
  if (i === 1) return key[0].toLowerCase() + key.slice(1);
  return key.substring(0, i - 1).toLowerCase() + key.substring(i - 1);
}

function camelizeKeys(obj: unknown): unknown {
  if (Array.isArray(obj)) return obj.map(camelizeKeys);
  if (obj !== null && typeof obj === 'object' && !(obj instanceof Date)) {
    const entries = Object.entries(obj as Record<string, unknown>).map(
      ([k, v]) => [keyToCamel(k), camelizeKeys(v)] as const,
    );
    return Object.fromEntries(entries);
  }
  return obj;
}

interface RequestOptions {
  timeoutMs?: number;
  skipAuth?: boolean;
}

export interface RequestHistoryEntryClient {
  id: string;
  method: string;
  path: string;
  statusCode: number;
  durationMs: number;
  timestamp: string; // ISO8601
  requestBody?: string;
  responseBody?: string;
  errorMessage?: string;
}

// ---------------------------------------------------------------------------
// Persistent request history (30-day retention, localStorage-backed)
// ---------------------------------------------------------------------------

const HISTORY_STORAGE_KEY = 'hnswlite_request_history_v1';
const HISTORY_RETENTION_MS = 30 * 24 * 60 * 60 * 1000; // 30 days
const HISTORY_MAX_ENTRIES = 5000;

let _history: RequestHistoryEntryClient[] = loadHistory();
const _historyListeners: Array<(entries: RequestHistoryEntryClient[]) => void> = [];

function loadHistory(): RequestHistoryEntryClient[] {
  try {
    const raw = localStorage.getItem(HISTORY_STORAGE_KEY);
    if (!raw) return [];
    const parsed = JSON.parse(raw) as RequestHistoryEntryClient[];
    if (!Array.isArray(parsed)) return [];
    return purgeStale(parsed);
  } catch {
    return [];
  }
}

function purgeStale(entries: RequestHistoryEntryClient[]): RequestHistoryEntryClient[] {
  const cutoff = Date.now() - HISTORY_RETENTION_MS;
  const fresh = entries.filter((e) => {
    const t = new Date(e.timestamp).getTime();
    return Number.isFinite(t) && t >= cutoff;
  });
  if (fresh.length > HISTORY_MAX_ENTRIES) fresh.length = HISTORY_MAX_ENTRIES;
  return fresh;
}

function persistHistory(): void {
  try {
    localStorage.setItem(HISTORY_STORAGE_KEY, JSON.stringify(_history));
  } catch {
    // quota exceeded — trim aggressively and retry once
    if (_history.length > 1000) {
      _history.length = 1000;
      try {
        localStorage.setItem(HISTORY_STORAGE_KEY, JSON.stringify(_history));
      } catch {
        // give up silently
      }
    }
  }
}

function notifyHistory(): void {
  const snapshot = _history.slice();
  for (const cb of _historyListeners) cb(snapshot);
}

function recordHistory(entry: RequestHistoryEntryClient): void {
  _history.unshift(entry);
  _history = purgeStale(_history);
  persistHistory();
  notifyHistory();
}

export function getRequestHistory(): RequestHistoryEntryClient[] {
  // Defensive: purge stale on every read so stale entries never appear.
  const purged = purgeStale(_history);
  if (purged.length !== _history.length) {
    _history = purged;
    persistHistory();
  }
  return _history.slice();
}

export function clearRequestHistory(): void {
  _history = [];
  persistHistory();
  notifyHistory();
}

export function removeRequestHistoryEntry(id: string): void {
  const before = _history.length;
  _history = _history.filter((e) => e.id !== id);
  if (_history.length !== before) {
    persistHistory();
    notifyHistory();
  }
}

export function subscribeRequestHistory(cb: (entries: RequestHistoryEntryClient[]) => void): () => void {
  _historyListeners.push(cb);
  return () => {
    const idx = _historyListeners.indexOf(cb);
    if (idx >= 0) _historyListeners.splice(idx, 1);
  };
}

// Periodic purge while the dashboard is open (hourly).
setInterval(() => {
  const before = _history.length;
  _history = purgeStale(_history);
  if (_history.length !== before) {
    persistHistory();
    notifyHistory();
  }
}, 60 * 60 * 1000);

// ---------------------------------------------------------------------------
// HTTP core
// ---------------------------------------------------------------------------

async function request<T>(
  method: string,
  path: string,
  body?: unknown,
  opts?: RequestOptions,
): Promise<T> {
  const headers: Record<string, string> = { 'Content-Type': 'application/json' };
  if (apiKey && !opts?.skipAuth) headers[apiKeyHeader] = apiKey;

  const controller = new AbortController();
  const timeoutMs = opts?.timeoutMs ?? 30000;
  const timeoutId = setTimeout(() => controller.abort(), timeoutMs);

  const requestBodyStr = body ? JSON.stringify(body) : undefined;
  const started = performance.now();
  const id = crypto.randomUUID();

  try {
    const res = await fetch(`${BASE_URL}${path}`, {
      method,
      headers,
      body: requestBodyStr,
      signal: controller.signal,
    });

    clearTimeout(timeoutId);
    const durationMs = performance.now() - started;

    const text = await res.text();

    recordHistory({
      id,
      method,
      path,
      statusCode: res.status,
      durationMs,
      timestamp: new Date().toISOString(),
      requestBody: requestBodyStr,
      responseBody: text,
      errorMessage: res.ok ? undefined : text,
    });

    if (res.status === 401) {
      onUnauthorized?.();
      throw new Error('Unauthorized');
    }

    if (!res.ok) {
      let msg = `${res.status}: ${text || res.statusText}`;
      try {
        const e = JSON.parse(text);
        msg = e.Message || e.message || e.Error || e.error || msg;
      } catch {
        // non-JSON error body — use raw text
      }
      throw new Error(msg);
    }

    if (res.status === 204 || text.length === 0) return undefined as T;

    const parsed = JSON.parse(text);
    return camelizeKeys(parsed) as T;
  } catch (err) {
    clearTimeout(timeoutId);
    if (err instanceof DOMException && err.name === 'AbortError') {
      const durationMs = performance.now() - started;
      recordHistory({
        id,
        method,
        path,
        statusCode: 0,
        durationMs,
        timestamp: new Date().toISOString(),
        requestBody: requestBodyStr,
        errorMessage: 'Request timed out',
      });
      throw new Error('Request timed out');
    }
    throw err;
  }
}

function get<T>(path: string, opts?: RequestOptions): Promise<T> {
  return request<T>('GET', path, undefined, opts);
}
function post<T>(path: string, body?: unknown, opts?: RequestOptions): Promise<T> {
  return request<T>('POST', path, body, opts);
}
function del<T>(path: string, opts?: RequestOptions): Promise<T> {
  return request<T>('DELETE', path, undefined, opts);
}

export async function ping(): Promise<boolean> {
  try {
    const res = await fetch(`${BASE_URL}/`, { method: 'HEAD' });
    return res.ok;
  } catch {
    return false;
  }
}

function buildEnumQueryString(query?: EnumerationQuery): string {
  if (!query) return '';
  const parts: string[] = [];
  if (query.maxResults !== undefined) parts.push(`maxResults=${query.maxResults}`);
  if (query.skip !== undefined) parts.push(`skip=${query.skip}`);
  if (query.continuationToken) parts.push(`continuationToken=${encodeURIComponent(query.continuationToken)}`);
  if (query.ordering) parts.push(`ordering=${query.ordering}`);
  if (query.prefix) parts.push(`prefix=${encodeURIComponent(query.prefix)}`);
  if (query.suffix) parts.push(`suffix=${encodeURIComponent(query.suffix)}`);
  if (query.createdAfterUtc) parts.push(`createdAfterUtc=${encodeURIComponent(query.createdAfterUtc)}`);
  if (query.createdBeforeUtc) parts.push(`createdBeforeUtc=${encodeURIComponent(query.createdBeforeUtc)}`);
  return parts.length > 0 ? `?${parts.join('&')}` : '';
}

/**
 * Enumerate indexes with filtering, sorting, and pagination. The server returns
 * an EnumerationResult — callers that legitimately need every record should use
 * {@link listAllIndexes} which drains pages internally.
 */
export function enumerateIndexes(query?: EnumerationQuery): Promise<EnumerationResult<IndexSummary>> {
  return get<EnumerationResult<IndexSummary>>(`/v1.0/indexes${buildEnumQueryString(query)}`);
}

/**
 * Drain every page of index enumeration. Use only on pages that need an aggregate
 * view (stat tiles, dropdowns) — never for a paginated table.
 *
 * Uses the server-enforced max of 1000 per page, so this is O(N/1000) round-trips.
 */
export async function listAllIndexes(): Promise<IndexSummary[]> {
  const all: IndexSummary[] = [];
  let skip = 0;
  const pageSize = 1000;
  for (;;) {
    const page = await enumerateIndexes({
      maxResults: pageSize,
      skip,
      ordering: 'NameAscending',
    });
    all.push(...page.objects);
    if (page.endOfResults) break;
    skip += page.objects.length;
    if (page.objects.length === 0) break; // defensive against stuck cursors
  }
  return all;
}

export function getIndex(name: string): Promise<IndexSummary> {
  return get<IndexSummary>(`/v1.0/indexes/${encodeURIComponent(name)}`);
}

export function createIndex(req: CreateIndexRequest): Promise<IndexSummary> {
  return post<IndexSummary>('/v1.0/indexes', req);
}

export function deleteIndex(name: string): Promise<void> {
  return del<void>(`/v1.0/indexes/${encodeURIComponent(name)}`);
}

export function addVector(name: string, req: AddVectorRequest): Promise<AddVectorRequest> {
  return post<AddVectorRequest>(`/v1.0/indexes/${encodeURIComponent(name)}/vectors`, req);
}

export function addVectors(name: string, req: AddVectorsRequest): Promise<AddVectorsRequest> {
  return post<AddVectorsRequest>(`/v1.0/indexes/${encodeURIComponent(name)}/vectors/batch`, req);
}

export function removeVector(name: string, guid: string): Promise<void> {
  return del<void>(`/v1.0/indexes/${encodeURIComponent(name)}/vectors/${encodeURIComponent(guid)}`);
}

export function getVector(name: string, guid: string): Promise<VectorEntry> {
  return get<VectorEntry>(
    `/v1.0/indexes/${encodeURIComponent(name)}/vectors/${encodeURIComponent(guid)}`,
  );
}

export function enumerateVectors(
  name: string,
  query?: EnumerationQuery,
  includeVectors: boolean = false,
): Promise<EnumerationResult<VectorEntry>> {
  const qs = buildEnumQueryString(query);
  const sep = qs.length > 0 ? '&' : '?';
  return get<EnumerationResult<VectorEntry>>(
    `/v1.0/indexes/${encodeURIComponent(name)}/vectors${qs}${sep}includeVectors=${includeVectors}`,
  );
}

export function searchVectors(name: string, req: SearchRequest): Promise<SearchResponse> {
  return post<SearchResponse>(`/v1.0/indexes/${encodeURIComponent(name)}/search`, req);
}

export interface RawResponse {
  status: number;
  statusText: string;
  durationMs: number;
  headers: Record<string, string>;
  body: string;
  ok: boolean;
}

/**
 * Low-level request helper used by the API Explorer. Returns the full response
 * (status, headers, duration, raw body) instead of a parsed payload so the UI
 * can display everything. Still goes through the history recorder.
 */
export async function rawRequest(
  method: string,
  path: string,
  bodyStr?: string,
): Promise<RawResponse> {
  const headers: Record<string, string> = {};
  if (bodyStr && bodyStr.length > 0) headers['Content-Type'] = 'application/json';
  if (apiKey) headers[apiKeyHeader] = apiKey;

  const controller = new AbortController();
  const timeoutId = setTimeout(() => controller.abort(), 30000);
  const started = performance.now();
  const id = crypto.randomUUID();

  try {
    const res = await fetch(`${BASE_URL}${path}`, {
      method,
      headers,
      body: bodyStr && bodyStr.length > 0 ? bodyStr : undefined,
      signal: controller.signal,
    });
    clearTimeout(timeoutId);
    const durationMs = performance.now() - started;
    const text = await res.text();

    const headerObj: Record<string, string> = {};
    res.headers.forEach((v, k) => {
      headerObj[k] = v;
    });

    recordHistory({
      id,
      method,
      path,
      statusCode: res.status,
      durationMs,
      timestamp: new Date().toISOString(),
      requestBody: bodyStr,
      responseBody: text,
      errorMessage: res.ok ? undefined : text,
    });

    if (res.status === 401) onUnauthorized?.();

    return {
      status: res.status,
      statusText: res.statusText,
      durationMs,
      headers: headerObj,
      body: text,
      ok: res.ok,
    };
  } catch (err) {
    clearTimeout(timeoutId);
    const durationMs = performance.now() - started;
    const msg = err instanceof DOMException && err.name === 'AbortError'
      ? 'Request timed out'
      : err instanceof Error
        ? err.message
        : String(err);
    recordHistory({
      id,
      method,
      path,
      statusCode: 0,
      durationMs,
      timestamp: new Date().toISOString(),
      requestBody: bodyStr,
      errorMessage: msg,
    });
    throw err instanceof Error ? err : new Error(msg);
  }
}

export async function validateApiKey(): Promise<boolean> {
  try {
    await enumerateIndexes({ maxResults: 1 });
    return true;
  } catch (err) {
    if (err instanceof Error && err.message === 'Unauthorized') return false;
    throw err;
  }
}
