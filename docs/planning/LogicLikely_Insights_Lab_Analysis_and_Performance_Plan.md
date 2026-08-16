# LogicLikely Insights Lab Analysis and Performance Plan

**Status:** Phases 0–1 implemented; Phases 2–6 have not started

**Last updated:** 2026-08-15

**Scope:** Analysis algorithms, repeatable performance measurement, historical comparison, results UX, and safe GraphMap visualization

## 1. Purpose

Build an internal Insights Lab that can:

- Run and explain the graph engine's analysis algorithms.
- Measure database, REST API, algorithm, browser, and GraphMap work independently.
- Compare compatible results over time as algorithms and infrastructure change.
- Exercise deterministic graphs from 1,000 through 100,000 nodes without asking GraphMap to render an unsafe view.
- Preserve correctness evidence alongside performance data.

This plan complements the original [Structural Insights Engine Implementation Plan](./LogicLikely_Structural_Insights_Engine_Implementation_Plan.md). It turns those algorithm goals into a versioned, measurable, and inspectable workflow.

## 2. Locked Decisions

| Area | Decision |
|---|---|
| Lab routes | Add internal `/lab/analyze` and `/lab/performance` routes, separate from the existing demo. |
| Execution | Support explicit UI-queued runs and repeatable CLI suites. Ordinary navigation and search are not persisted as benchmark runs. |
| History | Store benchmark history in a reset-safe PostgreSQL `benchmark` schema and support versioned JSON export. |
| Comparison host | The current stable ARM64 Mac, profile ID `ll-arm64-mac-primary`, is the authoritative performance host. Hosted CI timing is informational only. |
| Dataset matrix | Balanced, wide, deep, and shared-diamond graphs at 1K, 10K, and 100K nodes. |
| Critical counters | Implement `exact`, `greedy`, and `auto` strategies in v1. |
| Counter objective | Find the fewest eligible counters that move the target to or below the configured probability threshold. |
| Counter threshold | Configurable; default log-odds `-1`, approximately `26.9%` probability. |
| Auto strategy | Select by a deterministic candidate-count cutoff calibrated to about two seconds on the authoritative host. |
| Robustness | Preserve the merged behavior as `robustness-v0` until its semantics are deliberately revised and versioned. |
| Result retention | Store compact summaries, distributions, canonical digests, and at most the top 100 ranked items in PostgreSQL. Full output is an optional external JSON artifact. |
| GraphMap package | Make narrow source changes, package GraphMap `0.3.0` locally, and update the vendored immutable tarball dependency. |
| GraphMap warning | A prospective view with 1,000 through 1,200 materialized nodes is allowed and shows a non-modal warning. |
| GraphMap block | A prospective view above 1,200 materialized nodes is rejected before expensive rendering work. |
| GraphMap override | No end-user “render anyway” action. A developer may deliberately change the configured budget in code or a controlled test profile. |
| CI policy | Correctness remains required. Performance runs are manual or scheduled, named, artifact-producing, and non-blocking. |
| Baseline timing | Authoritative historical baselines begin after the accepted 100K dataset/corpus commit is available. |

## 3. Current Baseline and Constraints

### 3.1 Proven behavior

- GraphMap is responsive with compact projections of 10K-node graphs.
- GraphMap search is fast when the union of matches and required ancestor chains is small.
- Full expansion is acceptable around the 1K dataset scale.
- A large source graph is not itself a reason to block GraphMap; the prospective visible projection is what matters.
- Graph fetches and current REST operations are fast at 10K, but 100K must be measured rather than assumed.

### 3.2 Current gaps

- Analysis responses are mostly logged to the browser console rather than presented as durable, explainable results.
- The Phase 1 benchmark tables, correlation/timing seams, export validation, and worker isolation foundation exist; repeatable performance harnesses and historical comparison UI do not yet exist.
- Existing algorithm endpoints load and map the complete graph for each request.
- Strongest-path calculations return scalar scores without the ordered node/edge chain needed for visualization.
- The current minimal-counter endpoint is a one-shot heuristic, not the planned exact/greedy pair.
- Current GraphMap search warns about large results only after search nodes have been materialized and laid out.
- Expand All, branch expansion, Show More, and future controlled result focus have no shared render budget.
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
        +----> Playwright browser + GraphMap journey
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

