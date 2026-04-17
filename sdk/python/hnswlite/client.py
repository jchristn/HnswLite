"""Synchronous Python client for the HnswLite REST API."""

from typing import Any, Dict, List, Optional
from urllib.parse import quote

import requests

from .exceptions import HnswLiteApiError


class HnswLiteClient:
    """Synchronous client for the HnswLite REST API.

    Args:
        base_url: Base URL of the HnswLite server (e.g. ``http://localhost:8321``).
        api_key: API key sent via the authentication header.
        api_key_header: Name of the header used for authentication.
            Defaults to ``x-api-key``.
    """

    def __init__(
        self,
        base_url: str,
        api_key: str,
        api_key_header: str = "x-api-key",
    ):
        self.base_url = base_url.rstrip("/")
        self.api_key = api_key
        self.api_key_header = api_key_header
        self._session = requests.Session()
        self._session.headers.update({
            self.api_key_header: self.api_key,
            "Content-Type": "application/json",
            "Accept": "application/json",
        })

    # ------------------------------------------------------------------
    # Internal helpers
    # ------------------------------------------------------------------

    def _url(self, path: str) -> str:
        return f"{self.base_url}{path}"

    @staticmethod
    def _raise_for_status(resp: requests.Response) -> None:
        if resp.status_code >= 200 and resp.status_code < 300:
            return
        error = ""
        message = ""
        try:
            body = resp.json()
            error = str(body.get("Error", body.get("error", "")))
            message = str(body.get("Message", body.get("message", "")))
        except Exception:
            message = resp.text or resp.reason or ""
        raise HnswLiteApiError(
            status_code=resp.status_code,
            error=error or str(resp.status_code),
            message=message,
        )

    # ------------------------------------------------------------------
    # Health / ping
    # ------------------------------------------------------------------

    def ping(self) -> bool:
        """``GET /`` -- unauthenticated health ping. Returns ``True`` on 200."""
        resp = requests.get(self._url("/"))
        self._raise_for_status(resp)
        return True

    def head_ping(self) -> bool:
        """``HEAD /`` -- unauthenticated head ping. Returns ``True`` on 200."""
        resp = requests.head(self._url("/"))
        self._raise_for_status(resp)
        return True

    # ------------------------------------------------------------------
    # Indexes
    # ------------------------------------------------------------------

    def enumerate_indexes(
        self,
        *,
        max_results: Optional[int] = None,
        skip: Optional[int] = None,
        ordering: Optional[str] = None,
        prefix: Optional[str] = None,
        suffix: Optional[str] = None,
        created_after_utc: Optional[str] = None,
        created_before_utc: Optional[str] = None,
    ) -> Dict[str, Any]:
        """``GET /v1.0/indexes`` -- list indexes with optional filters.

        Returns the parsed ``EnumerationResult<IndexResponse>`` dict.
        """
        params: Dict[str, Any] = {}
        if max_results is not None:
            params["maxResults"] = max_results
        if skip is not None:
            params["skip"] = skip
        if ordering is not None:
            params["ordering"] = ordering
        if prefix is not None:
            params["prefix"] = prefix
        if suffix is not None:
            params["suffix"] = suffix
        if created_after_utc is not None:
            params["createdAfterUtc"] = created_after_utc
        if created_before_utc is not None:
            params["createdBeforeUtc"] = created_before_utc

        resp = self._session.get(self._url("/v1.0/indexes"), params=params)
        self._raise_for_status(resp)
        return resp.json()

    def create_index(
        self,
        name: str,
        dimension: int,
        storage_type: str = "RAM",
        distance_function: str = "Cosine",
        m: int = 16,
        max_m: int = 32,
        ef_construction: int = 200,
    ) -> Dict[str, Any]:
        """``POST /v1.0/indexes`` -- create a new index.

        Returns the created ``IndexResponse`` dict.
        """
        body = {
            "Name": name,
            "Dimension": dimension,
            "StorageType": storage_type,
            "DistanceFunction": distance_function,
            "M": m,
            "MaxM": max_m,
            "EfConstruction": ef_construction,
        }
        resp = self._session.post(self._url("/v1.0/indexes"), json=body)
        self._raise_for_status(resp)
        return resp.json()

    def get_index(self, name: str) -> Dict[str, Any]:
        """``GET /v1.0/indexes/{name}`` -- retrieve index metadata."""
        resp = self._session.get(self._url(f"/v1.0/indexes/{name}"))
        self._raise_for_status(resp)
        return resp.json()

    def delete_index(self, name: str) -> None:
        """``DELETE /v1.0/indexes/{name}`` -- delete an index."""
        resp = self._session.delete(self._url(f"/v1.0/indexes/{name}"))
        self._raise_for_status(resp)
        return None

    # ------------------------------------------------------------------
    # Vectors
    # ------------------------------------------------------------------

    def add_vector(
        self,
        name: str,
        vector: List[float],
        guid: Optional[str] = None,
        vector_name: Optional[str] = None,
        labels: Optional[List[str]] = None,
        tags: Optional[Dict[str, Any]] = None,
    ) -> Dict[str, Any]:
        """``POST /v1.0/indexes/{name}/vectors`` -- add a single vector.

        ``vector_name``, ``labels``, and ``tags`` are optional metadata that
        can later be used to filter search and enumeration results. ``tags``
        values may be any JSON-serialisable type; server-side filtering
        compares them via their stringified form.

        Returns the echoed ``AddVectorRequest`` dict.
        """
        body: Dict[str, Any] = {"Vector": vector}
        if guid is not None:
            body["GUID"] = guid
        if vector_name is not None:
            body["Name"] = vector_name
        if labels is not None:
            body["Labels"] = labels
        if tags is not None:
            body["Tags"] = tags
        resp = self._session.post(
            self._url(f"/v1.0/indexes/{name}/vectors"), json=body,
        )
        self._raise_for_status(resp)
        return resp.json()

    def add_vectors(
        self,
        name: str,
        vectors: List[Dict[str, Any]],
    ) -> Dict[str, Any]:
        """``POST /v1.0/indexes/{name}/vectors/batch`` -- add vectors in batch.

        ``vectors`` is a list of dicts, each with a ``Vector`` key (list of
        float) and an optional ``GUID`` key (str).

        Returns the echoed ``AddVectorsRequest`` dict.
        """
        body = {"Vectors": vectors}
        resp = self._session.post(
            self._url(f"/v1.0/indexes/{name}/vectors/batch"), json=body,
        )
        self._raise_for_status(resp)
        return resp.json()

    def remove_vector(self, name: str, guid: str) -> None:
        """``DELETE /v1.0/indexes/{name}/vectors/{guid}`` -- remove a vector."""
        resp = self._session.delete(
            self._url(f"/v1.0/indexes/{name}/vectors/{guid}"),
        )
        self._raise_for_status(resp)
        return None

    def enumerate_vectors(
        self,
        index_name: str,
        *,
        max_results: Optional[int] = None,
        skip: Optional[int] = None,
        continuation_token: Optional[str] = None,
        ordering: Optional[str] = None,
        prefix: Optional[str] = None,
        suffix: Optional[str] = None,
        created_after_utc: Optional[str] = None,
        created_before_utc: Optional[str] = None,
        labels: Optional[List[str]] = None,
        tags: Optional[Dict[str, str]] = None,
        case_insensitive: bool = False,
        include_vectors: bool = False,
    ) -> Dict[str, Any]:
        """``GET /v1.0/indexes/{name}/vectors`` -- list vectors with optional filters.

        When ``include_vectors`` is ``False`` (the default) the returned objects
        contain only the ``GUID`` field; when ``True`` each object also carries
        its ``Vector`` values.

        ``labels`` and ``tags`` apply metadata filtering (AND semantics on both):
        a record is kept only when every supplied label is present on its
        ``Labels`` and every supplied tag key/value matches its ``Tags``.
        Set ``case_insensitive=True`` to compare labels, tag keys, and tag
        values using an ordinal case-insensitive comparison.

        The returned dict includes a ``FilteredCount`` indicating how many
        records were dropped by the label/tag filter (independent of other
        filters such as ``prefix``).

        Returns the parsed ``EnumerationResult<VectorEntry>`` dict.
        """
        params: Dict[str, Any] = {}
        if max_results is not None:
            params["maxResults"] = max_results
        if skip is not None:
            params["skip"] = skip
        if continuation_token is not None:
            params["continuationToken"] = continuation_token
        if ordering is not None:
            params["ordering"] = ordering
        if prefix is not None:
            params["prefix"] = prefix
        if suffix is not None:
            params["suffix"] = suffix
        if created_after_utc is not None:
            params["createdAfterUtc"] = created_after_utc
        if created_before_utc is not None:
            params["createdBeforeUtc"] = created_before_utc
        if labels:
            # Labels are transmitted as a comma-separated string. Individual
            # labels may not contain commas; such labels cannot be expressed
            # via the query-string form.
            params["labels"] = ",".join(labels)
        if tags:
            # Tags are transmitted as ``key:value`` pairs joined by commas.
            # Keys may not contain ':' or ','; values may not contain ','.
            params["tags"] = ",".join(f"{k}:{v}" for k, v in tags.items())
        if case_insensitive:
            params["caseInsensitive"] = "true"
        params["includeVectors"] = "true" if include_vectors else "false"

        path = f"/v1.0/indexes/{quote(index_name, safe='')}/vectors"
        resp = self._session.get(self._url(path), params=params)
        self._raise_for_status(resp)
        return resp.json()

    def get_vector(self, index_name: str, vector_guid: str) -> Dict[str, Any]:
        """``GET /v1.0/indexes/{name}/vectors/{guid}`` -- retrieve a single vector.

        The returned dict always includes the ``Vector`` values in addition to
        the ``GUID``. Raises :class:`HnswLiteApiError` on 404 when the vector
        does not exist.
        """
        path = (
            f"/v1.0/indexes/{quote(index_name, safe='')}"
            f"/vectors/{quote(vector_guid, safe='')}"
        )
        resp = self._session.get(self._url(path))
        self._raise_for_status(resp)
        return resp.json()

    # ------------------------------------------------------------------
    # Search
    # ------------------------------------------------------------------

    def search(
        self,
        name: str,
        vector: List[float],
        k: int = 10,
        ef: Optional[int] = None,
        labels: Optional[List[str]] = None,
        tags: Optional[Dict[str, str]] = None,
        case_insensitive: bool = False,
    ) -> Dict[str, Any]:
        """``POST /v1.0/indexes/{name}/search`` -- nearest-neighbor search.

        ``labels`` and ``tags`` filter the top-K candidates after graph
        traversal (AND semantics on both). Because filtering is post-HNSW,
        the response may contain fewer than ``k`` results -- the returned
        dict's ``FilteredCount`` reports how many candidates were dropped.

        ``case_insensitive=True`` compares labels, tag keys, and tag values
        using an ordinal case-insensitive comparison.

        Returns the ``SearchResponse`` dict containing ``Results``,
        ``SearchTimeMs``, and ``FilteredCount``.
        """
        body: Dict[str, Any] = {"Vector": vector, "K": k}
        if ef is not None:
            body["Ef"] = ef
        if labels is not None:
            body["Labels"] = labels
        if tags is not None:
            body["Tags"] = tags
        if case_insensitive:
            body["CaseInsensitive"] = True
        resp = self._session.post(
            self._url(f"/v1.0/indexes/{name}/search"), json=body,
        )
        self._raise_for_status(resp)
        return resp.json()
