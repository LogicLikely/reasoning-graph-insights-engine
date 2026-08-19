# Performance reporting implementation plan

> This document supersedes the original candidate-limited reporting plan. The
> former 20-candidate cap, `candidateLimit` stop reason, key bindings, manual
> warm-up/repetition protocol, and absence of an elapsed-time budget no longer
> describe the current design. See
> [Performance Reporting and Insights Lab](../performance_reporting.md) for the
> complete user-facing behavior.

## Implemented foundation

1. Run greedy and time-bounded exhaustive counter-set search behind a shared
   solver/evaluator contract using the production Bayesian-factor objective.
   Each combined subset is recalculated exactly rather than assembled from
   independent legacy likelihood-ratio contributions.
2. Record versioned performance runs in
   `artifacts/performance/performance-runs.json`, including build, graph,
   invocation, timing, resource, outcome, and operation-specific details.
3. Instrument the greedy counter set, exhaustive counter set, evidence impact,
   least-robust node, robustness ranking, and leaf-update operations.
4. Provide the Insights Lab Run, History, and Trends UI with named benchmark
   sets, sequential standard stress-suite execution, cancellation where the
   backend can honor it, result detail views, metric/axis controls, and inline
   interpretation guidance.
5. Use the deterministic graph root for counter-set and evidence-impact runs.
   Exclude pathological deep-chain graphs from the standard suite while
   leaving them available for deliberate individual runs.
6. Add a 100-node balanced, wide, and shared-diamond tier and calibrate every
   non-deep tier against the production Bayesian evaluator. Preserve topology,
   node and edge counts, and kind counts while relocating the final ten percent
   of nodes as structural-leaf objections. Neutral evidence and near-identity
   support propagation isolate the intended greedy-versus-exhaustive workload.
   Keep the 100,000-node tier optional because end-to-end runs are generally
   impractical on a development machine.

## Current exhaustive-reference design

1. Search the complete reachable counter-candidate universe in increasing
   cardinality. Candidate ordering is deterministic, but there is no candidate
   count knob or truncation.
2. Apply one server-owned 120-second compute budget, beginning before problem
   preparation. Request cancellation remains a separate outcome and wins if it
   races the deadline.
3. Return budget expiry as an HTTP-200 partial result with:
   - outcome status `timedOut`;
   - proof status `notProven`;
   - stop reason `timeBudget`.
4. Preserve enough frontier data to describe unfinished work honestly:
   candidate count, subset evaluations, largest cardinality fully exhausted,
   active-cardinality evaluations and total, total subset-space size,
   preparation/search elapsed time, timeout stage, and best set/odds found.
   Arbitrarily large combination counts are serialized as decimal strings.
5. Retain any proof established by a just-completed subset evaluation. The time
   budget prevents more work from starting; it does not erase a result that was
   already computed.
6. Keep progress and timing measurements out of `resultDigest`. They remain in
   report details, while the digest identifies the semantic algorithm result.

## Benchmark protocol

- Run the standard stress suite once for each branch/merge set being compared.
- Prefer a Release backend and keep the machine otherwise idle. The Lab records
  the actual build/environment but deliberately does not enforce them.
- Do not add hidden warm-ups or repetitions. A single run is displayed as raw
  `n=1`, not as an average.
- Treat timed-out chart points as right-censored lower bounds, never as ordinary
  two-minute completions. Use subset count and cardinality-frontier details to
  compare how much exhaustive work each branch completed within the budget.
- Run the production Bayesian fixture contract and complete a real reset smoke
  check before collecting demonstration data. Do not mix `stress-v1` and
  `stress-v2` runs. Cross-model results are end-to-end algorithm/model
  comparisons rather than pure implementation-speed measurements; within one
  calculation model, compare only matching graph fingerprints.

## Deferred

- Hidden warm-up orchestration or repeated-run statistics.
- User-configurable time budgets or candidate limits.
- Peak/native-memory profiling and automatic cross-client concurrency control.
