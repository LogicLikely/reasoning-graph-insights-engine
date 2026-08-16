# Insights Lab Phase 1 contracts

**Contract status:** Frozen for Phase 1; reconciled by Phase 3.5 and extended
with Phase 4 controlled-measurement evidence

**Contract family:** `insights-lab-v1`

**Scope:** Correlation, named phase measurement, reset-safe benchmark storage,
versioned run export, isolated-worker supervision, and controlled-run raw
measurement evidence

This record implements Phase 1 of the Insights Lab plan and records the Phase 4
controlled-run extensions that consume it. It preserves the Phase 0 operation,
algorithm, result, canonical JSON, and compatibility contracts. Phase 4 now
implements the CLI benchmark runner and controlled PostgreSQL, REST, browser,
and isolated-worker journeys. It does not implement Lab routes, calibration,
or an authoritative baseline. The operational contract is documented in
[Insights benchmark suite operations](../../operations/insights-benchmark-suite.md).

## 1. Correlation

The HTTP correlation headers are exactly:

- `X-Insights-Run-Id`
- `X-Insights-Sample-Id`

Both values are non-empty UUIDs in canonical hyphenated (`D`) form. Both
headers absent means an ambient request: no correlation context is created and
nothing is persisted. If either header is supplied, both must be supplied
exactly once and be valid; otherwise the request fails with a structured `400`
validation response. A valid pair is installed in a scoped accessor and echoed
on the response.

The run ID identifies the complete explicit run. The sample ID identifies one
iteration across layers, so multiple phase-measurement rows intentionally share
the same run and sample IDs. Correlation proves identity only. Supplying headers
does not authorize persistence and never turns ordinary graph navigation into a
benchmark run.

## 2. Common phase registry

Layer and phase strings are ordinal, versioned data rather than display copy.
The registry order is the canonical export order for otherwise equal samples.

| Layer | Frozen phases |
|---|---|
| `postgresql-repository` | `connection-open-wait`, `graph-lookup`, `node-query`, `edge-query`, `evidence-json-materialization`, `graph-construction`, `catalog-aggregation` |
| `backend-service-api` | `dto-mapping`, `validation`, `calculation-context-construction`, `algorithm`, `algorithm.<subphase>`, `ranking`, `result-shaping`, `digest-generation`, `serialization` |
| `benchmark-orchestration` | `fixture-construction`, `operation-execution`, `worker-supervision`, `exact-greedy-quality-comparison`, `persistence`, `export-validation` |
| `transport` | `response-bytes`, `time-to-first-byte`, `full-transfer` |
| `browser-data` | `axios-receipt-parse`, `json-parse`, `domain-mapping`, `graph-map-adapter`, `search-index-construction`, `search-completion` |
| `graph-map` | `node-edge-materialization`, `dagre-layout`, `react-commit`, `deferred-edge-commit`, `viewport-fit` |
| `lab-result` | `result-render`, `react-commit` |
| `end-to-end` | `action-to-stable-result-and-view` |

Algorithm subphases use lowercase dot-separated kebab-case tokens. Phase
durations use a monotonic clock, decimal milliseconds, and cannot be negative.
Recorder sequence is assigned when measurement begins, which preserves
deterministic invocation order for nested scopes. Durations are inclusive for
their own scope; cross-layer timings may nest and must not be added as though
they were mutually exclusive.

Every phase timing states where its time boundary came from. The exact
`timingBoundaryProvenance` tokens are `directly-instrumented`,
`externally-observed`, and `estimated`. A collector-owned monotonic scope is
directly instrumented. A duration supplied to the collector must explicitly
declare external observation or estimation (or direct instrumentation if the
caller owns a trustworthy monotonic boundary); the collector never guesses.
Approximate or reconstructed data is always `estimated`.

The backend measures repository fetch/catalog phases, graph and calculation
context construction, service DTO mapping, exposed analysis phases, canonical
digest generation, and MVC response serialization. A correlated response
exposes phases completed before headers are committed through `Server-Timing`;
trailer-capable controlled HTTP/2 journeys receive serialization and any other
late completed phases through the `Server-Timing` trailer. Response byte count,
true time-to-first-byte, and full transfer remain client-observed transport
measurements. The Phase 4 consumer harness implements the applicable browser
and GraphMap phases without changing GraphMap 0.2.0; unavailable GraphMap
internals remain unclaimed.

Phase 4 adds the neutral `json-parse` and `search-completion` browser seams.
The earlier `axios-receipt-parse` and `search-index-construction` tokens remain
readable for compatibility, but a fetch-based harness must not call explicit
`JSON.parse` work Axios receipt parsing, and GraphMap 0.2.0 exposes no internal
search-index boundary. Its visible search-status completion is therefore
externally observed as `search-completion`. Result-only React Profiler commits
use `lab-result/react-commit`; `graph-map/react-commit` remains specific to a
GraphMap consumer commit.

