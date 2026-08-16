# LogicLikely Insights Lab Analysis and Performance Plan

**Status:** Phases 0, 1, and 3 are implemented and committed; Phase 2 was dropped; Phase 3.5 is implemented, verified, and committed by the user as `0c3415a`; Phases 4–6 have not started

**Last updated:** 2026-08-16

**Scope:** Analysis algorithms, repeatable performance measurement, historical comparison, results UX, and optional GraphMap result visualization

## 1. Purpose

Build an internal Insights Lab that can:

- Run and explain the graph engine's analysis algorithms.
- Measure database, REST API, algorithm, browser, result-panel, and GraphMap work independently.
- Compare compatible results over time as algorithms and infrastructure change.
- Exercise deterministic graphs from 1,000 through 100,000 nodes.
- Preserve correctness evidence alongside performance data.
- Present complete, useful results without making a canvas visualization part of algorithm correctness.

This plan complements the original [Structural Insights Engine Implementation Plan](./LogicLikely_Structural_Insights_Engine_Implementation_Plan.md). It turns those algorithm goals into a versioned, measurable, and inspectable workflow.

## 2. Locked Decisions

| Area | Decision |
| --- | --- |
| Lab routes | Add internal `/lab/analyze` and `/lab/performance` routes, separate from the existing demo. |
| Execution | Support explicit UI-queued runs and repeatable CLI suites. Ordinary navigation and search are not persisted as benchmark runs. |
| History | Store benchmark history in a reset-safe PostgreSQL `benchmark` schema and support versioned JSON export. |
| Comparison host | The current stable ARM64 Mac, profile ID `ll-arm64-mac-primary`, is the authoritative performance host. Hosted CI timing is informational only. |
| Dataset matrix | Balanced, wide, deep, and shared-diamond graphs at 1K, 10K, and 100K nodes. |
| Critical counters | Implement and compare `exact`, `greedy`, and `auto` strategies. |
| Counter objective | Find the fewest eligible counters that move the target to or below the configured probability threshold. |
| Counter threshold | Configurable; default log-odds `-1`, approximately `26.9%` probability. |
| Auto strategy | Select by a deterministic candidate-count cutoff calibrated to about two seconds on the authoritative host. |
| Robustness | Preserve the merged behavior as `robustness-v0` until its semantics are deliberately revised and versioned. |
| Result retention | Store compact summaries, distributions, canonical digests, and at most the top 100 ranked items in PostgreSQL. Full output is an optional external JSON artifact. |
| GraphMap boundary | Retain the currently accepted GraphMap dependency. This initiative does not require GraphMap source, package, or public-API changes. |
| CI policy | Correctness remains required. Performance runs are manual or scheduled, named, artifact-producing, and non-gating. |
| Baseline timing | Authoritative historical baselines begin after the accepted 100K dataset/corpus commit is available. |

## 3. Current Baseline and Constraints

### 3.1 Completed foundation

- Phase 0 froze the initial operation, result, identity, and failure contracts.
- Phase 1 implemented the correlation, timing, persistence, export, and worker-isolation foundation.
- Phase 3 implemented the versioned analysis operations and compatibility surfaces.
- Phase 3.5 reconciled those artifacts with the unchanged GraphMap integration boundary.
- GraphMap is responsive with compact projections of large source graphs.
- GraphMap search is fast when the union of matches and required ancestor chains is compact.
- Full expansion is a designated small-dataset benchmark scenario.
- Graph fetches and current REST operations are fast at 10K, but 100K must be measured rather than assumed.

### 3.2 Remaining gaps

- The existing application does not yet provide the intended durable, explainable Lab result UI.
- Repeatable benchmark runners and the historical comparison UI do not yet exist.
- Existing algorithm requests may still pay complete graph retrieval and mapping costs that need separate measurement.
- Browser, result-panel, and GraphMap phases are not yet measured consistently by a controlled harness.
- Deep recursive operations can fail from call-stack depth before their asymptotic runtime becomes the limiting factor.

## 4. Target Architecture

```text
Deterministic dataset
        |
        v
Explicit run request (Lab UI or CLI)
        |
        v
Serial run queue / orchestrator
        |
        +----> isolated algorithm worker / BenchmarkDotNet
        |
        +----> REST + PostgreSQL journey
        |
        +----> Playwright browser + result-render journey
        |
        v
Correlated samples + canonical result digest
        |
        +----> reset-safe PostgreSQL benchmark schema
        |
        +----> portable versioned JSON artifact
        |
        v
/lab/analyze results and /lab/performance history/comparison
```

The operation registry, run identity, phase names, result digest rules, and JSON schema are shared across the UI, API, CLI, and benchmark harnesses.

GraphMap is an optional consumer of selected analysis results. An algorithm result remains complete when it is shown only as a summary, table, distribution, or ordered textual path.

## 5. Operation and Measurement Registry

Use one extensible registry rather than one-off buttons and result handlers. Core analysis operations appear in both the Lab and benchmarks; supporting diagnostics are benchmark-only in v1.

