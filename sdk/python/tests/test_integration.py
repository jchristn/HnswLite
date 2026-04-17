#!/usr/bin/env python3
"""Integration tests for the HnswLite Python SDK.

Usage:
    python test_integration.py --base-url http://localhost:8321 --api-key mykey
"""

import argparse
import sys
import time
import uuid

# Allow running from the repo root or the tests/ directory.
sys.path.insert(0, ".")
sys.path.insert(0, "..")

from hnswlite import HnswLiteClient, HnswLiteApiError


# ---------------------------------------------------------------------------
# Helpers
# ---------------------------------------------------------------------------

passed = 0
failed = 0


def _report(name: str, ok: bool, detail: str = ""):
    global passed, failed
    if ok:
        passed += 1
        print(f"  PASS  {name}")
    else:
        failed += 1
        msg = f"  FAIL  {name}"
        if detail:
            msg += f"  -- {detail}"
        print(msg)


def run_test(name: str):
    """Decorator that catches exceptions and reports pass/fail."""
    def decorator(fn):
        def wrapper(*args, **kwargs):
            try:
                fn(*args, **kwargs)
                _report(name, True)
            except AssertionError as exc:
                _report(name, False, str(exc))
            except Exception as exc:
                _report(name, False, repr(exc))
        return wrapper
    return decorator


# ---------------------------------------------------------------------------
# Tests
# ---------------------------------------------------------------------------