BenchmarkDotNet owns repeatable in-process operation measurements and its own
runtime statistics. The controlled runner owns fixture construction, isolated
worker supervision, persistence, and export validation. Those orchestration
durations remain separate raw rows and are not relabeled as algorithm time.
`lab-result/result-render` is reserved for the Lab result surface and is not
produced by the Goal 1 command-line runner.

Each raw sample has an iteration classification. Phase 4 producers emit
`iterationKind` as exactly `setup`, `warmup`, or `measured`, and `temperature`
as exactly `cold` or `warm`. JIT/cache evidence uses stable tokens, including
`pre-jit`, `post-jit`, `cold-cache`, and `warm-cache`. The v1 reader continues
to accept non-empty legacy labels because older persisted rows had a free-form
classification contract; each unknown label is an incompatible standalone
bucket and is never aggregated with a canonical population. Setup and warmup
rows remain visible and must not be folded into measured rows; cold and warm
populations must not be aggregated together.

The required `operationCounters` member is either `null` when no trustworthy
counter source exists or an object with the nullable fields `candidateCount`,
`visitedNodeCount`, `visitedEdgeCount`, `algorithmIterationCount`,
`cancellationCheckCount`, and `thresholdAttained`. Numeric counter values are
nonnegative. These counters are algorithm work evidence, not values inferred
from elapsed time.

### 2.1 Phase 4 suite profiles and reset semantics

The executable profile contract is:

- `quick`: no warmup and one recorded measured iteration, with warm run-level
  sample mode;
- `standard`: one recorded warmup followed by three recorded measured
  iterations, with warm run-level sample mode;
- `cold`: no warmup and one recorded measured iteration, executable only when
  every iteration starts a fresh isolated .NET worker or fresh Node and
  Chromium processes; the static production-profiling Storybook HTTP server
  remains shared and is not restarted; and
- `authoritative`: configuration and validation only, with zero scheduled
  iterations and execution/baseline promotion refused until Phase 6.

Every profile iteration has a distinct sample ID. All completed iteration
samples and compact outputs remain part of the run, including evidence emitted
before a later failure. Setup, warmup, and measured rows are never aggregated
as one population.

Run-level sample mode does not erase raw process-state evidence. A quick or
standard browser/isolated journey can contain cold child-owned phase rows when
that browser or worker is fresh, while its shared API/repository phases remain
warm. Shared-runner setup and quality-comparison work remains warm when the
runner was not reset.

`cold` is a deliberately scoped child-process classification. It does not mean
that the runner, API, API connection pool, static production-profiling
Storybook HTTP server process or serving/cache state, PostgreSQL
process/shared buffers, filesystem cache, OS page cache, or network/kernel
state was restarted or cleared. REST and graph-fetch/search browser cases
whose shared-service state cannot be reset are registered as structured cold
skips. API-free bounded result rendering and isolated algorithm work may run
cold because their relevant Node, Chromium, or .NET worker processes are
freshly launched; the Storybook HTTP server remains shared.

Dataset installation is a setup exchange before a scenario's iterations. It
uses a correlated exact-HTTP/2 `POST /api/graphs/reset`, rebuilds only
`public.graphs`, `public.nodes`, and `public.edges`, and installs the requested
canonical stress data. It never owns the independent `benchmark` schema and is
never included in measured graph-fetch or algorithm time. Destructive setup is
restricted to an explicitly opted-in loopback API and a disposable database
whose name identifies it as test/benchmark/disposable.

Before any destructive request, the runner and API independently prove that
their configured connections reach the same live PostgreSQL target. Each
observes `[current_database, inet_server_addr, inet_server_port, UTC
pg_postmaster_start_time]`, with local-socket/zero fallbacks and microsecond UTC
formatting. It hashes UTF-8
`postgres-reset-target-v1\n<jsonb tuple>`. The runner also requires its parsed
database name to equal its observed `current_database`, then sends the expected
name and lowercase opaque `sha256:` fingerprint. The API repeats the probe in
the reset transaction and compares both fields before executing seed SQL. A
difference rolls back without seed work and is refused as
`409 database-reset-identity-mismatch`. The disposable-name, loopback, and
explicit-opt-in guards remain mandatory; the identity handshake does not
replace them.

### 2.2 Transport and browser limitations

The controlled REST client uses an exact HTTP/2 boundary. Phases completed
before response commit are read from the `Server-Timing` header; serialization
and other late phases are read from the `Server-Timing` trailer. The separate
browser-compatible API endpoint may use HTTP/1, where browser fetch does not
expose trailers. A browser journey must not manufacture late serialization
evidence; the paired REST journey owns that observation.

