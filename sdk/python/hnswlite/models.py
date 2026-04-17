"""
Data models for the HnswLite REST API.

These dataclasses document the shape of request/response objects.
The client methods return plain dicts (parsed JSON with PascalCase keys),
but these classes serve as a typed reference for consumers.
"""

from dataclasses import dataclass, field
from typing import Any, Dict, List, Optional


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
    Name: Optional[str] = None
    Labels: Optional[List[str]] = None
    Tags: Optional[Dict[str, Any]] = None


@dataclass
class AddVectorsRequest:
    """Request body for POST /v1.0/indexes/{name}/vectors/batch."""
    Vectors: List[AddVectorRequest] = field(default_factory=list)


@dataclass
class SearchRequest:
    """Request body for POST /v1.0/indexes/{name}/search.

    When ``Labels`` or ``Tags`` are supplied the server performs the HNSW top-K
    search first and then drops any result whose vector metadata does not
    satisfy the filter (AND semantics across both fields). The response may
    therefore contain fewer than ``K`` results; see ``SearchResponse.FilteredCount``
    for the number of candidates dropped.

    Comparisons are case-sensitive unless ``CaseInsensitive`` is true.
    """
    Vector: List[float]
    K: int = 10
    Ef: Optional[int] = None
    Labels: Optional[List[str]] = None
    Tags: Optional[Dict[str, str]] = None
    CaseInsensitive: bool = False


@dataclass
class EnumerationQuery:
    """Query parameters for paginated enumeration endpoints."""
    MaxResults: Optional[int] = None
    Skip: Optional[int] = None
    ContinuationToken: Optional[str] = None
    Ordering: Optional[str] = None
    Prefix: Optional[str] = None
    Suffix: Optional[str] = None
    CreatedAfterUtc: Optional[str] = None
    CreatedBeforeUtc: Optional[str] = None
    Labels: Optional[List[str]] = None
    Tags: Optional[Dict[str, str]] = None
    CaseInsensitive: bool = False


@dataclass
class VectorSearchResult:
    """A single search result."""
    GUID: str
    Vector: List[float]
    Distance: float
    Name: Optional[str] = None
    Labels: Optional[List[str]] = None
    Tags: Optional[Dict[str, Any]] = None


@dataclass
class SearchResponse:
    """Response from search endpoint."""
    Results: List[VectorSearchResult] = field(default_factory=list)
    SearchTimeMs: float = 0.0
    FilteredCount: int = 0


@dataclass
class VectorEntry:
    """A single entry returned by vector enumeration or retrieval endpoints."""
    GUID: str
    Vector: Optional[List[float]] = None
    Name: Optional[str] = None
    Labels: Optional[List[str]] = None
    Tags: Optional[Dict[str, Any]] = None


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
    FilteredCount: int = 0