| Operation key | Purpose | v1 exposure | Initial result surface |
| --- | --- | --- | --- |
| `graph.catalog` | Measure catalog retrieval/count aggregation with all stress graphs installed. | Benchmark diagnostic | Timing and count summary |
| `graph.fetch` | Fetch, transfer, parse, and adapt a complete graph. | Benchmark diagnostic | Graph/payload summary |
| `graph.search` | Find matches and the complete required ancestor union. | Benchmark diagnostic | Match/union counts and optional projection |
| `path.strongest` | Find strongest paths in the requested direction. | Analysis and benchmark | Summary, ranked paths, optional graph context |
| `path.single-pair` | Exercise the current min/max single-pair path diagnostic. | Benchmark diagnostic | Diagnostic result and timing |
| `evidence.impact-ranking` | Rank supporting and counter evidence by target probability impact. | Analysis and benchmark | Summary, distribution, top 100 |
| `counter.critical-set` | Find a threshold-reaching counter set with exact, greedy, or auto strategy. | Analysis and benchmark | Selected counters, quality, optional graph context |
| `node.robustness` | Rank nodes by the versioned robustness calculation. | Analysis and benchmark | Least robust summary, distribution, top 100 |
| `likelihood.recalculate` | Recalculate a selected node/ancestor chain after a defined change. | Benchmark diagnostic | Before/after likelihood summary |

Every operation returns a common envelope containing:

- Operation key and semantic version.
- Requested and actually used strategy.
- Graph and target identifiers.
- Canonical parameters and parameter digest.
- Execution status: `queued`, `running`, `succeeded`, `failed`, `timed-out`, `cancelled`, `crashed`, or `skipped`.
- Summary metrics.
- Total result cardinality.
- Up to 100 deterministic result items.
- Canonical result digest.
- Optional ordered path data.
- Phase timings and resource measurements.
- Validation notices and error details.

Presentation state is not part of algorithm identity or result-digest material.

## 6. Algorithm Contracts

### 6.1 Strongest paths

- Preserve direction and target semantics explicitly in the request.
- Return the accumulated score plus ordered node IDs and edge IDs.
- Include deterministic tie-breaking.
- Support the canonical stress scenarios:
  - Root/downstream.
  - Deepest leaf/upstream.
  - Selected pair where the existing single-pair implementation is being measured.
- Separate graph-context construction from traversal, result reconstruction, sorting, and response shaping in timings.
- Treat the ordered path as the authoritative result whether or not the UI also presents graph context.

### 6.2 Evidence impact ranking

- Version the existing behavior before optimization.
- Return supporting and counter rankings with baseline probability, counterfactual probability, and raw probability delta.
- Avoid repeated strongest-path traversals within one run where shared work can be reused without changing semantics.
- Store the full result count and digest, but retain only summary/distribution/top-100 data in PostgreSQL.
- Expose the responsible path or context when the algorithm can identify it.

### 6.3 Critical-counter exact, greedy, and auto

The threshold predicate is `resulting log-odds <= configured threshold`, equivalently `resulting probability <= configured threshold probability`. The objective is lexicographic:

1. Satisfy the threshold predicate.
2. Minimize the number of selected counters.
3. Prefer the greater below-threshold margin.
4. Break remaining ties by stable ordinal node ID order.

The algorithm contract must define candidate eligibility and the meaning of removing or applying a counter. Excluded counters must be excluded consistently from the calculation context; they must not remain in traversal and then be counted again.

#### Exact

- Search eligible counter subsets and prove optimal cardinality for tractable candidate counts.
- Use deterministic enumeration and safe pruning that cannot discard an optimal solution.
- Enforce cancellation and a hard timeout.
- Run out of process so a crash or runaway search cannot terminate the API.

#### Greedy

- Recompute each remaining candidate's marginal effect after every selection.
- Select the best deterministic marginal improvement.
- Stop when the threshold is reached or no candidate improves the result.
- Report threshold attainment; never label a non-attaining set “minimal.”

#### Auto

- Use a configured candidate-count cutoff, not an uncontrolled elapsed-time switch during execution.
- Calibrate the cutoff as the largest candidate count whose median exact core runtime is at most two seconds for every required calibration scenario under the authoritative `standard` profile.
- Record the cutoff, candidate count, selected strategy, and selection reason in every run.

#### Exact-versus-greedy comparison

For tractable cases, record:

- Runtime and resource cost.
- Threshold attainment.
- Selected-set cardinality.
- Cardinality gap from optimal.
- Resulting probability and threshold margin.
- Set overlap and deterministic result digest.

### 6.4 Node robustness

Treat the merged implementation as `robustness-v0`, not as a general graph-connectivity or global-resilience measure.

For node `v`, v0:

1. Finds the maximum accumulated log likelihood ratio over structural leaf-to-node paths.
2. Algebraically subtracts that path contribution from the node's stored posterior log-odds.
3. Measures the absolute probability change.
4. Returns `exp(-absolute change)`, where `1` means unaffected and lower means less robust.

The v0 contract must disclose that it:

