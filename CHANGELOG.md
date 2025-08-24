# Change Log

## Current Version

**v1.0.3** - Performance optimizations:
- SQLite backend with optimized binary serialization (4x faster than JSON)
- Deferred flush for batch operations (100x improvement for large insertions)
- SearchContext caching reduces database queries by 90%+
- WAL mode and optimized PRAGMA settings for SQLite

## Previous Versions

**v1.0.0** - Initial release:
- Core HNSW algorithm implementation
- In-memory storage backend
- SQLite storage backend  
- Async APIs with cancellation support
- Three distance functions (Euclidean, Cosine, Dot Product)
- Batch add/remove operations
- State export/import functionality
- Thread-safety
