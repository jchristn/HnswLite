# HnswLite SDKs

Client libraries for the HnswLite REST API. Each SDK provides 100% endpoint
coverage, typed models, pagination via `EnumerationQuery` / `EnumerationResult`,
and a test harness that exercises every method against a live server.

## Available SDKs

| Language   | Directory          | Package name      | Runtime requirements              |
|------------|--------------------|--------------------|-----------------------------------|
| C# (.NET)  | `sdk/csharp/`      | `HnswLite.Sdk`    | .NET 8 or .NET 10                 |
| Python     | `sdk/python/`      | `hnswlite-sdk`    | Python 3.9+, `requests >= 2.28`   |
| JavaScript | `sdk/js/`          | `hnswlite-sdk`    | Node 18+ (native `fetch`)         |

Each subdirectory contains:

- The SDK source.
- A `README.md` with install instructions and usage examples for every method.
- A test harness that takes a server base URL and API key and exercises the full
  API surface (create / read / enumerate / search / add / batch / remove / delete / ping).

## Running test harnesses

All harnesses assume an HnswLite server is running locally at `http://localhost:8080`
with `RequireAuthentication: true` and the API key from `hnswindex.json`.

### C#

```bash
cd sdk/csharp
dotnet run --project HnswLite.Sdk.Test -- http://localhost:8080 YOUR_API_KEY
```

### Python

```bash
cd sdk/python
pip install -e .
python tests/test_integration.py --base-url http://localhost:8080 --api-key YOUR_API_KEY
```

### JavaScript / TypeScript

```bash
cd sdk/js
npm install && npm run build
BASE_URL=http://localhost:8080 API_KEY=YOUR_API_KEY node dist/tests/integration.js
```

## API coverage matrix

### Filtering by labels and tags (v1.2+)

All three SDKs accept the same optional metadata filter triple — `labels`,
`tags`, and `caseInsensitive` (AND semantics across both filters) — on `search`
and `enumerateVectors`. The response carries a `FilteredCount` showing how many
records were dropped by the filter.

```csharp
// C#
var resp = await client.SearchAsync("demo", new SearchRequest {
    Vector = query,
    K = 10,
    Labels = new List<string> { "red", "small" },
    Tags   = new Dictionary<string, string> { { "env", "prod" } },
    CaseInsensitive = false
});
Console.WriteLine($"Matches={resp.Results.Count}, Filtered={resp.FilteredCount}");
```

```python
# Python
resp = client.search(
    "demo", query, k=10,
    labels=["red", "small"],
    tags={"env": "prod"},
    case_insensitive=False,
)
print(resp["Results"], resp["FilteredCount"])
```

```ts
// TypeScript
const resp = await client.search("demo", {
  vector: query, k: 10,
  labels: ["red", "small"],
  tags: { env: "prod" },
  caseInsensitive: false,
});
console.log(resp.results, resp.filteredCount);
```

All three SDKs expose the same method set:

| Endpoint                                    | C# method              | Python method          | JS/TS method           |
|---------------------------------------------|------------------------|------------------------|------------------------|
| `GET /`                                     | `PingAsync`            | `ping`                 | `ping`                 |
| `HEAD /`                                    | `HeadPingAsync`        | `head_ping`            | `headPing`             |
| `GET /v1.0/indexes`                         | `EnumerateIndexesAsync`| `enumerate_indexes`    | `enumerateIndexes`     |
| `POST /v1.0/indexes`                        | `CreateIndexAsync`     | `create_index`         | `createIndex`          |
| `GET /v1.0/indexes/{name}`                  | `GetIndexAsync`        | `get_index`            | `getIndex`             |
| `DELETE /v1.0/indexes/{name}`               | `DeleteIndexAsync`     | `delete_index`         | `deleteIndex`          |
| `POST /v1.0/indexes/{name}/search`          | `SearchAsync`          | `search`               | `search`               |
| `POST /v1.0/indexes/{name}/vectors`         | `AddVectorAsync`       | `add_vector`           | `addVector`            |
| `POST /v1.0/indexes/{name}/vectors/batch`   | `AddVectorsAsync`      | `add_vectors`          | `addVectors`           |
| `DELETE /v1.0/indexes/{name}/vectors/{guid}`| `RemoveVectorAsync`    | `remove_vector`        | `removeVector`         |
