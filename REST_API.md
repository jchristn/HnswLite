# HnswLite REST API

All REST APIs are served by the `HnswIndex.Server` Watson host. Every endpoint
returns JSON (or an empty body on `204 No Content`).

- **Base URL:** `http://{hostname}:{port}` (default `http://localhost:8080`).
- **Authentication:** `x-api-key: <AdminApiKey>` header on every route except
  `GET /`, `HEAD /`, and any `OPTIONS` pre-flight. The header name is
  configurable via `Server.AdminApiKeyHeader` in `hnswindex.json`.
- **CORS:** CORS headers are emitted on every response. OPTIONS pre-flight is
  served by Watson's pre-flight hook (bypasses authentication). Origins / methods
  / headers are configured under the `Cors` block in `hnswindex.json`.
- **Content type:** `application/json` for request bodies that exist.

## Enumeration contract

Every GET that returns a collection follows a single, consistent contract:

- Query-string parameters populate a server-side `EnumerationQuery`.
- The response body is an `EnumerationResult<T>`.
- **There are no "get all" endpoints.** Clients are expected to page.

### `EnumerationQuery`

All parameters are optional. Aliases are matched case-insensitively.

| Parameter          | Type     | Aliases        | Default            | Notes |
|--------------------|----------|----------------|--------------------|-------|
| `maxResults`       | int      | `max`, `limit` | `100`              | Clamped to `[1, 1000]`. |
| `skip`             | int      | `offset`       | `0`                | Must be `>= 0`. |
| `continuationToken`| GUID     | `token`        | *(none)*           | Cursor of the last object from the previous page. Reserved — not every collection supports it yet. Mutually exclusive with `skip > 0`. |
| `ordering`         | enum     | `order`        | `CreatedDescending`| One of `CreatedAscending`, `CreatedDescending`, `NameAscending`, `NameDescending`. |
| `prefix`           | string   |                | *(none)*           | Case-insensitive `StartsWith` filter on the record's identifying name. |
| `suffix`           | string   |                | *(none)*           | Case-insensitive `EndsWith` filter. |
| `createdAfterUtc`  | ISO-8601 | `after`        | *(none)*           | Keep only records created strictly after this timestamp. |
| `createdBeforeUtc` | ISO-8601 | `before`       | *(none)*           | Keep only records created strictly before this timestamp. |
| `labels`           | string   |                | *(none)*           | Comma-separated list of required labels. **AND semantics** — a record is kept only when every label is present on its `Labels`. Individual labels cannot contain `,`. |
| `tags`             | string   |                | *(none)*           | Comma-separated list of `key:value` pairs. **AND semantics** — every key must exist on the record's `Tags` with a stringified value equal to the filter value. Keys cannot contain `:` or `,`; values cannot contain `,`. |
| `caseInsensitive`  | bool     |                | `false`            | When `true`, labels, tag keys, and tag values are compared using `StringComparison.OrdinalIgnoreCase`. Accepts `true` / `false` / `1` / `0`. |

Invalid values (`skip=-1`, unknown `ordering`, malformed timestamp, `createdAfterUtc >= createdBeforeUtc`, malformed `tags` segment, unrecognised `caseInsensitive` value, or `skip > 0` combined with a `continuationToken`) yield `400 Bad Request` with a descriptive message.

### `EnumerationResult<T>`

```json
{
  "Success": true,
  "MaxResults": 25,
  "Skip": 0,
  "EndOfResults": false,
  "TotalRecords": 137,
  "RecordsRemaining": 112,
  "TimestampUtc": "2026-04-16T00:44:46.248497Z",
  "FilteredCount": 0,
  "Objects": [ /* page contents */ ]
}
```

`ContinuationToken` is serialized only when the page is not the last and the
underlying collection supports cursor paging. `TotalRecords` reflects the
filtered set — not the total records in the database. `FilteredCount` is the
number of records dropped by the `labels` / `tags` metadata filter specifically
(independent of `prefix`/`suffix`/`createdAfter/Before`) — zero when no
metadata filter was supplied.

## Endpoints

### `GET /`

Root homepage (HTML). Unauthenticated.

### `HEAD /`

Header-only root check. Unauthenticated.

### `OPTIONS *`

CORS pre-flight. Served by Watson's pre-flight hook — bypasses authentication.

---

### `GET /v1.0/indexes`

Enumerate indexes.

- Query: `EnumerationQuery` — see above.
- Response: `EnumerationResult<IndexResponse>`.

`IndexResponse` shape:

```json
{
  "GUID": "uuid",
  "Name": "string",
  "Dimension": 384,
  "StorageType": "RAM | SQLite",
  "DistanceFunction": "Euclidean | Cosine | DotProduct",
  "M": 16,
  "MaxM": 32,
  "EfConstruction": 200,
  "VectorCount": 0,
  "CreatedUtc": "ISO-8601"
}
```

Example:

```bash
curl -H "x-api-key: $KEY" \
  "http://localhost:8080/v1.0/indexes?maxResults=25&skip=0&ordering=NameAscending&prefix=prod-"
```

---

### `POST /v1.0/indexes`

Create a new index.

