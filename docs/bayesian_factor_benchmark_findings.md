# Bayesian-Factor Benchmark Findings

## Executive summary

The benchmark work produced a useful scientific result: the final Bayesian-factor
implementation is more internally coherent than the legacy likelihood-ratio model,
but several operations currently pay for that coherence by repeating full-graph
Bayesian calculations many times. The resulting regressions are real, measurable,
and explainable. They are not caused primarily by PostgreSQL loading, the merge
resolution, the priority queue, or ordinary benchmark noise.

The most important discovery is that the three benchmark sets do not represent
three implementations of the same calculation model:

| Benchmark set | Minimal counter, evidence impact, robustness | Leaf update |
| --- | --- | --- |
| `LL-699-simple baseline` | Legacy likelihood-ratio model | Legacy likelihood-ratio model |
| `LL-699-BayesianFactorRework` | Legacy likelihood-ratio model | Bayesian posterior-odds model |
| `LL-699-final` | Bayesian posterior-odds model | Bayesian posterior-odds model |

The performance records themselves establish this distinction through
`algorithm.calculationModel`. In particular, the middle set's minimal-counter
records say `graph-likelihood-calculator`, despite the benchmark-set name. The
partner's later minimal-counter conversion in commit `f4106b1` was not present in
that measured branch. Consequently, the final minimal-counter line is the first
one in this comparison to measure the production Bayesian objective.

The final greedy minimal-counter implementation performs a complete singleton
Bayesian calculation for every candidate before selecting a result. With candidates
fixed at ten percent of graph nodes, this produces approximately quadratic scaling.
At 10,000 nodes it performs 1,008 complete Bayesian calculations to return eight
counters. The final balanced-tree run took 11.9 seconds and cumulatively allocated
16.47 GB, versus 132 milliseconds and 95.6 MB for the middle benchmark set.

This should be treated as a model-coherence-oriented architectural step with clear
optimization headroom. It is not evidence that Bayesian factors are intrinsically
slow. It is evidence that rebuilding, pruning, and recalculating nearly the whole
graph once per candidate does not scale.

## Scope and evidence

This report summarizes the Release-mode runs stored in
`artifacts/performance/performance-runs.json` on August 18, 2026. The analyzed file
contains 162 benchmark-assigned records: 54 in each of three named benchmark sets,
with one observation for each plotted graph/operation combination. Two unassigned
leaf-update records were excluded. The standard graphs used here contain 100,
1,000, or 10,000 nodes in balanced-tree, wide-star, and shared-diamond shapes. The
optional 100,000-node tier was not needed to expose the relevant behavior.

Important limitations:

- Each point is `n=1`; no dedicated warm-up run or repeated sample was recorded.
- Small differences can include JIT, cache, and machine-state effects.
- The very large final regressions are nevertheless directionally decisive because
  wall time, CPU, cumulative allocations, and garbage collections all move together.
- Managed allocations are cumulative allocation volume, not retained or peak RAM.
- The graph fingerprints differ across benchmark sets. Cross-model comparisons are
  end-to-end system comparisons, not identical-input microbenchmarks.
- Source-control fields in these particular performance records are empty, so the
  benchmark-set names, branch history, calculation-model metadata, and graph
  fingerprints must be considered together.
- All runs report Release, .NET 8.0.14, Arm64 macOS, eight logical processors, and
  server GC, but the suite ran in a fixed order rather than a randomized one.
- CPU time is process-wide and can include unrelated backend work. Managed
  allocation is measured only on the executing managed thread; it excludes native
  and other-thread allocation, and becomes unavailable if the operation changes
  threads.

See [Performance Reporting and Insights Lab](performance_reporting.md) for the
reporting contract and UI interpretation rules.

## Terminology

The complexity discussion uses these variables:

| Symbol | Meaning |
| --- | --- |
| `V` | Number of graph nodes |
| `E` | Number of graph edges |
| `C` | Reachable objection nodes eligible as counter candidates |
| `K` | Candidates selected into the greedy result before reaching the threshold |
| `A` | Evidence/objection nodes considered by one evidence-impact calculation |
| `H` | Total target-to-downstream-evidence relationships considered by robustness |
| `S` | Subsets completed by one time-bounded exhaustive search |
| `M` | Changed node plus affected ancestors recalculated by a leaf update |

