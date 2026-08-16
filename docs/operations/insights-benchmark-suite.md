# Insights benchmark suite operations

**Status:** Phase 4 controlled-measurement runbook. The suite is
non-authoritative; Phase 5 UI and Phase 6 calibration/baseline promotion are
not part of these commands.

**Artifact contract:** `insights-run-export-v1`

This runbook operates the integrated PostgreSQL, REST, browser, and isolated
algorithm journeys through the single Phase 4 benchmark runner. The executable
profile and scenario registries remain the source of truth. Review them before
each run:

```bash
dotnet run --project backend.BenchmarkRunner -c Release -- list --profile quick
dotnet run --project backend.BenchmarkRunner -c Release -- list --profile standard
dotnet run --project backend.BenchmarkRunner -c Release -- list --profile cold
dotnet run --project backend.BenchmarkRunner -c Release -- list --profile authoritative
```

`list` is read-only. It prints deterministic scenario order and identifies
each runnable or structured-skip case. Do not replace a registered skip with an
unrecorded omission.

Before running, use the repository's .NET SDK and `.nvmrc` Node version, start
a disposable local PostgreSQL instance, install the locked frontend packages,
and install the matching Playwright Chromium binary:

```bash
npm --prefix frontend ci
cd frontend
npx playwright install chromium
cd ..
dotnet build reasoning-graph-insights-engine.sln -c Release
```

Keep the API, static Storybook server, and runner at the same source revision
and Release build. The manifests record the actual runtime, browser, GraphMap,
PostgreSQL, source revision, build, host, and environment identities; do not
edit those values into apparent compatibility after a run.

## 1. Profiles

| Profile | Recorded iterations | Process/cache contract | Intended use |
| --- | --- | --- | --- |
| `quick` | No warmup; one measured iteration | Warm run-level mode with uncontrolled shared services. A browser or isolated scenario still launches its required child process, and those child-owned phase rows disclose that fresh-process state. | Bounded local correctness and end-to-end smoke work |
| `standard` | One recorded warmup, then three recorded measured iterations | Warm run-level mode. The runner, API, PostgreSQL, filesystem, and OS caches are shared across iterations. Each browser iteration launches fresh Node and Chromium processes; each isolated algorithm iteration launches a fresh .NET worker, and those child-owned phase rows are classified cold. | Repeatable local development evidence |
| `cold` | No warmup; one measured iteration | Cold child process only. Every runnable case starts either a fresh isolated .NET worker or fresh Node and Chromium processes. The static production-profiling Storybook HTTP server process and its serving/cache state stay shared; the profile also does **not** restart the runner or API, reset PostgreSQL state, or clear PostgreSQL, filesystem, or OS caches. | Explicit child-process/JIT startup evidence |
| `authoritative` | Zero | Configuration and validation only. Execution and baseline promotion are refused. | Phase 6 placeholder, not a runnable profile |

Setup, warmup, and measured rows remain separate in raw evidence. Warm and
cold populations must never be combined in one aggregate. Within one standard
scenario, every warmup and measured iteration has a distinct sample ID and its
own result/output identity; a later failure must not erase earlier samples or
outputs.

The profile's run-level `sampleMode` and each raw row's process/cache
classification answer different questions. A warm standard run can contain
cold browser- or worker-owned phase rows because those particular child
processes are fresh, while repository/service rows remain warm because their
processes and caches are shared. Setup and shared-runner quality-comparison
rows also stay warm when their state was not reset.

The word “cold” means only the child-process reset stated above. REST journeys
and graph-fetch/search browser journeys are deliberately registered as cold
skips because the API process, its connection-pool state, PostgreSQL caches,
and OS page cache are not reset. API-free result-render journeys may run cold
because the relevant Node and Chromium processes are newly launched, even
though the static production-profiling Storybook HTTP server and its
serving/cache state are shared and not restarted.

`authoritative` is intentionally non-executable. The safe check is:

```bash
dotnet run --project backend.BenchmarkRunner -c Release -- run --profile authoritative
```

It must refuse before dataset setup, benchmark-store mutation, or scenario
execution. Do not add `--install-datasets` or treat this refusal as a baseline.

## 2. Controlled local topology

Use a disposable local PostgreSQL database whose name contains `test`,
`benchmark`, or `disposable`. The suite has four long-lived processes and two
API addresses:

```text
benchmark runner
  |-- exact HTTP/2 (h2c) --> API REST endpoint --> disposable PostgreSQL
  |-- Node/Playwright --> Chromium --> HTTP/1 browser endpoint on the same API
  `-- isolated .NET workers

