export { HnswLiteApiError } from "./errors.js";
export type {
  IndexResponse,
  CreateIndexRequest,
  AddVectorRequest,
  AddVectorsRequest,
  SearchRequest,
  SearchResponse,
  VectorSearchResult,
  EnumerationQuery,
  EnumerationResult,
  VectorEntry,
} from "./types.js";

import { HnswLiteApiError } from "./errors.js";
import type {
  IndexResponse,
  CreateIndexRequest,
  AddVectorRequest,
  AddVectorsRequest,
  SearchRequest,
  SearchResponse,
  EnumerationQuery,
  EnumerationResult,
  VectorEntry,
} from "./types.js";

// ── Case-conversion helpers ──────────────────────────────────────────

function toPascalCase(str: string): string {
  return str.charAt(0).toUpperCase() + str.slice(1);
}

function toCamelCase(str: string): string {
  return str.charAt(0).toLowerCase() + str.slice(1);
}

/**
 * Fields whose inner keys are DATA (not schema) and must not be transformed
 * between camelCase and PascalCase by the key-walker. Comparison is
 * case-insensitive so `tags`, `Tags`, and any variant are all opaque.
 */
const OPAQUE_KEYS: ReadonlySet<string> = new Set(["tags"]);

function isOpaqueKey(k: string): boolean {
  return OPAQUE_KEYS.has(k.toLowerCase());
}

/** Recursively convert all object keys from camelCase to PascalCase. */
function keysToPascal(obj: unknown): unknown {
  if (obj === null || obj === undefined) return obj;
  if (Array.isArray(obj)) return obj.map(keysToPascal);
  if (typeof obj === "object") {
    const out: Record<string, unknown> = {};
    for (const [k, v] of Object.entries(obj as Record<string, unknown>)) {
      out[toPascalCase(k)] = isOpaqueKey(k) ? v : keysToPascal(v);
    }
    return out;
  }
  return obj;
}

/** Recursively convert all object keys from PascalCase to camelCase. */
function keysToCamel(obj: unknown): unknown {
  if (obj === null || obj === undefined) return obj;
  if (Array.isArray(obj)) return obj.map(keysToCamel);
  if (typeof obj === "object") {
    const out: Record<string, unknown> = {};
    for (const [k, v] of Object.entries(obj as Record<string, unknown>)) {
      out[toCamelCase(k)] = isOpaqueKey(k) ? v : keysToCamel(v);
    }
    return out;
  }
  return obj;
}

// ── Client ───────────────────────────────────────────────────────────

export class HnswLiteClient {
  private readonly baseUrl: string;
  private readonly apiKey: string;
  private readonly apiKeyHeader: string;

  /**
   * Create a new HnswLite SDK client.
   *
   * @param baseUrl   Base URL of the HnswLite server (e.g. `http://localhost:8321`).
   * @param apiKey    API key value sent via the auth header.
   * @param apiKeyHeader  Name of the header used for authentication (default `x-api-key`).
   */
  constructor(baseUrl: string, apiKey: string, apiKeyHeader: string = "x-api-key") {
    // Strip trailing slash
    this.baseUrl = baseUrl.replace(/\/+$/, "");
    this.apiKey = apiKey;
    this.apiKeyHeader = apiKeyHeader;
  }

  // ── Private helpers ────────────────────────────────────────────────

  private authHeaders(): Record<string, string> {
    return { [this.apiKeyHeader]: this.apiKey };
  }

  private async request(
    method: string,
    path: string,
    options?: { body?: unknown; auth?: boolean; query?: Record<string, string | number | undefined> },
  ): Promise<Response> {
    const auth = options?.auth ?? true;
    let url = `${this.baseUrl}${path}`;

    if (options?.query) {
      const params = new URLSearchParams();
      for (const [k, v] of Object.entries(options.query)) {
        if (v !== undefined && v !== null && v !== "") {
          params.set(k, String(v));
        }
      }
      const qs = params.toString();
      if (qs) url += `?${qs}`;
    }

    const headers: Record<string, string> = {};
    if (auth) Object.assign(headers, this.authHeaders());

    if (options?.body !== undefined) {
      headers["Content-Type"] = "application/json";
    }

    const res = await fetch(url, {
      method,
      headers,
      body: options?.body !== undefined ? JSON.stringify(keysToPascal(options.body)) : undefined,
    });

    return res;
  }

