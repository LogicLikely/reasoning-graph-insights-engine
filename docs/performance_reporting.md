# Performance Reporting and Insights Lab

The application records backend performance runs for the current graph algorithms and for recalculation after a node-likelihood edit. The Insights Lab launches individual operations or a sequential standard stress suite in the current browser session, exposes the persisted run history, and charts performance trends for named benchmark sets. It does not schedule repetitions or enforce non-overlap across clients.

Open **Insights Lab** from the Graph Overview panel. The **Run** tab creates or selects a benchmark set and launches an operation against the active graph; each information button expands a plain-language explanation of what that operation measures and its important limitations. The **History** tab shows every persisted run newest-first in a bounded, scrollable table. Selecting a run opens a dedicated detail view with its complete recorded metadata and bounded result preview; **Back to all runs** returns to the table. The **Trends** tab compares the selected performance metric across stress-graph sizes, shapes, and benchmark sets.

## Recorded operations

| Lab action | Recorded operation |
| --- | --- |
| Minimal counter set | Greedy minimal counter set for the benchmark target |
| Time-bounded exhaustive search | Exhaustive minimum-cardinality counter-set search for the benchmark target, with a fixed server-owned time budget |
| Evidence impact ranking | Evidence-impact ranking for the benchmark target |
| Least robust node | Least robust node in the graph |
| Robustness ranking | Full node-robustness ranking |
| Leaf update | Reapply the current `priorOdds` to the ordinal-highest node, then recalculate and persist its ancestors |

Minimal counter set, time-bounded exhaustive search, and evidence impact ranking always use the graph's deterministic root node, independent of the node selected on the canvas. Stress graphs use the shared root ID `n-00000`, which keeps the target stable across graph sizes, shapes, and benchmark sets. The two robustness operations use the entire active graph. Fixture graphs support all five read-only operations; Leaf update is database-only.

The Lab deliberately chooses the ordinal-highest node for the leaf-update workload and tolerates that node not being a leaf. It reapplies the current value so the full update/recalculation/persistence path is measured without intentionally changing graph state. The report records whether the node was actually a leaf, the old and new values, affected-node count, maximum ancestor distance, and persisted-row count. Ordinary database edits that include `priorOdds` continue to create leaf-update records as well.

The Lab offers best-effort request cancellation for the greedy, exhaustive, and robustness operations. Evidence-impact and leaf-update runs do not expose Cancel because their current backend work cannot be stopped reliably and safely mid-operation. Request cancellation is distinct from the exhaustive search's expected time-budget outcome.

## Standard stress suite

**Run standard stress suite** executes all six operations against every currently installed balanced, wide, and shared-diamond database stress graph. Deep-chain graphs are intentionally excluded because their pathological depth can make operations extremely slow or terminate the backend; they remain available for deliberate individual runs. Included graphs follow the canonical order shown by the database-reset UI, and operations run sequentially, graph by graph, with Leaf update last. Every request uses the benchmark set selected when the suite starts. The suite does not render each graph and never overlaps its own requests.

The canonical matrix contains 100, 1,000, 10,000, and 100,000-node versions of each included shape. With all optional graphs installed, that is 12 graphs and 72 recorded operations. The 100-node graphs are generated from the same deterministic node and edge rules as the larger tiers; their derived likelihoods are recalculated for the smaller graph rather than copied from a larger stored graph.

Each graph-and-operation combination is executed and recorded once. No hidden warm-up runs or repetitions are added. Consequently, fixed-order cold-start and cache effects can remain visible in the data; warm-up policy is intentionally deferred.

The exhaustive operation has a two-minute compute budget per graph. With the nine 1K-and-larger balanced, wide, and shared-diamond graphs, its portion of a suite can therefore take up to about 18 minutes, plus the three completing 100-node searches, the other operations, and graph-loading overhead. A time-budget result is an expected completed request, so the suite records it and continues.

## Minimal-counter benchmark workload

The non-deep stress graphs preserve their original topology, node kinds, and near-neutral `1.001`/`0.999` edge likelihood ratios. During seeding, only root and objection priors are solved against a checked-in workload contract:

- the root begins at log odds `0.200` (about 55% probability);
- every objection contributes `-0.160` log odds at the root;
- seven objections leave the target at `-0.920`, above the `-1` cutoff;
- eight objections move it to `-1.080`, so the global minimum cardinality is eight.