`C` and `K` are deliberately different. In the 10,000-node calibrated graph,
`C = 1,000` while `K = 8`. The final greedy solver returns eight nodes only after
measuring all 1,000 candidates during ranking.

## What the minimal-counter workload means

The canonical target is the root node `n-00000`. The counter-set operation starts
without objection candidates, adds selected objections, and recalculates the root.
The current cutoff is root log odds `-1`, equivalent to approximately 26.9%
probability. It is an application and benchmark rule, not a universal Bayesian
standard.

The calibrated workload is:

| State | Root log odds | Approximate probability | Outcome |
| --- | ---: | ---: | --- |
| No counters | `0.20` | 55.0% | Above cutoff |
| Seven counters | `-0.92` | 28.5% | Still above cutoff |
| Eight counters | `-1.08` | 25.4% | Threshold reached |

Therefore the intended minimum-cardinality answer is eight. The greedy operation
finds a threshold-reaching set; it does not prove global minimum cardinality. The
time-bounded exhaustive operation proves minimum cardinality only when it completes.
On the 100-node graphs it proves eight after 969 subset evaluations. On larger
graphs it normally reaches the two-minute budget before proof.

## Dataset comparability

The legacy and final fixtures are comparable at the workload level, not at the raw
value or fingerprint level.

The following properties are intentionally held constant:

- graph slugs, sizes, shapes, connectivity, node counts, and edge counts;
- aggregate claim, evidence, and objection counts;
- candidates equal to ten percent of graph nodes;
- canonical root target;
- initial root log odds `0.20` and threshold `-1`;
- seven-counter failure and eight-counter success;
- the 100-node exhaustive frontier of 969 evaluations.

The final `stress-v2` fixture necessarily differs in several details:

- Objections move from distributed positions to the final ten percent of nodes so
  that they are structural leaves. The production BF calculator takes authored
  Bayes factors from structural leaves; internal objection values are not equivalent.
- Displaced evidence is moved to a preceding window to preserve kind counts.
- Ordinary evidence is BF-neutral with score 50, prior log odds zero, and posterior
  log odds zero.
- Each objection has prior zero and authored leaf log BF `-0.16`.
- Non-deep edges are strong support edges with
  `P(child | parent) = 0.999999999` and
  `P(child | not parent) = 0.000000001`.
- The deep-chain fixtures retain their older values and remain outside the standard
  suite.

It was possible to make the new edge ratios look cosmetically similar to the old
`1.001` and `0.999` likelihood multipliers. For example, `0.5005 / 0.5 = 1.001`.
That mapping does not preserve behavior under the nonlinear BF recurrence. A
`-0.16` leaf BF becomes only about `-0.0000798` after one such edge and attenuates
further with depth; even an infinitely negative leaf is bounded near `-0.001` after
that edge. The old-looking probabilities therefore could not support a uniform
eight-counter boundary across graph shapes and depths.

More moderate BF probabilities could preserve a controlled boundary only with
shape/depth-specific calibration, increasing fixture complexity. Those constants
could still be calculated offline and inserted directly; the expensive failure was
performing recursive path calibration during reset. The selected values prioritize
repeatable behavior over superficial numerical resemblance.

The correct scientific framing is:

- **Strong comparison:** greedy-versus-exhaustive scaling inside each branch;
- **Useful ballpark comparison:** end-to-end legacy-model versus Bayesian-model
  behavior on equivalently scaled and calibrated synthetic workloads;
- **Unsupported claim:** one implementation is a specific percentage faster or
  slower on identical raw inputs.

## Minimal-counter measurements

### Greedy compute time

All values below are compute milliseconds from one Release run.