## 5. Operation and Measurement Registry

Use one extensible registry rather than one-off buttons and result handlers. Core analysis operations appear in both the Lab and benchmarks; supporting diagnostics are benchmark-only in v1.

| Operation key | Purpose | v1 exposure | Initial result surface |
|---|---|---|---|
| `graph.catalog` | Measure catalog retrieval/count aggregation with all stress graphs installed. | Benchmark diagnostic | Timing and count summary |
| `graph.fetch` | Fetch, transfer, parse, and adapt a complete graph. | Benchmark diagnostic | Graph/payload summary |
| `graph.search` | Find matches and the complete ancestor union, then admit or reject visualization. | Benchmark diagnostic | Counts, status, optional safe projection |
| `path.strongest` | Find strongest paths in the requested direction. | Analysis and benchmark | Summary, ranked paths, GraphMap focus |
| `path.single-pair` | Exercise the current min/max single-pair path diagnostic. | Benchmark diagnostic | Diagnostic result and timing |
| `evidence.impact-ranking` | Rank supporting and counter evidence by target probability impact. | Analysis and benchmark | Summary, distribution, top 100 |
| `counter.critical-set` | Find a threshold-reaching counter set with exact, greedy, or auto strategy. | Analysis and benchmark | Selected counters, quality, GraphMap focus |
| `node.robustness` | Rank nodes by the versioned robustness calculation. | Analysis and benchmark | Least robust summary, distribution, top 100 |
| `likelihood.recalculate` | Recalculate a selected node/ancestor chain after a defined change. | Benchmark diagnostic | Before/after likelihood summary |

Every operation returns a common envelope containing:

- Operation key and semantic version.
- Requested and actually used strategy.
- Graph and target identifiers.
- Canonical parameters and parameter digest.
- Execution status: `queued`, `running`, `succeeded`, `failed`, `timed-out`, `cancelled`, `crashed`, or `skipped`.
- Visualization admission: `not-requested`, `allowed`, `warned`, or `blocked`.
- Summary metrics.
- Total result cardinality.
- Up to 100 deterministic result items.
- Canonical result digest.
- Optional ordered path projections.
- Phase timings and resource measurements.
- Warnings, validation failures, and error details.

Execution and visualization status are independent. For example, a strongest-path calculation can succeed while GraphMap blocks its path projection.

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
- A path over the GraphMap budget remains a valid algorithm result even when its canvas projection is blocked.

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

## 7. GraphMap Render-Budget Contract

### 7.1 Exact thresholds

The decision is based on the prospective materialized view, not the total source graph:

| Prospective materialized nodes | Decision |
|---:|---|
| 0–999 | Allow without a budget warning |
| 1,000–1,200 | Allow and show a persistent, non-modal warning |
| 1,201 or more | Block before materialization/layout/rendering |

The safety count includes every node ReactFlow would materialize, including GraphMap's synthetic “More” controls. Telemetry also breaks out canonical graph nodes, synthetic nodes, and projected edges so the user-facing message remains explainable.

Node count is the initial hard safety policy. Projected edge count and density are recorded for admitted views; a node-blocked view may stop before calculating edges. A separate edge warning/block threshold will be calibrated on representative dense graphs rather than guessed before measurement.

The legacy search-only warning above 400 nodes is removed and replaced by this shared contract; otherwise it would contradict the 0–999 no-warning range.

### 7.2 Public package contract

GraphMap `0.3.0` should expose a typed contract similar to:

```ts
type GraphMapRenderBudget = {
  warnAtNodes?: number       // default: 1000, inclusive
  blockAboveNodes?: number   // default: 1200; 1200 is allowed
}

type RenderBudgetSource =
  | "initial"
  | "graph-update"
  | "node-toggle"
  | "show-more"
  | "expand-all"
  | "search"
  | "controlled-view"

type RenderBudgetDecision = "allow" | "warn" | "block"
```

Node settings accept finite positive integers and must satisfy `warnAtNodes <= blockAboveNodes`. Invalid consumer configuration must never disable the safeguard: use the safe defaults and emit a structured configuration error. Edge thresholds are not part of the initial `0.3.0` contract; add them only after Phase 6 calibration demonstrates a need.

Expose the budget through GraphMap, CanonicalGraphMap, and AdaptedGraphMap. Emit a structured callback containing:

- Source and decision.
- Current and candidate graph-node counts.
- Candidate synthetic-node and total-node counts.
- Current edge count and candidate edge count when computed.
- Whether candidate counts are exact or an “at least” lower bound.
- Warning and blocking thresholds.
- Preflight duration.
- Block reason.

Callbacks fire once per decision, not on every React render.

### 7.3 Admission behavior

Use one shared, pure admission decision for:

- Initial and replacement graph projections.
- Node toggle.
- Show More and Show Fewer.
- Expand All and Collapse All.
- Search match-plus-ancestor projection.
- Controlled strongest-path, critical-counter, evidence-impact, and robustness focus.
- Future consumer-supplied result projections.

The preflight must:

1. Derive prospective distinct nodes iteratively and cycle-safely, stopping as soon as 1,201 is established when an exact count is not otherwise needed.
2. Count shared-DAG nodes once.
3. Include synthetic controls in the materialized total.
4. Classify the node request as allow, warn, or block.
5. If node-admitted, compute projected edges without invoking presentation callbacks and record density.
6. Commit render-affecting expansion/canvas-view state atomically only when admitted. Search intent and result metadata may update while the canvas retains its safe projection.
7. Invoke Dagre, node presenters, ReactFlow state changes, and viewport fitting only for an admitted projection.

The existing recursive expansion traversal must not be used to preflight a 100K-deep chain. A node-blocked projection may omit its exact edge count rather than scanning or materializing edges unnecessarily.

### 7.4 User behavior

- Warning is non-modal, accessible, and does not require confirmation.
- Blocked expansion leaves expansion state, selection, and viewport unchanged.
- Collapse, Show Fewer, clearing search, and other node-reducing actions always remain available.
- Expand All is disabled or safely rejected when its prospective view exceeds the budget.
- Blocked search retains the query, match count, and required-node count while leaving the last safe ordinary graph visible.
- Blocked search copy says “required to visualize,” not “total shown,” and Fit View is disabled.
- A blocked controlled algorithm path leaves the complete textual/tabular result available.
- No path is silently truncated; that would misrepresent the reasoning chain.
- No end-user bypass is provided.
- A blocked replacement graph must not display the previous graph as though it were the new one. Use the new graph's safe collapsed projection or an explicit empty blocked state.

Compressed, windowed, or paged path visualization may be designed later. It is not part of the first release.

### 7.5 View readiness

Add an `onViewLifecycle` phase callback so the consumer can observe:

- Search/preflight completed.
- Layout completed.
- React nodes committed.
- Deferred edges committed.
- Viewport fitting completed.
- View warned or blocked.

Reserve `onViewReady` for the terminal stable state of an admitted projection, whether allowed normally or admitted with a warning. A blocked view emits the lifecycle/render-budget event and never emits `onViewReady` for the rejected projection.

### 7.6 Package delivery

- Implement and test changes in the GraphMap source repository.
- Bump the package to `0.3.0`.
- Add a new immutable `0.3.0` npm tarball; do not overwrite the `0.2.0` artifact.
- Record its checksum/provenance.
- Update the vendor README/checksum metadata, `.gitignore` rules if needed, `package.json`, and the lockfile to the exact new tarball.
- Verify `npm ci`, package type exposure, unit tests, consumer tests, and production build.

## 8. Results and Visualization UX

### 8.1 `/lab/analyze`

Provide:

- Graph and target selectors.
- Operation registry and version display.
- Operation-specific parameters.
- Exact/greedy/auto strategy control where applicable.
- Configurable counter probability threshold.
- Run, cancel, and status controls.
- Summary cards.
- Distribution and compact quality metrics.
- Deterministic top-100 table with total count.
- Raw JSON export for the current result.
- GraphMap result focus after render-budget admission.

GraphMap presentation should use restrained outlines, badges, or card accents without overwriting the established support/rebut edge language. Selecting a result row may focus:

- A strongest path.
- Critical counters and their relevant chains.
- Evidence-impact context.
- The path responsible for a robustness score.

If focus is blocked, the selected row, ordered textual path, counts, and reason remain visible.

### 8.2 `/lab/performance`

Provide:

