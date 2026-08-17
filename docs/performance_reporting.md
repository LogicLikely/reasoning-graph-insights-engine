# Performance Reporting PoC

The application records backend performance runs for the current graph algorithms and for recalculation after a node-likelihood edit. This is a manual proof of concept: it records each operation, but it does not schedule runs, prevent overlap, perform repetitions, or calculate summary statistics.

## Recorded operations

The analysis keys are case-insensitive. `I`, `B`, and `E` require a selected node; `R` and `J` operate on the active graph.

| Key or action | Recorded operation |
| --- | --- |
| `I` | Greedy minimal counter set for the selected node |
| `B` | Bounded brute-force minimal counter set for the selected node |
| `E` | Evidence-impact ranking for the selected node |
| `R` | Least robust node in the graph |
| `J` | Full node-robustness ranking |
| Save a likelihood edit | Recalculate and persist ancestors after updating a database-backed node's likelihood (`priorOdds`) |

For the leaf-edit workload, the user is responsible for selecting the intended leaf. The report records whether the edited node was actually a leaf, the old and new values, affected-node count, maximum ancestor distance, and persisted-row count. Edits that do not include `priorOdds` do not create a leaf-recalculation run.

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

## Bounded brute-force proof semantics

`B` has a hardcoded limit of 20 reachable counter candidates. Candidates are ordered deterministically by greedy priority and then node ID, and only the first 20 are searched. Subsets are considered in increasing cardinality.

- With 20 or fewer candidates, completion is reported as `proven`: the full candidate universe was available to the search. A returned set has proven minimum cardinality; if no set crosses the threshold, that absence is also proven for the available candidates.
- With more than 20 candidates, the run is always `notProven` with stop reason `candidateLimit`, because candidates were excluded. A result may still cross the threshold, but global minimality has not been established.
- There is no separate elapsed-time cutoff. The bounded search runs until it finds a threshold-crossing set, exhausts its candidate universe, is cancelled, or fails.

The JSON records total, searched, and excluded candidate counts, subset evaluations, fully exhausted cardinality, threshold values, proof status, and stop reason.

## Manual benchmark protocol

The user must keep runs separate and hold inputs constant. Do not press another analysis key or start another edit until the current operation finishes.

For each algorithm, graph, and selected node combination:

1. Prefer starting the backend in Release mode:

   ```bash
   dotnet run --configuration Release --project backend/backend.csproj
   ```

   Release is recommended, not required. The report records the configuration that actually ran, so Debug results remain identifiable.

2. Execute the exact operation once as an ordinary warm-up. It is recorded like every other run; note its run number and exclude it from comparisons. This reduces first-use noise from just-in-time compilation, runtime optimization, initial database connections, and cold caches.
3. Execute five measured repetitions, one at a time, with the same graph, target node, and inputs.
4. Retain all five raw runs and compare their median `computeElapsedMilliseconds` and median `operationElapsedMilliseconds` rather than relying on one run.

For a leaf edit, restore the leaf to the same starting likelihood before every measured edit. If restoration is performed through the application, it also creates a recorded run; exclude those restoration run numbers from the five-run comparison. Restore once after the warm-up as well.

## Interpreting resource measurements

Wall-clock compute time is the primary algorithm metric. Total operation time adds loading and other backend work; leaf-update totals also include persistence. CPU, allocation, and garbage-collection values are supporting diagnostics:

- CPU time is a process-wide delta. Other backend activity and overlapping runs can contaminate it, so keep the backend otherwise idle and enforce non-overlap manually.
- Managed allocations are measured for the current thread. If execution changes managed threads, `allocatedBytes` is `null` and `allocationMeasurement` reports that the thread changed.
- Allocation figures do not include native allocations, allocations on other threads, or peak memory. Garbage-collection deltas are also process-wide.

Do not compare runs from different build configurations, graph fingerprints, targets, or input values as though they were repetitions of the same benchmark.
