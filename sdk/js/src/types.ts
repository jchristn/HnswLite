/**
 * HnswLite SDK TypeScript type definitions.
 * All interfaces use camelCase properties; the client converts to/from
 * PascalCase when communicating with the server.
 */

// ── Index ────────────────────────────────────────────────────────────

export interface IndexResponse {
  guid: string;
  name: string;
  dimension: number;
  storageType: string;
  distanceFunction: string;
  m: number;
  maxM: number;
  efConstruction: number;
  vectorCount: number;
  createdUtc: string;
}

export interface CreateIndexRequest {
  name: string;
  dimension: number;
  storageType?: string;
  distanceFunction?: string;
  m?: number;
  maxM?: number;
  efConstruction?: number;
}

// ── Vectors ──────────────────────────────────────────────────────────

export interface AddVectorRequest {
  guid?: string;
  vector: number[];
  /** Optional human-readable name for the vector. */
  name?: string;
  /** Optional classification labels; null or omitted disables. */
  labels?: string[];
  /**
   * Optional arbitrary key/value tags. Values may be any JSON-serialisable
   * type; server-side filtering compares them via their stringified form.
   * Note: tag keys are treated as data, not schema — they are NOT transformed
   * between camelCase/PascalCase by the SDK.
   */
  tags?: Record<string, unknown>;
}

export interface AddVectorsRequest {
  vectors: AddVectorRequest[];
}

// ── Search ───────────────────────────────────────────────────────────

export interface SearchRequest {
  vector: number[];
  k: number;
  ef?: number | null;
  /**
   * Optional label filter (AND semantics). A result is kept only when every
   * label in this list is present on the vector's Labels. Null or empty
   * disables label filtering.
   */
  labels?: string[];
  /**
   * Optional tag filter (AND semantics). A result is kept only when every
   * key in this record exists on the vector's Tags and its stringified value
   * equals the filter value. Tag keys and values are passed through as-is.
   */
  tags?: Record<string, string>;
  /**
   * When true, label and tag comparisons are case-insensitive. Default false.
   */
  caseInsensitive?: boolean;
}

export interface VectorSearchResult {
  guid: string;
  vector: number[];
  distance: number;
  /** Optional name set on the vector. */
  name?: string | null;
  /** Optional labels set on the vector. */
  labels?: string[] | null;
  /** Optional tags set on the vector. */
  tags?: Record<string, unknown> | null;
}

export interface SearchResponse {
  results: VectorSearchResult[];
  searchTimeMs: number;
  /**
   * Number of HNSW candidates dropped by the server-side label/tag filter.
   * Zero when no filter was supplied.
   */
  filteredCount: number;
}

// ── Enumeration ──────────────────────────────────────────────────────

export interface EnumerationQuery {
  maxResults?: number;
  skip?: number;
  continuationToken?: string;
  ordering?: string;
  prefix?: string;
  suffix?: string;
  createdAfterUtc?: string;
  createdBeforeUtc?: string;
  /** Label filter (AND). Empty or omitted disables. */
  labels?: string[];
  /** Tag filter (AND). Keys and values are compared as strings. */
  tags?: Record<string, string>;
  /** Case-insensitive label/tag comparison when true. Default false. */
  caseInsensitive?: boolean;
}

// ── Vector entry ─────────────────────────────────────────────────────

export interface VectorEntry {
  guid: string;
  vector?: number[];
  name?: string | null;
  labels?: string[] | null;
  tags?: Record<string, unknown> | null;
}

export interface EnumerationResult<T> {
  success: boolean;
  maxResults: number;
  skip: number;
  continuationToken: string | null;
  endOfResults: boolean;
  totalRecords: number;
  recordsRemaining: number;
  timestampUtc: string;
  objects: T[];
  /**
   * Number of records dropped by the server-side label/tag filter. Zero when
   * no metadata filter was supplied.
   */
  filteredCount: number;
}