  private async assertOk(res: Response): Promise<void> {
    if (!res.ok) {
      const body = await res.text().catch(() => "");
      throw new HnswLiteApiError(res.status, res.statusText, body);
    }
  }

  private async json<T>(res: Response): Promise<T> {
    await this.assertOk(res);
    const raw = await res.json();
    return keysToCamel(raw) as T;
  }

  // ── Public API ─────────────────────────────────────────────────────

  /** `GET /` — Health ping (unauthenticated). Resolves `true` on 200. */
  async ping(): Promise<boolean> {
    const res = await this.request("GET", "/", { auth: false });
    return res.ok;
  }

  /** `HEAD /` — Head ping (unauthenticated). Resolves `true` on 200. */
  async headPing(): Promise<boolean> {
    const res = await this.request("HEAD", "/", { auth: false });
    return res.ok;
  }

  /** Build a query-string record from an `EnumerationQuery`. */
  private buildEnumerationQuery(
    query?: EnumerationQuery,
  ): Record<string, string | number | undefined> {
    const q: Record<string, string | number | undefined> = {};
    if (query) {
      if (query.maxResults !== undefined) q["maxResults"] = query.maxResults;
      if (query.skip !== undefined) q["skip"] = query.skip;
      if (query.continuationToken !== undefined) q["continuationToken"] = query.continuationToken;
      if (query.ordering !== undefined) q["ordering"] = query.ordering;
      if (query.prefix !== undefined) q["prefix"] = query.prefix;
      if (query.suffix !== undefined) q["suffix"] = query.suffix;
      if (query.createdAfterUtc !== undefined) q["createdAfterUtc"] = query.createdAfterUtc;
      if (query.createdBeforeUtc !== undefined) q["createdBeforeUtc"] = query.createdBeforeUtc;
      // Labels are joined with commas. Individual labels must not contain ','.
      // URLSearchParams (used in request()) handles percent-encoding of the
      // overall value, so we don't pre-encode here.
      if (query.labels && query.labels.length > 0) {
        const filtered = query.labels.filter((s) => s !== undefined && s !== null && s !== "");
        if (filtered.length > 0) q["labels"] = filtered.join(",");
      }
      // Tags are serialised as `key:value,key:value`. Keys must not contain ':'
      // or ','; values must not contain ','.
      if (query.tags) {
        const parts: string[] = [];
        for (const [k, v] of Object.entries(query.tags)) {
          if (!k) continue;
          parts.push(`${k}:${v ?? ""}`);
        }
        if (parts.length > 0) q["tags"] = parts.join(",");
      }
      if (query.caseInsensitive) q["caseInsensitive"] = "true";
    }
    return q;
  }

  /** `GET /v1.0/indexes` — List / search indexes (paginated). */
  async enumerateIndexes(query?: EnumerationQuery): Promise<EnumerationResult<IndexResponse>> {
    const q = this.buildEnumerationQuery(query);
    const res = await this.request("GET", "/v1.0/indexes", { query: q });
    return this.json<EnumerationResult<IndexResponse>>(res);
  }

  /** `POST /v1.0/indexes` — Create a new index. */
  async createIndex(request: CreateIndexRequest): Promise<IndexResponse> {
    const res = await this.request("POST", "/v1.0/indexes", { body: request });
    if (res.status !== 201 && !res.ok) {
      const body = await res.text().catch(() => "");
      throw new HnswLiteApiError(res.status, res.statusText, body);
    }
    const raw = await res.json();
    return keysToCamel(raw) as IndexResponse;
  }