- Named suite selection.
- Serial queue with pending/running/completed states.
- Cancellation.
- Per-run environment and dataset identity.
- Phase timeline.
- Sample distributions and percentile summaries.
- Execution failure/timeout/crash inspection alongside independent visualization-admission inspection.
- Historical trend and compatible-run comparison.
- Versioned JSON export.
- Explicit baseline promotion on the authoritative host.

Do not mount unbounded result tables. History and ranked outputs are paged or limited to the retained top 100.

## 9. Instrumentation Model

### 9.1 Correlation

Assign a run ID and sample ID at the orchestrator. Propagate them through HTTP, server phases, worker execution, browser marks, persisted rows, and exported JSON.

### 9.2 Required phases

| Layer | Phases |
|---|---|
| PostgreSQL/repository | Connection/open wait, graph lookup, node query, edge query, evidence JSON materialization, catalog aggregation |
| Backend service/API | DTO mapping, validation, calculation-context construction, algorithm subphases, ranking, result shaping, serialization |
| Transport | Response bytes, time to first byte, full transfer |
| Browser data | Axios receipt/parse, domain mapping, GraphMap adapter, search-index construction |
| GraphMap | Preflight, node/edge materialization, Dagre layout, React commit, deferred edge commit, viewport fit, `onViewLifecycle`, `onViewReady` |
| End to end | User/runner action through stable result and stable visual state |

Server phases should be available to the controlled Lab journey through structured timing data or `Server-Timing` headers while also being persisted with the sample.

### 9.3 Required measurements

- Wall-clock duration in a named unit.
- Iteration and warm/cold/JIT/cache classification.
- Requested, canonical, synthetic, and rendered node counts.
- Requested and rendered edge counts and density when computed.
- Search match count and complete required ancestor-union count.
- Result cardinality.
- Request/response bytes.
- Allocations, GC counts, CPU time, and working-set change where practical.
- Execution outcome plus the independent visualization-admission outcome.
- Exception/error classification without sensitive data.

Console output inside measured loops must be removed or disabled in benchmark execution because it distorts results.

## 10. Run Identity, Persistence, and Export

### 10.1 Reset-safe storage

Create an idempotently initialized PostgreSQL `benchmark` schema that is deliberately untouched by graph seed/reset scripts.

Minimum tables:

- `benchmark.runs`: immutable identity/manifest fields plus mutable lifecycle status and completion fields.
- `benchmark.samples`: per-iteration operation/phase measurements.
- `benchmark.outputs`: compact result summary, top-100 payload, distribution, digest, and optional artifact reference.

Prove with integration coverage that resetting graph data does not delete benchmark history.

### 10.2 Run manifest

Capture:

- Run ID, name, status, start/end time, and runner type.
- Scenario and operation keys.
- Graph slug, shape, actual node/edge counts, and maximum depth.
- Dataset generator version.
- Corpus ID and fingerprint.
- Topology/input fingerprint.
- Algorithm key and semantic version.
- Requested/used strategy.
- Canonical parameters and digest.
- Target node/path IDs.
- Git commit SHA and dirty-worktree flag.
- Build configuration.
- .NET, Node, browser, GraphMap, PostgreSQL, and relevant dependency versions.
- OS, architecture, CPU, logical core count, and memory.
- Environment profile name.
- Warmup/sample/cache policy.
- Timeout/cancellation policy.

The graph slug alone is not a sufficient dataset identity.

### 10.3 Compatibility rules

Default comparison requires matching:

- Scenario and operation.
- Dataset/input fingerprint.
- Algorithm semantic version.
- Canonical parameter digest.
- Environment profile.
- Build mode and measurement units.

The UI rejects or visibly labels incompatible comparisons. It never silently presents them as a regression/improvement pair.

### 10.4 JSON format

- Version the export schema.
- Include the complete manifest, samples, compact outputs, and digests.
- Validate exported JSON against its versioned schema and canonical digests.
- Upload raw run JSON as manual/scheduled CI artifacts.
- Do not commit every noisy run to the repository.
- Commit only deliberately promoted compact baseline metadata when desired.

## 11. Harnesses and Execution Policy

### 11.1 Backend algorithm harness

Add a dedicated BenchmarkDotNet project for pure calculation-context and algorithm benchmarks:

- Release build.
- Deterministic immutable input per invocation.
- Derived state cloned or recalculated per iteration when mutation is possible.
- Allocation and GC diagnostics.
- Parameterized operation, graph, target, and strategy.

