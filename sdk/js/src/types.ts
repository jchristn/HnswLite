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
}

export interface AddVectorsRequest {
  vectors: AddVectorRequest[];
}

// ── Search ───────────────────────────────────────────────────────────

export interface SearchRequest {
  vector: number[];
  k: number;
  ef?: number | null;
}

export interface VectorSearchResult {
  guid: string;
  vector: number[];
  distance: number;
}

export interface SearchResponse {
  results: VectorSearchResult[];
  searchTimeMs: number;
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
}

// ── Vector entry ─────────────────────────────────────────────────────

export interface VectorEntry {
  guid: string;
  vector?: number[];
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
}
