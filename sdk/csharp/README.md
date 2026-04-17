# HnswLite C# SDK

C# SDK for the HnswLite REST API. Provides full async coverage of every endpoint for index management, vector operations, and K-nearest-neighbour search.

## Installation

```bash
dotnet add package HnswLite.Sdk
```

Or via the NuGet Package Manager:

```
Install-Package HnswLite.Sdk
```

## Quick Start

```csharp
using HnswLite.Sdk;
using HnswLite.Sdk.Models;

using HnswLiteClient client = new HnswLiteClient(
    baseUrl: "http://localhost:8080",
    apiKey: "your-api-key"
);
```

## Usage Examples

### Health Check (Ping)

```csharp
// GET / — unauthenticated health ping
bool isAlive = await client.PingAsync();
Console.WriteLine("Server alive: " + isAlive);

// HEAD / — unauthenticated head ping
bool headOk = await client.HeadPingAsync();
```

### Create an Index

```csharp
IndexResponse index = await client.CreateIndexAsync(new CreateIndexRequest
{
    Name = "my-index",
    Dimension = 384,
    StorageType = "RAM",           // "RAM" or "SQLite"
    DistanceFunction = "Cosine",   // "Euclidean", "Cosine", or "DotProduct"
    M = 16,
    MaxM = 32,
    EfConstruction = 200
});

Console.WriteLine("Created index: " + index.Name + " (GUID: " + index.GUID + ")");
```

### Get an Index

```csharp
IndexResponse index = await client.GetIndexAsync("my-index");
Console.WriteLine("Vectors: " + index.VectorCount);
```

### Enumerate Indexes

```csharp
EnumerationResult<IndexResponse> page = await client.EnumerateIndexesAsync(new EnumerationQuery
{
    MaxResults = 25,
    Skip = 0,
    Ordering = EnumerationOrderEnum.NameAscending,
    Prefix = "prod-"
});

foreach (IndexResponse idx in page.Objects)
{
    Console.WriteLine(idx.Name + " (" + idx.VectorCount + " vectors)");
}

Console.WriteLine("End of results: " + page.EndOfResults);
Console.WriteLine("Total records: " + page.TotalRecords);
```

### Add a Single Vector

```csharp
AddVectorRequest echo = await client.AddVectorAsync("my-index", new AddVectorRequest
{
    Vector = new List<float> { 0.1f, 0.2f, 0.3f /* ... */ }
});

Console.WriteLine("Added vector GUID: " + echo.GUID);
```

### Add a Batch of Vectors

```csharp
AddVectorsRequest batchEcho = await client.AddVectorsAsync("my-index", new AddVectorsRequest
{
    Vectors = new List<AddVectorRequest>
    {
        new AddVectorRequest { Vector = new List<float> { 0.1f, 0.2f, 0.3f } },
        new AddVectorRequest { Vector = new List<float> { 0.4f, 0.5f, 0.6f } },
        new AddVectorRequest { Vector = new List<float> { 0.7f, 0.8f, 0.9f } }
    }
});

Console.WriteLine("Added " + batchEcho.Vectors.Count + " vectors");
```

### Search (K-Nearest Neighbours)

```csharp
SearchResponse result = await client.SearchAsync("my-index", new SearchRequest
{
    Vector = new List<float> { 0.1f, 0.2f, 0.3f /* ... */ },
    K = 10,
    Ef = 200  // optional; null uses server default
});

Console.WriteLine("Search took " + result.SearchTimeMs + " ms");

foreach (VectorSearchResult r in result.Results)
{
    Console.WriteLine("  GUID: " + r.GUID + " Distance: " + r.Distance);
}
```

### Filter by Labels and Tags (v1.2+)

`SearchRequest` and `EnumerationQuery` both accept optional `Labels`, `Tags`,
and `CaseInsensitive` fields. Filters use **AND** semantics across both —
a record is kept only when every supplied label is present AND every
supplied tag key/value matches. The response exposes a `FilteredCount`
reporting how many candidates were dropped.

```csharp
SearchResponse result = await client.SearchAsync("my-index", new SearchRequest
{
    Vector = new List<float> { 0.1f, 0.2f, 0.3f, 0.4f },
    K = 10,
    Labels = new List<string> { "red", "small" },
    Tags = new Dictionary<string, string> { { "env", "prod" } },
    CaseInsensitive = false,
});

Console.WriteLine($"Matches: {result.Results.Count}, filtered out: {result.FilteredCount}");
```

### Enumerate Vectors

```csharp
// GUIDs only (includeVectors defaults to false)
EnumerationResult<VectorEntryResponse> page = await client.EnumerateVectorsAsync(
    "my-index",
    new EnumerationQuery { MaxResults = 50 });

Console.WriteLine("Total vectors: " + page.TotalRecords);
foreach (VectorEntryResponse v in page.Objects)
{
    Console.WriteLine("  GUID: " + v.GUID);
}

// Include the full vector values
EnumerationResult<VectorEntryResponse> detailed = await client.EnumerateVectorsAsync(
    "my-index",
    new EnumerationQuery { MaxResults = 10 },
    includeVectors: true);

foreach (VectorEntryResponse v in detailed.Objects)
{
    Console.WriteLine("  GUID: " + v.GUID + " Dim: " + v.Vector.Count);
}
```

### Get a Single Vector

```csharp
Guid vectorId = Guid.Parse("...");
VectorEntryResponse entry = await client.GetVectorAsync("my-index", vectorId);

Console.WriteLine("GUID: " + entry.GUID);
Console.WriteLine("Vector: [" + string.Join(", ", entry.Vector) + "]");
```

### Remove a Vector

```csharp
Guid vectorId = Guid.Parse("...");
await client.RemoveVectorAsync("my-index", vectorId);
```

### Delete an Index

```csharp
await client.DeleteIndexAsync("my-index");
```

## Error Handling

All non-2xx responses throw `HnswLiteApiException`:

```csharp
try
{
    IndexResponse index = await client.GetIndexAsync("nonexistent");
}
catch (HnswLiteApiException ex)
{
    Console.WriteLine("Status: " + (int)ex.StatusCode);  // e.g. 404
    Console.WriteLine("Error:  " + ex.Error);             // e.g. "IndexNotFound"
    Console.WriteLine("Detail: " + ex.ApiMessage);        // Human-readable message
}
```

## Cancellation

Every async method accepts an optional `CancellationToken`:

```csharp
using CancellationTokenSource cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
IndexResponse index = await client.GetIndexAsync("my-index", cts.Token);
```

## Custom API Key Header

The API key header name is configurable (defaults to `x-api-key`):

```csharp
using HnswLiteClient client = new HnswLiteClient(
    baseUrl: "http://localhost:8080",
    apiKey: "your-api-key",
    apiKeyHeader: "Authorization"
);
```

## Running the Test Harness

```bash
cd sdk/csharp/HnswLite.Sdk.Test
dotnet run -- http://localhost:8080 your-api-key
```

The test harness exercises every SDK method and prints pass/fail for each. Exit code 0 means all tests passed.

## Target Frameworks

- .NET 8.0
- .NET 10.0
