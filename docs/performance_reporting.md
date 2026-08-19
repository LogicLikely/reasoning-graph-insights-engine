# Performance Reporting and Insights Lab

The application records backend performance runs for the current graph algorithms and for recalculation after a node-likelihood edit. The Insights Lab launches individual operations or a sequential standard stress suite in the current browser session, exposes the persisted run history, and charts performance trends for named benchmark sets. It does not schedule repetitions or enforce non-overlap across clients.

Open **Insights Lab** from the Graph Overview panel. The **Run** tab creates or selects a benchmark set and launches an operation against the active graph; each information button expands a plain-language explanation of what that operation measures and its important limitations. The **History** tab shows every persisted run newest-first in a bounded, scrollable table. Selecting a run opens a dedicated detail view with its complete recorded metadata and bounded result preview; **Back to all runs** returns to the table. The **Trends** tab compares the selected performance metric across stress-graph sizes, shapes, and benchmark sets.

## Recorded operations

| Lab action | Recorded operation |
| --- | --- |
| Minimal counter set | Greedy counter-set search using the target's Bayesian-factor-derived posterior odds |
| Time-bounded exhaustive search | Exhaustive minimum-cardinality counter-set search against the same Bayesian-factor objective, with a fixed server-owned time budget |
| Evidence impact ranking | Leave-one-evidence-out Bayesian-factor ranking for the benchmark target |
| Least robust node | Node with the greatest Bayesian-factor-derived probability sensitivity to removing one downstream evidence or objection |
| Robustness ranking | Full ranking by that one-at-a-time evidence-removal sensitivity |
| Leaf update | Reapply the current `priorOdds` to the ordinal-highest node, then recalculate and persist that node and its ancestors |

Minimal counter set, time-bounded exhaustive search, and evidence impact ranking always use the graph's deterministic root node, independent of the node selected on the canvas. Stress graphs use the shared root ID `n-00000`, which keeps the target stable across graph sizes, shapes, and benchmark sets. The two robustness operations use the entire active graph. Fixture graphs support all five read-only operations; Leaf update is database-only.

The read-only analyses use the pruned Bayesian-factor/posterior-odds model. Evidence impact recalculates the target after removing each reachable evidence or objection node in turn. Robustness performs the same one-at-a-time removal experiment for every possible target, records the largest absolute probability change for each target, and reports `exp(-change)` as its robustness score. These values describe sensitivity inside the current model; they do not establish truth or evidence quality.

The Lab deliberately chooses the ordinal-highest node for the leaf-update workload and tolerates that node not being a leaf. It reapplies the current value so the full update/recalculation/persistence path is measured without intentionally changing graph state. The report records whether the node was actually a leaf, the old and new values, affected-node count, maximum ancestor distance, and persisted-row count. Ordinary database edits that include `priorOdds` continue to create leaf-update records as well.

The Lab offers best-effort request cancellation for the greedy, exhaustive, and robustness operations. Evidence-impact and leaf-update runs do not currently expose Cancel. Request cancellation is distinct from the exhaustive search's expected time-budget outcome.

## Standard stress suite

**Run standard stress suite** executes all six operations against every currently installed balanced, wide, and shared-diamond database stress graph. Deep-chain graphs are intentionally excluded because their pathological depth can make operations extremely slow or terminate the backend; they remain available for deliberate individual runs. Included graphs follow the canonical order shown by the database-reset UI, and operations run sequentially, graph by graph, with Leaf update last. Every request uses the benchmark set selected when the suite starts. The suite does not render each graph and never overlaps its own requests.

The canonical matrix contains 100, 1,000, 10,000, and 100,000-node versions of each included shape. With all optional graphs installed, that is 12 graphs and 72 recorded operations. The 100-node graphs use the same deterministic topology and calibration rules as the larger tiers; their node roles and values are generated for that size rather than copied from a larger stored graph.

Each graph-and-operation combination is executed and recorded once. No hidden warm-up runs or repetitions are added. Consequently, fixed-order cold-start and cache effects can remain visible in the data; warm-up policy is intentionally deferred.

The exhaustive operation has a two-minute compute budget per graph. The calibrated 100-node graphs have ten candidates and are small enough for exhaustive search to prove the minimum. The 1,000-node and larger graphs retain ten percent of their nodes as candidates and are designed to expose the combinatorial boundary by reaching the time budget. Nine installed graphs through 10,000 nodes can therefore spend up to about 18 minutes in exhaustive search, plus the other operations and graph-loading overhead. A time-budget result is an expected completed request, so the suite records it and continues. The 100,000-node tier remains available, but it is optional and generally impractical for the standard comparison because its graph loading and Bayesian analyses can add substantial time and memory beyond the exhaustive budget.

## Bayesian minimal-counter seed calibration