- Body: `CreateIndexRequest` — `Name`, `Dimension` (>0), `StorageType`
  (`"RAM"` or `"SQLite"`), `DistanceFunction` (`"Euclidean"`, `"Cosine"`, or
  `"DotProduct"`), `M`, `MaxM`, `EfConstruction`.
- Response: `201 Created` + `IndexResponse`.
- Errors: `400` (invalid body / dimension), `409` (name already exists).

### `GET /v1.0/indexes/{name}`

Get a single index by name.

- Response: `200 OK` + `IndexResponse`.
- Errors: `404 IndexNotFound`.

### `DELETE /v1.0/indexes/{name}`

Delete an index and all its vectors.

- Response: `204 No Content`.
- Errors: `404 IndexNotFound`.

---

### `POST /v1.0/indexes/{name}/search`

K-nearest-neighbour query.

- Body:
  ```json
  {
    "Vector": [0.1, 0.2, 0.3, 0.4],
    "K": 10,
    "Ef": null,
    "Labels": ["red", "small"],
    "Tags": { "env": "prod", "owner": "alice" },
    "CaseInsensitive": false
  }
  ```
  - `Labels`, `Tags`, `CaseInsensitive` are optional (v1.2+). `Labels` uses **AND** semantics (every label must be present on the vector). `Tags` uses **AND** on key/value equality; stored tag values are stringified via `Convert.ToString(value, InvariantCulture)` before comparison. `CaseInsensitive=true` folds both labels and tag keys/values with `OrdinalIgnoreCase`.
- Response: `SearchResponse` — `{ "Results": [{GUID, Vector, Distance, Name, Labels, Tags}], "SearchTimeMs": n, "FilteredCount": m }`.
  - `Name` / `Labels` / `Tags` are populated on each result when set on the stored vector (null otherwise).
  - `FilteredCount` is the number of HNSW candidates dropped by the metadata filter — zero when no filter is set. Because filtering is applied **after** graph traversal, restrictive filters can return fewer than `K` results; `FilteredCount` makes this visible.
- Errors: `400` (dimension mismatch), `404` (index not found).

### `GET /v1.0/indexes/{name}/vectors`

Enumerate vectors stored in an index.

- Query: standard `EnumerationQuery` fields (`maxResults`, `skip`, `ordering`, `prefix`, `suffix`, `createdAfterUtc`, `createdBeforeUtc`, `labels`, `tags`, `caseInsensitive`) — see *Enumeration contract* above. The `prefix` filter is matched case-insensitively against the GUID's string form. The `labels` / `tags` filters are applied **before** pagination, so `TotalRecords` reflects the filtered set and `FilteredCount` reports how many records were dropped by them.
- Additional query parameter: `includeVectors=true|false` (default `false`). Aliases: `IncludeVectors`, `include`. When `false` the `Vector` field is omitted from every object in the page.
- Response: `EnumerationResult<VectorEntryResponse>` with `Objects` shaped as:
  ```json
  { "GUID": "uuid", "Vector": [0.1, 0.2, 0.3] }
  ```
  The `Vector` property is present when `includeVectors=true`, omitted otherwise.
- Errors: `400` (invalid query), `404 IndexNotFound`.

Examples:

```bash
# listing only (no vector bodies, default)
curl -H "x-api-key: $KEY" \
  "http://localhost:8080/v1.0/indexes/my-index/vectors?maxResults=25&skip=0"

# include the vector values
curl -H "x-api-key: $KEY" \
  "http://localhost:8080/v1.0/indexes/my-index/vectors?maxResults=10&includeVectors=true"

# filter by labels and tags (AND semantics, case-insensitive)
curl -H "x-api-key: $KEY" \
  "http://localhost:8080/v1.0/indexes/my-index/vectors?labels=red,small&tags=env:prod,owner:alice&caseInsensitive=true"
```

### `GET /v1.0/indexes/{name}/vectors/{guid}`

Fetch a single vector by GUID. The `Vector` field is always populated.

- Response: `200 OK` + `VectorEntryResponse`.
- Errors: `400` (malformed GUID), `404 IndexNotFound` (unknown index), `404 VectorNotFound` (unknown GUID in this index).

### `POST /v1.0/indexes/{name}/vectors`

Add a single vector.

- Body: `{ "GUID": "optional", "Vector": [...] }`.
- Response: `201 Created` echoing the request body.
- Errors: `400`, `404`.

### `POST /v1.0/indexes/{name}/vectors/batch`

Add a batch of vectors.

- Body: `{ "Vectors": [{ "GUID"?, "Vector": [...] }, ...] }`.
- Response: `201 Created` echoing the request body.
- Errors: `400`, `404`.

### `DELETE /v1.0/indexes/{name}/vectors/{guid}`

Remove a vector by GUID.

- Response: `204 No Content` on success; `404` if absent.

---

## Error response shape

All non-2xx responses use the same envelope:

```json
{
  "Error": "BadRequest",
  "Message": "Human-readable diagnostic",
  "Timestamp": "2026-04-16T00:44:47.237597Z"
}
```

`Error` is an `ApiErrorEnum` value: `BadRequest`, `Unauthorized`, `Forbidden`,
`NotFound`, `IndexNotFound`, `VectorNotFound`, `Conflict`, `InternalServerError`,
`InvalidDimension`, `StorageError`.
