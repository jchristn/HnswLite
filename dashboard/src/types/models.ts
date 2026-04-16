export interface IndexSummary {
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
  Name: string;
  Dimension: number;
  StorageType: 'RAM' | 'SQLite';
  DistanceFunction: 'Euclidean' | 'Cosine' | 'DotProduct';
  M: number;
  MaxM: number;
  EfConstruction: number;
}

export interface AddVectorRequest {
  GUID?: string;
  Vector: number[];
  Name?: string;
  Labels?: string[];
  Tags?: Record<string, unknown>;
}

export interface AddVectorsRequest {
  Vectors: AddVectorRequest[];
}

export interface SearchRequest {
  Vector: number[];
  K: number;
  Ef?: number | null;
}

export interface VectorSearchResult {
  guid: string;
  vector: number[];
  distance: number;
  name?: string;
  labels?: string[];
  tags?: Record<string, unknown>;
}

export interface VectorEntry {
  guid: string;
  vector?: number[];
  name?: string;
  labels?: string[];
  tags?: Record<string, unknown>;
}

export interface SearchResponse {
  results: VectorSearchResult[];
  searchTimeMs: number;
}

export interface ApiErrorResponse {
  error: string;
  message: string;
  timestamp: string;
}

export type EnumerationOrder =
  | 'CreatedAscending'
  | 'CreatedDescending'
  | 'NameAscending'
  | 'NameDescending';

export interface EnumerationQuery {
  maxResults?: number;
  skip?: number;
  continuationToken?: string;
  ordering?: EnumerationOrder;
  prefix?: string;
  suffix?: string;
  createdAfterUtc?: string;
  createdBeforeUtc?: string;
}

export interface EnumerationResult<T> {
  success: boolean;
  maxResults: number;
  skip: number;
  continuationToken?: string;
  endOfResults: boolean;
  totalRecords: number;
  recordsRemaining: number;
  timestampUtc: string;
  objects: T[];
}

export interface RequestHistoryEntry {
  id: string;
  method: string;
  path: string;
  statusCode: number;
  durationMs: number;
  timestamp: string;
  body?: string;
  response?: string;
  errorMessage?: string;
}