- Uses stored posterior odds rather than fully removing and recalculating the graph.
- Currently does not filter the path by support edge kind.
- Uses edge weights but not a leaf evidence contribution.
- Computes recursively and is unsafe on sufficiently deep chains.
- Produces a compressed theoretical range of approximately `0.3679` through `1`.

Before an authoritative robustness baseline, complete a partner semantic checkpoint covering:

- Eligible ranked node kinds.
- Eligible leaf/path endpoint kinds.
- Support-only, counter-only, and mixed-path behavior.
- Stored versus recomputed posterior semantics.
- Formula and display range.
- Deterministic tie-breaking.

Freeze golden vectors for v0. Any semantic correction becomes a new version rather than silently changing historical meaning.

Rich robustness results include:

- Node ID, title, kind, and rank.
- Robustness score.
- Original probability.
- Hypothetical probability.
- Raw absolute probability delta.
- Accumulated path log-LR and LR.
- Ordered path node IDs and edge IDs.
- Semantic version.

Compute the graph-wide ranking once. “Least robust” is the first ranked item, not a second execution.

Run v0 in an isolated worker on deep graphs. If recursion fails, first implement an iterative DAG/topological equivalent that preserves the frozen result digest. A behavior change requires a new semantic version.

## 7. GraphMap Integration Boundary

GraphMap remains useful as a visual aid, but it is not the result system and it is not part of algorithm correctness.

- Keep the currently accepted GraphMap dependency unchanged.
- Do not modify, repackage, or introduce a new GraphMap public API for this initiative.
- Use existing presentation hooks for node and edge emphasis when they add explanatory value.
- Use existing search and view behavior without making it an algorithm-result transport.
- Make summaries, ranked tables, distributions, and ordered paths complete on their own.
- Treat a graph projection as optional presentation attached to a result, not as the result itself.
- Measure GraphMap from the consumer and browser harness through performance marks, React Profiler, and Playwright observations.
- Label consumer-observed layout or settling boundaries as estimates when GraphMap does not expose an exact internal callback.

This boundary keeps the Lab free to improve result presentation without coupling later phases to GraphMap internals.

## 8. Results UX

### 8.1 Analyze route

`/lab/analyze` provides:

- Graph and operation selection.
- Target, direction, threshold, strategy, timeout, and operation-specific parameters.
- A queued/running/completed state model.
- Cancellation where the operation supports it.
- A result header containing operation version, actual strategy, graph identity, elapsed time, and execution status.
- An operation-specific result body.
- Optional GraphMap context using only the accepted package capabilities.

Operation result bodies:

| Operation | Primary presentation |
| --- | --- |
| Strongest path | Path score, ordered node/edge steps, ranked alternatives, and optional emphasized path |
| Evidence impact | Baseline, distribution, supporting/counter tabs, top-100 table, and optional selected-item context |
| Critical counters | Threshold result, exact/greedy comparison, selected set, quality metrics, and optional selected-set context |
| Node robustness | Metric definition, least-robust summary, distribution, top-100 table, raw deltas, and optional selected-path context |

The result panel must handle success with zero items, validation failure, timeout, cancellation, crash, and explicit skip without treating them as the same state.

### 8.2 Performance route

`/lab/performance` provides:

- Scenario/profile selection.
- Serial queue status and cancellation.
- Current run phase and progress.
- Median, p95, min/max, sample count, bytes, allocations, and other available resource metrics.
- Phase waterfall or stacked timing view.
- Compatible-run comparison over time.
- Algorithm quality comparison where exact and approximate results overlap.
- Failure, timeout, crash, and skip history.
- Dataset, code, runtime, database, browser, and machine identity.
- Versioned JSON export.

### 8.3 Large result handling

- Persist and render summaries, distributions, and deterministic top-100 rows by default.
- Page or virtualize larger retained lists.
- Keep the complete result cardinality and digest even when the full item list is external.
- Never require the complete source graph or complete result list to be mounted in the browser to inspect an analysis.
- Keep ordered textual paths available when an operation returns path data.
- Make optional graph context a progressive enhancement.

## 9. Instrumentation Model

### 9.1 Correlation

Assign one run ID to an explicit Lab or CLI run. Propagate it through:

- Browser request headers.
- ASP.NET request handling.
- Repository calls.
- Algorithm worker messages.
- Benchmark samples.
- Persisted outputs.
- Exported artifacts.

### 9.2 Server phases

Record separate timings for:

- Graph metadata lookup.
- Node query.
- Edge query.
- Evidence JSON materialization.
- Graph/context construction.
- Service DTO mapping.
- Algorithm core phases.
- Result reconstruction and sorting.
- Result digest generation.
- Persistence.
- Response serialization and payload bytes.

Do not use one “API time” value to stand in for all server work.

### 9.3 Algorithm phases

At minimum:

- Context build.
- Candidate discovery.
- Traversal or dynamic programming.
- Exact subset search or greedy iterations.
- Path reconstruction.
- Scoring.
- Sorting/top-N selection.
- Output shaping and digest.

Capture candidate count, visited nodes/edges, iteration count, result count, threshold attainment, timeout/cancellation checks, and operation-specific counters.

### 9.4 Browser and presentation phases

Capture where applicable:

- Request start to response headers.
- Full transfer.
- JSON parse.
- API-to-domain adaptation.
- Search computation.
- Node/edge model creation.
- Consumer-observed GraphMap layout/render interval.
- React commit.
- Viewport settling.
- Lab result-panel rendering.

Use the existing package as a black-box dependency. Consumer performance marks, React Profiler, browser timing, and Playwright observations provide the measurement seams. Document any phase that is approximate.

### 9.5 Sample policy

- Separate setup from measured work.
- Separate cold, warmup, and measured iterations.
- Preserve raw samples; derive medians and percentiles afterward.
- Record failures and partial phase data rather than discarding the run.
- Never combine samples whose scenario, dataset, algorithm, parameters, environment profile, or units are incompatible.

## 10. Persistence, Identity, and Export

Use a dedicated PostgreSQL `benchmark` schema initialized idempotently and untouched by graph reset.

### 10.1 Run identity

Persist:

- Run ID, scenario ID, status, timestamps, and profile.
- Graph slug, shape, actual node/edge counts, and depth.
- Dataset generator version.
- Corpus ID and fingerprint.
- Topology/input fingerprint.
- Operation key and semantic version.
- Requested and actual strategy.
- Canonical parameters and digest.
- Git SHA and dirty-worktree flag.
- Build configuration.
- .NET, Node, browser, GraphMap, PostgreSQL, OS, CPU, and memory identity.
- Warm/cold mode, warmup policy, and sample policy.

Graph slug alone is not enough to establish comparability.

### 10.2 Samples

Persist one row per operation phase and iteration with:

- Layer and phase.
- Duration and units.
- Warm/cold classification.
- Allocations and GC counts where available.
- Bytes and graph/result counts.
- Success state and structured failure data.

### 10.3 Outputs

Persist:

- Compact summary.
- Distribution buckets.
- Total cardinality.
- Top 100 deterministic items.
- Canonical result digest.
- Quality/oracle metrics.
- Optional external artifact URI and content digest.

### 10.4 Compatibility

Default comparisons require matching:

- Scenario and profile.
- Dataset/input fingerprint.
- Operation semantic version.
- Canonical parameter digest.
- Actual strategy where it changes behavior.
- Build/runtime environment class.
- Units and sample mode.

The UI may display incompatible runs together only with an explicit explanation; it must not calculate a regression percentage between them by default.

### 10.5 Export policy

- Use a versioned common JSON schema.
- Export the manifest, raw samples, summaries, output digest, and failure details.
- Upload raw run JSON as local/manual/scheduled workflow artifacts.
- Commit only deliberately promoted compact baselines, with provenance and hashes.
- Do not make runtime code depend on a developer-local artifact path.

## 11. Benchmark Harnesses

### 11.1 Pure algorithm harness

Add a dedicated BenchmarkDotNet project for:

- Graph-context construction.
- Strongest paths.
- Evidence-impact ranking.
- Robustness ranking.
- Critical-counter exact/greedy/auto.
- Likelihood recalculation.

Use in-memory immutable fixtures derived from canonical stress specs. Exclude database, HTTP, and JSON work from core algorithm timings.

Risky recursive or combinatorial scenarios run in a child process with timeout and crash capture.

### 11.2 REST and database harness

Exercise the real PostgreSQL repository and API:

- Catalog query with all stress graphs installed.
- Complete graph fetch.
- Analysis endpoint with database-loaded graph.
- Analysis endpoint with supplied graph context where supported.
- Response serialization and payload size.

Seed/install time is setup, not graph-fetch or algorithm time.

### 11.3 Browser harness

Use Playwright for controlled journeys:

- Actual REST graph fetch.
- Browser parse and adaptation.
- Search computation and result metadata.
- Representative GraphMap render measurement using existing capabilities.
- Lab result rendering.
- Optional selected-result graph context.

Use React Profiler and consumer performance marks where stable. Treat DOM settling as an approximation and identify it as such in the recorded phase.

### 11.4 Run modes

Provide named profiles:

- `quick`: correctness/smoke validation with minimal repetitions.
- `standard`: warmups plus repeated measured samples for local comparison.
- `cold`: separately labeled fresh-process/connection/cache scenarios.
- `authoritative`: controlled standard suite on `ll-arm64-mac-primary` with a clean Release build.

Never mix cold and warm samples in one percentile series. Benchmark setup and seed installation are measured separately from the operation under test.

## 12. Dataset and Scenario Matrix

The canonical matrix comes from [`StressGraphSeedCatalog.cs`](../../backend/Seeding/StressGraphSeedCatalog.cs):

| Shape | 1K | 10K | 100K | Primary stress |
| --- | ---: | ---: | ---: | --- |
| Balanced tree | Yes | Yes | Yes | Breadth and ordinary depth |
| Wide star | Yes | Yes | Yes | Fan-out, payload, ranking, and layout width |
| Deep chain | Yes | Yes | Yes | Recursion and path length |
| Shared-diamond DAG | Yes | Yes | Yes | Shared ancestry, unique counting, and roughly 2x edge density |

Persist generator version, corpus ID/fingerprint, and topology fingerprint for every run.

