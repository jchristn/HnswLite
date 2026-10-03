# HnswLite Telemetry

HnswLite emits metrics and traces for everything an on-call engineer needs to answer two questions from Grafana alone: **where did the time go**, and **what failed**. The HTTP layer comes from Watson's built-in telemetry; everything behind the routes (the service layer, the HNSW graph operations and their stages, the storage backends, the startup reload job, inventory, and runtime health) is instrumented by HnswLite itself.

- The **libraries** (`HnswLite`, `HnswLite.SqliteStorage`, `HnswLite.PostgresqlStorage`) emit through the .NET base class library only (`System.Diagnostics.Metrics.Meter` and `System.Diagnostics.ActivitySource`). They take no telemetry dependency, cost nearly nothing when nobody listens, and never throw from instrumentation.
- The **server** (`HnswIndex.Server`) hosts one [Radiant](https://www.nuget.org/packages/Radiant) pipeline (package `Radiant` 0.1.2) at its composition root. It subscribes to every meter and activity source in the process, pushes traces over OTLP to Tempo, and serves an in-process Prometheus scrape endpoint.
- The **compose stack** (`docker/compose.yaml`) brings up Prometheus, Tempo, and Grafana alongside the server, with datasources and six domain dashboards provisioned as code.

Logs are not shipped to Loki. The server has no background workers (only a one-shot startup reload, which is fully traced and metered), so logs stay in the existing syslog/file pipeline. Every server log line written inside a request already runs with Watson's `Activity.Current`, so adding an OTLP log exporter later would correlate automatically.

---

## Contents

1. [Quick start](#quick-start)
2. [How the signals flow](#how-the-signals-flow)
3. [Meter and activity source names](#meter-and-activity-source-names)
4. [Subscribing from your own host (library consumers)](#subscribing-from-your-own-host-library-consumers)
5. [Server configuration](#server-configuration)
6. [Metrics catalog](#metrics-catalog)
7. [Spans catalog](#spans-catalog)
8. [Coverage map](#coverage-map)
9. [Dashboards](#dashboards)
10. [Recommended alerts](#recommended-alerts)
11. [Troubleshooting](#troubleshooting)
12. [Production and security notes](#production-and-security-notes)
13. [Testing the instrumentation](#testing-the-instrumentation)

---

## Quick start

```bash
cd docker
docker compose up -d --build
```

| Tool | URL | Default credentials |
| --- | --- | --- |
| Grafana | `http://localhost:3000` (dashboards in the **HnswLite** folder) | `admin` / `admin` |
| Prometheus | `http://localhost:9090` | none |
| Tempo API | `http://localhost:3200` (browse traces through Grafana) | none |
| HnswLite dashboard | `http://localhost:8081/dashboard/` (External services card on the home page) | API key from `hnswlite/hnswindex.json` |

Host ports can be overridden with `HNSWLITE_GRAFANA_PORT`, `HNSWLITE_PROMETHEUS_PORT`, `HNSWLITE_TEMPO_PORT`, `HNSWLITE_OTLP_GRPC_PORT`, and `HNSWLITE_OTLP_HTTP_PORT`. Start order is enforced with health-gated `depends_on`: Tempo becomes healthy before the server starts exporting, and Grafana waits for Prometheus and Tempo.

## How the signals flow

```
                       ┌──────────────────────── hnswlite-server process ───────────────────────┐
 HTTP request ───────► │ Watson  (meter + source "Watson")       request span, HTTP metrics     │
                       │   └─ IndexManager (meter + source "HnswLite.Server")                   │
                       │        operation span "hnswlite <op>", stage spans, service metrics    │
                       │        └─ HnswIndex (meter + source "HnswLite")                         │
                       │             "hnsw.<op>" span, stage spans, graph/search metrics         │
                       │             └─ Storage provider (same "HnswLite" meter/source)          │
                       │                  "<provider> <operation>" client spans, storage metrics │
                       │                  └─ Npgsql (meter "Npgsql": pool and command metrics)   │
                       │                                                                         │
                       │ RadiantHost: subscribes to all of the above + .NET runtime              │
                       │   ├─ OTLP gRPC push ──────────────────────────────────► Tempo :4317     │
                       │   └─ Prometheus scrape endpoint :9464 ◄──────────────── Prometheus      │
                       └─────────────────────────────────────────────────────────────────────────┘
                                                Grafana ──► Prometheus (uid prometheus), Tempo (uid tempo)
```

Because Watson sets `Activity.Current` for the life of each handler, every span HnswLite starts nests under Watson's request span. One trace runs from the HTTP request down to the storage call. A real PostgreSQL add-vector trace from the compose stack looks like this:

```
POST /v1.0/indexes/{name}/vectors        [Watson]            14.17 ms  Server
  hnswlite vector.add                    [HnswLite.Server]   13.18 ms
    stage:insert                         [HnswLite.Server]   12.31 ms
      hnsw.add                           [HnswLite]          12.30 ms
        stage:queued                     [HnswLite]           0.01 ms
        stage:transaction_begin          [HnswLite]           0.05 ms
          postgresql BeginTransaction    [HnswLite]           0.04 ms  Client
        stage:storage_write              [HnswLite]           1.52 ms
        stage:graph_insert               [HnswLite]          10.18 ms
        stage:flush                      [HnswLite]           0.01 ms
        stage:commit                     [HnswLite]           0.48 ms
    stage:metadata_write                 [HnswLite.Server]    0.85 ms
```

Inbound W3C `traceparent` headers are honored (Watson `PropagateContext = true`), so a caller's trace continues into HnswLite.

## Meter and activity source names

These strings are the contract between the emitters and any collector. They are defined once in code: library names in `Hnsw.HnswTelemetryNames` (`src/HnswIndex/HnswTelemetryNames.cs`) and server names in `HnswIndex.Server.Telemetry.ServerTelemetryNames` (`src/HnswIndex.Server/Telemetry/ServerTelemetryNames.cs`).

| Name | Kind | Emitted by | Covers |
| --- | --- | --- | --- |
| `HnswLite` | Meter and ActivitySource | `HnswLite`, `HnswLite.SqliteStorage`, `HnswLite.PostgresqlStorage` | Index operations and stages, write lock, search work, storage providers |
| `HnswLite.Server` | Meter and ActivitySource | `HnswIndex.Server` | Service-layer operations and stages, auth, API errors, reload job, inventory, build/config info |
| `Watson` | Meter and ActivitySource | Watson 7.1 | HTTP server metrics and the per-request span |
| `Npgsql` | Meter (and optional ActivitySource) | Npgsql 10 | PostgreSQL connection pool and command metrics |
| `hnswlite-server` | Meter | Radiant host | Process metrics (working set, uptime) |
| (runtime) | Meter | OpenTelemetry runtime instrumentation via Radiant | GC, heap, thread pool, JIT, exceptions, lock contention |

## Subscribing from your own host (library consumers)

The NuGet libraries never reference an exporter. Subscribe to `HnswLite` from your application's own pipeline.

With Radiant:

```csharp
using Hnsw;
using Radiant;

RadiantSettings settings = new RadiantSettings("my-service");
settings.Sources.AddMeter(HnswTelemetryNames.MeterName);               // "HnswLite"
settings.Sources.AddActivitySource(HnswTelemetryNames.ActivitySourceName); // "HnswLite"
settings.Sources.AddMeter("Npgsql");                                   // optional: PostgreSQL pool metrics

// Seconds-scale buckets for the duration histograms (see "Histogram buckets" below).
settings.Metrics.DefineAll(
    Convention.Histogram(HnswTelemetryNames.IndexOperationDuration, "s", HnswTelemetry.DurationBuckets),
    Convention.Histogram(HnswTelemetryNames.IndexStageDuration, "s", HnswTelemetry.DurationBuckets),
    Convention.Histogram(HnswTelemetryNames.StorageOperationDuration, "s", HnswTelemetry.DurationBuckets),
    Convention.Histogram(HnswTelemetryNames.SearchResults, "{result}", HnswTelemetry.CountBuckets),
    Convention.Histogram(HnswTelemetryNames.SearchNodesEvaluated, "{node}", HnswTelemetry.CountBuckets));

using (RadiantHost host = RadiantHost.Start(settings))
{
    // use HnswIndex as usual
}
```

With the OpenTelemetry SDK directly:

```csharp
Sdk.CreateMeterProviderBuilder().AddMeter("HnswLite").AddOtlpExporter().Build();
Sdk.CreateTracerProviderBuilder().AddSource("HnswLite").AddOtlpExporter().Build();
```

**Cost when unobserved.** Every record call checks `Instrument.Enabled` first, spans are only created when a listener samples the `HnswLite` source, and the storage providers skip timing entirely unless a listener is attached (`HnswTelemetry.IsStorageObserved`). Instrumentation failures are swallowed; they never surface to callers.

**Histogram buckets.** On .NET 9 and later the library passes bucket boundaries as instrument advice, so any OpenTelemetry host picks them up automatically. On .NET 8 the host must apply them as a view (as above); otherwise the SDK's default millisecond-oriented buckets put every duration in the first bucket. `HnswTelemetry.DurationBuckets` spans 100 µs to 120 s; `HnswTelemetry.CountBuckets` spans 0 to 100,000.

## Server configuration

Telemetry is configured in the `Telemetry` block of `hnswindex.json` (`HnswIndex.Server.Classes.TelemetrySettings`). Loopback defaults use `127.0.0.1`, never `localhost`.

| Key | Default | Description |
| --- | --- | --- |
| `Telemetry.Enable` | `true` | Master switch for the Radiant host and Watson telemetry. When `false` nothing is exported (emission stays a no-op). |
| `Telemetry.ServiceName` | `hnswlite-server` | `service.name` on every span and metric. |
| `Telemetry.OtlpEnable` | `true` | Push traces (and metrics) over OTLP. |
| `Telemetry.OtlpEndpoint` | `http://127.0.0.1:4317` | Collector endpoint. Compose uses `http://hnswlite-tempo:4317`. |
| `Telemetry.OtlpProtocol` | `grpc` | `grpc` (port 4317) or `httpprotobuf` (port 4318). |
| `Telemetry.PrometheusEnable` | `true` | Serve the Prometheus scrape endpoint for every subscribed meter. |
| `Telemetry.PrometheusHostname` | `127.0.0.1` | Bind host. In compose this is `hnswlite-server`, the container's DNS name. Wildcards are not accepted by Radiant 0.1.2 (see [Troubleshooting](#troubleshooting)). |
| `Telemetry.PrometheusPort` | `9464` | Scrape port (1..65535). Not published to the host in compose. |
| `Telemetry.TraceSamplingRatio` | `1.0` | Parent-based head sampling, 0.0..1.0. |
| `Telemetry.IncludeRuntimeMetrics` | `true` | .NET runtime and process metrics. |
| `Telemetry.TraceDatabaseCommands` | `false` | Also subscribe to Npgsql's per-command spans. Off by default because an index build issues many small commands; HnswLite's own storage spans and metrics already attribute PostgreSQL time. Npgsql pool metrics are always collected. |

Watson's built-in telemetry is set explicitly at startup: `Settings.Telemetry.Enable` follows `Telemetry.Enable`, and `EnableMetrics`, `EnableTraces`, and `PropagateContext` are `true`. Watson's own in-process `/metrics` endpoint stays off because the Radiant endpoint already serves Watson's meter along with the rest. Watson's HTTP metrics and request span are not duplicated by HnswLite.

If the Radiant host fails to start (for example the scrape port is taken), the server logs a warning that names the root cause and keeps serving without exported telemetry.

## Metrics catalog

Prometheus names are what the exporter produces: dots become underscores, counters gain `_total`, units in seconds gain `_seconds`, histograms expose `_bucket`/`_sum`/`_count`, and brace units such as `{operation}` are dropped. Labels are low-cardinality by construction: index names, vector IDs, and free text never appear on a metric (they go on spans).

Common label values:

- `hnswlite.outcome` (`hnswlite_outcome`): `success`, `error`, `cancelled`.
- `error.type` (`error_type`): full exception type name, present only when the outcome is not `success`. It is bounded by the set of exception types in the code.
- `hnswlite.storage.type` (`hnswlite_storage_type`): `postgresql`, `sqlite`, `ram`, `unknown`.

### Library: meter `HnswLite`

| Instrument (Prometheus name) | Type | Unit | Labels | Description |
| --- | --- | --- | --- | --- |
| `hnswlite.index.operation.duration` (`hnswlite_index_operation_duration_seconds`) | Histogram | s | `hnswlite.operation`, `hnswlite.outcome`, `error.type` | End-to-end duration of an index operation. |
| `hnswlite.index.operations` (`hnswlite_index_operations_total`) | Counter | {operation} | `hnswlite.operation`, `hnswlite.outcome`, `error.type` | Index operations by outcome. |
| `hnswlite.index.stage.duration` (`hnswlite_index_stage_duration_seconds`) | Histogram | s | `hnswlite.operation`, `hnswlite.stage`, `hnswlite.outcome` | Per-stage duration, including `queued` (waiting for the write lock). Its `_count` is the per-stage counter. |
| `hnswlite.index.vectors` (`hnswlite_index_vectors_total`) | Counter | {vector} | `hnswlite.operation` | Vectors added, removed, or imported (successful operations only). |
| `hnswlite.index.lock.waiting` (`hnswlite_index_lock_waiting`) | UpDownCounter | {operation} | `hnswlite.operation` | Write operations waiting for an index's write lock. |
| `hnswlite.index.lock.held` (`hnswlite_index_lock_held`) | UpDownCounter | {operation} | `hnswlite.operation` | Write operations holding a write lock (capacity is one per index). |
| `hnswlite.search.results` (`hnswlite_search_results`) | Histogram | {result} | none | Results returned by `GetTopKAsync`. |
| `hnswlite.search.nodes.evaluated` (`hnswlite_search_nodes_evaluated`) | Histogram | {node} | none | Distance evaluations per search: the work per query. |
| `hnswlite.search.cache.requests` (`hnswlite_search_cache_requests_total`) | Counter | {request} | `hnswlite.cache.result` (`hit`, `miss`) | Node lookups against the per-search `SearchContext` cache. |
| `hnswlite.storage.operation.duration` (`hnswlite_storage_operation_duration_seconds`) | Histogram | s | `hnswlite.storage.provider`, `hnswlite.storage.operation`, `hnswlite.outcome`, `error.type` | Duration of a storage call. |
| `hnswlite.storage.operations` (`hnswlite_storage_operations_total`) | Counter | {operation} | same as above | Storage calls by provider, operation, and outcome. |
| `hnswlite.storage.transactions` (`hnswlite_storage_transactions_total`) | Counter | {transaction} | `hnswlite.storage.provider`, `hnswlite.transaction.outcome` (`committed`, `rolled_back`, `failed`) | Storage transactions by outcome (PostgreSQL; SQLite uses no explicit transactions). |

`hnswlite.operation` values: `add`, `add_batch`, `remove`, `remove_batch`, `search`, `export`, `import`.

`hnswlite.stage` values by operation:

| Operation | Stages, in order |
| --- | --- |
| `add`, `add_batch` | `queued`, `transaction_begin`, `storage_write`, `graph_insert`, `flush`, `commit` (`rollback` on failure) |
| `remove` | `queued`, `transaction_begin`, `graph_unlink`, `entry_point_update`, `flush`, `commit` (`rollback`) |
| `remove_batch` | `queued`, `transaction_begin`, `graph_unlink`, `entry_point_update`, `graph_repair`, `flush`, `commit` (`rollback`) |
| `search` | `entry_point`, `greedy_descent`, `layer0_search`, `result_build` |
| `export` | `storage_read` |
| `import` | `queued`, `transaction_begin`, `clear`, `storage_write`, `graph_insert`, `flush`, `commit` (`rollback`) |

`hnswlite.storage.provider` values: `sqlite`, `postgresql`. The in-memory provider is not instrumented: it does no I/O, and its time is fully visible in the index stage metrics.

`hnswlite.storage.operation` values (code-defined, bounded):

- **SQLite** records every provider call: `Open`, `AddNode`, `AddNodes`, `RemoveNode`, `RemoveNodes`, `GetNode`, `GetNodes`, `TryGetNode`, `GetAllNodeIds`, `GetCount`, `GetEntryPoint`, `SetEntryPoint`, `GetNodeLayer`, `SetNodeLayer`, `RemoveNodeLayer`, `GetAllNodeLayers`, `ClearLayers`, `GetLayerCount`, `Flush`, plus the node-level writes `SaveNeighbors` and `SaveMetadata`.
- **PostgreSQL** records database round-trips (cached reads are not counted): `EnsureSchema`, `EnsureIndex`, `ListIndexes`, `BeginTransaction`, `Commit`, `Rollback`, and each command-issuing method (`AddNode`, `AddNodes`, `RemoveNode`, `RemoveNodes`, `GetNodes`, `LoadNeighbors`, `GetAllNodeIds`, `GetCount`, `GetEntryPoint`, `SetEntryPoint`, `GetNodeLayer`, `SetNodeLayer`, `RemoveNodeLayer`, `GetAllNodeLayers`, `ClearLayers`, `Clear`, `UpsertNeighbor`, `DeleteNeighbor`, `UpdateMetadata`).

### Server: meter `HnswLite.Server`

| Instrument (Prometheus name) | Type | Unit | Labels | Description |
| --- | --- | --- | --- | --- |
| `hnswlite.server.operation.duration` (`hnswlite_server_operation_duration_seconds`) | Histogram | s | `hnswlite.operation`, `hnswlite.storage.type`, `hnswlite.outcome`, `error.type` | Duration of a service operation behind an API route. |
| `hnswlite.server.operations` (`hnswlite_server_operations_total`) | Counter | {operation} | same as above | Service operations by outcome. |
| `hnswlite.server.stage.duration` (`hnswlite_server_stage_duration_seconds`) | Histogram | s | `hnswlite.operation`, `hnswlite.stage`, `hnswlite.outcome` | Per-stage duration of a service operation. |
| `hnswlite.server.search.results` (`hnswlite_server_search_results`) | Histogram | {result} | none | Results returned to the caller after metadata filtering. |
| `hnswlite.server.search.filtered` (`hnswlite_server_search_filtered_total`) | Counter | {result} | none | Candidates removed by label/tag filters. |
| `hnswlite.server.indexes` (`hnswlite_server_indexes`) | Gauge | {index} | `hnswlite.storage.type` | Indexes currently loaded. |
| `hnswlite.server.vectors` (`hnswlite_server_vectors`) | Gauge | {vector} | `hnswlite.storage.type` | Vectors held by loaded indexes. |
| `hnswlite.server.auth.requests` (`hnswlite_server_auth_requests_total`) | Counter | {request} | `hnswlite.auth.result` (`success`, `missing_key`, `invalid_key`, `disabled`) | API key authentication decisions. |
| `hnswlite.server.api.errors` (`hnswlite_server_api_errors_total`) | Counter | {error} | `hnswlite.api.error` (an `ApiErrorEnum` name) | API error responses by error code. |
| `hnswlite.server.reload.jobs` (`hnswlite_server_reload_jobs_total`) | Counter | {job} | `hnswlite.outcome` | Startup index-reload jobs. |
| `hnswlite.server.reload.duration` (`hnswlite_server_reload_duration_seconds`) | Histogram | s | `hnswlite.outcome` | Reload job duration. |
| `hnswlite.server.reload.stage.duration` (`hnswlite_server_reload_stage_duration_seconds`) | Histogram | s | `hnswlite.stage` (`sqlite`, `postgresql`), `hnswlite.outcome` | Reload stage duration. |
| `hnswlite.server.reload.stages` (`hnswlite_server_reload_stages_total`) | Counter | {stage} | `hnswlite.stage`, `hnswlite.outcome` | Reload stage executions. |
| `hnswlite.server.reload.indexes` (`hnswlite_server_reload_indexes_total`) | Counter | {index} | `hnswlite.storage.type`, `hnswlite.reload.result` (`loaded`, `skipped`, `failed`) | Indexes processed by the reload job. |
| `hnswlite.server.reload.last_success` (`hnswlite_server_reload_last_success_seconds`) | Gauge | s | none | Unix time of the last successful reload job, 0 when none. |
| `hnswlite.server.build.info` (`hnswlite_server_build_info`) | Gauge | {info} | `hnswlite.version`, `dotnet.runtime` | Always 1; carries the version. |
| `hnswlite.server.config.info` (`hnswlite_server_config_info`) | Gauge | {info} | `hnswlite.config.default_storage_type`, `hnswlite.config.require_authentication`, `hnswlite.config.cors_enabled`, `hnswlite.config.otlp_enabled`, `hnswlite.config.prometheus_enabled` | Always 1; carries safe configuration only (no keys or connection strings). |

Server `hnswlite.operation` values and their stages:

| Operation | Stages |
| --- | --- |
| `index.create` | `storage_open`, `metadata_persist` (SQLite) |
| `index.get`, `index.list` | none (in-memory lookups) |
| `index.delete` | `dispose` |
| `vector.add` | `insert`, `metadata_write` (when metadata is supplied) |
| `vector.add_batch` | `insert`, `metadata_write` |
| `vector.get` | `node_fetch` |
| `vector.list` | `enumerate_ids`, `filter` (when filtering), `metadata_fetch` |
| `vector.remove` | `remove` |
| `search` | `topk`, `metadata_fetch`, `filter` |

`index.list` carries `hnswlite.storage.type="unknown"` because it spans every index.

### Watson (HTTP layer, reference)

Watson 7.1 emits these on meter `Watson`; HnswLite does not duplicate them. The full list is in Watson's `TELEMETRY.md`.

| Prometheus name | Type | Key labels |
| --- | --- | --- |
| `http_server_request_duration_seconds` | Histogram | `http_request_method`, `http_response_status_code`, `http_route` (template) |
| `http_server_active_requests` | UpDownCounter | `http_request_method`, `url_scheme` |
| `http_server_request_body_size_bytes`, `http_server_response_body_size_bytes` | Histogram | method, status |
| `watson_server_up`, `watson_server_uptime_seconds` | Gauge | none |
| `watson_server_connections_active`, `watson_server_connections_total` | Gauge / Counter | protocol version |
| `watson_server_exceptions_total` | Counter | `error_type` |
| `watson_route_matches_total`, `watson_route_unmatched_total` | Counter | route type/route, method |
| `watson_auth_requests_total` | Counter | auth mode/results |
| `watson_server_received_bytes_total`, `watson_server_sent_bytes_total` | Counter | none |

### Npgsql (PostgreSQL pool)

Collected from meter `Npgsql` (Npgsql 10): `db_client_connection_count` (by `db_client_connection_state`: `idle`, `used`), `db_client_connection_max`, `db_client_operation_npgsql_executing`, `db_client_operation_duration_seconds`, `db_client_connection_npgsql_create_time_seconds`, and `db_client_operation_npgsql_bytes_read_bytes_total` / `_written_bytes_total`. The `db_client_connection_pool_name` label is the connection string with the password removed by Npgsql.

### Runtime and process

From the Radiant host on .NET 8: `process_runtime_dotnet_gc_*`, `process_runtime_dotnet_thread_pool_*`, `process_runtime_dotnet_exceptions_count_total`, `process_runtime_dotnet_monitor_lock_contention_count_total`, `process_runtime_dotnet_jit_*`, plus `process_memory_usage_bytes`, `process_thread_count`, and `process_uptime_seconds`. On .NET 9+ hosts the runtime metrics use the `dotnet_*` names; the Runtime dashboard queries both.

## Spans catalog

| Span name | Source | Kind | Parent | Key attributes | Status |
| --- | --- | --- | --- | --- | --- |
| `{METHOD} {route}` | `Watson` | Server | inbound `traceparent` or root | `http.request.method`, `http.route`, `http.response.status_code`, client address, user agent; `hnswlite.api.error` when an API error is returned | Error on 5xx |
| `hnswlite {operation}` | `HnswLite.Server` | Internal | Watson request span | `hnswlite.operation`, `hnswlite.storage.type`, `hnswlite.index.name`, `hnswlite.vector.id`, `hnswlite.batch.size`, `hnswlite.result.count`, `hnswlite.filtered.count` | Ok, or Error with `error.type` and an `exception` event |
| `stage:{stage}` | `HnswLite.Server` | Internal | `hnswlite {operation}` | `hnswlite.stage` | Ok / Error |
| `job:index_reload` | `HnswLite.Server` | Internal | root (startup) | `hnswlite.result.count` | Ok / Error |
| `stage:sqlite`, `stage:postgresql` | `HnswLite.Server` | Internal | `job:index_reload` | `hnswlite.stage`, `hnswlite.reload.loaded` | Ok / Error |
| `hnsw.{operation}` | `HnswLite` | Internal | caller's span (for example `stage:topk`) | `hnswlite.operation`, `hnswlite.vector.dimension`, `hnswlite.distance_function`, `hnswlite.batch.size`, `hnswlite.node.layer`, `hnswlite.search.k`, `hnswlite.search.ef`, `hnswlite.search.results`, `hnswlite.search.nodes_evaluated` | Ok / Error |
| `stage:{stage}` | `HnswLite` | Internal | `hnsw.{operation}` | `hnswlite.stage` | Ok / Error |
| `{provider} {operation}` (for example `postgresql AddNodes`, `sqlite Open`) | `HnswLite` | Client | current stage span | `db.system.name`, `db.operation.name`, `hnswlite.storage.provider`, `hnswlite.storage.operation` | Ok / Error |

Storage client spans are emitted only for coarse operations: open/schema/index, batch writes and removals, full-table reads, transactions, flush, and clear. Per-node reads and writes (`GetNode`, `SaveNeighbors`, `UpsertNeighbor`, ...) are metrics-only, because an insert performs hundreds of them and spans would flood the trace. Their time is still visible in the enclosing stage span and in `hnswlite_storage_operation_duration_seconds`.

Failures set the span status to Error, add the `error.type` attribute, and attach an `exception` event (type, message, stack). Spans carry identifiers such as index names and vector IDs; metrics never do. No vector values, metadata payloads, API keys, or connection strings are recorded anywhere.

## Coverage map

| Area | Where | Metrics | Spans |
| --- | --- | --- | --- |
| HTTP API (11 routes) | `HnswIndexServer.cs`, `API/REST/RestServiceHandler.cs` | Watson HTTP metrics, `hnswlite.server.api.errors` | Watson request span |
| Authentication | `HnswIndexServer.AuthenticateRequestHandler` | `hnswlite.server.auth.requests` | Watson span |
| Service layer: index create/get/list/delete, vector add/batch/get/list/remove, search | `Services/IndexManager.cs` | `hnswlite.server.operation*`, `hnswlite.server.stage.duration`, search results/filtered | `hnswlite {op}` + `stage:*` |
| Library: add, add_batch, remove, remove_batch, search, export, import | `HnswIndex/HsnwIndex.cs` | `hnswlite.index.operation*`, `hnswlite.index.stage.duration`, `hnswlite.index.vectors` | `hnsw.{op}` + `stage:*` |
| Concurrency slot (per-index write lock) | `HnswIndex._IndexLock` | `hnswlite.index.lock.waiting/held`, `queued` stage | `stage:queued` |
| Cache (per-search node cache) | `HnswIndex/SearchContext.cs` | `hnswlite.search.cache.requests`, `hnswlite.search.nodes.evaluated` | attributes on `hnsw.search` |
| Outbound: SQLite | `HnswIndex.SqliteStorage` | `hnswlite.storage.*` (`sqlite`) | `sqlite {op}` |
| Outbound: PostgreSQL | `HnswIndex.PostgresqlStorage`, Npgsql | `hnswlite.storage.*` (`postgresql`), `hnswlite.storage.transactions`, Npgsql pool | `postgresql {op}` (optional Npgsql command spans) |
| Startup reload job | `IndexManager.ReloadPersistedIndexesAsync` | `hnswlite.server.reload.*` | `job:index_reload` + `stage:*` |
| Inventory | `IndexManager.GetInventory` | `hnswlite.server.indexes`, `hnswlite.server.vectors` | n/a |
| Lifecycle and config | `ServerTelemetry` | `hnswlite.server.build.info`, `hnswlite.server.config.info`, `watson_server_up/uptime` | n/a |
| Runtime | Radiant host | .NET runtime and process metrics | n/a |

## Dashboards

Dashboards live in `assets/grafana/` and are provisioned into the Grafana folder **HnswLite** by `docker/grafana/provisioning/dashboards/hnswlite-dashboards.yaml`. Datasources have the stable UIDs `prometheus` and `tempo` (`docker/grafana/provisioning/datasources/hnswlite-datasources.yaml`). Every dashboard carries a link menu to the others.

| Dashboard (uid) | Use it to answer | Main panels |
| --- | --- | --- |
| **Overview** (`hnswlite-overview`) | Is it healthy, and what changed? Start here. | Up, uptime, request rate, 5xx ratio, indexes, vectors, search/add p95, operation error ratio, version, last reload, lock waiters; requests by status; service operations and errors; p95 by operation; recent and failed traces (Tempo) |
| **HTTP** (`hnswlite-http`) | Which endpoint is slow or failing? | Rate by route, status classes, p50/p95/p99, p95 by route, active requests, unmatched routes and exceptions, auth decisions, API errors by code, connections, throughput |
| **Index Operations** (`hnswlite-index`) | Which graph stage is slow? Are writes queuing? | Operations by outcome, p95 by operation, errors by type, vector throughput, p95 and time share by stage (`$operation` variable), stage failures, lock waiters/holders, queued p95, service write path by stage and storage type |
| **Search** (`hnswlite-search`) | Why is search slow? | Rate, p95, error ratio, cache hit ratio, latency quantiles, p95 by storage type, service and graph stage p95, nodes evaluated, results, cache lookups, filtered results, slowest search traces |
| **Storage & Integrations** (`hnswlite-storage`) | Is the database the cause? | Calls by provider/outcome, errors by operation and type, top p95 by operation, time per operation, transactions, Npgsql connections by state vs max, executing commands, command latency, create time and bytes, storage open/persist stages, inventory by storage type |
| **Runtime & Lifecycle** (`hnswlite-runtime`) | Did startup succeed? Is the process healthy? | Build and config info, reload jobs/last success/duration, indexes reloaded by result, reload stages, memory, GC, thread pool, exceptions, allocation rate, lock contention |

The product dashboard's home page has an **External services** card listing Grafana, Prometheus, and Tempo with copyable URLs, default credentials, and a reachability check. Override its URLs at dashboard build time with `HNSWLITE_GRAFANA_URL`, `HNSWLITE_PROMETHEUS_URL`, and `HNSWLITE_TEMPO_URL`.

## Recommended alerts

```yaml
groups:
  - name: hnswlite
    rules:
      - alert: HnswLiteDown
        expr: absent(watson_server_up == 1) or up{job="hnswlite-server"} == 0
        for: 1m
        labels: { severity: critical }
        annotations: { summary: "HnswLite server is down or not being scraped" }

      - alert: HnswLiteHttp5xxRatioHigh
        expr: |
          sum(rate(http_server_request_duration_seconds_count{http_response_status_code=~"5.."}[5m]))
            / clamp_min(sum(rate(http_server_request_duration_seconds_count[5m])), 1e-9) > 0.05
        for: 5m
        labels: { severity: warning }

      - alert: HnswLiteSearchLatencyHigh
        expr: |
          histogram_quantile(0.95, sum by (le) (rate(hnswlite_server_operation_duration_seconds_bucket{hnswlite_operation="search"}[5m]))) > 0.5
        for: 10m
        labels: { severity: warning }
        annotations: { summary: "Search p95 above 500 ms; check the Search dashboard stage panels" }

      - alert: HnswLiteOperationErrors
        expr: sum by (hnswlite_operation, error_type) (rate(hnswlite_server_operations_total{hnswlite_outcome="error"}[5m])) > 0.1
        for: 5m
        labels: { severity: warning }

      - alert: HnswLiteStorageErrors
        expr: sum by (hnswlite_storage_provider, hnswlite_storage_operation, error_type) (rate(hnswlite_storage_operations_total{hnswlite_outcome="error"}[5m])) > 0
        for: 5m
        labels: { severity: warning }

      - alert: HnswLiteWriteLockContention
        expr: sum(hnswlite_index_lock_waiting) > 5
        for: 5m
        labels: { severity: warning }
        annotations: { summary: "Writes are queuing behind the per-index write lock" }

      - alert: HnswLiteStorageTransactionsFailing
        expr: sum(rate(hnswlite_storage_transactions_total{hnswlite_transaction_outcome=~"rolled_back|failed"}[10m])) > 0
        for: 10m
        labels: { severity: warning }

      - alert: HnswLitePostgresPoolSaturated
        expr: sum(db_client_connection_count{db_client_connection_state="used"}) / clamp_min(sum(db_client_connection_max), 1) > 0.9
        for: 5m
        labels: { severity: warning }

      - alert: HnswLiteReloadFailed
        expr: increase(hnswlite_server_reload_jobs_total{hnswlite_outcome!="success"}[1h]) > 0 or increase(hnswlite_server_reload_indexes_total{hnswlite_reload_result="failed"}[1h]) > 0
        labels: { severity: warning }
        annotations: { summary: "Startup reload failed or skipped an index; check Runtime & Lifecycle" }

      - alert: HnswLiteAuthFailuresSpike
        expr: sum(rate(hnswlite_server_auth_requests_total{hnswlite_auth_result=~"invalid_key|missing_key"}[5m])) > 1
        for: 10m
        labels: { severity: info }
```

## Troubleshooting

- **Prometheus target down with `unit "bytes" not a suffix of metric "watson.server.received.bytes"`.** Prometheus 3 negotiates OpenMetrics with UTF-8 names, and the OpenTelemetry exporter then emits dotted names that fail Prometheus' unit validation. `docker/prometheus.yaml` sets `metric_name_validation_scheme: legacy` globally, and on the job `scrape_protocols: ['PrometheusText0.0.4']` with `metric_name_escaping_scheme: underscores`. Keep those settings in any Prometheus that scrapes HnswLite.
- **`telemetry disabled (start failed) ... Cause: UriFormatException`.** Radiant 0.1.2 rejects `*` and `+` as `PrometheusHostname` (and `0.0.0.0` fails with `HttpListenerException`), even though Radiant's README describes wildcards. Bind a concrete name instead: the container's DNS name in compose, or `127.0.0.1` on a host.
- **The scrape endpoint is unreachable from the host in compose.** This is intentional: port 9464 is not published. Use Prometheus (`http://localhost:9090`) or `docker compose exec hnswlite-prometheus wget -qO- http://hnswlite-server:9464/metrics`.
- **Tempo receives metric pushes it does not accept.** Radiant 0.1.2 has one OTLP exporter setting for both traces and metrics, so with `OtlpEnable` the server also pushes metrics to Tempo every 15 s. Tempo rejects them and nothing is lost: Prometheus scrapes the same metrics. If the noise matters, point `OtlpEndpoint` at an OpenTelemetry Collector that routes traces to Tempo.
- **Every duration lands in the first bucket.** The host did not apply the seconds-scale buckets. See [Histogram buckets](#subscribing-from-your-own-host-library-consumers).

## Production and security notes

- Change Grafana's admin password for any non-local deployment: set `GRAFANA_ADMIN_PASSWORD` (and optionally `GRAFANA_ADMIN_USER`) in the environment or an untracked `.env`. Sign-up is disabled (`GF_USERS_ALLOW_SIGN_UP=false`). Never commit a real password.
- Do not publish Prometheus, Tempo, or the server's 9464 scrape endpoint on a public interface. None of them has authentication.
- Labels are bounded by construction, and identifiers, payloads, keys, and connection strings never appear on metrics. Spans carry index names and vector IDs for investigation; treat trace storage as operational data.
- Leave Watson's forwarded-header trust off unless the server sits behind a known proxy (see Watson's `TELEMETRY.md`).
- Sampling: lower `Telemetry.TraceSamplingRatio` for very high write rates. Metrics are unaffected by sampling.

## Testing the instrumentation

`src/Test.Shared/TelemetrySuites.cs` proves the telemetry with in-memory BCL listeners (`TelemetryCapture`: a `MeterListener` plus an `ActivityListener`, scoped to a root test span). No collector is needed. The suites run under the console runner and the xUnit, NUnit, and MSTest adapters:

- **Library**: stable source names; operation, stage (including `queued`), vector, lock, and search metrics; span nesting; batch, export, and import; failure outcomes with `error.type`, error span status, and exception events; cancellation; and the no-listener path.
- **Storage**: SQLite per-operation metrics and client spans, an SQLite open failure, and PostgreSQL storage/transaction/schema telemetry (runs when `HNSWLITE_TEST_POSTGRES_CONNECTION` is set).
- **Server**: every service operation and stage, the library span nesting under the server span, failure paths, the reload job (job/stage spans, counters, last-success gauge, a corrupt index counted as failed), inventory/build/config gauges without secrets, auth and API error counters, and the Radiant host (subscriptions, bucket views, a live Prometheus scrape, a disabled host, and a start failure that does not throw).
