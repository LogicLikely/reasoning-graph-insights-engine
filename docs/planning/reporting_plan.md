Nothing else is blocking. One code-specific correction: the greedy minimal-counter-set logic currently lives inside `GraphService`, so it needs a behavior-preserving extraction. E/R/J are already substantially separated. The first new capability will still be bounded brute force.

I’m also assuming there is no separate elapsed-time limit: B searches its bounded universe until completion or normal request cancellation.

## Implementation plan

1. Extract minimal-counter-set foundations
   - Introduce a shared solver contract and target-log-odds evaluator.
   - Move the existing greedy behavior into its own solver without changing its mathematics.
   - Remove its hot-loop console logging.
   - Ensure greedy and brute force use the same candidate definition, threshold, and evaluator.

2. Implement bounded brute force
   - Hardcode the candidate limit at 20.
   - Enumerate subsets in increasing cardinality.
   - When more than 20 counters are reachable, select 20 deterministically using greedy priority with node ID tie-breaking.
   - Report `notProven` whenever candidates were excluded.
   - Capture subset evaluations, exhausted cardinalities, best set, threshold result, elapsed time, and cancellation.
   - Keep the implementation in new solver files to minimize `BaysianFactorRework` conflicts.

3. Add the B operation
   - Preserve I as greedy.
   - Add a dedicated backend bounded-brute-force operation.
   - Bind B to that operation using the currently selected node.
   - Avoid a configuration toggle or other frontend controls.

4. Add the reporting framework
   - Add a common run record for build, graph, input, timing, resource, and outcome data.
   - Log the actual build configuration—Debug or Release—without enforcing either.
   - Capture in-memory computation and total backend operation separately.
   - Capture CPU time, managed allocations, and GC deltas as best-effort measurements.
   - Keep algorithm-specific fields inside `details`.

5. Add the JSON store
   - Hardcode the repository-relative path:

     ```text
     artifacts/performance/performance-runs.json
     ```

   - Gitignore the generated results.
   - Use one versioned JSON document containing a `runs` array.
   - Assign sequential file-local run numbers.
   - Write atomically after all performance timers have stopped.
   - Record completed, failed, cancelled, and not-proven executions.

6. Instrument every agreed operation
   - I: greedy minimal counter set.
   - B: bounded brute-force minimal counter set.
   - E: evidence-impact ranking.
   - R: least robust node.
   - J: robustness ranking.
   - Leaf update: recalculation plus load and persistence phases.

   Instrumentation will use low-overhead counts derived from inputs and results. Detailed hot-loop counters will remain out of scope.

7. Verify behavior and compatibility
   - Add unit tests for greedy preservation, exact enumeration, deterministic truncation, proof status, cancellation, and candidate limits.
   - Exercise the 11-node, 27-node, 96-node/no-counter, and over-20-candidate cases.
   - Add storage tests using temporary test paths while production wiring remains hardcoded.
   - Verify every operation writes a valid record with correctly separated timers.
   - Check compilation and semantic compatibility with `BaysianFactorRework`.
   - Ensure JSON writing itself never appears in the measured operation time.

8. Document the manual benchmark protocol
   - The user ensures runs do not overlap.
   - Warm-up is one ordinary recorded keypress that is excluded during comparison.
   - Then perform five measured repetitions and compare medians.
   - Leaf state must be restored before each repeated leaf-update run.
   - No automatic warm-up orchestration or benchmark runner will be added yet.

Deferred intentionally: configuration UI, configurable candidate limits, automatic concurrency enforcement, automatic repetitions, elapsed-time budgets, peak-memory profiling, reporting UI, and the frontend test suite.

No repository changes have been made yet.