Graph fetch/search browser evidence includes the exact resource's observed
`PerformanceResourceTiming.nextHopProtocol`, or a nonblank
`resourceTimingLimitation` when the browser does not disclose it. The protocol
is never inferred from configured scheme. Cross-origin correlation headers can
trigger a CORS preflight, so browser request-to-headers timing may include that
preflight and must not be relabeled as server-only time.

The static controlled harness identifies itself as
`storybook-production-profiling`. A development Storybook build identifies
itself separately and cannot be accepted as the production-profiling
environment. Unexpected page/console errors fail a journey. No browser error,
including a `ResizeObserver` message, is globally suppressed.

### 2.3 Cross-layer reconciliation

Repository, service, response serialization, transport, browser, and
end-to-end durations are inclusive observations owned by different clocks and
may nest. Reconciliation does not force equality and does not sum them as
exclusive phases. Derived differences are labeled overhead observations and
may include scheduling, queueing, connection handling, CORS preflight,
serialization, transfer, parsing, React/render work, and stable-view detection.

Identity fields, counts, statuses, units, and digests reconcile exactly where
their contracts apply. Time deltas remain diagnostic raw evidence rather than
an invented internal phase.

### 2.4 Scenario, artifact, and execution policy

The deterministic standard registry covers all four canonical shapes at 1K,
10K, and 100K for REST fetch, collapsed presentation, bounded searches, and
the required analysis operations. Complete GraphMap expansion runs only for
the designated balanced-1K graph; every other expansion remains a structured
skip. Large deep-chain searches whose ancestor union would materialize most of
the graph are also structured skips. Unsupported or unsafe work remains in the
run as `failed`, `timed-out`, `cancelled`, `crashed`, or `skipped` evidence and
is never silently omitted.

Safe non-deep strongest-path, evidence-impact, greedy-counter, and likelihood
cases execute in the shared runner so a standard warmup can establish actual
post-JIT work. Deep variants are isolated. Exact/auto counter cases and every
robustness case are isolated. Auto-greedy entries remain present for every
dataset, but the three wide-graph entries are structured skips because no
bounded wide target can honestly resolve auto to greedy.

Browser scenario dataset, parameter, and strategy values are registry-locked;
CLI overrides are refused so the manifest, browser journey/query, bounded
input, and materialization-safety evidence cannot diverge. Non-browser dataset
overrides recompute shape-dependent worker isolation, including deep
strongest, evidence, greedy-counter, robustness, and likelihood work.

The operational entry points are the single `backend.BenchmarkRunner` CLI and
its `list`/`run` commands. Controlled graph-browser runs use a separately
running API with exact-HTTP/2 REST and browser-compatible endpoints, exact CORS
origin configuration, and the static production-profiling Storybook harness.
The complete command lines and safety preconditions live in the
[suite runbook](../../operations/insights-benchmark-suite.md).

`--persist` writes the reset-safe `benchmark` schema and reloads the durable
snapshot. `--export-dir` writes one validated v1 JSON artifact per run ID; the
recommended ignored local path is
`artifacts/insights-benchmarks/<profile>/<run-id>.json`. Performance runs remain
manual or explicitly scheduled and informational. They are not ordinary CI
timing gates. `/lab/analyze`, `/lab/performance`, and historical result UX are
deferred to Phase 5; authoritative execution, calibration, and baseline
promotion remain deferred to Phase 6.

## 3. Explicit, reset-safe persistence

`benchmark_schema.sql` initializes `benchmark.runs`, `benchmark.samples`, and
`benchmark.outputs` idempotently. Initialization is an explicit operation
before benchmark use rather than an API-startup dependency, preserving the
current ability to start health endpoints without an available database.

The storage rules are:

- Graph reset owns only `public.graphs`, `public.nodes`, and `public.edges`.
  Benchmark tables have no relationship to the `public` schema; all foreign
  keys remain within `benchmark`.
- Every mutation requires an `ExplicitBenchmarkRunIntent` whose run ID matches
  the payload. Ambient API requests have no persistence entry point.
- The complete typed manifest, sample, and compact-output logical values are
  stored as JSONB, including original offset strings. Indexed relational
  columns duplicate comparison and lifecycle fields but do not replace the
  stored payload. Canonical spelling and digests are produced when exporting.
- Sample timing-boundary provenance is duplicated in a constrained relational
  column for auditing and remains identical to the JSON payload. The
  `operationCounters` JSON member is always present, including when its value
  is explicitly `null`.
- Identity-generated entry ordinals preserve append order and allow multiple
  phase rows to share one sample ID. Samples and complete compact outputs
  flushed before a worker failure remain available.