| Shape | Size | Simple baseline | BayesianFactorRework set | Final BF |
| --- | ---: | ---: | ---: | ---: |
| Balanced | 100 | 19.23 | 19.68 | 22.71 |
| Balanced | 1,000 | 19.25 | 20.43 | 177.40 |
| Balanced | 10,000 | 125.49 | 132.25 | 11,918.69 |
| Wide | 100 | 0.78 | 0.56 | 2.58 |
| Wide | 1,000 | 4.41 | 4.45 | 83.18 |
| Wide | 10,000 | 19.99 | 20.90 | 8,972.25 |
| Shared diamond | 100 | 3.15 | 3.56 | 4.04 |
| Shared diamond | 1,000 | 54.41 | 53.89 | 143.44 |
| Shared diamond | 10,000 | 722.41 | 747.99 | 19,701.54 |

At 10,000 nodes, final versus the middle benchmark set is approximately:

- `90.1x` slower for balanced;
- `429.2x` slower for wide;
- `26.3x` slower for shared diamond.

Wide star is an especially useful control. Candidates are structural leaves in both
layouts, it has only one edge from each candidate to the root, and its node/edge/kind
counts remain aligned. Its large regression demonstrates that objection relocation
or complex path structure is not the primary cause.

### Allocation evidence

The allocation signature matches the time-complexity diagnosis.

| 10K shape | Middle-set allocations | Final allocations | Ratio |
| --- | ---: | ---: | ---: |
| Balanced | 95.6 MB | 16.47 GB | `172x` |
| Wide | 27.1 MB | 13.77 GB | `508x` |
| Shared diamond | 1.09 GB | 19.08 GB | `17.5x` |

Final 10K greedy runs triggered hundreds of generation 0, 1, and 2 collections,
where the earlier runs triggered at most a handful. Graph loading was only tens of
milliseconds; it cannot explain compute times of 9–20 seconds.

### Why the final greedy algorithm is approximately quadratic

The final solver performs these steps:

1. Calculate the no-counter baseline once.
2. For every one of the `C` candidates, construct an induced graph and perform a
   complete singleton prune-and-BF calculation to obtain its ranking priority.
3. Add candidates in priority order and recalculate combined prefixes until `K`
   candidates cross the threshold. The first singleton result is cached, leaving
   approximately `K - 1` new prefix calculations.

This is `C + K` complete Bayesian graph calculations:

| Nodes | `C` | `K` | Complete BF calculations |
| ---: | ---: | ---: | ---: |
| 100 | 10 | 8 | 18 |
| 1,000 | 100 | 8 | 108 |
| 10,000 | 1,000 | 8 | 1,008 |

Assuming one induced-graph construction, pruning pass, and BF recurrence costs
`O(V + E)`, final greedy time is:

`O((C + K)(V + E) + C log C)`

Because `K <= C`, this simplifies to:

`O(C(V + E))`

The fixture has `C = V / 10`, and all standard shapes have `E = O(V)`, so:

`O(V^2)`

Peak live space is approximately `O(V + E + C)`, but cumulative allocations are
`O(C(V + E))` because each candidate calculation creates fresh graph collections,
indexes, pruning state, and BF state. This distinction explains multi-gigabyte
allocation totals without implying multi-gigabyte retained memory.

At 10K, 1,000 of 1,008 full calculations—99.2%—exist solely to rank candidates.
The recorded `candidatesExamined: 8` counts candidates removed from the priority
queue and added to the answer. It does not count the 1,000 singleton evaluations
performed during ranking.

### Legacy greedy complexity

The earlier algorithm also ranked all `C` candidates, but did substantially less
work for each ranking. It built shared graph state, recalculated legacy likelihoods,
and ranked a candidate using stored posterior odds and a candidate-to-root path
multiplier. Selected additive contributions were calculated once and cached.

A useful parameterized expression is:

`O(R(V,E) + (C + K)P + C log C + K^2)`

where `R(V,E)` is the shared legacy recalculation work and `P` is the cost of one
candidate-to-root path search. The `K^2` term accounts for repeatedly summing each
growing prefix; it is negligible in these measurements because `K = 8`. For the
measured fixed-`K`, unique-path sparse fixtures, this is approximately log-linear
rather than quadratic:

