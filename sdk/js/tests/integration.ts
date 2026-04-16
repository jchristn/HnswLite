/**
 * HnswLite SDK integration tests.
 *
 * Usage:
 *   BASE_URL=http://localhost:8321 API_KEY=mykey npx tsx tests/integration.ts
 *
 * Or pass as positional args:
 *   npx tsx tests/integration.ts http://localhost:8321 mykey
 */

import { HnswLiteClient, HnswLiteApiError } from "../src/index.js";
import type {
  IndexResponse,
  EnumerationResult,
  SearchResponse,
  AddVectorRequest,
  AddVectorsRequest,
  VectorEntry,
} from "../src/index.js";

// ── Config ───────────────────────────────────────────────────────────

const BASE_URL = process.argv[2] || process.env.BASE_URL || "http://localhost:8321";
const API_KEY = process.argv[3] || process.env.API_KEY || "default";

const client = new HnswLiteClient(BASE_URL, API_KEY);

// ── Harness ──────────────────────────────────────────────────────────

interface TestResult {
  name: string;
  passed: boolean;
  error?: string;
}

const results: TestResult[] = [];

async function run(name: string, fn: () => Promise<void>): Promise<void> {
  try {
    await fn();
    results.push({ name, passed: true });
    console.log(`  PASS  ${name}`);
  } catch (err: unknown) {
    const msg = err instanceof Error ? err.message : String(err);
    results.push({ name, passed: false, error: msg });
    console.log(`  FAIL  ${name}: ${msg}`);
  }
}

function assert(condition: boolean, message: string): void {
  if (!condition) throw new Error(`Assertion failed: ${message}`);
}

// ── Helpers ──────────────────────────────────────────────────────────

const TEST_INDEX = `sdk-test-${Date.now()}`;
const DIMENSION = 4;
let vectorGuid: string = "";

// ── Tests ────────────────────────────────────────────────────────────