def run_all(base_url: str, api_key: str):
    client = HnswLiteClient(base_url=base_url, api_key=api_key)
    index_name = f"test-{uuid.uuid4().hex[:8]}"
    dimension = 4
    vector_guids: list = []

    # -- ping -----------------------------------------------------------------

    @run_test("ping")
    def test_ping():
        assert client.ping() is True

    @run_test("head_ping")
    def test_head_ping():
        assert client.head_ping() is True

    test_ping()
    test_head_ping()

    # -- create index ---------------------------------------------------------

    @run_test("create_index")
    def test_create_index():
        resp = client.create_index(
            name=index_name,
            dimension=dimension,
            storage_type="RAM",
            distance_function="Cosine",
        )
        assert resp["Name"] == index_name
        assert resp["Dimension"] == dimension
        assert "GUID" in resp

    test_create_index()

    # -- get index ------------------------------------------------------------

    @run_test("get_index")
    def test_get_index():
        resp = client.get_index(index_name)
        assert resp["Name"] == index_name
        assert resp["Dimension"] == dimension

    test_get_index()

    # -- enumerate indexes (basic) --------------------------------------------

    @run_test("enumerate_indexes")
    def test_enumerate_indexes():
        resp = client.enumerate_indexes()
        assert "Objects" in resp
        names = [o["Name"] for o in resp["Objects"]]
        assert index_name in names

    test_enumerate_indexes()

    # -- enumerate indexes (pagination) ---------------------------------------

    @run_test("enumerate_indexes_pagination")
    def test_enumerate_indexes_pagination():
        resp = client.enumerate_indexes(max_results=1, skip=0)
        assert resp["MaxResults"] == 1
        assert isinstance(resp["Objects"], list)

    test_enumerate_indexes_pagination()

    # -- enumerate indexes (prefix filter) ------------------------------------

    @run_test("enumerate_indexes_prefix")
    def test_enumerate_indexes_prefix():
        resp = client.enumerate_indexes(prefix="test-")
        names = [o["Name"] for o in resp["Objects"]]
        assert index_name in names

    test_enumerate_indexes_prefix()

    # -- add vector -----------------------------------------------------------

    @run_test("add_vector")
    def test_add_vector():
        resp = client.add_vector(index_name, [1.0, 0.0, 0.0, 0.0])
        assert "GUID" in resp
        vector_guids.append(resp["GUID"])

    test_add_vector()

    # -- add vector with explicit GUID ----------------------------------------

    @run_test("add_vector_with_guid")
    def test_add_vector_with_guid():
        explicit_guid = str(uuid.uuid4())
        resp = client.add_vector(
            index_name, [0.0, 1.0, 0.0, 0.0], guid=explicit_guid,
        )
        assert resp["GUID"] == explicit_guid
        vector_guids.append(explicit_guid)

    test_add_vector_with_guid()

    # -- add vectors (batch) --------------------------------------------------

    @run_test("add_vectors_batch")
    def test_add_vectors_batch():
        batch = [
            {"Vector": [0.0, 0.0, 1.0, 0.0]},
            {"Vector": [0.0, 0.0, 0.0, 1.0]},
        ]
        resp = client.add_vectors(index_name, batch)
        assert "Vectors" in resp
        assert len(resp["Vectors"]) == 2
        for v in resp["Vectors"]:
            vector_guids.append(v["GUID"])

    test_add_vectors_batch()

    # -- enumerate vectors (no vector values) ---------------------------------

    @run_test("enumerate_vectors")
    def test_enumerate_vectors():
        resp = client.enumerate_vectors(index_name, max_results=10)
        assert "Objects" in resp
        assert resp["TotalRecords"] >= 3, (
            f"expected at least 3 records, got {resp['TotalRecords']}"
        )
        for obj in resp["Objects"]:
            assert "Vector" not in obj or obj["Vector"] is None, (
                "Vector should be absent or None when include_vectors=False"
            )

    test_enumerate_vectors()

    # -- enumerate vectors (include Vector values) ----------------------------

    @run_test("enumerate_vectors_include_vectors")
    def test_enumerate_vectors_include_vectors():
        resp = client.enumerate_vectors(
            index_name, max_results=1, include_vectors=True,
        )
        assert "Objects" in resp
        assert len(resp["Objects"]) == 1, (
            f"expected 1 object, got {len(resp['Objects'])}"
        )
        obj = resp["Objects"][0]
        assert obj.get("Vector") is not None, "Vector must be populated"
        assert len(obj["Vector"]) == dimension, (
            f"expected Vector length {dimension}, got {len(obj['Vector'])}"
        )

    test_enumerate_vectors_include_vectors()

    # -- get vector (single) --------------------------------------------------

    @run_test("get_vector")
    def test_get_vector():
        assert len(vector_guids) > 0, "no vector GUIDs available"
        first_guid = vector_guids[0]
        resp = client.get_vector(index_name, first_guid)
        assert resp["GUID"] == first_guid, (
            f"expected GUID {first_guid}, got {resp.get('GUID')}"
        )
        assert resp.get("Vector") is not None, "Vector must be populated"
        assert len(resp["Vector"]) == dimension

    test_get_vector()

    # -- search ---------------------------------------------------------------

    @run_test("search")
    def test_search():
        resp = client.search(index_name, [1.0, 0.0, 0.0, 0.0], k=2)
        assert "Results" in resp
        assert "SearchTimeMs" in resp
        assert len(resp["Results"]) <= 2

    test_search()

    # -- search with ef -------------------------------------------------------

    @run_test("search_with_ef")
    def test_search_with_ef():
        resp = client.search(index_name, [1.0, 0.0, 0.0, 0.0], k=2, ef=50)
        assert "Results" in resp

    test_search_with_ef()

    # -- add vectors with metadata for filter tests ---------------------------

    filter_guid_a = None
    filter_guid_b = None
    filter_guid_c = None

    @run_test("add_vector_with_metadata")
    def test_add_vector_with_metadata():
        nonlocal filter_guid_a, filter_guid_b, filter_guid_c
        a = client.add_vector(
            index_name, [0.5, 0.5, 0.0, 0.0],
            vector_name="filter-a",
            labels=["red", "small"],
            tags={"env": "prod", "owner": "alice"},
        )
        b = client.add_vector(
            index_name, [0.4, 0.4, 0.1, 0.0],
            vector_name="filter-b",
            labels=["red", "big"],
            tags={"env": "prod", "owner": "bob"},
        )
        c = client.add_vector(
            index_name, [0.3, 0.3, 0.2, 0.0],
            vector_name="filter-c",
            labels=["blue", "small"],
            tags={"env": "dev", "owner": "alice"},
        )
        filter_guid_a = a["GUID"]
        filter_guid_b = b["GUID"]
        filter_guid_c = c["GUID"]
        vector_guids.extend([filter_guid_a, filter_guid_b, filter_guid_c])

    test_add_vector_with_metadata()

    # -- search with Labels filter (AND) --------------------------------------

    @run_test("search_labels_and")
    def test_search_labels_and():
        resp = client.search(
            index_name, [0.5, 0.5, 0.0, 0.0], k=10,
            labels=["red", "small"],
        )
        # Only vector A has BOTH 'red' AND 'small'.
        guids = [r["GUID"] for r in resp["Results"]]
        assert filter_guid_a in guids, f"expected {filter_guid_a} in {guids}"
        assert filter_guid_b not in guids
        assert filter_guid_c not in guids
        assert resp.get("FilteredCount", 0) > 0

    test_search_labels_and()

    # -- search with Tags filter (AND) ----------------------------------------

    @run_test("search_tags_and")
    def test_search_tags_and():
        resp = client.search(
            index_name, [0.5, 0.5, 0.0, 0.0], k=10,
            tags={"env": "prod", "owner": "alice"},
        )
        guids = [r["GUID"] for r in resp["Results"]]
        assert filter_guid_a in guids
        assert filter_guid_b not in guids
        assert filter_guid_c not in guids

    test_search_tags_and()

    # -- search with case_insensitive=True ------------------------------------

    @run_test("search_case_insensitive")
    def test_search_case_insensitive():
        miss = client.search(
            index_name, [0.5, 0.5, 0.0, 0.0], k=10,
            labels=["RED"], case_insensitive=False,
        )
        assert len(miss["Results"]) == 0, (
            f"case-sensitive 'RED' matched {len(miss['Results'])} results"
        )

        hit = client.search(
            index_name, [0.5, 0.5, 0.0, 0.0], k=10,
            labels=["RED"], case_insensitive=True,
        )
        guids = [r["GUID"] for r in hit["Results"]]
        assert filter_guid_a in guids and filter_guid_b in guids, (
            f"case-insensitive 'RED' should match A and B; got {guids}"
        )

    test_search_case_insensitive()

    # -- enumerate with Labels filter and case_insensitive --------------------

    @run_test("enumerate_labels_case_insensitive")
    def test_enumerate_labels_case_insensitive():
        resp = client.enumerate_vectors(
            index_name, max_results=100,
            labels=["RED"], case_insensitive=True,
        )
        assert resp["TotalRecords"] == 2, (
            f"expected 2 records, got {resp['TotalRecords']}"
        )
        assert resp.get("FilteredCount", 0) > 0

    test_enumerate_labels_case_insensitive()

    # -- remove vector --------------------------------------------------------

    @run_test("remove_vector")
    def test_remove_vector():
        guid = vector_guids.pop()
        result = client.remove_vector(index_name, guid)
        assert result is None

    test_remove_vector()

    # -- delete index ---------------------------------------------------------

    @run_test("delete_index")
    def test_delete_index():
        result = client.delete_index(index_name)
        assert result is None

    test_delete_index()

    # -- verify delete raises 404 --------------------------------------------

    @run_test("get_deleted_index_404")
    def test_deleted_index_404():
        try:
            client.get_index(index_name)
            assert False, "Expected HnswLiteApiError"
        except HnswLiteApiError as exc:
            assert exc.status_code == 404

    test_deleted_index_404()

    # -- summary --------------------------------------------------------------
    print()
    print(f"Results: {passed} passed, {failed} failed, {passed + failed} total")
    return failed == 0


# ---------------------------------------------------------------------------
# CLI entry point
# ---------------------------------------------------------------------------

def main():
    parser = argparse.ArgumentParser(description="HnswLite SDK integration tests")
    parser.add_argument("--base-url", required=True, help="Base URL of the HnswLite server")
    parser.add_argument("--api-key", required=True, help="API key for authentication")
    args = parser.parse_args()

    print(f"Running integration tests against {args.base_url}\n")
    ok = run_all(args.base_url, args.api_key)
    sys.exit(0 if ok else 1)


if __name__ == "__main__":
    main()