The non-deep stress graphs are deterministic fixtures calibrated against the same production Bayesian-factor evaluator used by both counter-set solvers. Their topology, node and edge counts, and counts by node kind remain unchanged. Within each graph, kinds are relocated so that the final ten percent of nodes are structural-leaf objections. Evidence displaced from that tail is moved into the immediately preceding node window, preserving the original evidence count.

The fixture values deliberately isolate the counter-set workload:

- the root prior and posterior begin at log odds `0.200` (about 55% probability);
- evidence is neutral, with score 50 and prior and posterior log odds of `0`;
- every objection has prior log odds `0` and an authored leaf log Bayes factor of `-0.160`;
- every non-deep edge is a support edge with `P(child | parent) = 0.999999999` and `P(child | not parent) = 0.000000001`, which propagates leaf Bayes factors with negligible attenuation;
- deep-chain values retain their previous generation rules because deep graphs are outside the standard suite.

The executable contract evaluates an in-memory mirror of the generated graphs through `GraphPosteriorOddsCalculator` and `BayesianMinimalCounterSetEvaluator`. It verifies the seven/eight boundary for the 100-, 1,000-, and 10,000-node tiers. Each 100-node graph has ten candidates, so exhaustive search proves the eight-node minimum after 969 subset evaluations. The 1,000- and 10,000-node graphs supply the same qualifying eight-node boundary inside much larger candidate universes, while exhaustive search is expected to return a time-budget result rather than a proof. The optional 100,000-node tier uses the same construction but is not exercised by the ordinary calibration test because doing so would make routine test runs unreasonably expensive.

This is an intentionally engineered complexity fixture, not a model of realistic evidence quality or a claim that natural graphs have interchangeable objections. The strong propagation probabilities and neutral evidence make the greedy-versus-exhaustive scaling story legible. Do not combine pre-calibration `stress-v1` runs with `stress-v2` runs. Comparisons across the legacy likelihood-ratio and Bayesian calculation models are end-to-end algorithm/model comparisons, not pure implementation-speed benchmarks; inspect `calculationModel`, threshold outcome, candidate count, and graph fingerprint when interpreting them. Comparisons within the same model should use identical reset data and matching fingerprints.

The calibration check builds representative graphs in memory and verifies the production evaluator/solver contract without writing performance records:

```bash
dotnet test backend.Tests/backend.Tests.csproj \
  --filter FullyQualifiedName~StressGraphBenchmarkContractTests \
  --logger "console;verbosity=detailed"
```

The reset SQL encodes these values while inserting nodes and edges; it does not perform a recursive post-insert calibration pass. Static SQL tests check that it mirrors the in-memory contract, but they do not execute PostgreSQL. Before collecting a final benchmark set, complete one real reset and confirm the installed graph counts and first 100-node counter-set results. Reset cost is now tied primarily to graph generation and insertion rather than to enumerating ancestor paths.

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

The exhaustive reference operation searches every reachable objection candidate against the same Bayesian-factor target calculation used by the greedy operation; it does not truncate the candidate universe. Subsets are considered in increasing cardinality, with a stable node-ID order inside each cardinality. The backend owns one fixed 120,000 ms compute budget. The budget starts before problem preparation and has no Lab control.

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
3. Execute each measured operation one at a time, or use **Run standard stress suite** for the installed balanced, wide, and shared-diamond stress graphs. Each combination is recorded once; the Lab does not launch a hidden warm-up. Budget up to two minutes for each exhaustive run. Installing through 10,000 nodes produces nine standard graphs and 54 planned runs; add the 100,000-node tier only for a deliberate experiment with substantially more time and memory available.
4. If repetitions are useful, keep the graph, canonical target, and inputs unchanged. Trends retains the raw runs and plots the selected metric's median with the sample count.

The Lab's same-value leaf update does not require restoration. If benchmarking an ordinary value-changing likelihood edit instead, restore the leaf to the same starting likelihood before every measured edit. A restoration performed through the application creates another recorded run and must be excluded from the comparison.

## Interpreting resource measurements

Wall-clock compute time is the primary algorithm metric. Total operation time adds loading and other backend work; leaf-update totals also include persistence. CPU, allocation, and garbage-collection values are supporting diagnostics:

- CPU time is a process-wide delta. Other backend activity and overlapping runs can contaminate it, so keep the backend otherwise idle and enforce non-overlap manually.
- Managed allocations are measured for the current thread. If execution changes managed threads, `allocatedBytes` is `null` and `allocationMeasurement` reports that the thread changed.
- Allocation figures do not include native allocations, allocations on other threads, or peak memory. Garbage-collection deltas are also process-wide.

Build configuration and the remaining environment metadata are recorded for interpretation but are not enforced by the Lab. The person running a benchmark is responsible for keeping the environment and inputs suitably consistent.