This gives every standard graph a usable greedy result while holding the logical counter-set problem constant. The candidate universe still grows with graph size: 10, 100, 1,000, and 10,000 objections. The 100-node exhaustive runs can prove the eight-node minimum after 969 subset evaluations; the larger tiers expose the combinatorial search boundary under the fixed time budget.

The executable calibration check builds all three 100-node shapes in memory, runs both solvers, verifies the proven eight-node result, and prints the complete standard workload matrix without writing performance records:

```bash
dotnet test backend.Tests/backend.Tests.csproj \
  --filter FullyQualifiedName~StressGraphBenchmarkContractTests \
  --logger "console;verbosity=detailed"
```

If one request fails, the suite continues with the remaining combinations and lists the affected graph and operation in its final summary. The corresponding backend report appears in History when reporting reached the persistence stage.

**Stop suite** prevents the next operation from starting. It also requests cancellation of the current operation when that operation supports cancellation. Evidence-impact and Leaf update are allowed to finish before the suite stops. Runs already completed remain in History, and the Lab refreshes History when the suite finishes or stops.

## Results file

Runs are appended to the hardcoded repository-relative file:

```text
artifacts/performance/performance-runs.json
```

The file is gitignored. It is one valid JSON document with file-local, sequential run numbers:

```json
{
  "schemaVersion": 2,
  "benchmarkSets": [],
  "runs": []
}
```

A benchmark set has a backend-generated ID, a user-supplied name, and a creation timestamp. Runs launched from the Lab carry the selected set ID. Reports produced by ordinary application edits or callers that do not select a set remain unassigned and do not participate in Trends.

Each run has the following top-level fields:

| Field | Contents |
| --- | --- |
| `runNumber`, `startedAtUtc`, `benchmarkSetId` | Run identity, UTC start time, and optional benchmark-set membership |
| `algorithm` | Algorithm name, implementation, and calculation model |
| `build` | Actual Debug or Release configuration, .NET/OS/architecture/processor/GC context, and available source metadata |
| `graph` | Slug, graph type, node and edge counts, kind counts, known depth, and fingerprint |
| `invocation` | Data source, target or changed node, changed values, and parameters |
| `timing` | Load, in-memory compute, persistence where applicable, and total operation milliseconds |
| `resources` | Compute-scope CPU time, managed allocations, and garbage-collection deltas |
| `outcome` | Status, result count and digest, or error information |
| `details` | Measurements specific to that algorithm |

JSON persistence happens after the measured operation timers stop. Completed, timed-out, failed, and cancelled runs can all be recorded. A `timedOut` exhaustive run is a normal HTTP-200 partial result, not a failed request or a user cancellation.

The frontend reads the same document through `GET /api/performance-runs` and creates named sets through `POST /api/performance-runs/benchmark-sets`. Run numbers and benchmark-set IDs are assigned by the backend store; users supply only the benchmark-set name.

## Trends

Trends includes assigned stress-graph runs with completed outcomes and expected time-budget exhaustive results. The focused counter-set comparison renders those stopped searches as right-censored observations: the dashed line is the configured budget, while the marker is the measured stopped-run duration—not a completed runtime. In the generic views, CPU, allocation, and subset measurements from those runs are labeled as partial values captured at stop. Cancelled, failed, unassigned, and non-stress runs remain available in History but are not charted.

The **Metric** selector offers:

- **Compute time** (the default): wall-clock algorithm compute time.
- **Total operation time**: backend graph loading, computation, and operation persistence where applicable.
- **CPU time**: process-wide CPU consumed during the compute scope.
- **Managed allocations**: current-thread managed allocation volume during the compute scope, not retained or peak memory.
- **Subset evaluations** (time-bounded exhaustive search only): the number of fully evaluated candidate subsets, including the initial empty set. This hardware-independent count exposes the exhaustive search's combinatorial work and distinguishes timed-out runs that share the same wall-clock budget.

Repeated matching runs use the median of the selected metric. Runs without a valid value for that metric do not contribute to its median, sample count, or run-number list. Time and CPU values are shown in milliseconds; allocation values use one consistent IEC byte unit for the visible chart and table.

- **Scale within benchmark set** compares node counts inside one set, using graph shapes as series.
- **Compare benchmark sets** compares selected sets across node counts for one graph shape.

Each plotted value shows the median when more than one matching run exists and exposes its sample count. A single observation remains visibly identified as `n=1`; the Lab does not imply statistical confidence from one run. A data table accompanies the chart so the selected values are available without relying on color or pointer interaction.