- Graph identifiers and fingerprints are stored as values, never as foreign
  keys to resettable graph rows.
- Lifecycle fields may change without reconstructing the immutable manifest
  identity. A terminal manifest has a completion time; queued/running manifests
  do not. Terminal state is immutable except for an idempotent repeat of the
  same outcome.

No compact output is invented for a failure that has no complete logical
result digest. Failure, timeout, cancellation, and crash evidence belongs in
the run/sample outcomes, alongside any already-flushed samples or complete
outputs.

## 4. Versioned export and validation

The export identity remains `insights-run-export-v1`, schema version `1`, with
one manifest plus sample, compact-output, and section-digest collections.

Before export, samples are ordered by iteration, sample ID, registered
layer/phase order, and canonical JSON as a final tie-break. Outputs are ordered
by sample ID, operation-registry order, operation key, and canonical JSON.
Array order is digest material.

Export creation and import validate:

- the checked-in Draft 2020-12 v1 JSON Schema, evaluated with format
  validation, plus strict enum/member representation;
- complete manifest identity and nonnegative measurement metadata;
- terminal-status/completion-time consistency;
- run, scenario, operation, algorithm, parameter, target, strategy, and unit
  agreement across manifest, samples, and outputs;
- canonical-parameter digests;
- the manifest, samples, and outputs section digests;
- deterministic collection order and the top-100 retention limit;
- non-empty iteration/temperature labels, timing-boundary provenance, and
  nonnegative nullable operation counters.

Serialization is canonical UTF-8 JSON with explicit nulls. A serialize/import
round trip must preserve all three section digests and canonical bytes.

When initializing a benchmark store created before Phase 4, every existing
sample row is preserved, its timing provenance is conservatively reconciled to
`estimated`, and its JSON payload receives `operationCounters: null`. Repeated
steady-state initialization does not rewrite current rows or replace current
constraints.

Current v1 manifests require `profileKey` and `samplingPolicy.sampleMode`.
Pre-Phase-4 v1 imports that omitted either member remain readable only after
their original source manifest digest has been validated. Typed ingestion then
normalizes each missing value to `legacy-unspecified` and recomputes the
manifest digest over that explicit conservative identity. Store migration
preserves all existing runs, samples, and outputs and assigns the same
incompatible legacy bucket; it never guesses that old evidence was quick,
standard, cold, or authoritative.

`resultDigest` covers the complete logical result. It is recomputed from
retained `items` only when `totalResultCardinality == items.Count`. A top-100
export whose logical result is larger can validate the digest's canonical
syntax and recorded identity, but cannot independently recompute the full
result digest without the referenced full-result artifact.

## 5. Isolated worker protocol

The protocol identity is `insights-worker-protocol-v1`, version `1`. Frames are
one canonical JSON object per line over redirected standard input/output.
Request, cancellation, and event frames carry matching non-empty run/sample
IDs. Event sequences are contiguous from zero and contain exactly one of a
sample, compact output, or terminal outcome. Exactly one terminal event is
allowed and no later event is accepted.

Standard output is protocol-only. Standard error is drained concurrently,
control characters are sanitized, and retained diagnostics are bounded.
Protocol lines are bounded. All fully parsed samples and outputs are retained
before any later malformed frame, timeout, cancellation, or crash.

The supervisor classification precedence is:

1. A caller cancellation or hard deadline observed first controls the returned
   `cancelled` or `timed-out` outcome. If cancellation is already observable
   when the deadline resolves, caller cancellation wins.
2. The supervisor sends a cooperative cancellation frame, waits the configured
   grace period, then terminates the whole process tree if necessary.
3. Process-start and malformed-protocol failures are `failed` with failure kind
   `execution`.
4. EOF or a nonzero exit before a terminal frame is `crashed`.
5. A valid non-success terminal outcome remains authoritative even if the worker
   exits nonzero afterward. A succeeded terminal followed by a nonzero exit is
   `crashed`.

Failure records use stable codes and sanitized exception type names. They do
not persist stack traces, command lines, environment values, or raw exception
messages.

## 6. Deferred semantic questions

Phase 1 introduces no new user decision gate. These questions remain
deliberately deferred under the original plan:

- Algorithm-partner acknowledgement of the frozen `robustness-v0` checkpoint
  is still required before promoting an authoritative robustness baseline.
- Any support/counter/path-kind or posterior-recalculation change to robustness
  requires a new semantic identity.
- The `auto` critical-counter cutoff is calibrated in Phase 6.
- The accepted 100K corpus fingerprint and the first authoritative baseline
  remain Phase 6 gates.
- Actual browser-observed TTFB/full-transfer boundaries and runner overhead are
  characterized by the Phase 4 controlled harness; server body-write timing is
  not relabeled as network completion.