- wide star: approximately `O(V log V)`;
- balanced tree: conservatively `O(V log^2 V)` in the literal implementation;
- shared-diamond DAG: potentially much worse because the path routines enumerate
  simple paths without memoizing every candidate result.

The legacy algorithm therefore has an exponential worst case on a general
many-path DAG, even though it performed well on the measured fixtures. Its favorable
measurements should not be interpreted as a universal complexity guarantee.

### Was the legacy answer really minimal?

Neither greedy implementation proves minimum cardinality on an arbitrary graph.

The legacy greedy ranking was a heuristic and could return a larger set than the
exhaustive reference. In the calibrated legacy fixture, however, every counter had
the same fixed `-0.16` contribution. Seven could not succeed and any eight could, so
the returned eight-node set truly was minimum by construction. The 100-node
exhaustive run proved it operationally.

The final greedy algorithm measures singleton effects against the actual BF model,
which is more model-faithful, but nonlinear interactions can still make a different
combination smaller. It returns a usable threshold-reaching set, not a proof.

The final exhaustive solver supplies that proof when it completes because it
evaluates exact BF subsets in increasing cardinality. Unlike the legacy additive
objective, a nonlinear BF objective cannot generally be solved by merely sorting
fixed independent contributions.

## Time-bounded exhaustive search

For `C` candidates and one full BF calculation per subset, the final exhaustive
reference costs:

`O(S(V + E))`

where `S` is the number of completed subsets. Before finding a minimum of size `K`,
`S` includes every subset of cardinality below `K` plus some cardinality-`K`
subsets. Its unrestricted worst case is:

`O(2^C(V + E))`

The two-minute compute budget limits elapsed time, not theoretical complexity. A
time-budget outcome is a completed request but a right-censored observation of
time-to-proof: it means "not proven within 120 seconds," not that proof completed in
120 seconds.

The subset-throughput contrast is substantial:

| Representative balanced run | Middle benchmark set | Final BF |
| --- | ---: | ---: |
| 1K subsets completed in two minutes | 1,009,495,947 | 124,254 |
| 10K subsets completed in two minutes | 1,435,709,112 | 10,390 |

The earlier exhaustive solver combined cached additive values; final recalculates a
nonlinear Bayesian graph for every subset. The lower final counts are therefore
expected and meaningful. They do not indicate a broken timer.

The 100-node graphs provide a particularly clean control: every shape and branch
completed the same 969 evaluations and proved the same eight-node minimum. The
legacy-model runs took 3.34–5.59 ms, while final BF took 139.82–215.63 ms. That
isolates the higher cost of one exact BF subset evaluation from differences in
search-frontier size. Every recorded 1K and 10K exhaustive run reached the time
budget and reported `timedOut` / `notProven`; those tiers should be interpreted
through subset counts and cardinality progress rather than elapsed time alone.

## Other operation findings

The same semantic conversion affects evidence impact and robustness. Representative
balanced-tree 10K measurements are:

| Operation | Middle model/time | Final model/time | Approximate factor |
| --- | ---: | ---: | ---: |
| Greedy minimal counter | Legacy, 132.3 ms | BF, 11.9 s | `90x` |
| Evidence impact | Legacy, 13.9 ms | BF, 56.3 s | `4,064x` |
| Least robust node | Legacy, 7.8 ms | BF, 248.5 s | `~32,000x` |
| Robustness ranking | Legacy, 9.1 ms | BF, 250.6 s | `~27,700x` |
| Leaf update | BF, 59.2 ms | BF, 81.2 ms | `1.37x` |

Leaf update is the closest cross-set control because it already used the Bayesian
posterior calculator in the middle benchmark set. Its results are mixed by shape and
size rather than catastrophically different. This supports the conclusion that the
database, reporting instrumentation, and merge did not globally slow every path.

### Evidence impact

Final evidence impact calculates the root with all evidence, then removes each
reachable evidence or objection node in turn and performs another full BF
calculation. If `A` nodes are considered, its nominal cost is:

`O((A + 1)(V + E))`

The 10K fixtures contain 2,999 evidence/objection nodes. Because `A = O(V)`, this
becomes approximately `O(V^2)` for the standard suite. Neutral evidence still
participates in graph structure, pruning, and leave-one-out evaluation; zero authored
BF does not automatically make removal structurally irrelevant.

### Robustness

Final robustness evaluates every possible target. For each target it calculates a
baseline, then removes each downstream evidence/objection node individually and
performs a full BF recalculation. Let `H` be the total number of downstream
target/evidence relationships. Its nominal cost is:

`O((V + H)(V + E))`

`H` depends heavily on topology. It can approach `O(V^2)`, giving an `O(V^3)`
worst case for sparse graphs. Balanced and shared-diamond graphs have many more
target/evidence relationships than wide stars, which helps explain their larger
times; BF, pruning, and induced-graph constants also vary by topology.

Both **Least robust node** and **Robustness ranking** build the complete robustness
map; one takes the minimum and the other sorts the map. Their compute times are
therefore naturally similar. Running both in the suite deliberately measures the
same expensive core twice as two separate public operations.

### Leaf update

Leaf update recalculates the changed node and its affected ancestors. If `M` nodes
are affected, its nominal BF cost is approximately `O(M(V + E))`. For the standard
wide, balanced, and shared-diamond leaf targets, `M` is small relative to `V`, so the
operation remains much less expensive than all-candidate or all-target analyses.

## Database-reset finding

The first calibrated reset attempted to solve legacy path contributions inside
PostgreSQL using recursive CTEs and a post-insert node update. When the 100K graphs
were included, the reset held one transaction and schema locks for more than twenty
minutes with no observable per-graph progress. This was a data-generation
regression, separate from the measured algorithm regressions.

The BF-native `stress-v2` reset now assigns calibrated values directly during node
and edge insertion. It removes the recursive calibration and post-insert rewrite,
retains the five-minute per-graph safety timeout, and logs each graph's start and
elapsed preparation time. The reset remains atomic, so its inserted rows are not
visible to other sessions until the final commit.

## Interpretation: coherence, accuracy, and performance

The final model is more internally coherent because it:

- uses both conditional probabilities in the nonlinear edge transform; the simple
  branch stored one importance/LR scalar, while the middle branch's legacy analyses
  reduced the new probability pair back to one LR;
- treats leaf observations as authored Bayes factors;
- uses compatible-path pruning before combining evidence;
- evaluates nonlinear BF recurrence for combined counter sets;
- applies the same posterior-odds model to counter sets, evidence impact,
  robustness, and updates.

This does **not** by itself establish empirical accuracy. Real-world accuracy
requires calibrated conditional probabilities, justified dependence assumptions,
representative graph structure, and validation against known outcomes. The current
stress fixture is intentionally synthetic and engineered for complexity behavior,
not evidence quality.

The final performance results likewise do not show that Bayesian inference must be
slow. They show that the current orchestration repeats an otherwise structured graph
calculation too many times and allocates fresh graph state on every repetition.

## Optimization opportunities

The measurements identify specific, testable directions for future work.

### Shared infrastructure

1. Build immutable node/edge indexes once per operation rather than once per
   candidate, target, or removed evidence node.
2. Separate graph membership changes from physical `Graph`, `List`, dictionary, and
   hash-set reconstruction. Use lightweight inclusion masks or persistent views.
3. Cache target-specific invariant topology/indexes and reuse BF messages where
   membership and pruning choices remain valid. Adding or removing nodes can change
   compatible paths, so affected pruning state must be invalidated or recomputed.
4. Profile induced-graph construction, pruning, recurrence, and allocation as
   separate stages. Wall time alone cannot identify which stage dominates after
   structural reuse is introduced.

### Greedy minimal counter

1. Batch singleton marginal calculations instead of launching `C` independent
   whole-graph calculations where mathematically possible.
2. Explore reverse-message, sensitivity, or dynamic-programming formulations for
   leaf-candidate marginal effects.
