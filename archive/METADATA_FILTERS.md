# Metadata Filters (Labels & Tags) — Implementation Plan

Status: **Complete (v1.2.0)** · Owner: _Joel_ · Target version: **v1.2.0**

Stored vectors already carry optional `Labels` (`List<string>`) and `Tags` (`Dictionary<string, object>`) metadata via `IHnswNode` (`src/HnswIndex/IHnswNode.cs:29,34`). Today neither `POST /search` nor `GET /vectors` can filter on that metadata — this plan adds it.

---

## 1. Goals

- **G1.** Filter search results (`POST /v1.0/indexes/{name}/search`) by Labels and/or Tags.
- **G2.** Filter enumeration results (`GET /v1.0/indexes/{name}/vectors`) by Labels and/or Tags.
- **G3.** Keep the API surface **consistent** with today's conventions:
  - Use the same filter field names, shapes, and semantics on both `SearchRequest` and `EnumerationQuery`.
  - Preserve existing request/response shapes — additions must be additive and optional.
  - Reuse the query-string idiom already used by `EnumerationQuery.FromQueryString` (`src/HnswIndex.Server/Classes/EnumerationQuery.cs:100`).
- **G4.** Deliver end-to-end: core/server + all three SDKs (C#, Python, JS) + dashboard UI + docs + tests.
- **G5.** Callers should be able to tell **why** they got fewer than K search results (or a smaller enumeration page) — expose a "filtered out" count alongside results.

## 2. Non-goals

- **NG1.** Rich query DSLs (e.g. nested AND/OR/NOT, numeric ranges on tags). If required later, add under a separate `TagQuery` string field — reserve the name now, implement later.
- **NG2.** Storage-layer pushdown (SQLite `json_extract` predicates). Start with in-memory post-filter; revisit after profiling.
- **NG3.** Changing the HNSW algorithm. No candidate-time filtering inside `GetTopKAsync`.

## 3. Key design decisions

### 3.1. Post-filter, not pre-filter (for Search)

HNSW's graph traversal cannot be cleanly filtered mid-search without risking disconnected reachability and degraded recall. We will:

1. Call `HnswIndex.GetTopKAsync` with `K` unchanged.
2. Apply Labels/Tags filters to the returned list in `IndexManager.SearchAsync` (`src/HnswIndex.Server/Services/IndexManager.cs:481`).
3. Accept that the response may contain **fewer than K** results when filters are restrictive — return a `FilteredCount` on the response so callers can see how many candidates were dropped by the filter.

**Deferred enhancement:** add an optional `CandidatePoolMultiplier` (search-time only) that multiplies `K`/`ef` internally before filtering to improve the chance of returning K results. Out of scope for v1.2.

### 3.2. Filter semantics — uniform AND across both fields

| Field | Type | Semantics |
|---|---|---|
| `Labels` | `List<string>?` | **All-of** (AND). A vector matches only if **every** label in the filter is present on the vector's `Labels`. |
| `Tags` | `Dictionary<string, string>?` | **All-of** (AND) on key/value equality. A vector matches only if, for **every** entry in the filter, the vector's `Tags[key].ToString()` equals the filter value. Missing key ⇒ no match. |

Using AND on both is symmetric, predictable, and matches the typical "narrow my results" mental model. Escape hatch for OR semantics: issue multiple searches client-side.

String-only tag values in v1.2 keeps parity with query-string encoding. Numeric/bool comparisons are deferred to a future `TagQuery`.

### 3.3. Case sensitivity — caller-controlled

Both `SearchRequest` and `EnumerationQuery` expose a `CaseInsensitive` boolean (default `false`, i.e. exact match). When `true`:

- Label comparisons use `StringComparison.OrdinalIgnoreCase`.
- Tag **keys** and **values** are compared with `OrdinalIgnoreCase`.

Query-string form: `?caseInsensitive=true` (also accepts `case=insensitive` / `ci=true` for terseness? — **decision: only `caseInsensitive` for clarity**). Documented in the XML docs on both classes and in the README.

### 3.4. `FilteredCount` on response objects (new in v1.2)

Both `SearchResponse` and `EnumerationResult` grow a new integer:

- `SearchResponse.FilteredCount` — how many HNSW candidates were dropped by the metadata filter. `0` when no filter is supplied or nothing matched to drop.
- `EnumerationResult.FilteredCount` — how many records were dropped by the metadata filter **before** pagination. `TotalRecords` remains the post-filter count.

This directly addresses "why did I get fewer than K?" without changing the established result shape.

### 3.5. Enumeration vs. Search — same field names

Both `SearchRequest` and `EnumerationQuery` gain the **same** triple: `Labels`, `Tags`, `CaseInsensitive`. This is the cornerstone of G3.

### 3.6. Enumeration query-string encoding

Extend `EnumerationQuery.FromQueryString` following the prefix/suffix idiom:

- `?labels=alpha,beta` → `Labels = ["alpha","beta"]` (comma-delimited, trimmed, empties dropped).
- `?tags=owner:alice,env:prod` → `Tags = {"owner":"alice","env":"prod"}`.
- `?caseInsensitive=true` (also `true/false/1/0`, case-insensitive parse).
- Reject malformed tag segments (no `:`) with `ArgumentException` to mirror existing behavior (`src/HnswIndex.Server/Classes/EnumerationQuery.cs:109`).

### 3.7. Pagination correctness

For enumeration, filters must be applied **before** pagination math (`Skip`, `MaxResults`, `TotalRecords`), so counts reflect the filtered set — same pattern used today for `Prefix`/`Suffix`. `FilteredCount` records the count dropped by label/tag filters specifically.

### 3.8. Backward compatibility

All new fields nullable/optional; `FilteredCount` defaults to `0`. Existing clients are unaffected. No wire-format changes beyond additive properties.

---

## 4. Proposed API shape

**Server — `SearchRequest`** (`src/HnswIndex.Server/Classes/SearchRequest.cs`)
```csharp
public List<string>? Labels { get; set; } = null;
public Dictionary<string, string>? Tags { get; set; } = null;
public bool CaseInsensitive { get; set; } = false;
```

**Server — `EnumerationQuery`** (`src/HnswIndex.Server/Classes/EnumerationQuery.cs`) — same three fields.

**Server — `SearchResponse`** adds:
```csharp
public int FilteredCount { get; set; } = 0;
```

**Server — `EnumerationResult`** adds the same `FilteredCount`.

**Query-string examples**
```
GET /v1.0/indexes/foo/vectors?labels=cat,dog&tags=owner:alice&caseInsensitive=true
POST /v1.0/indexes/foo/search   (body) { "Vector":[…], "K":10, "Labels":["cat"], "Tags":{"env":"prod"}, "CaseInsensitive":true }
```

---

## 5. Work plan (actionable checklist)

Mark each box `[x]` on completion. Sub-bullets are verification steps; don't tick the parent until sub-items pass.

### Phase 1 — Server contracts & filtering ✅

- [x] **1.1 Extend `SearchRequest`** — `src/HnswIndex.Server/Classes/SearchRequest.cs`
  - [x] Add `Labels`, `Tags`, `CaseInsensitive` optional members with XML docs.
- [x] **1.2 Extend `EnumerationQuery`** — `src/HnswIndex.Server/Classes/EnumerationQuery.cs`
  - [x] Add `Labels`, `Tags`, `CaseInsensitive` optional members with XML docs.
  - [x] Extend `FromQueryString` to parse `labels`, `tags`, `caseInsensitive`.
  - [x] Reject malformed tag segments (empty key or missing `:`) with `ArgumentException`.
- [x] **1.3 Extend `SearchResponse` + `EnumerationResult`** with `FilteredCount` (default `0`).
- [x] **1.4 Shared filter helper** — `src/HnswIndex.Server/Services/MetadataFilter.cs`
  - [x] `static bool Matches(IHnswNode? node, IReadOnlyCollection<string>? labels, IReadOnlyDictionary<string, string>? tags, bool caseInsensitive)`.
  - [x] Null/empty filter ⇒ always matches (no-op).
  - [x] Tag value comparison via `Convert.ToString(value, CultureInfo.InvariantCulture)`.
- [x] **1.5 Wire filter into `IndexManager.SearchAsync`**
  - [x] After node map fetch, drop results whose node fails `MetadataFilter.Matches`; count into `FilteredCount`.
- [x] **1.6 Wire filter into `IndexManager.EnumerateVectorsAsync`**
  - [x] Apply filter **before** skip/take so `TotalRecords` is accurate.
  - [x] Count dropped records into `FilteredCount`; reuse pre-fetched node map when available.

### Phase 2 — Server tests ✅

- [x] **2.1 Shared suite** — new `src/Test.Shared/MetadataFilterSuites.cs`, wired into `HnswSuites.All`
  - [x] `MetadataFilter.Helper.*` — direct unit tests over `MetadataFilter.Matches` (6 cases).
  - [x] `MetadataFilter.Search.*` — end-to-end via `IndexManager.SearchAsync` (7 cases: Labels AND, Tags AND, combined, case-insensitive, fewer-than-K, no-match, no-filter).
  - [x] `MetadataFilter.Enumerate.*` — end-to-end via `IndexManager.EnumerateVectorsAsync` (4 cases: label filter + pagination math, tag filter, case-insensitive, no-filter sanity).
  - [x] `MetadataFilter.QueryString.*` — `FromQueryString` parse tests (6 cases: labels, empty, empty segments, tags, malformed, caseInsensitive).
  - [x] **Note:** the vectors-enumeration endpoint does not surface continuation tokens today (token is always `null`), so the "with continuation token" case was dropped as inapplicable. The metadata filter composes correctly with `Prefix` by construction — covered implicitly through `TotalRecords` math; not called out as a dedicated case since `Prefix` ordering over GUIDs is non-deterministic with filters.
- [x] **2.2 All 81 tests green** under `Test.Automated` (net8.0). `Test.NUnit`, `Test.XUnit`, `Test.MSTest` adapters re-use `HnswSuites.All`, so they pick up the new suite automatically — verified by running Test.Automated which enumerates the same suite collection.
- [x] **2.3 `FromQueryString` unit tests** — included in `MetadataFilter.QueryString` suite (covers `labels=a,b` split, `labels=` → null, empty-segment dropping, `tags=k:v,k:v`, malformed tag → `ArgumentException`, `caseInsensitive=true/false/1/0` plus garbage rejection).

### Phase 3 — C# SDK ✅

- [x] **3.1 Request models** — added `Labels`, `Tags`, `CaseInsensitive` to `SearchRequest.cs` and `EnumerationQuery.cs`.
- [x] **3.2 Response models** — added `FilteredCount` to `SearchResponse.cs` and `EnumerationResult.cs`.
- [x] **3.3 Query-string builder** — `BuildEnumerationQueryParams` in `HnswLiteClient.cs` emits URL-encoded `labels=a,b`, `tags=k:v,k:v`, `caseInsensitive=true`.
- [x] **3.4 SDK harness** — added five filter cases to `HnswLite.Sdk.Test/Program.cs` (add-with-metadata, Labels AND, Tags AND, case-insensitive search, case-insensitive enumerate).
- [x] **3.5 Result model** — added `Name`, `Labels`, `Tags` to `VectorSearchResult.cs` (server already returned them; SDK now surfaces).
- [x] **3.6 Add metadata on insert** — pre-existing gap: added `Name`, `Labels`, `Tags` to the SDK's `AddVectorRequest.cs` so callers can set the metadata they'll later filter on. Batch variant inherits this through `AddVectorRequest` items.

### Phase 4 — Python SDK ✅

- [x] **4.1 Models** — `sdk/python/hnswlite/models.py`
  - [x] Added `Labels`, `Tags`, `CaseInsensitive` to `SearchRequest` and new `EnumerationQuery` dataclass.
  - [x] Added `FilteredCount` to `SearchResponse` and `EnumerationResult`.
  - [x] Added `Name`/`Labels`/`Tags` to `VectorSearchResult` and `VectorEntry` and `AddVectorRequest`.
- [x] **4.2 Client** — `sdk/python/hnswlite/client.py`
  - [x] `search()` gained `labels`, `tags`, `case_insensitive` kwargs, serialized into JSON body.
  - [x] `enumerate_vectors()` gained the same three, encoded into the query string (comma/colon joined; `requests` handles percent-encoding).
  - [x] `add_vector()` gained `vector_name`, `labels`, `tags` kwargs.
- [x] **4.3 Tests** — appended four filter cases to `sdk/python/tests/test_integration.py` (add-with-metadata, Labels AND, Tags AND, case-insensitive search, case-insensitive enumerate). Syntax-verified.

### Phase 5 — JavaScript SDK ✅

- [x] **5.1 Types** — `sdk/js/src/types.ts`
  - [x] Added `labels`, `tags`, `caseInsensitive` to `SearchRequest` and `EnumerationQuery`.
  - [x] Added `name`, `labels`, `tags` to `AddVectorRequest`, `VectorSearchResult`, `VectorEntry`.
  - [x] Added `filteredCount` to `SearchResponse` and `EnumerationResult<T>`.
- [x] **5.2 Client** — `sdk/js/src/index.ts`
  - [x] **Bug fix (pre-existing):** `keysToPascal`/`keysToCamel` were recursing into every object blindly, which would corrupt user-supplied `tags` keys (e.g. `{env: 'prod'}` → `{Env: 'prod'}`). Added an `OPAQUE_KEYS` set (`"tags"`) whose values are passed through verbatim, preserving the data-is-not-schema invariant.
  - [x] Extended `buildEnumerationQuery` to emit `labels=a,b`, `tags=k:v,k:v`, `caseInsensitive=true`. `URLSearchParams` handles percent-encoding of the joined value.
- [x] **5.3 Harness — `sdk/js/tests/integration.ts`** — appended five filter cases (add with metadata, Labels AND, Tags AND, case-insensitive search, case-insensitive enumerate) for parity with the C# and Python harnesses. `tsc --noEmit` clean.

### Phase 6 — Dashboard ✅

- [x] **6.1 Shared types** — `dashboard/src/types/models.ts`: added `Labels`/`Tags`/`CaseInsensitive` to `SearchRequest` and `EnumerationQuery`; added `filteredCount` to `SearchResponse` and `EnumerationResult`.
- [x] **6.2 API client** — `dashboard/src/api/client.ts`: extended `buildEnumQueryString` to emit `labels=...`, `tags=k:v,k:v`, `caseInsensitive=true`. Request path uses `encodeURIComponent` on the joined value.
- [x] **6.3 Search page** — collapsible **Metadata filters** section with Labels (comma-separated), Tags (JSON object), and Case-insensitive checkbox. Filter-parse errors are surfaced via the existing error banner. `FilteredCount` is appended to the result header ("7 matches · 3 filtered out").
- [x] **6.4 Vectors page** — same three inputs in a collapsible section alongside existing GUID-prefix / include-vectors controls. Changing any filter resets `skip` to 0 to keep pagination consistent. `FilteredCount` is shown above the pagination footer when > 0.
- [x] **6.5 Result display** — `SearchResultsTable` already shows labels/tags (pre-existing). Vectors page table already shows labels; no changes needed. Empty-state messages kept generic.
- [x] **6.6 Build verification** — `npm run build` runs `tsc && vite build`. `tsc` regenerates `.js` twins beside each `.tsx` locally, but those files are gitignored via `dashboard/.gitignore` (`src/**/*.js`, `src/**/*.js.map`), so only the `.tsx` sources are committed. Dist build: 0 type errors, 324 kB bundle.

### Phase 7 — Documentation ✅

- [x] **7.1 README** — added a "New in v1.2.0" section and a "Filtering by labels and tags" subsection with JSON body + cURL query-string examples, plus the v1.2 query-string limitations (commas in labels, colons in tag keys, commas in tag values).
- [x] **7.2 SDK README** — `sdk/README.md` gained a "Filtering by labels and tags (v1.2+)" block showing the three-language parity (C# / Python / TS).
- [x] **7.3 CHANGELOG** — v1.2.0 entry describing server, SDK, and dashboard changes plus the 23 new test cases. Renamed prior "(current)" to "(previous)".

### Phase 8 — Docker / end-to-end

- [ ] **8.1 Integration smoke script** — **Deferred.** The existing SDK harnesses (C# `HnswLite.Sdk.Test`, Python `test_integration.py`) exercise the filter paths end-to-end against a live server; no separate docker smoke script added in this phase. Revisit if CI needs a container-only path.
- [x] **8.2 Test discovery confirmed** — `Test.Automated`, `Test.NUnit`, `Test.XUnit`, `Test.MSTest` all enumerate `HnswSuites.All`, which now includes `MetadataFilterSuites.All`. Full solution build: **0 errors** (2 pre-existing MSTest0001 warnings). Full run: **81 / 81 passing** under `Test.Automated` (net8.0).

### Phase 9 — Release

- [x] **9.1 Version bumps — all at `1.2.0`.**
  - csproj (`<Version>1.1.2</Version>` → `<Version>1.2.0</Version>`): `src/HnswIndex/HnswIndex.csproj`, `src/HnswIndex.RamStorage/HnswIndex.RamStorage.csproj`, `src/HnswIndex.SqliteStorage/HnswIndex.SqliteStorage.csproj`, `src/HnswIndex.Server/HnswIndex.Server.csproj`, `sdk/csharp/HnswLite.Sdk/HnswLite.Sdk.csproj`.
  - `sdk/python/pyproject.toml`: `1.1.0` → `1.2.0`.
  - `sdk/js/package.json`: `1.1.0` → `1.2.0`.
  - `dashboard/package.json`: `1.1.0` → `1.2.0`.
  - `docker/compose.yaml`: `jchristn77/hnswlite-server:v1.1.0` and `jchristn77/hnswlite-dashboard:v1.1.0` → `v1.2.0`.
  - Solution build after bumps: **0 errors** (2 pre-existing MSTest0001 warnings).
- [ ] **9.2 Regenerate XML docs** — `HnswIndex.xml` is build-generated; a fresh `dotnet pack` run will refresh it.
- [ ] **9.3 Tag & publish NuGet / PyPI / npm** — release-time step, not in scope of this PR.
- [ ] **9.4 Final sanity check** — spin up dashboard against a fresh container, exercise both filter paths.

---

## 6. Open questions / resolved

- [x] **Q1. Case sensitivity.** *Resolved:* caller-controlled via `CaseInsensitive` property (default `false`). Documented on both request objects and in README.
- [ ] **Q2.** Should `Tags` support `null` value (i.e. "key exists, any value")? *Default:* no — strict `key:value` equality; add an "exists" predicate in a future `TagQuery`.
- [ ] **Q3.** Should `EnumerationQuery` filters apply to the **index listing** endpoint (which reuses `FromQueryString`)? Indexes don't have labels/tags today. *Default:* accept-and-ignore for parsing uniformity; document.

## 7. Risk register

| Risk | Mitigation |
|---|---|
| Post-filter causes fewer-than-K results; callers surprised | `FilteredCount` makes this visible; document clearly in `SearchRequest` XML docs + README. |
| Tag value comparison surprises with numeric/bool stored as `object` | Use invariant-culture `Convert.ToString`; add a unit test with numeric tag. |
| Query-string delimiter collisions (label containing `,` or tag value containing `:`) | Document as a limitation in v1.2; future `TagQuery` body field removes the limitation. |
| Dashboard `.ts`/`.js` drift | Rebuild `.js` twins as part of Phase 6. |
