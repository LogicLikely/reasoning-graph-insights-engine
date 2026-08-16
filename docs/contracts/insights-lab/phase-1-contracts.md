# Insights Lab Phase 1 contracts

**Contract status:** Frozen for Phase 1

**Contract family:** `insights-lab-v1`

**Scope:** Correlation, named phase measurement, reset-safe benchmark storage,
versioned run export, and isolated-worker supervision

This record implements Phase 1 of the Insights Lab plan. It preserves the
Phase 0 operation, algorithm, result, canonical JSON, compatibility, and
GraphMap contracts. It does not implement GraphMap admission, replacement
analysis algorithms, benchmark runners, Lab routes, calibration, or an
authoritative baseline; those remain Phases 2 through 6.

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
| `postgresql-repository` | `connection-open-wait`, `graph-lookup`, `node-query`, `edge-query`, `evidence-json-materialization`, `catalog-aggregation` |
| `backend-service-api` | `dto-mapping`, `validation`, `calculation-context-construction`, `algorithm`, `algorithm.<subphase>`, `ranking`, `result-shaping`, `serialization` |
| `transport` | `response-bytes`, `time-to-first-byte`, `full-transfer` |
| `browser-data` | `axios-receipt-parse`, `domain-mapping`, `graph-map-adapter`, `search-index-construction` |
| `graph-map` | `preflight`, `node-edge-materialization`, `dagre-layout`, `react-commit`, `deferred-edge-commit`, `viewport-fit`, `on-view-lifecycle`, `on-view-ready` |
| `end-to-end` | `action-to-stable-result-and-view` |

Algorithm subphases use lowercase dot-separated kebab-case tokens. Phase
durations use a monotonic clock, decimal milliseconds, and cannot be negative.
Recorder sequence is assigned when measurement begins, which preserves
deterministic invocation order for nested scopes. Durations are inclusive for
their own scope; cross-layer timings may nest and must not be added as though
they were mutually exclusive.

The backend currently measures the repository fetch/catalog phases and service
DTO mapping. A correlated response exposes server phases that finished before
headers were committed through `Server-Timing`. Response byte count belongs in
the transport measurement fields; true time-to-first-byte and full-transfer
remain client-observed measurements for the later controlled browser/API
runner. Reserving browser and GraphMap phase names here does not implement their
later-phase behavior.

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
- deterministic collection order and the top-100 retention limit.

Serialization is canonical UTF-8 JSON with explicit nulls. A serialize/import
round trip must preserve all three section digests and canonical bytes.

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
- Any GraphMap edge-density warning or block threshold is evidence-driven in
  Phase 6; Phase 1 does not guess one.
- The accepted 100K corpus fingerprint and the first authoritative baseline
  remain Phase 6 gates.
- Actual browser-observed TTFB/full-transfer boundaries and runner overhead are
  characterized by the Phase 4 controlled harness; server body-write timing is
  not relabeled as network completion.
