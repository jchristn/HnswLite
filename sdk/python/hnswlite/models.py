"""
Data models for the HnswLite REST API.

These dataclasses document the shape of request/response objects.
The client methods return plain dicts (parsed JSON with PascalCase keys),
but these classes serve as a typed reference for consumers.
"""

from dataclasses import dataclass, field
from typing import List, Optional


@dataclass
class CreateIndexRequest:
    """Request body for POST /v1.0/indexes."""
    Name: str
    Dimension: int
    StorageType: str = "RAM"  # "RAM" or "SQLite"
    DistanceFunction: str = "Cosine"  # "Euclidean", "Cosine", or "DotProduct"
    M: int = 16
    MaxM: int = 32
    EfConstruction: int = 200


@dataclass
class IndexResponse:
    """Response from index endpoints."""
    GUID: str
    Name: str
    Dimension: int
    StorageType: str
    DistanceFunction: str
    M: int
    MaxM: int
    EfConstruction: int
    VectorCount: int
    CreatedUtc: str


@dataclass
class AddVectorRequest:
    """Request body for POST /v1.0/indexes/{name}/vectors."""
    Vector: List[float]
    GUID: Optional[str] = None


@dataclass
class AddVectorsRequest:
    """Request body for POST /v1.0/indexes/{name}/vectors/batch."""
    Vectors: List[AddVectorRequest] = field(default_factory=list)


@dataclass
class SearchRequest:
    """Request body for POST /v1.0/indexes/{name}/search."""
    Vector: List[float]
    K: int = 10
    Ef: Optional[int] = None


@dataclass
class VectorSearchResult:
    """A single search result."""
    GUID: str
    Vector: List[float]
    Distance: float


@dataclass
class SearchResponse:
    """Response from search endpoint."""
    Results: List[VectorSearchResult] = field(default_factory=list)
    SearchTimeMs: float = 0.0


@dataclass
class VectorEntry:
    """A single entry returned by vector enumeration or retrieval endpoints."""
    GUID: str
    Vector: Optional[List[float]] = None


@dataclass
class EnumerationResult:
    """Paginated list response wrapper."""
    Success: bool
    MaxResults: int
    Skip: int
    ContinuationToken: Optional[str]
    EndOfResults: bool
    TotalRecords: int
    RecordsRemaining: int
    TimestampUtc: str
    Objects: list = field(default_factory=list)