### 12.1 Canonical scenarios

| Scenario | 1K | 10K | 100K |
| --- | --- | --- | --- |
| Catalog fetch with all graphs installed | Measure | Measure | Measure |
| Full graph REST fetch | Measure | Measure | Measure |
| Initial collapsed GraphMap projection | Measure | Measure | Measure |
| Full expansion | Measure | Not scheduled | Not scheduled |
| Search: no hit | Compute and report counts | Compute and report counts | Compute and report counts |
| Search: single shallow hit | Compute; optional representative graph context | Compute; optional representative graph context | Compute; result metadata is sufficient |
| Search: known multi-hit such as `999` | Compute matches and required ancestor union | Compute matches and required ancestor union | Compute matches and required ancestor union |
| Search: deepest-chain hit | Compute and validate ordered ancestry | Compute and validate ordered ancestry | Compute and validate ordered ancestry |
| Strongest path | Compute; optional graph context | Compute; graph context is scenario-specific | Compute; textual/tabular result required |
| Evidence-impact ranking | Compute | Compute | Compute or record timeout/failure |
| Node robustness ranking | Compute | Isolated compute | Isolated compute |
| Likelihood recalculation | Compute | Compute | Compute |
| Critical counter exact | Candidate-limited | Candidate-limited | Candidate-limited |
| Critical counter greedy/auto | Compute | Compute | Compute or record timeout/failure |

Search scenarios record both match count and the complete required-node union. Algorithm scenarios record complete result identity even when the browser journey presents only summaries, top items, or an ordered path.

### 12.2 Presentation fixtures

Maintain deterministic browser fixtures for:

- Collapsed graph rendering at each dataset scale.
- Compact search results with known match and ancestor-union counts.
- Full expansion of the designated small dataset.
- Strongest-path result presentation.
- Critical-counter result presentation.
- Evidence-impact and robustness rankings.
- Empty, failed, timed-out, cancelled, crashed, and skipped result states.

These fixtures test result presentation and measurement; they do not define algorithm semantics.

## 13. Phased Implementation Plan

### Phase 0 — Freeze contracts

**Status:** Completed and committed. Active contracts were reconciled by Phase 3.5.

Implementation record: [Phase 0 frozen contracts](../contracts/insights-lab/phase-0-contracts.md).

Execution constraints for any reconciliation work:

- Do not use `orchestrate-bounded-goals`.
- Do not use `curate-review-artifacts`.
- Do not create, stage, amend, or otherwise manage commits. Leave all commits for the user to make.

Delivered:

- Operation registry and semantic-version rules.
- Critical-counter candidate/removal contract and deterministic tie rules.
- `robustness-v0` characterization and partner semantic checkpoint.
- Run manifest, sample, output, failure-state, and JSON contracts.
- Golden fixtures for current algorithm behavior.

Acceptance:

- Current algorithm outputs have canonical digests on small fixtures.
- Incompatible-run rules are executable specifications.
- No benchmark history is declared authoritative before the contracts are frozen.

### Phase 1 — Measurement and persistence foundation

**Status:** Completed and committed. Active contracts were reconciled by Phase 3.5.

Implementation record: [Phase 1 frozen contracts](../contracts/insights-lab/phase-1-contracts.md).

Execution constraints for any reconciliation work:

- Do not use `orchestrate-bounded-goals`.
- Do not use `curate-review-artifacts`.
- Do not create, stage, amend, or otherwise manage commits. Leave all commits for the user to make.

Delivered:

- Correlation IDs and common phase names.
- Repository/API/transport timing seams.
- Reset-safe `benchmark` schema and repository.
- Versioned JSON export.
- Isolated worker protocol with cancellation, timeout, crash, and partial-result capture.

Acceptance:

- One fixture run produces reconcilable phase timings and complete identity metadata.
- Success, failure, timeout, cancellation, crash, and validation failure remain distinct.
- Explicit runs alone persist.
- Exported JSON validates without digest changes.
- Graph reset demonstrably preserves benchmark history.

### Phase 2 — Dropped: GraphMap package expansion

**Status:** Dropped after review. No Phase 2 work is required or carried forward.

The proposed GraphMap node-limit and associated package/API expansion will not be integrated, packaged, vendored, or used as a dependency. The currently accepted GraphMap package remains the baseline. Cleanup of related artifacts introduced elsewhere belongs exclusively to Phase 3.5.

### Phase 3 — Versioned analysis operations

**Status:** Completed and committed. Active contracts were reconciled by Phase 3.5.

Implementation record: [Phase 3 frozen contracts](../contracts/insights-lab/phase-3-contracts.md).

Execution constraints for any reconciliation work:

- Do not use `orchestrate-bounded-goals`.
- Do not use `curate-review-artifacts`.
- Do not create, stage, amend, or otherwise manage commits. Leave all commits for the user to make.

Delivered:

- Rich strongest-path output with ordered paths.
- Versioned evidence-impact result contract.
- Exact, greedy, and auto critical-counter implementations.
- Rich `robustness-v0` results and one-pass least/ranking behavior.
- Cancellation and deterministic ordering across operations.
- Legacy endpoint compatibility adapters.