Chromium --> static Storybook production-profiling harness
```

The exact-HTTP/2 endpoint is required for the controlled REST client and its
late `Server-Timing` trailer. The browser-compatible endpoint is separate
because ordinary Chromium fetches do not use cleartext HTTP/2. Both endpoints
must address the same separately running API process and database.

### 2.1 Start the API

From the repository root, replace the sample connection string with a
disposable local database:

```bash
env \
  Database__ConnectionString='Host=127.0.0.1;Port=5432;Database=logiclikely_benchmark_disposable;Username=postgres;Password=replace-me' \
  Cors__AllowedOrigins__0='http://127.0.0.1:6006' \
  Kestrel__Endpoints__RestHarness__Url='http://127.0.0.1:5087' \
  Kestrel__Endpoints__RestHarness__Protocols='Http2' \
  Kestrel__Endpoints__BrowserHarness__Url='http://127.0.0.1:5088' \
  Kestrel__Endpoints__BrowserHarness__Protocols='Http1' \
  dotnet run --project backend/backend.csproj -c Release --no-launch-profile
```

The CORS origin must match the Storybook origin exactly. The policy permits
the correlation request headers and exposes the echoed correlation headers and
`Server-Timing`. Do not use a frontend proxy for controlled samples: the
browser must observe the real API resource boundary.

### 2.2 Build and serve the browser harness

Build the static Storybook from the exact source revision being measured:

```bash
npm --prefix frontend run build-storybook
python3 -m http.server 6006 --bind 127.0.0.1 --directory frontend/storybook-static
```

The controlled static build uses React's production profiling entry and emits
the exact identity `storybook-production-profiling`. The runner rejects a
successful controlled browser result without that identity. Development
Storybook identifies itself as `storybook-development`; it is useful for
debugging but its timings must not masquerade as production-profiling samples.
The harness bypasses Storybook MSW and fetches the configured absolute API
URL.

## 3. Run commands

Open another shell at the repository root. REST and graph-browser scenarios
require both the API application connection string and the runner's independent
PostgreSQL identity connection string. Dataset installation additionally
requires the explicit destructive opt-in.

### 3.1 Bounded quick smoke

Run a single representative integrated scenario first:

```bash
env \
  Database__ConnectionString='Host=127.0.0.1;Port=5432;Database=logiclikely_benchmark_disposable;Username=postgres;Password=replace-me' \
  LOGICLIKELY_BENCHMARK_POSTGRES_CONNECTION_STRING='Host=127.0.0.1;Port=5432;Database=logiclikely_benchmark_disposable;Username=postgres;Password=replace-me' \
  LOGICLIKELY_ALLOW_DESTRUCTIVE_POSTGRES_BENCHMARK=1 \
  dotnet run --project backend.BenchmarkRunner -c Release -- run \
    --profile quick \
    --scenario quick.browser.collapsed.balanced-1k \
    --api-base-url http://127.0.0.1:5087 \
    --browser-api-base-url http://127.0.0.1:5088 \
    --browser-harness-url http://127.0.0.1:6006 \
    --frontend-dir frontend \
    --install-datasets \
    --persist \
    --export-dir artifacts/insights-benchmarks/quick
```

Remove `--scenario` to execute the complete quick registry serially. Supply
both API addresses and the Storybook address for the integrated registry;
otherwise the affected REST/browser scenarios produce structured failures
rather than silently becoming in-memory measurements.

### 3.2 Standard suite or representative standard case

The complete standard matrix is deliberately large. Inspect it with `list`
and run one representative scenario before scheduling the whole profile:

```bash
env \
  Database__ConnectionString='Host=127.0.0.1;Port=5432;Database=logiclikely_benchmark_disposable;Username=postgres;Password=replace-me' \
  LOGICLIKELY_BENCHMARK_POSTGRES_CONNECTION_STRING='Host=127.0.0.1;Port=5432;Database=logiclikely_benchmark_disposable;Username=postgres;Password=replace-me' \
  LOGICLIKELY_ALLOW_DESTRUCTIVE_POSTGRES_BENCHMARK=1 \
  dotnet run --project backend.BenchmarkRunner -c Release -- run \
    --profile standard \
    --scenario standard.graph-fetch.balanced-1k.rest \
    --api-base-url http://127.0.0.1:5087 \
    --browser-api-base-url http://127.0.0.1:5088 \
    --browser-harness-url http://127.0.0.1:6006 \
    --frontend-dir frontend \
    --install-datasets \
    --setup-timeout 600 \
    --persist \
    --export-dir artifacts/insights-benchmarks/standard