  /** `GET /v1.0/indexes/{name}` — Retrieve an index by name. */
  async getIndex(name: string): Promise<IndexResponse> {
    const res = await this.request("GET", `/v1.0/indexes/${encodeURIComponent(name)}`);
    return this.json<IndexResponse>(res);
  }

  /** `DELETE /v1.0/indexes/{name}` — Delete an index by name. */
  async deleteIndex(name: string): Promise<void> {
    const res = await this.request("DELETE", `/v1.0/indexes/${encodeURIComponent(name)}`);
    await this.assertOk(res);
  }

  /** `POST /v1.0/indexes/{name}/search` — Search for nearest neighbors. */
  async search(name: string, request: SearchRequest): Promise<SearchResponse> {
    const res = await this.request("POST", `/v1.0/indexes/${encodeURIComponent(name)}/search`, {
      body: request,
    });
    return this.json<SearchResponse>(res);
  }

  /** `POST /v1.0/indexes/{name}/vectors` — Add a single vector. */
  async addVector(name: string, request: AddVectorRequest): Promise<AddVectorRequest> {
    const res = await this.request("POST", `/v1.0/indexes/${encodeURIComponent(name)}/vectors`, {
      body: request,
    });
    if (res.status !== 201 && !res.ok) {
      const body = await res.text().catch(() => "");
      throw new HnswLiteApiError(res.status, res.statusText, body);
    }
    const raw = await res.json();
    return keysToCamel(raw) as AddVectorRequest;
  }

  /** `POST /v1.0/indexes/{name}/vectors/batch` — Add multiple vectors. */
  async addVectors(name: string, request: AddVectorsRequest): Promise<AddVectorsRequest> {
    const res = await this.request("POST", `/v1.0/indexes/${encodeURIComponent(name)}/vectors/batch`, {
      body: request,
    });
    if (res.status !== 201 && !res.ok) {
      const body = await res.text().catch(() => "");
      throw new HnswLiteApiError(res.status, res.statusText, body);
    }
    const raw = await res.json();
    return keysToCamel(raw) as AddVectorsRequest;
  }

  /** `DELETE /v1.0/indexes/{name}/vectors/{guid}` — Remove a vector. */
  async removeVector(name: string, guid: string): Promise<void> {
    const res = await this.request(
      "DELETE",
      `/v1.0/indexes/${encodeURIComponent(name)}/vectors/${encodeURIComponent(guid)}`,
    );
    await this.assertOk(res);
  }

  /**
   * `GET /v1.0/indexes/{name}/vectors` — Enumerate vectors in an index (paginated).
   *
   * @param indexName       Name of the index to enumerate.
   * @param query           Optional enumeration filter / pagination parameters.
   * @param includeVectors  When `true`, each entry's `vector` field is populated
   *                        with the raw vector values; otherwise only GUIDs are
   *                        returned. Default `false`.
   */
  async enumerateVectors(
    indexName: string,
    query?: EnumerationQuery,
    includeVectors?: boolean,
  ): Promise<EnumerationResult<VectorEntry>> {
    const q = this.buildEnumerationQuery(query);
    q["includeVectors"] = includeVectors === true ? "true" : "false";
    const res = await this.request(
      "GET",
      `/v1.0/indexes/${encodeURIComponent(indexName)}/vectors`,
      { query: q },
    );
    return this.json<EnumerationResult<VectorEntry>>(res);
  }

  /**
   * `GET /v1.0/indexes/{name}/vectors/{guid}` — Retrieve a single vector by GUID.
   * The returned `VectorEntry` always has `vector` populated.
   */
  async getVector(indexName: string, vectorGuid: string): Promise<VectorEntry> {
    const res = await this.request(
      "GET",
      `/v1.0/indexes/${encodeURIComponent(indexName)}/vectors/${encodeURIComponent(vectorGuid)}`,
    );
    return this.json<VectorEntry>(res);
  }
}