Acceptance:

- Exact critical counters are optimal on golden cases.
- Greedy quality is measured against exact where tractable.
- Auto records its actual strategy and reason.
- Robustness v0 matches frozen vectors.
- Result summaries, top 100, totals, paths, and digests are deterministic.
- No measured operation writes Console output inside its hot loop.

### Phase 3.5 — Remove dropped GraphMap node-limit artifacts

**Status:** Implemented and verified on 2026-08-16; committed by the user as `0c3415a`. Phase 4 has not started.

Completion record:

- The user confirmed that no durable v1 export corpus exists outside this project, so the pre-baseline `insights-run-export-v1` schema and example were revised in place.
- Existing in-project benchmark rows are reconciled without deleting runs, samples, outputs, or unrelated JSON data. Retired identifiers may remain only in compatibility migration code that removes them from stores initialized by the earlier v1 DDL and in cleanup-regression tests that prove their removal.
- The shared warning member was removed as required, including the Phase 3 worker prefix-reduction notice. Prefix reduction remains observable from retained item count versus total cardinality; ordered paths and the complete-result digest remain unchanged.
- The accepted vendored GraphMap `0.2.0` artifact, dependency declaration, lockfile entry, and public API remain unchanged.

Purpose: remove all work related to the abandoned feature that entered the repository through Phases 0, 1, and 3, while preserving the analysis, measurement, and result contracts those phases otherwise delivered.

Execution constraints:

- Do not use `orchestrate-bounded-goals`.
- Do not use `curate-review-artifacts`.
- Do not create, stage, amend, or otherwise manage commits. Leave all commits for the user to make.

Deliver:

- Remove limit, budget, preflight, warning, rejection, and visualization-status language and fields from the Phase 0 operation and result contracts.
- Remove related persistence columns, domain fields, timing names, export-schema properties, examples, repository mappings, and tests introduced in Phase 1.
- Remove related request/response fields, analysis DTO mappings, adapters, digest inputs, and tests introduced in Phase 3.
- Update the Phase 0, Phase 1, and Phase 3 contract records so their active contracts match this plan.
- Retain the last accepted GraphMap artifact and remove every dependency on the dropped package work.
- Remove threshold-specific fixtures and UI/CI expectations.
- Preserve neutral graph, node, edge, search, adaptation, layout, React, viewport, and result-render measurements.
- Preserve ordered paths, ranked results, operation semantic versions, canonical algorithm digests, benchmark history, and unrelated export data.
- Before changing the export contract, determine whether any v1 benchmark data must remain readable:
  - If no durable v1 data exists, revise the pre-baseline v1 schema and example in place.
  - If durable v1 data exists, introduce an explicit compatible reader or schema revision and document the conversion.
- Update tests and documentation to prove the cleanup without changing unrelated algorithm behavior.

Acceptance:

- No active runtime, database, export, UI, public contract, or test expectation contains the abandoned feature.
- This Phase 3.5 cleanup record and the Phase 2 decision record are the only remaining mentions of it in this plan.
- The currently accepted GraphMap dependency installs and builds without any new package API.
- Phase 0/1/3 correctness, persistence, export, compatibility, and algorithm-digest tests pass after cleanup.
- Consumer-side GraphMap and result-render timing remains measurable.
- No unrelated benchmark history or result data is lost.
- Phase 4 does not begin until this phase is complete.

### Phase 4 — Benchmark runners

**Status:** Goal 1, Benchmark Core, is complete as of 2026-08-16. Goal 2 is not started.

Goal 1 completion record:

- The shared run contract now identifies directly instrumented, externally observed, and estimated timing boundaries; keeps setup, warmup, measured, cold, and warm populations distinct; preserves raw samples and explicit units; and reserves neutral Lab result-render measurement for Goal 2. Pre-Phase-4 nonblank v1 classification labels remain readable as separate, non-aggregated compatibility buckets.
- The serial CLI runner uses the retained operation registry, deterministic stress fixtures, shared operation execution, the existing worker protocol, optional reset-safe PostgreSQL persistence, and schema-validated versioned JSON export. It records actual strategy selection, cancellation and every terminal lifecycle state, phase provenance and available operation counters, real source-revision state, and partial evidence from failed isolated work.
- The bounded `quick` profile covers the retained in-memory algorithms and records explicit reasons for deferred Goal 2 journeys, unsafe uncapped exact work, and unsafe in-process recursive work. Exact and auto critical-counter execution require an explicit positive candidate limit; recursive, combinatorial, and override-derived hazardous cases are isolated.
- The dedicated BenchmarkDotNet project covers calculation-context construction, strongest path, minimum and maximum single-pair paths, evidence impact, exact and greedy critical counters, both auto-strategy branches, robustness, and likelihood recalculation. Fixture preparation is outside measured work, mutation-prone state is reset per iteration, and MemoryDiagnoser plus available deterministic algorithm counters are included.
- Release verification completed with a clean six-project solution build; 403 backend tests passed with no skips against a disposable PostgreSQL database; two equivalent 15-case quick runs matched dataset, parameter, result, status, and scenario identities; and standard out-of-process BenchmarkDotNet `Dry` and `ShortRun` jobs each executed all 11 workloads successfully. These development measurements are informational and establish no authoritative thresholds or baselines.