```

Remove `--scenario` only when the full registered standard matrix is intended.
The profile timeout applies per operation iteration; `--setup-timeout` applies
to dataset installation and preparation. A bare duration is seconds.

### 3.3 Cold child-process case

Cold algorithm and result-render cases need no database setup. For example:

```bash
env \
  Database__ConnectionString='Host=127.0.0.1;Port=5432;Database=logiclikely_benchmark_disposable;Username=postgres;Password=replace-me' \
  dotnet run --project backend.BenchmarkRunner -c Release -- run \
    --profile cold \
    --scenario cold.strongest.balanced-1k.isolated-worker \
    --persist \
    --export-dir artifacts/insights-benchmarks/cold
```

Persistence still requires `Database__ConnectionString`. An API-free cold
result-render case additionally needs the static harness URL and frontend
directory. The runner starts fresh Node and Chromium processes for each such
iteration, but it reuses the already-running static production-profiling
Storybook HTTP server and that server's serving/cache state. The cold registry
retains REST and graph-browser cases as explicit skips; do not override them
into execution and call the result cold.

## 4. Dataset installation and reset truth

`--install-datasets` is destructive and is allowed only when all of these are
true:

- `LOGICLIKELY_ALLOW_DESTRUCTIVE_POSTGRES_BENCHMARK=1` is present;
- `--api-base-url` is a loopback URL; and
- `LOGICLIKELY_BENCHMARK_POSTGRES_CONNECTION_STRING` names a database containing
  `test`, `benchmark`, or `disposable`.

Those guards are necessary but not sufficient. Before it requests a reset, the
runner connects through `LOGICLIKELY_BENCHMARK_POSTGRES_CONNECTION_STRING` and
independently observes this PostgreSQL target tuple:

```text
[current_database, inet_server_addr, inet_server_port, UTC pg_postmaster_start_time]
```

The address and port use `local-socket` and `0` fallbacks, and the UTC start
time is formatted to microseconds. The runner first verifies that the parsed
connection-string database name equals the observed `current_database`. It
then hashes `postgres-reset-target-v1\n<jsonb tuple>` as UTF-8 and sends only
the expected name and lowercase opaque `sha256:` fingerprint in the reset
request. The API probes its own configured connection independently inside the
same transaction that would perform the reset. It compares both values before
executing any seed SQL; a mismatch rolls back with no seed command and returns
`409 database-reset-identity-mismatch`. Therefore a disposable-looking runner
connection string cannot authorize reset of a different database instance
behind the loopback API. Do not bypass, precompute, or reuse this handshake
across API/PostgreSQL restarts; the postmaster start time deliberately changes
its identity.

For each database-backed scenario, setup sends a correlated exact-HTTP/2
`POST /api/graphs/reset`. Catalog setup installs all 12 canonical stress
graphs; other scenarios install their selected graph. This setup occurs once
before that scenario's profile iterations and is recorded as setup, never as
graph-fetch or algorithm time.

The reset transaction drops and recreates only `public.graphs`,
`public.nodes`, and `public.edges`, then installs the ordinary seed graphs and
the requested deterministic stress graphs. It does not drop or truncate the
independent `benchmark` schema, so earlier persisted run history survives.

The reset does **not** restart the benchmark runner, API, or static
production-profiling Storybook HTTP server; reset the Storybook server's
serving/cache state; recycle API connection pools; restart PostgreSQL; clear
PostgreSQL shared buffers; evict PostgreSQL, graph, or Storybook files from the
filesystem cache; clear the OS page cache; or reset network/kernel state.
Standard REST results are therefore warm shared-service measurements. No
Phase 4 command establishes a service-cold or machine-cold baseline.

## 5. Scenario and skip policy

Quick registers 25 bounded entries: real catalog/fetch, existing
database-loaded and supplied-graph REST parity cases, collapsed/full/search
browser journeys, bounded result rendering, retained in-memory/isolated
algorithm cases, and four explicit unsupported/unsafe skips. It is the local
integrated smoke profile, not a reduced authoritative baseline.

The canonical data matrix contains balanced, wide, deep, and shared-diamond
graphs at 1K, 10K, and 100K nodes. Standard registers 159 scenarios, in
deterministic order:

- catalog retrieval with all canonical graphs and complete REST fetch for
  every graph;
- collapsed GraphMap presentation, no-hit search, and compact shallow search
  for every graph;
- complete GraphMap expansion for the designated `balanced-1k` graph only,
  with every other size/shape registered as
  `browser-full-expansion-designated-small-only`;
- large deep-chain searches that would materialize most of the graph as
  `browser-deep-search-materialization-unsafe` skips;
- strongest path, evidence-impact ranking, candidate-limited exact critical
  counter, greedy critical counter, both auto branch entries, node robustness,
  and likelihood recalculation for every graph. Safe non-deep
  strongest/evidence/greedy/likelihood cases run in the shared runner so the
  standard warmup establishes real post-JIT rows; their deep variants are
  isolated. Exact and auto work and all robustness cases are isolated.

All three wide-graph auto-greedy entries are registered as
`auto-greedy-bounded-wide-target-unavailable` skips. A wide leaf has no
eligible descendants, while the root exceeds the explicit tractable candidate
limit, so no honest bounded target can force auto to the greedy branch. The
corresponding exact, greedy, and auto-exact entries remain registered.

In total, standard has 143 runnable entries and 16 structured skips: 11 unsafe
full expansions, two large deep-chain materializing searches, and three
bounded-wide auto-greedy cases. Cold has five runnable fresh-child cases and
five shared-service cold skips.

Cold registers 10 cases: one fresh isolated strongest-path worker, four
API-free production-browser result renders, and five explicit REST/graph
browser skips whose shared API/PostgreSQL/OS state prevents an honest cold
classification.

Exact counter cases use an explicit candidate limit. The tractable balanced-1K
and shared-diamond-1K exact cases also retain an exact-versus-greedy quality comparison:
threshold attainment, selected cardinalities, optimality gap, set overlap,
below-threshold margins, evaluation counts, and both result digests. This
quality comparison runs in the shared runner process after the primary bounded
operation; it has its own directly instrumented
`benchmark-orchestration/exact-greedy-quality-comparison` phase. The evidence
is part of the compact output and survives persistence and portable export,
while canonical result items, result digest, and algorithm semantic identity
remain unchanged.

Unsafe or unsupported work remains a first-class terminal outcome:
`failed`, `timed-out`, `cancelled`, `crashed`, or `skipped` with stable failure
kind/code. A later scenario or iteration continues unless caller cancellation
stops the serial run. Prior completed samples and outputs remain exportable.

Browser scenarios are registry-locked: the CLI accepts timeout and persistence
controls but refuses dataset, parameter, or strategy overrides. This keeps the
manifest, browser action/query, bounded result preparation, and GraphMap
materialization-safety decision identical. For non-browser scenarios, a deep
dataset override recomputes worker-isolation requirements before execution;
an override cannot turn shape-hazardous deep work into an in-process
measurement.

## 6. Timing ownership and evidence

All durations are raw decimal milliseconds from an owning monotonic clock.
Each sample states one of the frozen provenance tokens; the suite does not
relabel an inferred boundary as an internal GraphMap or network measurement.

| Provenance | Phase examples |
| --- | --- |
| `directly-instrumented` | PostgreSQL repository scopes; service mapping, context, algorithm, shaping, and digest scopes; response serialization; explicit browser `JSON.parse`, domain mapping, GraphMap adapter wrapper; React Profiler commit interval; bounded result render; runner fixture, persistence, and export scopes |
| `externally-observed` | HTTP request-to-headers, full transfer, and byte counts; GraphMap node/edge DOM materialization and deferred edge completion; visible GraphMap search completion; Playwright action-to-stable-result/view; isolated-worker supervision |
| `estimated` | Consumer-observed Dagre/layout aggregate and viewport settling when GraphMap exposes no exact internal callback |

GraphMap 0.2.0 exposes no internal layout, search-index, or lifecycle callback.
The harness therefore records the existing visible search status, stable DOM
node/edge evidence, React commits, and stable viewport/edge geometry. It does
not emit a manufactured search-index boundary or duplicate GraphMap search.
Unexpected page errors and console errors fail the browser journey. There is
no blanket `ResizeObserver` suppression; any future exception must match one
reproduced, understood message exactly and remain disclosed in raw evidence.

### 6.1 HTTP/2 serialization trailer

Correlated API phases that finish before response headers are committed are
published in the `Server-Timing` header. Serialization and any other phases
that finish after commit are appended in the `Server-Timing` response trailer
when Kestrel and the exact HTTP/2 REST client support trailers. The runner must
observe that trailer rather than treating response-body write time as complete
network transfer.

The browser's HTTP/1 fetch endpoint does not expose response trailers to the
harness. Browser samples must not invent the late serialization phase. Use the
paired exact-HTTP/2 REST journey when server-side serialization evidence is
required; browser request-to-headers and full-transfer remain real
client-observed boundaries.

### 6.2 Resource Timing and CORS preflight

For graph fetch/search journeys, the harness reads
`PerformanceResourceTiming.nextHopProtocol` for the exact graph URL. It records
either a nonblank observed protocol or a nonblank
`resourceTimingLimitation`; it never infers HTTP/1 or HTTP/2 from the configured
URL. Cross-origin custom correlation headers cause a CORS preflight. Therefore
the browser action/request-to-headers interval may include preflight work; it
is not a pure application-request or server interval.

### 6.3 Reconciliation means overhead, not equality

Repository, service, serialization, transport, browser, and end-to-end phases
are inclusive measurements owned by different processes and clocks. They can
nest. Do not sum them, subtract them into an allegedly exact unmeasured phase,
or force their elapsed times to be equal.

Reconciliation requires exact run/sample correlation, scenario/operation
identity, dataset and parameter fingerprints, actual strategy, counts, result
digests, terminal status, and units where those fields apply. Timing deltas are
reported as overhead observations that can include scheduling, queueing,
connection handling, preflight, serialization, transfer, parsing, React work,
rendering, and observation lag. They are diagnostic, not exclusive phase
proofs.

## 7. Persistence and portable artifacts

With `--persist`, the runner explicitly initializes and writes
`benchmark.runs`, `benchmark.samples`, and `benchmark.outputs`, then reloads
the durable snapshot before producing the final export. Graph reset does not
own those tables. Setup, warmup, measured, partial failure, and terminal rows
remain visible. Compact outputs retain complete result identity and at most 100
ranked items; an optional external full-result artifact is separate.

With `--export-dir DIR`, each scenario writes one canonical JSON document as
`DIR/<run-id>.json`. The runner validates the `insights-run-export-v1` schema,
ordering, identities, section digests, and serialize/import round trip. A
persisted run is reloaded before final export, so the portable artifact reflects
the durable evidence, including all completed iterations.

The recommended local output root is:

```text
artifacts/insights-benchmarks/<profile>/<run-id>.json
```

That generated root and `frontend/storybook-static/` are ignored. Keep only
deliberately reviewed, sanitized evidence in a separately chosen tracked
location; do not commit disposable exports, browser build output, connection
strings, or machine-local paths.

## 8. Compatibility and interpretation

Default comparison is refused when scenario/profile, dataset input
fingerprint, operation semantic identity, canonical parameter digest, actual
behavior-changing strategy, environment class, sample mode, or measurement
units differ. Legacy free-form classifications remain separate incompatible
buckets rather than being guessed warm or cold.

Current v1 manifests explicitly carry `profileKey` and
`samplingPolicy.sampleMode`. A pre-Phase-4 v1 artifact that omitted one or both
members is accepted only after its original source manifest digest validates.
The reader then supplies `legacy-unspecified` for the missing identity, computes
a new manifest digest over that explicit normalized value, and keeps it in a
separate compatibility bucket. Database migration likewise preserves existing
runs, samples, and outputs while assigning conservative legacy identity; it
does not rewrite them as a current quick, standard, or cold population.

Deterministic equality applies to logical identity and results, not elapsed
time. Repeated compatible iterations should retain the same dataset,
parameter, algorithm, strategy, and result digests. Variation in raw durations
and resource observations is expected and remains unmodified.

## 9. Verification and CI policy

Before handing off a controlled local run, verify the relevant backend tests,
PostgreSQL integration tests, frontend unit/lint/production build, static
production-profiling Playwright harness, a bounded quick end-to-end case, and a
representative standard case. Record the exact commands and outcomes with the
review artifact; do not turn elapsed-time thresholds into ordinary pull-request
gates.

Performance work remains manual or explicitly scheduled, named,
artifact-producing, and informational. Ordinary CI continues to gate
correctness only. Phase 4 does not create `/lab/analyze` or
`/lab/performance`; those routes and the historical comparison UI are Phase 5.
The accepted 100K corpus, authoritative host run, auto-cutoff calibration, and
baseline promotion remain Phase 6 work.
