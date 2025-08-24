# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Project Overview

HnswLite is a pure C# implementation of Hierarchical Navigable Small World (HNSW) graphs for approximate nearest neighbor search. This is a .NET library designed to be thread-safe, embeddable, and extensible with pluggable storage backends.

## Build and Development Commands

```bash
# Build the entire solution
dotnet build

# Build specific projects
dotnet build HnswIndex/HnswIndex.csproj
dotnet build HnswIndex.RamStorage/HnswIndex.RamStorage.csproj 
dotnet build HnswIndex.SqliteStorage/HnswIndex.SqliteStorage.csproj

# Run tests
dotnet run --project Test.Ram
dotnet run --project Test.Sqlite

# Create NuGet packages (automatically created during build)
dotnet pack

# Clean build artifacts
dotnet clean
```

## Core Architecture

### Three-Layer Storage Architecture

The system uses a pluggable storage architecture with three main interfaces:

1. **`IHnswStorage`** - Manages vector nodes and their data
2. **`IHnswLayerStorage`** - Manages layer assignments for nodes  
3. **`IHnswNode`** - Represents individual nodes with neighbor connections

### Key Components

- **`HnswIndex`** (`HnswIndex/HsnwIndex.cs`) - Main entry point, orchestrates all operations
- **`SearchContext`** (`HnswIndex/SearchContext.cs`) - Caching layer that dramatically improves performance by reducing database queries
- **Distance Functions** - Euclidean, Cosine, and Dot Product implementations
- **Storage Backends** - RAM and SQLite implementations provided

### Performance Architecture

The library implements significant performance optimizations detailed in `PERFORMANCE_IMPROVEMENTS.md`:

- **Binary serialization** instead of JSON for 4x performance improvement
- **SearchContext caching** reduces database queries by 90%+
- **Batch operations** for bulk insertions and queries
- **Optimized SQLite settings** with WAL mode and binary storage

### Thread Safety

- Uses `SemaphoreSlim _IndexLock` for index-level operations
- Individual nodes have `ReaderWriterLockSlim` for neighbor operations
- All public APIs are thread-safe

## Project Structure

```
HnswIndex/           - Core algorithm and interfaces
├── HnswIndex.cs     - Main index implementation
├── SearchContext.cs - Performance-critical caching layer
├── I*.cs           - Storage and node interfaces
└── *Distance.cs    - Distance function implementations

HnswIndex.RamStorage/   - In-memory storage implementation
├── RamHnsw*.cs        - RAM-based storage classes

HnswIndex.SqliteStorage/ - SQLite storage implementation  
├── SqliteHnsw*.cs      - SQLite-based storage with binary optimization

Test.Ram/    - Test suite for RAM storage
Test.Sqlite/ - Test suite for SQLite storage (includes performance tests)
```

## Coding Standards

This codebase follows strict coding standards to maximize consistency and maintainability. ALL code files must conform to these rules:

### File Structure and Organization
- **Namespace declaration at top** with using statements INSIDE the namespace block
- **Using statement order**: Microsoft/system libraries first (alphabetical), then other usings (alphabetical)
- **Regions required** (in order): `Public-Members`, `Private-Members`, `Constructors-and-Factories`, `Public-Methods`, `Private-Methods`
- **Extra line breaks** before/after region statements (except when adjacent to braces)
- **One class per file** - no nested classes or multiple classes in single file

### Naming and Declarations
- **No `var` declarations** - Always use explicit types
- **Private members** use `_PascalCase` naming (e.g., `_FooBar` not `_fooBar`)
- **Public members** with explicit getters/setters using backing variables when validation needed
- **No tuples** - Create proper classes instead (tuples absolutely forbidden unless absolutely necessary)

### Documentation and Comments
- **All public members, constructors, and public methods** must have XML documentation
- **No documentation** on private members or private methods
- **Document exceptions** using `/// <exception>` tags
- **Document nullability, thread safety, defaults, minimums, maximums** in XML comments

### Async and Threading
- **Every async method** must accept `CancellationToken` (unless class has token member)
- **Use `ConfigureAwait(false)`** where appropriate
- **Check `CancellationToken.ThrowIfCancellationRequested()`** at appropriate places
- **Thread safety**: Use `ReaderWriterLockSlim` over `lock` for read-heavy scenarios
- **Document thread safety guarantees** in XML comments

### Error Handling and Validation
- **Input validation** with guard clauses at method start
- **Use `ArgumentNullException.ThrowIfNull()`** for .NET 6+ null checks
- **Specific exception types** rather than generic `Exception`
- **Meaningful error messages** with context
- **Exception filters** when appropriate: `catch (SqlException ex) when (ex.Number == 2601)`

### Resource Management
- **Nullable reference types** enabled (`<Nullable>enable</Nullable>` in project files)
- **Implement IDisposable/IAsyncDisposable** for unmanaged resources
- **Use `using` statements** for IDisposable objects
- **Follow full Dispose pattern** with `protected virtual void Dispose(bool disposing)`

### Best Practices
- **Configurable values** as public members with private backing fields (avoid hardcoded constants)
- **LINQ over manual loops** when readable
- **Use `.Any()` instead of `.Count() > 0`** for existence checks
- **Use `.FirstOrDefault()` with null checks** rather than `.First()`
- **Consider `.ToList()`** to avoid multiple enumeration issues
- **Proactively eliminate null reference possibilities**
- **IEnumerable methods** should have async variants with CancellationToken

### Code Quality
- **Compile without errors or warnings**
- **Never assume opaque class members exist** - ask for implementation details
- **Respect manual SQL strings** - assume there's a good reason
- **Validate README accuracy** when it exists

## Key Performance Considerations

- **SearchContext Usage**: Always use `SearchContext` for operations that access multiple nodes (see `AddNodesAsync` in HnswIndex)
- **Batch Operations**: Use `AddNodesAsync`/`RemoveNodesAsync` instead of individual operations for bulk data
- **Binary Storage**: SQLite backend uses binary serialization for optimal performance
- **Parameter Tuning**: 
  - `M` (connections per layer): 16-32 typical
  - `EfConstruction` (build quality): 200 default, higher = better quality but slower build
  - `Ef` (search quality): Set at search time based on quality/speed tradeoff

## Storage Backend Implementation

To create a custom storage backend, implement:
- `IHnswStorage` - Vector storage and retrieval
- `IHnswLayerStorage` - Layer assignment management  
- `IHnswNode` - Node with neighbor connections

See `HnswIndex.RamStorage` and `HnswIndex.SqliteStorage` as reference implementations.

## Test Architecture

- `Test.Ram/Program.cs` - Comprehensive test suite for RAM storage
- `Test.Sqlite/Program.cs` - Performance comparison tests between SQLite and RAM
- Both test suites validate identical behavior between storage backends
- Performance tests include progress reporting for long-running operations (2000+ vectors)

## ACID Compliance

SQLite storage maintains full ACID compliance:
- `PRAGMA synchronous=FULL` for complete durability
- WAL mode for crash recovery
- Immediate commits for critical operations
- No write buffering that could cause data loss