Goal 2 remains explicitly deferred. It includes real graph catalog/fetch/search and REST/database performance journeys, API serialization and network transfer, Playwright and browser parsing/adaptation, GraphMap and React/result-panel measurement, and integrated cold, standard, or authoritative suites. Phase 5 UI and Phase 6 calibration also remain unstarted.

Execution constraints:

- Do not use `orchestrate-bounded-goals`.
- Do not use `curate-review-artifacts`.
- Do not create, stage, amend, or otherwise manage commits. Leave all commits for the user to make.

Deliver:

- BenchmarkDotNet pure-algorithm project.
- CLI scenario orchestrator.
- Serial queue service/orchestration contract consumed by the CLI and, in Phase 5, the Lab UI.
- Playwright API/browser suites.
- Warm/cold/named profile support.
- Artifact production and benchmark-store persistence.
- Consumer-side GraphMap and Lab result-render measurement.

Acceptance:

- A failed or stack-overflowing worker cannot terminate the API or lose prior samples.
- Dataset digests make repeated runs reproducible.
- DB/API/algorithm/browser phases reconcile within documented overhead.
- GraphMap and result-render timings identify any approximate browser-observed boundaries.
- Repeated runs produce valid persisted records and portable JSON.

### Phase 5 — Insights Lab UI

**Status:** Not started. Depends on Phase 4.

Execution constraints:

- Do not use `orchestrate-bounded-goals`.
- Do not use `curate-review-artifacts`.
- Do not create, stage, amend, or otherwise manage commits. Leave all commits for the user to make.

Deliver:

- `/lab/analyze`.
- `/lab/performance`.
- Parameter controls and queued run status.
- Summary, distribution, top-100, history, and compatibility-aware comparison views.
- Ordered textual path presentation.
- Optional result context through the existing GraphMap integration.
- Versioned JSON export.

Acceptance:

- Every registered analysis displays its operation/version, execution status, summary or empty state, and total count.
- A 100K analysis can be inspected through summaries, distributions, top rows, and ordered paths without rendering the complete graph.
- Strongest-path and other path-bearing results remain understandable without GraphMap.
- Existing GraphMap capabilities can add context to a selected result without changing the package.
- Stale responses cannot replace newer graph/target selections.
- Result states and controls meet accessibility expectations.

### Phase 6 — Calibration and authoritative baselines

**Status:** Not started. Depends on Phase 5.

Execution constraints:

- Do not use `orchestrate-bounded-goals`.
- Do not use `curate-review-artifacts`.
- Do not create, stage, amend, or otherwise manage commits. Leave all commits for the user to make.

Deliver:

- Auto-strategy candidate cutoff calibrated as the largest count whose median exact core runtime is at most two seconds for every required calibration scenario under the authoritative `standard` profile.
- Deep-chain robustness validation and an iterative equivalent if required.
- Named authoritative baseline suite on `ll-arm64-mac-primary`.
- Manual/scheduled informational workflow and artifacts.
- Documented browser and result-render baselines for the designated scenarios.

Acceptance:

- The accepted 100K dataset/corpus has stable identity fingerprints.
- One named baseline covers every supported operation, shape, and size or records an explicit skip, timeout, failure, or crash.
- Every baseline has complete environment, code, dataset, algorithm, and parameter identity.
- Algorithm quality and performance comparisons follow the compatibility rules.
- Browser scenarios clearly distinguish source-graph work, result-panel work, and optional graph presentation.
- Performance remains informational and does not gate ordinary pull requests.

### Dependency order

```text
Phase 0 → Phase 1 → Phase 3 → Phase 3.5 → Phase 4 → Phase 5 → Phase 6

Phase 2: dropped; no dependency edges and no implementation work
```

Phase 3.5 completed the only cleanup prerequisite. No later phase depends on Phase 2.

## 14. Verification Matrix

### 14.1 Algorithms

- Golden outputs and canonical digests.
- Exact optimality and greedy quality.
- Deterministic ties and stable ordering.
- Support-only, counter-only, and mixed-path cases.
- Shared-DAG and cycle handling.
- Stale versus recalculated posterior contract.
- Cancellation and timeout propagation.
- Deep-chain worker crash containment.
- Path reconstruction correctness.

### 14.2 Persistence and API

- Run-manifest validation.
- Parameter/dataset/environment compatibility.
- Percentile aggregation and units.
- JSON schema and digest validation.
- Reset preserves history.
- Real PostgreSQL seed counts and fingerprints.
- DB-mode and supplied-graph-context parity.
- Legacy endpoint adapters.
- Paging/top-100 behavior.
- Empty-body model binding where supported.

### 14.3 GraphMap integration

