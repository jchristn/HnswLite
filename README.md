<img src="https://github.com/jchristn/HnswIndex/blob/main/assets/logo.png" width="256" height="256">

# HnswIndex

A pure C# implementation of Hierarchical Navigable Small World (HNSW) graphs for approximate nearest neighbor search. This library provides a thread-safe, embeddable solution for vector similarity search in .NET applications.

> **Note**: This library is in its early stages of development. We welcome your patience, constructive feedback, and contributions! Please be kind and considerate when reporting issues or suggesting improvements. I am not an expert on this topic and relied heavily on available AI tools to build this library. Pull requests are greatly appreciated!

## Overview

HnswIndex implements the Hierarchical Navigable Small World algorithm, which provides fast approximate nearest neighbor search with excellent recall rates. The library is designed to be embeddable, extensible, and easy to use in any .NET application.

### Key Features

- **Pure C# implementation** - No native dependencies
- **Thread-safe** - Safe for concurrent operations
- **Async/await support** - Modern async APIs with cancellation tokens
- **Pluggable storage** - Extensible storage backend interface
- **Multiple distance metrics** - Euclidean, Cosine, and Dot Product
- **State persistence** - Export/import index state
- **Batch operations** - Efficient bulk insert and remove
- **Comprehensive validation** - Input validation and error handling

## New in v1.0.x

**Initial version** featuring:

- Core HNSW algorithm implementation
- In-memory storage backend
- Async APIs with cancellation support
- Three distance functions (Euclidean, Cosine, Dot Product)
- Batch add/remove operations
- State export/import functionality
- Thread-safety

For version history, see [CHANGELOG.md](CHANGELOG.md).

## Use Cases

HnswIndex is ideal for:

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
- `EfConstruction`: Size of the candidate list when building the index (default: 200). This controls how thoroughly the algorithm searches for connections when adding new vectors. Higher values create better quality indices but take longer to build.
- `Ef` (search parameter): Size of the candidate list during search (default: 50-200). This controls how many paths the algorithm explores when searching. Higher values find better results but take more time. Set this based on your speed/quality needs.

2. **For Large Datasets**:
- Consider implementing a custom disk-based storage backend
- Use batch operations for initial index building
- Monitor memory usage closely

3. **For High-Dimensional Data**:
- Consider dimensionality reduction before indexing
- Use cosine distance for normalized embeddings

### Limitations

- All vectors must fit in memory (for default RAM storage)
- No GPU acceleration
- No built-in filtering or metadata storage
- Approximate search only (may miss some nearest neighbors)

## Bugs, Feedback, or Enhancement Requests

We value your input! If you encounter any issues or have suggestions:

- **Bug Reports**: Please [file an issue](https://github.com/yourusername/hnsw-net/issues) with reproduction steps
- **Feature Requests**: Start a [discussion](https://github.com/yourusername/hnsw-net/discussions) or create an issue
- **Questions**: Use the discussions forum for general questions
- **Contributions**: Pull requests are welcome! Please read our contributing guidelines first

## Simple Example

```csharp
using Hnsw;

// Create an index for 128-dimensional vectors
var index = new HNSWIndex(dimension: 128);

// Configure parameters (optional)
index.M = 16;
index.EfConstruction = 200;
index.DistanceFunction = new CosineDistance();

// Add vectors to the index
var vectorId = Guid.NewGuid();
var vector = new List<float>(128); // Your 128-dimensional embedding
// ... populate vector with data ...

await index.AddAsync(vectorId, vector);

// Add multiple vectors
var batch = new List<(Guid id, List<float> vector)>();
for (int i = 0; i < 1000; i++)
{
    var id = Guid.NewGuid();
    var v = GenerateRandomVector(128); // Your vector generation logic
    batch.Add((id, v));
}
await index.AddBatchAsync(batch);

// Search for nearest neighbors
var queryVector = new List<float>(128); // Your query embedding
// ... populate query vector ...

var neighbors = await index.GetTopKAsync(queryVector, k: 10);

foreach (var result in neighbors)
{
    Console.WriteLine($"ID: {result.GUID}, Distance: {result.Distance:F4}");
}

// Save the index
var state = await index.ExportStateAsync();
// ... serialize state to disk ...

// Load the index
var newIndex = new HNSWIndex(dimension: 128);
await newIndex.ImportStateAsync(state);
```

### Custom Storage Example

```csharp
// Implement your own storage backend
public class RedisHNSWStorage : IHNSWStorage
{
    // Implementation details...
}

// Use custom storage
var storage = new RedisHNSWStorage(connectionString);
var index = new HNSWIndex(dimension: 128, storage: storage);
```

## License

This library is available under the MIT license.

## Acknowledgments

This implementation is based on the paper: [Efficient and robust approximate nearest neighbor search using Hierarchical Navigable Small World graphs](https://arxiv.org/abs/1603.09320) by Yu. A. Malkov and D. A. Yashunin.
