# hnswlite-sdk

JavaScript/TypeScript SDK for the [HnswLite](https://github.com/jchristn/HnswLite) REST API.

## Install

```bash
npm install hnswlite-sdk
```

## Build from source

```bash
npm install
npm run build
```

## Quick start

```typescript
import { HnswLiteClient } from "hnswlite-sdk";

const client = new HnswLiteClient("http://localhost:8321", "my-api-key");
```

## API reference

### Constructor

```typescript
new HnswLiteClient(baseUrl: string, apiKey: string, apiKeyHeader?: string)
```

- `baseUrl` — Server base URL (e.g. `http://localhost:8321`).
- `apiKey` — Value sent in the authentication header.
- `apiKeyHeader` — Header name for the API key (default: `x-api-key`).

### `ping(): Promise<boolean>`

Health check (`GET /`). Unauthenticated. Returns `true` on HTTP 200.

```typescript
const ok = await client.ping();
```

### `headPing(): Promise<boolean>`

Head health check (`HEAD /`). Unauthenticated. Returns `true` on HTTP 200.

```typescript
const ok = await client.headPing();
```

### `enumerateIndexes(query?): Promise<EnumerationResult<IndexResponse>>`

List indexes with optional filtering and pagination.

```typescript
// List all
const result = await client.enumerateIndexes();

// With filters
const filtered = await client.enumerateIndexes({
  prefix: "my-",
  maxResults: 10,
  skip: 0,
  ordering: "CreatedDescending",
});

for (const idx of result.objects) {
  console.log(idx.name, idx.dimension);
}
```

### `createIndex(request): Promise<IndexResponse>`

Create a new index.

```typescript
const index = await client.createIndex({
  name: "my-index",
  dimension: 128,
  storageType: "InMemory",
  distanceFunction: "CosineDistance",
});
console.log(index.guid, index.createdUtc);
```

### `getIndex(name): Promise<IndexResponse>`

Retrieve an index by name.

```typescript
const index = await client.getIndex("my-index");
console.log(index.vectorCount);
```

### `deleteIndex(name): Promise<void>`

Delete an index by name.

```typescript
await client.deleteIndex("my-index");
```

### `search(name, request): Promise<SearchResponse>`

Search for nearest neighbors.

```typescript
const response = await client.search("my-index", {
  vector: [0.1, 0.2, 0.3 /* ... */],
  k: 10,
  ef: 50, // optional
});

for (const result of response.results) {
  console.log(result.guid, result.distance);
}
console.log(`Search took ${response.searchTimeMs}ms`);
```

### `addVector(name, request): Promise<AddVectorRequest>`

Add a single vector to an index.

```typescript
const added = await client.addVector("my-index", {
  guid: "optional-custom-guid", // omit to auto-generate
  vector: [0.1, 0.2, 0.3 /* ... */],
});
console.log(added.guid);
```

### `addVectors(name, request): Promise<AddVectorsRequest>`

Add multiple vectors in a single batch request.

```typescript
const added = await client.addVectors("my-index", {
  vectors: [
    { vector: [0.1, 0.2, 0.3] },
    { vector: [0.4, 0.5, 0.6] },
    { guid: "my-id", vector: [0.7, 0.8, 0.9] },
  ],
});
console.log(`Added ${added.vectors.length} vectors`);
```

### `removeVector(name, guid): Promise<void>`

Remove a vector from an index by its GUID.

```typescript
await client.removeVector("my-index", "some-guid");
```

### `enumerateVectors(indexName, query?, includeVectors?): Promise<EnumerationResult<VectorEntry>>`

Enumerate vectors in an index with optional filtering and pagination. When
`includeVectors` is `false` (the default), only GUIDs are returned; when
`true`, each entry also includes its raw `vector` values.

```typescript
// GUIDs only (lightweight)
const page = await client.enumerateVectors("my-index", { maxResults: 100 });
for (const entry of page.objects) {
  console.log(entry.guid); // entry.vector === undefined
}

// Include vector values
const full = await client.enumerateVectors("my-index", { maxResults: 10 }, true);
for (const entry of full.objects) {
  console.log(entry.guid, entry.vector);
}
```

### `getVector(indexName, vectorGuid): Promise<VectorEntry>`

Retrieve a single vector by its GUID. The returned `VectorEntry` always has
`vector` populated. Throws `HnswLiteApiError` (status 404) if the vector does
not exist.

```typescript
const entry = await client.getVector("my-index", "some-guid");
console.log(entry.guid, entry.vector);
```

## Error handling

All non-2xx responses throw `HnswLiteApiError`:

```typescript
import { HnswLiteApiError } from "hnswlite-sdk";

try {
  await client.getIndex("nonexistent");
} catch (err) {
  if (err instanceof HnswLiteApiError) {
    console.error(err.status);     // 404
    console.error(err.statusText); // "Not Found"
    console.error(err.body);       // raw response body
  }
}
```

## Running integration tests

```bash
BASE_URL=http://localhost:8321 API_KEY=mykey npm test
```

## License

MIT
