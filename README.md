<img src="https://github.com/jchristn/HnswLite/blob/main/assets/logo.png" width="256" height="256">

# HnswLite

A pure C# implementation of Hierarchical Navigable Small World (HNSW) graphs for approximate nearest neighbor search. This library provides a thread-safe, embeddable solution for vector similarity search in .NET applications.

> **Note**: This library is in its early stages of development. We welcome your patience, constructive feedback, and contributions! Please be kind and considerate when reporting issues or suggesting improvements. I am not an expert on this topic and relied heavily on available AI tools to build this library. Pull requests are greatly appreciated!

[![NuGet Version](https://img.shields.io/nuget/v/HnswLite.svg?style=flat)](https://www.nuget.org/packages/HnswLite/) [![NuGet](https://img.shields.io/nuget/dt/HnswLite.svg)](https://www.nuget.org/packages/HnswLite) 

## Overview

HnswLite implements the Hierarchical Navigable Small World algorithm, which provides fast approximate nearest neighbor search with excellent recall rates. The library is designed to be embeddable, extensible, and easy to use in any .NET application.

### Key Features

- **Pure C# implementation** - No native dependencies
- **Thread-safe** - Safe for concurrent operations
- **Async/await support** - Modern async APIs with cancellation tokens
- **Pluggable storage** - Extensible storage backend interface
- **Multiple distance metrics** - Euclidean, Cosine, and Dot Product
- **Flexible deployment** - Support for RAM or Sqlite, or build-your-own backend
- **Batch operations** - Efficient bulk insert and remove
- **Comprehensive validation** - Input validation and error handling

## New in v1.0.x

- SQLite backend with optimized binary serialization (4x faster than JSON)
- Deferred flush for batch operations (100x improvement for large insertions)
- SearchContext caching reduces database queries by 90%+
- WAL mode and optimized PRAGMA settings for SQLite
- Standalone REST server (`HnswIndex.Server`) with Docker image and Postman collection
- Core HNSW algorithm implementation
- In-memory storage backend
- SQLite storage backend  
- Async APIs with cancellation support
- Three distance functions (Euclidean, Cosine, Dot Product)
- Batch add/remove operations
- State export/import functionality
- Thread-safety

For version history, see [CHANGELOG.md](CHANGELOG.md).

## Use Cases

HnswLite is ideal for:

- **Semantic Search** - Find similar documents, sentences, or paragraphs based on embeddings
- **Recommendation Systems** - Discover similar items, users, or content
- **Image Similarity** - Search for visually similar images using feature vectors
- **Anomaly Detection** - Identify outliers by finding distant neighbors
- **Clustering** - Group similar items together based on vector proximity
- **RAG Applications** - Retrieval-Augmented Generation for LLM applications
- **Duplicate Detection** - Find near-duplicate content in large datasets

## Performance and Scalability Recommendations

### Recommended Limits

- **Vector dimensions**: 50-1000 (optimal: 128-768)
- **Dataset size**: Up to 1-10M vectors (depending on dimensions and available RAM)
- **Memory usage**: Approximately `(vector_count * dimension * 4 bytes) + (vector_count * M * 32 bytes)`

> Note: the above are *estimations*.  This library has not been tested (yet) at any level of scale.

### Performance Characteristics

- **Index build time**: O(N log N) expected
- **Search time**: O(log N) expected
- **Memory complexity**: O(N * M) where M is the connectivity parameter

### Optimization Tips

1. **Parameter Tuning**:
- `M`: Number of connections per vector (default: 16). Think of this as how many "friends" each vector has in the network. More connections mean better search quality but use more memory. For most cases, 16-32 works well.
- `EfConstruction`: Size of the candidate list when building the index (default: 200). This controls how thoroughly the algorithm searches for connections when adding new vectors. Higher values create better quality indices but take longer to build. For faster batch insertion, consider reducing to 50-100.
- `Ef` (search parameter): Size of the candidate list during search (default: 50-200). This controls how many paths the algorithm explores when searching. Higher values find better results but take more time. Set this based on your speed/quality needs.
- `Seed`: Set a consistent seed value for reproducible index builds (useful for testing).

2. **For Large Datasets**:
- Use SQLite backend for persistence and lower memory usage
- Always use `AddNodesAsync` for batch operations instead of individual `AddAsync` calls
- SQLite backend automatically handles flushing - no manual flush needed
- Consider reducing `EfConstruction` for faster insertion (trade-off with search quality)

3. **For High-Dimensional Data**:
- Keep dimensions under 384 for optimal performance
- Consider dimensionality reduction before indexing (PCA, UMAP, etc.)
- Use cosine distance for normalized embeddings (common with text embeddings)
- Monitor memory usage: ~4 bytes per dimension per vector plus graph overhead

## Bugs, Feedback, or Enhancement Requests

We value your input! If you encounter any issues or have suggestions:

- **Bug Reports**: Please [file an issue](https://github.com/jchristn/HnswLite/issues) with reproduction steps
- **Feature Requests**: Start a [discussion](https://github.com/jchristn/HnswLite/discussions) or create an issue
- **Questions**: Use the discussions forum for general questions
- **Contributions**: Pull requests are welcome! Please read our contributing guidelines first

## Simple Example

```csharp
using Hnsw;
using Hnsw.RamStorage;
using Hnsw.SqliteStorage;

// Create an index for 128-dimensional vectors in RAM
HnswIndex index = new HnswIndex(128, new RamHnswStorage(), new RamHnswLayerStorage());

// Or using SQLite (with proper disposal)
using SqliteHnswStorage sqliteStorage = new SqliteHnswStorage("my-index.db");
using SqliteHnswLayerStorage sqliteLayerStorage = new SqliteHnswLayerStorage(sqliteStorage.Connection);
HnswIndex sqliteIndex = new HnswIndex(128, sqliteStorage, sqliteLayerStorage);

// Configure parameters (optional)
index.M = 16;
index.EfConstruction = 200;
index.DistanceFunction = new CosineDistance();

// Add vectors to the index
Guid vectorId = Guid.NewGuid();
List<float> vector = new List<float>(128); // Your 128-dimensional embedding
// ... populate vector with data ...

await index.AddAsync(vectorId, vector);

// Add multiple vectors
Dictionary<Guid, List<float>> batch = new Dictionary<Guid, List<float>>();
for (int i = 0; i < 1000; i++)
{
    Guid id = Guid.NewGuid();
    List<float> v = GenerateRandomVector(128); // Your vector generation logic
    batch[id] = v;
}
await index.AddNodesAsync(batch);

// Search for nearest neighbors
List<float> queryVector = new List<float>(128); // Your query embedding
// ... populate query vector ...

List<SearchResult> neighbors = await index.GetTopKAsync(queryVector, k: 10);

foreach (SearchResult result in neighbors)
{
    Console.WriteLine($"ID: {result.GUID}, Distance: {result.Distance:F4}");
}

// Save the index
HnswState state = await index.ExportStateAsync();
// ... serialize state to disk ...

// Load the index
HnswIndex newIndex = new HnswIndex(128, new RamHnswStorage(), new RamHnswLayerStorage());
await newIndex.ImportStateAsync(state);
```

### Best Practices

1. **Resource Management**:
   - Always use `using` statements with SQLite storage to ensure proper cleanup
   - The SQLite backend automatically flushes pending changes on disposal
   - No manual flush is needed - the library handles this internally

2. **Batch Operations**:
   ```csharp
   // GOOD: Use batch operations for multiple vectors
   Dictionary<Guid, List<float>> batch = new Dictionary<Guid, List<float>>();
   // ... populate batch ...
   await index.AddNodesAsync(batch);
   
   // AVOID: Individual adds in a loop
   foreach (Item item in items)
   {
       await index.AddAsync(item.Id, item.Vector); // Slower
   }
   ```

3. **Search Performance**:
   ```csharp
   // Adjust ef parameter based on your needs
   List<SearchResult> quickResults = await index.GetTopKAsync(query, k: 10, ef: 50);  // Faster, lower quality
   List<SearchResult> bestResults = await index.GetTopKAsync(query, k: 10, ef: 400);  // Slower, higher quality
   ```

### Custom Storage Example

Refer to `Hnsw.RamStorage` and `Hnsw.SqliteStorage` for actual implementations. To implement your own backend, you need to implement:

- `IHnswLayerStorage` - Manages layer assignments for nodes
- `IHnswNode` - Represents a single node with its vector and neighbors
- `IHnswStorage` - Handles node persistence and retrieval

## Running in Docker

Refer to the `src/Docker` directory for assets related to running in Docker.  The Docker image can be found on [Docker Hub](https://hub.docker.com/repository/docker/jchristn/hnswindex-server/general) and a Postman collection is contained within this repository's root directory.

## License

This library is available under the MIT license.

## Acknowledgments

This implementation is based on the paper: [Efficient and robust approximate nearest neighbor search using Hierarchical Navigable Small World graphs](https://arxiv.org/abs/1603.09320) by Yu. A. Malkov and D. A. Yashunin.
