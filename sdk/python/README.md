# HnswLite Python SDK

Python client for the [HnswLite](https://github.com/jchristn/HnswLite) REST API.

## Installation

```bash
pip install hnswlite-sdk
```

Or install from source:

```bash
cd sdk/python
pip install .
```

## Quick Start

```python
from hnswlite import HnswLiteClient

client = HnswLiteClient(
    base_url="http://localhost:8321",
    api_key="your-api-key",
)
```

## API Reference

### Health Checks

```python
# GET / (unauthenticated)
client.ping()       # returns True

# HEAD / (unauthenticated)
client.head_ping()  # returns True
```

### Index Management

```python
# Create an index
index = client.create_index(
    name="my-index",
    dimension=128,
    storage_type="RAM",           # "RAM" or "SQLite"
    distance_function="Cosine",   # "Cosine", "Euclidean", or "DotProduct"
    m=16,
    max_m=32,
    ef_construction=200,
)
print(index["GUID"], index["Name"])

# Get an index
index = client.get_index("my-index")
print(index["VectorCount"])

# List indexes with optional filters
result = client.enumerate_indexes(
    max_results=10,
    skip=0,
    ordering="CreatedDescending",
    prefix="my-",
    suffix="-index",
    created_after_utc="2025-01-01T00:00:00Z",
    created_before_utc="2026-12-31T23:59:59Z",
)
for obj in result["Objects"]:
    print(obj["Name"])

# Delete an index
client.delete_index("my-index")  # returns None
```

### Vector Operations

```python
# Add a single vector (GUID auto-generated)
vec = client.add_vector("my-index", [0.1, 0.2, 0.3, 0.4])
print(vec["GUID"])

# Add a single vector with an explicit GUID
vec = client.add_vector("my-index", [0.5, 0.6, 0.7, 0.8], guid="my-guid-1")

# Add vectors in batch
batch = [
    {"Vector": [0.1, 0.2, 0.3, 0.4]},
    {"Vector": [0.5, 0.6, 0.7, 0.8], "GUID": "my-guid-2"},
]
resp = client.add_vectors("my-index", batch)
for v in resp["Vectors"]:
    print(v["GUID"])

# Remove a vector
client.remove_vector("my-index", "my-guid-1")  # returns None

# Enumerate vectors in an index (paginated; GUIDs only by default)
result = client.enumerate_vectors(
    "my-index",
    max_results=100,
    skip=0,
    continuation_token=None,
    ordering="CreatedDescending",
    prefix=None,
    suffix=None,
    created_after_utc="2025-01-01T00:00:00Z",
    created_before_utc="2026-12-31T23:59:59Z",
    include_vectors=False,
)
for obj in result["Objects"]:
    print(obj["GUID"])

# Enumerate with the Vector values populated
result = client.enumerate_vectors("my-index", max_results=10, include_vectors=True)
for obj in result["Objects"]:
    print(obj["GUID"], obj["Vector"])

# Retrieve a single vector (Vector values always included)
vec = client.get_vector("my-index", "my-guid-2")
print(vec["GUID"], vec["Vector"])
```

### Search

```python
result = client.search("my-index", vector=[0.1, 0.2, 0.3, 0.4], k=5, ef=100)
print(f"Search took {result['SearchTimeMs']} ms")
for r in result["Results"]:
    print(r["GUID"], r["Distance"])
```

### Error Handling

```python
from hnswlite import HnswLiteApiError

try:
    client.get_index("nonexistent")
except HnswLiteApiError as e:
    print(e.status_code)  # 404
    print(e.error)
    print(e.message)
```

## Running Integration Tests

```bash
cd sdk/python
python tests/test_integration.py --base-url http://localhost:8321 --api-key your-key
```

## License

MIT