### 11.2 Orchestrator and CLI

Add a `tools/performance` runner that:

- Uses the common scenario registry and JSON schema.
- Seeds/validates required graphs outside measured samples.
- Runs suites serially by default.
- Starts isolated workers for risky algorithms.
- Applies timeout and cancellation.
- Captures partial samples if a worker crashes.
- Imports successful and failed outcomes into benchmark storage.

### 11.3 API and browser harness

Use Playwright for controlled API/UI journeys:

- Actual REST graph fetch.
- Browser parse and adaptation.
- Search and preflight.
- Safe GraphMap layout/render.
- Warned and blocked render-budget outcomes.
- Lab result rendering and result focus.

Use React Profiler/performance marks, `onViewLifecycle`, and terminal `onViewReady` for controlled phase boundaries. DOM settling alone is only a fallback approximation.

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
|---|---:|---:|---:|---|
| Balanced tree | Yes | Yes | Yes | Breadth and ordinary depth |
| Wide star | Yes | Yes | Yes | Fan-out, payload, ranking, and layout width |
| Deep chain | Yes | Yes | Yes | Recursion, path length, and oversized search/path projection |
| Shared-diamond DAG | Yes | Yes | Yes | Shared ancestry, unique counting, and roughly 2x edge density |

Persist generator version, corpus ID/fingerprint, and topology fingerprint for every run.

### 12.1 Canonical scenarios

| Scenario | 1K expectation | 10K expectation | 100K expectation |
|---|---|---|---|
| Catalog fetch with all graphs installed | Measure | Measure | Measure |
| Full graph REST fetch | Measure | Measure | Measure |
| Initial collapsed GraphMap projection | Render | Render | Render |
| Full expansion | Render with warning at the actual materialized count | Stop at the blocking boundary before layout and record preflight | Stop at the blocking boundary before layout and record preflight |
| Search: no hit | Compute, no result view | Compute, no result view | Compute, no result view |
| Search: single shallow hit | Render if admitted | Render if admitted | Render if admitted |
| Search: known multi-hit such as `999` | Render or warn by count | Admit or block by required union | Admit or block by required union |
| Search: deepest-chain hit | Render through budget | Expected block when over budget | Expected block when over budget |
| Strongest path | Compute and focus if admitted | Compute; focus subject to budget | Compute; focus subject to budget |
| Evidence-impact ranking | Compute | Compute | Compute or record timeout/failure |
| Node robustness ranking | Compute | Isolated compute | Isolated compute |
| Likelihood recalculation | Compute | Compute | Compute |
| Critical counter exact | Candidate-limited | Candidate-limited | Candidate-limited |
| Critical counter greedy/auto | Compute | Compute | Compute or record timeout/failure |

Search scenarios record both match count and complete required-node union. A blocked render is an expected successful safety outcome, not an algorithm failure.

### 12.2 Boundary fixtures

Add deterministic projection fixtures for:

- 999 materialized nodes: allow, no warning.
- 1,000 materialized nodes: allow and warn.
- 1,200 materialized nodes: allow and warn.
- 1,201 materialized nodes: block.

Cover trees, shared DAGs, cycles, synthetic More controls, graph replacement, and controlled algorithm views.

## 13. Phased Implementation Plan

### Phase 0 — Freeze contracts

Implementation record: [Phase 0 frozen contracts](../contracts/insights-lab/phase-0-contracts.md).

Deliver:

- Operation registry and semantic-version rules.
- Critical-counter candidate/removal contract and deterministic tie rules.
- `robustness-v0` characterization and partner semantic checkpoint.
- Run manifest, sample, output, failure-state, and JSON contracts.
- GraphMap render-budget contract.
- Golden fixtures for current algorithm behavior.

Acceptance:

- The 999/1,000/1,200/1,201 render decisions are unambiguous.
- Current algorithm outputs have canonical digests on small fixtures.
- Incompatible-run rules are executable specifications.
- No benchmark history is declared authoritative before these contracts are frozen.

### Phase 1 — Measurement and persistence foundation

Implementation record: [Phase 1 frozen contracts](../contracts/insights-lab/phase-1-contracts.md).

Deliver:

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

### Phase 2 — GraphMap 0.3.0 safety and lifecycle

