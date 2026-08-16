import test, { afterEach } from "node:test";
import assert from "node:assert/strict";

import { HnswLiteApiError, HnswLiteClient } from "../src/index.js";

interface FetchCall {
  url: string;
  init: RequestInit;
}

const originalFetch = globalThis.fetch;

afterEach(() => {
  globalThis.fetch = originalFetch;
});

function jsonResponse(body: unknown, init?: ResponseInit): Response {
  return new Response(JSON.stringify(body), {
    status: init?.status ?? 200,
    statusText: init?.statusText,
    headers: { "Content-Type": "application/json" },
  });
}

function mockFetch(handler: (url: string, init: RequestInit) => Response | Promise<Response>): FetchCall[] {
  const calls: FetchCall[] = [];
  globalThis.fetch = (async (input: RequestInfo | URL, init?: RequestInit) => {
    const url = input instanceof Request ? input.url : String(input);
    const requestInit = init ?? {};
    calls.push({ url, init: requestInit });
    return handler(url, requestInit);
  }) as typeof fetch;
  return calls;
}

test("createIndex sends PascalCase JSON and returns camelCase data", async () => {
  const calls = mockFetch((url, init) => {
    assert.equal(url, "http://localhost:8080/v1.0/indexes");
    assert.equal(init.method, "POST");

    const headers = new Headers(init.headers);
    assert.equal(headers.get("x-api-key"), "secret");
    assert.equal(headers.get("content-type"), "application/json");
    assert.deepEqual(JSON.parse(init.body as string), {
      Name: "demo",
      Dimension: 3,
      StorageType: "RAM",
      DistanceFunction: "CosineDistance",
    });

    return jsonResponse({
      Guid: "index-guid",
      Name: "demo",
      Dimension: 3,
      StorageType: "RAM",
      DistanceFunction: "CosineDistance",
      M: 16,
      MaxM: 32,
      EfConstruction: 200,
      VectorCount: 0,
      CreatedUtc: "2026-08-15T00:00:00Z",
    }, { status: 201, statusText: "Created" });
  });

  const client = new HnswLiteClient("http://localhost:8080///", "secret");
  const result = await client.createIndex({
    name: "demo",
    dimension: 3,
    storageType: "RAM",
    distanceFunction: "CosineDistance",
  });

  assert.equal(calls.length, 1);
  assert.equal(result.guid, "index-guid");
  assert.equal(result.createdUtc, "2026-08-15T00:00:00Z");
  assert.equal(result.vectorCount, 0);
});

test("addVector preserves tag keys while converting schema keys", async () => {
  mockFetch((url, init) => {
    assert.equal(url, "https://api.example/v1.0/indexes/idx/vectors");
    assert.deepEqual(JSON.parse(init.body as string), {
      Guid: "vector-guid",
      Vector: [1, 2],
      Name: "sample",
      Labels: ["red"],
      Tags: {
        camelCaseKey: "kept",
        PascalCaseKey: "also-kept",
      },
    });

    return jsonResponse({
      Guid: "vector-guid",
      Vector: [1, 2],
      Name: "sample",
      Labels: ["red"],
      Tags: {
        camelCaseKey: "kept",
        PascalCaseKey: "also-kept",
      },
    }, { status: 201, statusText: "Created" });
  });

  const client = new HnswLiteClient("https://api.example", "secret");
  const result = await client.addVector("idx", {
    guid: "vector-guid",
    vector: [1, 2],
    name: "sample",
    labels: ["red"],
    tags: {
      camelCaseKey: "kept",
      PascalCaseKey: "also-kept",
    },
  });

  assert.deepEqual(result.tags, {
    camelCaseKey: "kept",
    PascalCaseKey: "also-kept",
  });
});

test("enumerateVectors serializes filters and includeVectors query values", async () => {
  const calls = mockFetch((url) => {
    const parsed = new URL(url);
    assert.equal(parsed.pathname, "/v1.0/indexes/index%2Fwith%20slash/vectors");
    assert.equal(parsed.searchParams.get("maxResults"), "10");
    assert.equal(parsed.searchParams.get("skip"), "2");
    assert.equal(parsed.searchParams.get("labels"), "red,small");
    assert.equal(parsed.searchParams.get("tags"), "env:prod,owner:alice");
    assert.equal(parsed.searchParams.get("caseInsensitive"), "true");
    assert.equal(parsed.searchParams.get("includeVectors"), "true");

    return jsonResponse({
      Success: true,
      MaxResults: 10,
      Skip: 2,
      ContinuationToken: null,
      EndOfResults: true,
      TotalRecords: 0,
      RecordsRemaining: 0,
      TimestampUtc: "2026-08-15T00:00:00Z",
      Objects: [],
      FilteredCount: 0,
    });
  });

  const client = new HnswLiteClient("https://api.example", "secret");
  const result = await client.enumerateVectors(
    "index/with slash",
    {
      maxResults: 10,
      skip: 2,
      labels: ["red", "", "small"],
      tags: { env: "prod", owner: "alice" },
      caseInsensitive: true,
    },
    true,
  );

  assert.equal(calls.length, 1);
  assert.equal(result.success, true);
  assert.equal(result.filteredCount, 0);
});

test("getIndex throws HnswLiteApiError with response details on failure", async () => {
  mockFetch(() => new Response("missing index", {
    status: 404,
    statusText: "Not Found",
  }));

  const client = new HnswLiteClient("https://api.example", "secret");

  await assert.rejects(
    () => client.getIndex("missing"),
    (error: unknown) => {
      assert.ok(error instanceof HnswLiteApiError);
      assert.equal(error.status, 404);
      assert.equal(error.statusText, "Not Found");
      assert.equal(error.body, "missing index");
      return true;
    },
  );
});