3. Consider a cheap heuristic shortlist followed by exact BF verification. This
   deliberately trades ranking guarantees for speed and must be visible in metadata.
4. Reuse invariant graph, pruning, and message state across multi-counter prefix
   calculations. The implementation already caches scalar baseline/singleton target
   odds, but nonlinear singleton results cannot generally be combined into a prefix
   result.
5. Add reporting fields such as `priorityEvaluations`, `fullModelEvaluations`, and
   ranking elapsed time. `candidatesExamined` currently describes only selected
   candidates and hides the dominant work.

Reducing fixture candidate counts would make the graph look faster, but it would not
remove the algorithmic hot path. Dataset changes should serve a stated experiment,
not conceal an implementation cost.

### Exhaustive reference

1. Investigate branch-and-bound only after deriving valid bounds for the nonlinear
   BF objective.
2. Use monotonicity or dominance pruning only where it is proven for the actual
   pruning/recurrence semantics.
3. Retain the exact 100-node proof fixture as a correctness oracle while larger runs
   remain time-censored scalability experiments.

### Evidence impact and robustness

1. Reuse intermediate context, pruning, and BF state from the already-computed
   all-evidence baseline where valid. Node removal can change compatible-path
   pruning and must trigger affected-state recomputation.
2. Derive incremental leave-one-out updates rather than reconstructing the graph for
   every removed node.
3. Reuse intermediate results across related targets where graph direction and
   pruning semantics permit it.
4. Consider sharing or caching the complete robustness map when both least-robust
   and ranking results are requested against an unchanged fingerprint.

### Leaf update

1. Build graph indexes and reusable context once for the affected set rather than
   starting a fresh full-graph prune/BF calculation for each of the `M` targets.
2. Investigate one bottom-up pass over the affected subgraph while preserving each
   target's compatible-path pruning semantics.
3. Retain the current batch persistence of recalculated values; the opportunity is
   primarily in compute-state reuse, not additional database batching.

Every optimization should preserve executable semantic tests for pruning,
nonlinear counter interactions, threshold outcomes, and proof status. A faster
implementation that silently returns to independent additive contributions would
not be equivalent.

## Recommended follow-up experiments

1. **Establish a Bayesian-before/Bayesian-after comparison.** The current middle set
   is legacy for the read-only algorithms. Performance claims about BF optimization
   require two branches using the same BF objective and matching `stress-v2`
   fingerprints.
2. **Add operation-stage measurements.** Record context/index construction,
   pruning, BF recurrence, ranking, and prefix evaluation separately.
3. **Add model-evaluation counters.** Count singleton, combined-prefix, and
   leave-one-out BF calculations explicitly.
4. **Profile allocations.** The near-quadratic allocation curve is at least as
   actionable as elapsed time and should be checked after each optimization.
5. **Retain 100/1K/10K tiers.** The 100-node tier provides exact proof; 1K exposes
   the transition; 10K reveals asymptotic behavior. The 100K tier is unnecessary
   until the repeated-calculation hot paths improve.
6. **Repeat only when needed.** The existing one-run-per-branch protocol is useful
   for demonstration, but smaller optimization claims should use controlled repeats
   after warm-up and report medians plus variation.
7. **Separate performance from empirical validation.** Use stress graphs for
   computational behavior and a different, domain-calibrated dataset for claims
   about inferential quality.

## Final takeaway

The benchmark did not merely report that one branch was slower. It explained why.
The legacy operations reused additive state and cheap path heuristics. The final
operations repeatedly evaluate a more internally coherent, nonlinear Bayesian
model. Candidate, evidence, and target counts then multiply full-graph work into
quadratic or worse scaling.

That is a productive result. The semantic foundation is now explicit, the workload
is controlled, the principal hot paths are known, and future optimization can be
measured without weakening the model. The appropriate description of the current
state is therefore:

> The Bayesian-factor rework improves internal probabilistic coherence and exposes
> clear opportunities for structural reuse, incremental calculation, and better
> reporting. Its present implementation is a model-faithful baseline, not the
> final performance design.