Deliver:

- Shared iterative render-budget preflight.
- Atomic expansion state transitions.
- Search, expansion, graph-update, and controlled-view admission.
- Warning and blocked UX.
- Structured render-budget events.
- Controlled result focus.
- `onViewLifecycle`.
- `onViewReady`.
- Versioned package/tarball integration.

Acceptance:

- All entry paths pass boundary tests at 999, 1,000, 1,200, and 1,201.
- A rejected projection does not invoke node presenters, Dagre, ReactFlow state updates, or viewport fitting. An independently admitted collapsed fallback for a replacement graph may render.
- A 100K-deep candidate establishes the 1,201-node block without recursion failure or work proportional to layout of the complete chain.
- Blocked same-graph actions preserve the last safe state; graph replacement never leaves stale prior-graph content labeled as current.
- Node-reducing actions remain available.
- GraphMap `0.3.0` installs cleanly from the vendored tarball and exposes the new public types.

### Phase 3 — Versioned analysis operations

Deliver:

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

### Phase 4 — Benchmark runners

Deliver:

- BenchmarkDotNet pure-algorithm project.
- CLI scenario orchestrator.
- Serial queue service/orchestration contract consumed by the CLI and, in Phase 5, the Lab UI.
- Playwright API/browser suites.
- Warm/cold/named profile support.
- Artifact production and benchmark-store persistence.

Acceptance:

- A failed or stack-overflowing worker cannot terminate the API or lose prior samples.
- Dataset digests make repeated runs reproducible.
- DB/API/algorithm/browser phases reconcile within documented overhead.
- Warned and blocked GraphMap outcomes are captured as first-class measurements.

### Phase 5 — Insights Lab UI

Deliver:

- `/lab/analyze`.
- `/lab/performance`.
- Parameter controls and queued run status.
- Summary, distribution, top-100, history, and compatibility-aware comparison views.
- Safe GraphMap result focus.
- Versioned JSON export.

Acceptance:

- Every registered analysis displays its operation/version, execution status, summary or empty state, and total count.
- A 100K analysis can be inspected without mounting thousands of rows or invoking an unsafe GraphMap layout.
- Over-budget results display the admission status/reason and an ordered textual path when the operation returns one.
- Warning and block messages are accessible.
- Stale responses cannot replace newer graph/target selections.

### Phase 6 — Calibration and authoritative baselines

Deliver:

- Auto-strategy candidate cutoff calibrated as the largest count whose median exact core runtime is at most two seconds for every required calibration scenario under the authoritative `standard` profile.
- Edge-density evidence and, if needed, GraphMap edge thresholds.
- Deep-chain robustness validation and iterative equivalent if required.
- Named authoritative baseline suite on `ll-arm64-mac-primary`.
- Manual/scheduled informational workflow and artifacts.

Acceptance:

- The accepted 100K dataset/corpus has stable identity fingerprints.
- One named baseline covers every supported operation, shape, and size or records an explicit skip/timeout/failure.
- Full expansion succeeds only in the supported range; 10K/100K rejection establishes the blocking boundary without invoking presenters, layout, or the rejected ReactFlow update, and records preflight duration.
- Every baseline has complete environment, code, dataset, algorithm, and parameter identity.
- Performance remains informational and does not gate ordinary pull requests.

### Dependency order

```text
Phase 0 ──┬──> Phase 1 ──> Phase 3 ──> Phase 4 ──┐
          └──> Phase 2 ──────────────────────────┼──> Phase 5 ──> Phase 6
                                                 ┘
```

GraphMap admission safety must land before algorithm-driven canvas focus is enabled.

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

### 14.3 GraphMap

- Every admission source at 999, 1,000, 1,200, and 1,201.
- Unique counting in shared DAGs.
- Cycle-safe and deep iterative preflight.
- Synthetic More-node accounting.
- Atomic rejected expansion.
- Search retains query/counts and skips layout.
- Graph replacement cannot display stale graph identity.
- Reducing actions remain enabled.
- One callback per decision.
- Accessible warning/live-region behavior.
- `onViewLifecycle` ordering and terminal `onViewReady` behavior.
- Package tarball public-type consumer test.

### 14.4 Frontend Lab