async function main(): Promise<void> {
  console.log(`\nHnswLite SDK Integration Tests`);
  console.log(`  Server : ${BASE_URL}`);
  console.log(`  Index  : ${TEST_INDEX}\n`);

  // 1. Ping
  await run("ping (GET /)", async () => {
    const ok = await client.ping();
    assert(ok === true, "expected true");
  });

  // 2. Head ping
  await run("headPing (HEAD /)", async () => {
    const ok = await client.headPing();
    assert(ok === true, "expected true");
  });

  // 3. Create index
  await run("createIndex", async () => {
    const idx: IndexResponse = await client.createIndex({
      name: TEST_INDEX,
      dimension: DIMENSION,
    });
    assert(idx.name === TEST_INDEX, `expected name '${TEST_INDEX}', got '${idx.name}'`);
    assert(idx.dimension === DIMENSION, `expected dimension ${DIMENSION}`);
    assert(typeof idx.guid === "string" && idx.guid.length > 0, "expected guid");
    assert(typeof idx.createdUtc === "string", "expected createdUtc");
  });

  // 4. Get index
  await run("getIndex", async () => {
    const idx = await client.getIndex(TEST_INDEX);
    assert(idx.name === TEST_INDEX, "name mismatch");
    assert(idx.dimension === DIMENSION, "dimension mismatch");
  });

  // 5. Enumerate indexes
  await run("enumerateIndexes (no filter)", async () => {
    const result: EnumerationResult<IndexResponse> = await client.enumerateIndexes();
    assert(typeof result.success === "boolean", "expected success field");
    assert(Array.isArray(result.objects), "expected objects array");
    assert(typeof result.totalRecords === "number", "expected totalRecords");
  });

  // 6. Enumerate indexes with query
  await run("enumerateIndexes (with prefix)", async () => {
    const result = await client.enumerateIndexes({ prefix: "sdk-test-", maxResults: 10 });
    assert(result.objects.length > 0, "expected at least one result");
  });

  // 7. Add single vector
  await run("addVector", async () => {
    const resp = await client.addVector(TEST_INDEX, {
      vector: [1.0, 2.0, 3.0, 4.0],
    }) as AddVectorRequest;
    assert(Array.isArray(resp.vector), "expected vector array in response");
    assert(typeof resp.guid === "string" && resp.guid.length > 0, "expected guid in response");
    vectorGuid = resp.guid!;
  });

  // 8. Add batch vectors
  await run("addVectors (batch)", async () => {
    const resp = await client.addVectors(TEST_INDEX, {
      vectors: [
        { vector: [5.0, 6.0, 7.0, 8.0] },
        { vector: [9.0, 10.0, 11.0, 12.0] },
      ],
    }) as AddVectorsRequest;
    assert(Array.isArray(resp.vectors), "expected vectors array in response");
    assert(resp.vectors.length === 2, "expected 2 vectors");
  });

  // 9. Search
  await run("search", async () => {
    const resp: SearchResponse = await client.search(TEST_INDEX, {
      vector: [1.0, 2.0, 3.0, 4.0],
      k: 3,
    });
    assert(Array.isArray(resp.results), "expected results array");
    assert(typeof resp.searchTimeMs === "number", "expected searchTimeMs");
    assert(resp.results.length > 0, "expected at least one result");
    assert(typeof resp.results[0].guid === "string", "expected guid on result");
    assert(typeof resp.results[0].distance === "number", "expected distance on result");
  });

  // 10. Search with ef
  await run("search (with ef)", async () => {
    const resp = await client.search(TEST_INDEX, {
      vector: [1.0, 2.0, 3.0, 4.0],
      k: 2,
      ef: 50,
    });
    assert(resp.results.length > 0, "expected results");
  });

  // 11. Enumerate vectors (without vector values)
  await run("enumerateVectors (includeVectors=false)", async () => {
    const result: EnumerationResult<VectorEntry> = await client.enumerateVectors(
      TEST_INDEX,
      { maxResults: 10 },
      false,
    );
    assert(Array.isArray(result.objects), "expected objects array");
    assert(
      result.totalRecords >= 3,
      `expected totalRecords >= 3, got ${result.totalRecords}`,
    );
    for (const entry of result.objects) {
      assert(typeof entry.guid === "string" && entry.guid.length > 0, "expected guid on entry");
      assert(entry.vector === undefined, "expected vector to be undefined");
    }
  });

  // 12. Enumerate vectors (with vector values)
  await run("enumerateVectors (includeVectors=true)", async () => {
    const result = await client.enumerateVectors(TEST_INDEX, { maxResults: 1 }, true);
    assert(result.objects.length === 1, `expected exactly 1 object, got ${result.objects.length}`);
    const entry = result.objects[0];
    assert(entry.vector !== undefined, "expected vector to be defined");
    assert(
      Array.isArray(entry.vector) && entry.vector!.length === DIMENSION,
      `expected vector.length === ${DIMENSION}`,
    );
  });

  // 13. Get single vector
  await run("getVector", async () => {
    assert(vectorGuid.length > 0, "no vector guid captured from addVector");
    const entry = await client.getVector(TEST_INDEX, vectorGuid);
    assert(entry.guid === vectorGuid, `expected guid '${vectorGuid}', got '${entry.guid}'`);
    assert(entry.vector !== undefined, "expected vector to be defined");
    assert(
      Array.isArray(entry.vector) && entry.vector!.length === DIMENSION,
      `expected vector.length === ${DIMENSION}`,
    );
  });

  // 14. Remove vector
  await run("removeVector", async () => {
    assert(vectorGuid.length > 0, "no vector guid captured from addVector");
    await client.removeVector(TEST_INDEX, vectorGuid);
    // If no error thrown, it succeeded (204).
  });

  // 15. Delete index (cleanup)
  await run("deleteIndex", async () => {
    await client.deleteIndex(TEST_INDEX);
    // 204 — no error means success.
  });

  // 16. Verify deletion — getIndex should throw
  await run("getIndex after delete (expect 404)", async () => {
    try {
      await client.getIndex(TEST_INDEX);
      throw new Error("expected HnswLiteApiError but call succeeded");
    } catch (err) {
      if (err instanceof HnswLiteApiError) {
        assert(err.status === 404, `expected 404, got ${err.status}`);
      } else {
        throw err;
      }
    }
  });

  // ── Summary ────────────────────────────────────────────────────────

  console.log("\n--- Summary ---");
  const passed = results.filter((r) => r.passed).length;
  const failed = results.filter((r) => !r.passed).length;
  console.log(`  ${passed} passed, ${failed} failed, ${results.length} total\n`);

  if (failed > 0) {
    console.log("Failed tests:");
    for (const r of results.filter((r) => !r.passed)) {
      console.log(`  - ${r.name}: ${r.error}`);
    }
    process.exit(1);
  }

  console.log("All tests passed.");
  process.exit(0);
}

main().catch((err) => {
  console.error("Unhandled error:", err);
  process.exit(1);
});
