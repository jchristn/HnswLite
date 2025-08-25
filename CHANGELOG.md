# Change Log

## Current Version

v1.0.x
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
- Initial release

## Previous Versions

Notes from previous versions will be pasted here.