- Operation-specific controls and validation.
- Queue, cancellation, and status transitions.
- Summary/top-100/distribution rendering.
- Compatible and incompatible history comparisons.
- Safe row-to-GraphMap focus.
- Blocked results show execution status, admission reason, counts, and an ordered textual path when applicable.
- Stale response suppression.
- Storybook states, accessibility, Vitest, Cucumber, and Playwright journeys.

## 15. CI and Baseline Policy

- Keep deterministic correctness, contract, digest, and GraphMap safety tests in ordinary CI.
- Add PostgreSQL integration coverage using a controlled real database fixture.
- Keep performance runs out of pull-request pass/fail decisions.
- Provide manual and optional scheduled performance workflows.
- Upload versioned JSON and harness artifacts.
- Treat GitHub-hosted timing as diagnostic because host hardware varies.
- Promote a baseline only from `ll-arm64-mac-primary`, a clean Release build, and an accepted dataset fingerprint.
- Store dirty-worktree runs for investigation if explicitly requested, but do not promote them as authoritative.

## 16. Risks and Mitigations

| Risk | Mitigation |
|---|---|
| Full graph loading dominates every algorithm | Time graph retrieval/context separately; enable future reuse only after baseline semantics are preserved. |
| Exact counter search explodes | Candidate cutoff, timeout, cancellation, isolated process, and explicit skipped/timed-out outcomes. |
| Greedy result is mislabeled minimal | Report strategy and threshold attainment; reserve optimal/minimal language for proven exact output. |
| Robustness semantics are misunderstood | Freeze `robustness-v0`, expose raw components/path, complete partner checkpoint, and version revisions. |
| Deep recursion terminates a process | Isolated workers, deep scenarios, iterative equivalents, and crash recording. |
| GraphMap freezes before showing a warning | Shared pre-layout admission, atomic state, hard block above 1,200. |
| A dense graph defeats the node ceiling | Record edges/density from day one and calibrate a separate edge budget. |
| Search/path truncation misrepresents reasoning | Block visualization and retain full textual result; never silently truncate. |
| Benchmark reset deletes history | Separate reset-safe schema and integration test. |
| Seed or corpus changes invalidate trends | Persist generator, corpus, topology, and input fingerprints. |
| Hosted runner noise looks like regression | Authoritative named host; hosted results informational only. |
| Large outputs overwhelm DB/UI | Top 100, summaries/distributions, digest, paging, optional external artifact. |
| Instrumentation changes measured behavior | Keep phases lightweight, measure overhead, and preserve raw samples. |

## 17. Non-Goals

- Rendering or virtualizing complete 10K/100K graphs.
- Replacing ReactFlow or Dagre.
- A user-facing unsafe render override.
- Silent path truncation.
- Compressed/windowed GraphMap paths in the first release.
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
- Robustness has a frozen semantic contract, rich path/result output, and safe deep-graph behavior.
- Database, API, algorithm, transport, browser, and GraphMap phases are independently measurable.
- Explicit runs persist across graph resets and export portably.
- Compatible historical runs can be compared without conflating dataset, algorithm, parameter, or environment changes.
- GraphMap warns at 1,000, allows through 1,200, and blocks 1,201+ consistently at every view entry point.
- No rejected projection invokes expensive layout/render work. Same-graph rejection preserves the last safe state; replacement uses the new graph's safe fallback or explicit blocked state.
- 1K full expansion is covered; 10K/100K visualization is safely partial or rejected.
- The 12-graph deterministic matrix has a named authoritative baseline or an explicit recorded outcome.
- Correctness and safety tests run in normal CI; performance suites remain named and informational.
- The GraphMap `0.3.0` tarball, frontend integration, CLI, UI, storage, tests, and operational documentation are verified from a clean checkout.

## 19. Pre-Implementation Checkpoints

No further user decision is required before implementation begins. The following are execution checkpoints already contained in the plan:

1. Confirm `robustness-v0` semantics with the algorithm partner before calling its baseline authoritative.
2. Freeze candidate eligibility/removal semantics for critical counters in golden cases.
3. Complete and fingerprint the accepted 100K dataset/corpus before promoting historical baselines.
4. Calibrate the auto-strategy candidate cutoff on the authoritative host.
5. Measure dense views and set an edge budget if the evidence requires one.

Implementation authorization currently extends through Phase 1 only. Phases 2–6 remain unstarted until separately authorized.