- The currently accepted package installs and builds unchanged.
- Existing search behavior continues to return correct match and ancestor-union metadata.
- Existing graph adaptation preserves node and edge identity.
- Existing presentation hooks emphasize selected result nodes and edges correctly where used.
- Consumer-side timing marks and Playwright measurements are correlated to the run.
- Browser-observed timing boundaries are labeled as approximate.
- Existing GraphMap regression tests remain green.

### 14.4 Frontend Lab

- Operation-specific controls and validation.
- Queue, cancellation, and status transitions.
- Summary/top-100/distribution rendering.
- Ordered path rendering.
- Compatible and incompatible history comparisons.
- Optional row-to-graph context using existing capabilities.
- Stale response suppression.
- Empty, failed, timed-out, cancelled, crashed, and skipped states.
- Storybook states, accessibility, Vitest, Cucumber, and Playwright journeys.

## 15. CI and Baseline Policy

- Keep deterministic correctness, contract, digest, and result-rendering tests in ordinary CI.
- Add PostgreSQL integration coverage using a controlled real database fixture.
- Keep performance runs out of pull-request pass/fail decisions.
- Provide manual and optional scheduled performance workflows.
- Upload versioned JSON and harness artifacts.
- Treat GitHub-hosted timing as diagnostic because host hardware varies.
- Promote a baseline only from `ll-arm64-mac-primary`, a clean Release build, and an accepted dataset fingerprint.
- Store dirty-worktree runs for investigation if explicitly requested, but do not promote them as authoritative.

## 16. Risks and Mitigations

| Risk | Mitigation |
| --- | --- |
| Full graph loading dominates every algorithm | Time graph retrieval/context separately; enable future reuse only after baseline semantics are preserved. |
| Exact counter search explodes | Candidate cutoff, timeout, cancellation, isolated process, and explicit skipped/timed-out outcomes. |
| Greedy result is mislabeled minimal | Report strategy and threshold attainment; reserve optimal/minimal language for proven exact output. |
| Robustness semantics are misunderstood | Freeze `robustness-v0`, expose raw components/path, complete partner checkpoint, and version revisions. |
| Deep recursion terminates a process | Isolated workers, deep scenarios, iterative equivalents, and crash recording. |
| Canvas presentation obscures the real result | Make summaries, tables, distributions, and ordered paths authoritative; use graph context only as an optional aid. |
| GraphMap core changes create disproportionate risk | Keep the accepted dependency unchanged and instrument it from the consumer. |
| Benchmark reset deletes history | Separate reset-safe schema and integration test. |
| Seed or corpus changes invalidate trends | Persist generator, corpus, topology, and input fingerprints. |
| Hosted runner noise looks like regression | Authoritative named host; hosted results informational only. |
| Large outputs overwhelm DB/UI | Top 100, summaries/distributions, digest, paging, optional external artifact. |
| Instrumentation changes measured behavior | Keep phases lightweight, measure overhead, and preserve raw samples. |

## 17. Non-Goals

- Modifying, repackaging, or versioning GraphMap source or its public API.
- Rendering or virtualizing complete 10K/100K graphs.
- Replacing ReactFlow or Dagre.
- Making canvas presentation necessary to understand an analysis result.
- Guaranteeing greedy optimality.
- Unbounded exact counter execution.
- Redefining `robustness-v0` without a new semantic version.
- Persisting ordinary user navigation, ambient search, or non-explicit runs.
- Production APM/observability, distributed benchmarking, or cloud load testing.
- Performance thresholds on variable GitHub-hosted runners.
- Production graph editing or integration with LogicLikely's primary production database.

## 18. Definition of Done

The initiative is complete when:

- Every registered analysis returns an explainable, versioned result in `/lab/analyze`.
- Exact/greedy/auto critical-counter behavior is implemented and compared on tractable cases.
- Robustness has a frozen semantic contract, rich path/result output, and contained deep-graph behavior.
- Database, API, algorithm, transport, browser, result-panel, and GraphMap phases are independently measurable to the precision documented for each phase.
- Explicit runs persist across graph resets and export portably.
- Compatible historical runs can be compared without conflating dataset, algorithm, parameter, or environment changes.
- The 12-graph deterministic matrix has a named authoritative baseline or an explicit recorded outcome.
- Full expansion is benchmarked for the designated small dataset; large datasets retain complete algorithm and textual/tabular result coverage.
- Optional GraphMap result context works through the unchanged accepted dependency.
- Correctness tests run in normal CI; performance suites remain named and informational.
- The frontend integration, CLI, UI, storage, tests, and operational documentation are verified from a clean checkout.

## 19. Pre-Implementation Checkpoints

Phase 3.5 is complete. Before the corresponding later baseline and presentation work:

1. Confirm `robustness-v0` semantics with the algorithm partner before calling its baseline authoritative.
2. Confirm candidate eligibility/removal semantics for critical counters against the frozen golden cases.
3. Complete and fingerprint the accepted 100K dataset/corpus before promoting historical baselines.
4. Calibrate the auto-strategy candidate cutoff on the authoritative host.
5. Establish the named browser/result-render scenarios used for authoritative comparison.

Phase 3.5 is implemented and verified. Phases 4–6 remain unstarted until the user explicitly starts them. All future implementation commits remain for the user to make.
