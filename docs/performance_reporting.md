# Performance Reporting and Insights Lab

The application records backend performance runs for the current graph algorithms and for recalculation after a node-likelihood edit. The Insights Lab launches individual operations or a sequential stress suite in the current browser session, exposes the persisted run history, and charts compute-time trends for named benchmark sets. It does not schedule repetitions or enforce non-overlap across clients.

Open **Insights Lab** from the Graph Overview panel. The **Run** tab creates or selects a benchmark set and launches an operation against the active graph; each information button expands a plain-language explanation of what that operation measures and its important limitations. The **History** tab shows every persisted run newest-first in a bounded, scrollable table. Selecting a run opens a dedicated detail view with its complete recorded metadata and bounded result preview; **Back to all runs** returns to the table. The **Trends** tab compares compute time across stress-graph sizes, shapes, and benchmark sets.

## Recorded operations

| Lab action | Recorded operation |
| --- | --- |
| Minimal counter set | Greedy minimal counter set for the benchmark target |
| Bounded minimal counter set | Bounded brute-force minimal counter set for the benchmark target |
| Evidence impact ranking | Evidence-impact ranking for the benchmark target |
| Least robust node | Least robust node in the graph |
| Robustness ranking | Full node-robustness ranking |
| Leaf update | Reapply the current `priorOdds` to the ordinal-highest node, then recalculate and persist its ancestors |

Minimal counter set, bounded minimal counter set, and evidence impact ranking always use the graph's deterministic root node, independent of the node selected on the canvas. Stress graphs use the shared root ID `n-00000`, which keeps the target stable across graph sizes, shapes, and benchmark sets. The two robustness operations use the entire active graph. Fixture graphs support all five read-only operations; Leaf update is database-only.

The Lab deliberately chooses the ordinal-highest node for the leaf-update workload and tolerates that node not being a leaf. It reapplies the current value so the full update/recalculation/persistence path is measured without intentionally changing graph state. The report records whether the node was actually a leaf, the old and new values, affected-node count, maximum ancestor distance, and persisted-row count. Ordinary database edits that include `priorOdds` continue to create leaf-update records as well.

The Lab offers best-effort request cancellation for the greedy, bounded, and robustness operations. Evidence-impact and leaf-update runs do not expose Cancel because their current backend work cannot be stopped reliably and safely mid-operation.

## Stress suite

**Run stress suite** executes all six operations against every currently installed database stress graph. Graphs follow the canonical order shown by the database-reset UI, and operations run sequentially, graph by graph, with Leaf update last. Every request uses the benchmark set selected when the suite starts. The suite does not render each graph and never overlaps its own requests.

Each graph-and-operation combination is executed and recorded once. No hidden warm-up runs or repetitions are added. Consequently, fixed-order cold-start and cache effects can remain visible in the data; warm-up policy is intentionally deferred.

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

JSON persistence happens after the measured operation timers stop. Completed, failed, cancelled, and bounded-but-not-proven runs can all be recorded.

The frontend reads the same document through `GET /api/performance-runs` and creates named sets through `POST /api/performance-runs/benchmark-sets`. Run numbers and benchmark-set IDs are assigned by the backend store; users supply only the benchmark-set name.

## Trends

Trends includes assigned stress-graph runs with successful outcomes and uses `computeElapsedMilliseconds` as the performance measure. Cancelled, failed, timed-out, unassigned, and non-stress runs remain available in History but are not charted.

- **Scale within benchmark set** compares node counts inside one set, using graph shapes as series.
- **Compare benchmark sets** compares selected sets across node counts for one graph shape.

Each plotted value shows the median when more than one matching run exists and exposes its sample count. A single observation remains visibly identified as `n=1`; the Lab does not imply statistical confidence from one run. A data table accompanies the chart so the selected values are available without relying on color or pointer interaction.

## Bounded brute-force proof semantics

The bounded minimal counter-set operation has a hardcoded limit of 20 reachable counter candidates. Candidates are ordered deterministically by greedy priority and then node ID, and only the first 20 are searched. Subsets are considered in increasing cardinality.

- With 20 or fewer candidates, completion is reported as `proven`: the full candidate universe was available to the search. A returned set has proven minimum cardinality; if no set crosses the threshold, that absence is also proven for the available candidates.
- With more than 20 candidates, the run is always `notProven` with stop reason `candidateLimit`, because candidates were excluded. A result may still cross the threshold, but global minimality has not been established.
- There is no separate elapsed-time cutoff. The bounded search runs until it finds a threshold-crossing set, exhausts its candidate universe, is cancelled, or fails.

The JSON records total, searched, and excluded candidate counts, subset evaluations, fully exhausted cardinality, threshold values, proof status, and stop reason.

## Optional manual benchmark protocol

Each individual operation button in Insights Lab launches and records exactly one operation. **Run stress suite** is the explicit exception: it queues one recorded run for every installed stress-graph and operation combination. For a manual comparison, keep runs separate and hold inputs constant; do not start another client while a run or suite is active.

For each algorithm and graph combination:

1. Prefer starting the backend in Release mode:

   ```bash
   dotnet run --configuration Release --project backend/backend.csproj
   ```

   Release is recommended, not required. The report records the configuration that actually ran, so Debug results remain identifiable.

2. Select or create the intended benchmark set in the Lab.
3. Execute each measured operation one at a time, or use **Run stress suite** for the installed stress graphs. Each combination is recorded once; the Lab does not launch a hidden warm-up.
4. If repetitions are useful, keep the graph, canonical target, and inputs unchanged. Trends retains the raw runs and plots their median compute time with the sample count.

The Lab's same-value leaf update does not require restoration. If benchmarking an ordinary value-changing likelihood edit instead, restore the leaf to the same starting likelihood before every measured edit. A restoration performed through the application creates another recorded run and must be excluded from the comparison.

## Interpreting resource measurements

Wall-clock compute time is the primary algorithm metric. Total operation time adds loading and other backend work; leaf-update totals also include persistence. CPU, allocation, and garbage-collection values are supporting diagnostics:

- CPU time is a process-wide delta. Other backend activity and overlapping runs can contaminate it, so keep the backend otherwise idle and enforce non-overlap manually.
- Managed allocations are measured for the current thread. If execution changes managed threads, `allocatedBytes` is `null` and `allocationMeasurement` reports that the thread changed.
- Allocation figures do not include native allocations, allocations on other threads, or peak memory. Garbage-collection deltas are also process-wide.

Build configuration and the remaining environment metadata are recorded for interpretation but are not enforced by the Lab. The person running a benchmark is responsible for keeping the environment and inputs suitably consistent.
