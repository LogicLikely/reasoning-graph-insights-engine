# Performance Reporting and Insights Lab

The application records backend performance runs for the current graph algorithms and for recalculation after a node-likelihood edit. The Insights Lab launches one operation at a time in the current browser session and exposes the persisted run history. It does not schedule repetitions, enforce non-overlap across clients, or calculate summary statistics.

Open **Insights Lab** from the Graph Overview panel. The **Run** tab launches an operation against the active graph; each information button expands a plain-language explanation of what that operation measures and its important limitations. The **History** tab shows every persisted run newest-first in a bounded, scrollable table. Selecting a run opens a dedicated detail view with its complete recorded metadata and bounded result preview; **Back to all runs** returns to the table.

## Recorded operations

| Lab action | Recorded operation |
| --- | --- |
| Minimal counter set | Greedy minimal counter set for the selected node |
| Bounded minimal counter set | Bounded brute-force minimal counter set for the selected node |
| Evidence impact ranking | Evidence-impact ranking for the selected node |
| Least robust node | Least robust node in the graph |
| Robustness ranking | Full node-robustness ranking |
| Leaf update | Reapply the current `priorOdds` to the ordinal-highest node, then recalculate and persist its ancestors |

Minimal counter set, bounded minimal counter set, and evidence impact ranking require a selected node. The two robustness operations use the entire active graph. Fixture graphs support all five read-only operations; Leaf update is database-only.

The Lab deliberately chooses the ordinal-highest node for the leaf-update workload and tolerates that node not being a leaf. It reapplies the current value so the full update/recalculation/persistence path is measured without intentionally changing graph state. The report records whether the node was actually a leaf, the old and new values, affected-node count, maximum ancestor distance, and persisted-row count. Ordinary database edits that include `priorOdds` continue to create leaf-update records as well.

The Lab offers best-effort request cancellation for the greedy, bounded, and robustness operations. Evidence-impact and leaf-update runs do not expose Cancel because their current backend work cannot be stopped reliably and safely mid-operation.

## Results file

Runs are appended to the hardcoded repository-relative file:

```text
artifacts/performance/performance-runs.json
```

The file is gitignored. It is one valid JSON document with file-local, sequential run numbers:

```json
{
  "schemaVersion": 1,
  "runs": []
}
```

Each run has the following top-level fields:

| Field | Contents |
| --- | --- |
| `runNumber`, `startedAtUtc` | Run identity and UTC start time |
| `algorithm` | Algorithm name, implementation, and calculation model |
| `build` | Actual Debug or Release configuration, .NET/OS/architecture/processor/GC context, and available source metadata |
| `graph` | Slug, graph type, node and edge counts, kind counts, known depth, and fingerprint |
| `invocation` | Data source, target or changed node, changed values, and parameters |
| `timing` | Load, in-memory compute, persistence where applicable, and total operation milliseconds |
| `resources` | Compute-scope CPU time, managed allocations, and garbage-collection deltas |
| `outcome` | Status, result count and digest, or error information |
| `details` | Measurements specific to that algorithm |

JSON persistence happens after the measured operation timers stop. Completed, failed, cancelled, and bounded-but-not-proven runs can all be recorded.

The frontend reads the same document through `GET /api/performance-runs`. Run numbers are assigned by the backend store; users do not supply run or batch identifiers.

## Bounded brute-force proof semantics

The bounded minimal counter-set operation has a hardcoded limit of 20 reachable counter candidates. Candidates are ordered deterministically by greedy priority and then node ID, and only the first 20 are searched. Subsets are considered in increasing cardinality.

- With 20 or fewer candidates, completion is reported as `proven`: the full candidate universe was available to the search. A returned set has proven minimum cardinality; if no set crosses the threshold, that absence is also proven for the available candidates.
- With more than 20 candidates, the run is always `notProven` with stop reason `candidateLimit`, because candidates were excluded. A result may still cross the threshold, but global minimality has not been established.
- There is no separate elapsed-time cutoff. The bounded search runs until it finds a threshold-crossing set, exhausts its candidate universe, is cancelled, or fails.

The JSON records total, searched, and excluded candidate counts, subset evaluations, fully exhausted cardinality, threshold values, proof status, and stop reason.

## Optional manual benchmark protocol

Each click in Insights Lab launches and records exactly one operation. For a manual comparison, keep runs separate and hold inputs constant; do not start another operation until the current operation finishes.

For each algorithm, graph, and selected node combination:

1. Prefer starting the backend in Release mode:

   ```bash
   dotnet run --configuration Release --project backend/backend.csproj
   ```

   Release is recommended, not required. The report records the configuration that actually ran, so Debug results remain identifiable.

2. Execute the exact operation once as an ordinary warm-up. It is recorded like every other run; note its run number and exclude it from comparisons. This reduces first-use noise from just-in-time compilation, runtime optimization, initial database connections, and cold caches.
3. Execute five measured repetitions, one at a time, with the same graph, target node, and inputs.
4. Retain all five raw runs and compare their median `computeElapsedMilliseconds` and median `operationElapsedMilliseconds` rather than relying on one run.

The Lab's same-value leaf update does not require restoration. If benchmarking an ordinary value-changing likelihood edit instead, restore the leaf to the same starting likelihood before every measured edit. A restoration performed through the application creates another recorded run and must be excluded from the comparison.

## Interpreting resource measurements

Wall-clock compute time is the primary algorithm metric. Total operation time adds loading and other backend work; leaf-update totals also include persistence. CPU, allocation, and garbage-collection values are supporting diagnostics:

- CPU time is a process-wide delta. Other backend activity and overlapping runs can contaminate it, so keep the backend otherwise idle and enforce non-overlap manually.
- Managed allocations are measured for the current thread. If execution changes managed threads, `allocatedBytes` is `null` and `allocationMeasurement` reports that the thread changed.
- Allocation figures do not include native allocations, allocations on other threads, or peak memory. Garbage-collection deltas are also process-wide.

Do not compare runs from different build configurations, graph fingerprints, targets, or input values as though they were repetitions of the same benchmark.