Graph size always uses a logarithmic X-axis. The Y-axis can use linear spacing for absolute differences or logarithmic spacing for ratios and widely separated values. Positive values retain true logarithmic spacing; zero uses a separate baseline below the positive range. **How to read** expands an inline guide explaining the axes, series, points, selected metric, scale, and comparison caveats without opening another modal.

## Time-bounded exhaustive proof semantics

The exhaustive reference operation searches every reachable counter candidate; it no longer truncates the candidate universe. Candidates are ordered deterministically by greedy priority and then node ID, and subsets are considered in increasing cardinality. The backend owns one fixed 120,000 ms compute budget. The budget starts before problem preparation and has no Lab control.

- If a threshold-crossing subset is found, all smaller cardinalities have already been exhausted, so the returned set has proven minimum cardinality (`proofStatus: proven`, `stopReason: completed`).
- If the initial target already meets the threshold, the empty set is globally minimal and is immediately proven.
- If every subset is exhausted without crossing the threshold, the absence of a solution is proven.
- If the budget expires first, the request returns HTTP 200 with `outcome.status: timedOut`, `proofStatus: notProven`, and `stopReason: timeBudget`. This is an expected partial outcome. It is not a proof that no set exists and must not be graphed as an ordinary two-minute completion.
- Explicit client cancellation remains `cancelled` and takes precedence when it races the deadline. Unexpected exceptions remain `failed`.

Budget cancellation is cooperative inside problem preparation and candidate evaluation. The solver also checks elapsed monotonic time before beginning more subset work. An individual non-cooperative calculation step can therefore finish slightly after the nominal budget; any proof established by a just-completed evaluation is retained rather than discarded.

The JSON records the fixed budget, all-candidate counts, subset evaluations, the largest fully exhausted cardinality, the active cardinality and its evaluated/total combinations, the total subset-space size as an arbitrary-precision decimal string, preparation/search elapsed time, best result found, threshold values, proof status, timeout stage, and stop reason. `subsetEvaluations / totalPossibleSubsets` describes the fraction of the maximum subset space inspected; it is not a reliable percent-complete estimate because increasing-cardinality search may stop as soon as it proves a minimum.

## Optional manual benchmark protocol

Each individual operation button in Insights Lab launches and records exactly one operation. **Run standard stress suite** is the explicit exception: it queues one recorded run for every included standard stress-graph and operation combination. For a manual comparison, keep runs separate and hold inputs constant; do not start another client while a run or suite is active.

For each algorithm and graph combination:

1. Prefer starting the backend in Release mode:

   ```bash
   dotnet run --configuration Release --project backend/backend.csproj
   ```

   Release is recommended, not required. The report records the configuration that actually ran, so Debug results remain identifiable.

2. Select or create the intended benchmark set in the Lab.
3. Execute each measured operation one at a time, or use **Run standard stress suite** for the installed balanced, wide, and shared-diamond stress graphs. Each combination is recorded once; the Lab does not launch a hidden warm-up. With all 12 standard graphs installed, allow roughly 18 minutes for the nine 1K-and-larger exhaustive attempts if they all consume their full two-minute budget; the three 100-node exhaustive runs are designed to complete.
4. If repetitions are useful, keep the graph, canonical target, and inputs unchanged. Trends retains the raw runs and plots the selected metric's median with the sample count.

The Lab's same-value leaf update does not require restoration. If benchmarking an ordinary value-changing likelihood edit instead, restore the leaf to the same starting likelihood before every measured edit. A restoration performed through the application creates another recorded run and must be excluded from the comparison.

## Interpreting resource measurements

Wall-clock compute time is the primary algorithm metric. Total operation time adds loading and other backend work; leaf-update totals also include persistence. CPU, allocation, and garbage-collection values are supporting diagnostics:

- CPU time is a process-wide delta. Other backend activity and overlapping runs can contaminate it, so keep the backend otherwise idle and enforce non-overlap manually.
- Managed allocations are measured for the current thread. If execution changes managed threads, `allocatedBytes` is `null` and `allocationMeasurement` reports that the thread changed.
- Allocation figures do not include native allocations, allocations on other threads, or peak memory. Garbage-collection deltas are also process-wide.

Build configuration and the remaining environment metadata are recorded for interpretation but are not enforced by the Lab. The person running a benchmark is responsible for keeping the environment and inputs suitably consistent.
