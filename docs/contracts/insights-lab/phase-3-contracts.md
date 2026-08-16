# Insights Lab Phase 3 contracts

**Contract status:** Frozen for Phase 3

**Contract family:** `insights-lab-v1`

**Scope:** Versioned analysis cores, rich deterministic results, algorithm
cancellation/isolation, and compatibility adapters for the pre-Lab graph API

This record implements Phase 3 of the Insights Lab plan. It preserves the
Phase 0 semantic identities and canonical-result rules and consumes the Phase 1
worker protocol. It does not implement GraphMap admission, benchmark suites,
the performance CLI or queue, Lab routes, calibration, or authoritative
baselines. Those remain in Phase 2 or Phases 4 through 6.

## 1. Common result materialization

Every Phase 3 operation produces a deterministic complete logical item array
before applying retention:

- Floating-derived logical item values are projected to 12 fractional decimal
  places with midpoint-to-even rounding.
- The result digest is SHA-256 over the canonical JSON representation of the
  complete ordered item array. Operation identity, correlation IDs, timing,
  resource data, and visualization state are not digest material.
- At most the first 100 items are retained in a compact output. Total result
  cardinality describes the complete logical array.
- Worker transport retention begins from that deterministic top-100 prefix. If
  its canonical output event exceeds the byte limit advertised by the Phase 1
  supervisor, the worker retains the largest leading prefix of complete items
  whose corresponding complete ordered paths fit. Provided the base output
  envelope itself fits, zero items and zero paths are retained when even one
  complete item/path projection cannot fit; otherwise worker execution fails.
- Byte-bounded retention omits whole items only. It never truncates an item,
  node-ID sequence, edge-ID sequence, or individual ordered path. Retained
  paths remain aligned with retained items; paths belonging only to omitted
  items are omitted.
- Byte-bounded retention does not change total result cardinality or the digest
  of the complete logical item array. A reduced prefix appends the exact stable
  warning token `retained-items-reduced-to-fit-worker-protocol-line`.
- An ordered path contains one more node ID than edge IDs, is never silently
  truncated, and records its accumulated log likelihood ratio.
- Summary and distribution values are deterministic projections, but the item
  array remains the independently recomputable digest material required by the
  Phase 1 export validator.
- Cancellation is checked during validation, calculation-context construction,
  traversal, subset enumeration, ranking, shaping, and digest projection.
  Analysis hot loops do not write to `Console`.

Input node and edge insertion order is not result meaning. Every operation
uses ordinal identifiers for deterministic graph traversal and tie-breaking.

## 2. `strongest-path-v1`

The rich operation accepts a graph, a start node ID, and an explicit `up` or
`down` direction.

- Every edge kind participates. Every edge likelihood ratio must be positive.
- The complete input must be a directed acyclic graph. A cycle anywhere in the
  input is a validation failure, including a cycle disconnected from the start.
- `up` follows an edge from `From` to `To`; `down` traverses the same structural
  edge from `To` to `From`.
- The zero-edge start-to-self path is retained as a result item.
- For each reachable end node, minimum and maximum accumulated log-LR paths are
  computed. The selected path has the greater absolute score; equal magnitude
  selects the greater signed score; a remaining tie selects the ordinal node-ID
  sequence and then edge-ID sequence.
- The complete ranking uses absolute score descending, signed score descending,
  node-ID sequence ordinal, then edge-ID sequence ordinal.

Each item records a one-based rank, end node ID, normalized accumulated log-LR,
and ordered node/edge IDs. The summary records the start, direction, reachable
count, and first-ranked path. The distribution records supporting, counter,
and neutral path counts plus the minimum and maximum scores.

## 3. `evidence-impact-v0`

The rich operation preserves the characterized v0 calculation while making
its context explicit:

- The complete graph must be a positive-LR DAG under the same validation rule
  as strongest path.
- Evidence endpoints are kinds `evidence` and `objection`, case-insensitively.
  The legacy kind `counter` is not an evidence endpoint in
  `likelihood-recalculate-v0` and is therefore not silently added here.
- The target baseline is recalculated from the target prior plus every reachable
  evidence endpoint's selected strongest downstream path, then clamped to the
  existing `[-100, 100]` log-odds range. The target's stored posterior is not
  used.
- A counterfactual subtracts one endpoint's selected path score from that
  recalculated baseline. Raw delta is baseline probability minus counterfactual
  probability.
- Zero-score endpoints appear in neither partition.
- Supporting and counter partitions each order by absolute raw delta descending,
  then node ID ordinal. Ranks restart at one within each partition.
- Complete digest/retention order is the supporting partition followed by the
  counter partition.

Each item includes partition, node identity/title/kind, accumulated log-LR,
baseline and counterfactual probabilities, raw delta, and the responsible
ordered path. The summary includes target identity, recalculated baseline, and
partition counts. Distribution values are computed separately per partition.

## 4. `critical-counter-v1`

Phase 0 candidate eligibility, removal/application, threshold, and objective
rules remain authoritative. Phase 3 freezes the executable strategies below.

### Exact

- Evaluate the no-candidate baseline first.
- Enumerate ordinal candidate combinations by increasing cardinality without a
  fixed-width bit mask.
- Evaluate every subset at the first attaining cardinality before applying
  margin and ordinal-ID tie-breaking, then stop. This proves optimal
  cardinality.
- Do not apply monotonicity pruning; nested candidates and restored structural
  context may interact.
- Exhaustive non-attainment is successful. It returns the empty baseline under
  the frozen non-attaining cardinality-first objective, sets
  `searchExhausted` and `provedUnattainable`, and does not claim an optimal
  critical set.

### Greedy

- Start from the no-candidate baseline.
- Rebuild and reevaluate every `selected + remaining candidate` subset after
  each selection.
- Select the strict improvement with the lowest resulting log odds; ties use
  candidate ID ordinal.
- Stop on attainment or when no candidate strictly improves the current result.
- Retain the improvements selected before non-attainment, but never label them
  minimal or optimal.

### Auto and quality

`auto` requires an explicitly supplied nonnegative candidate cutoff. It uses
exact when `candidateCount <= cutoff`, otherwise greedy, and records the actual
strategy and one of:

- `candidate-count-at-or-below-cutoff`
- `candidate-count-above-cutoff`

No elapsed-time strategy switch is allowed. The numeric cutoff remains
uncalibrated until Phase 6.

Exact-versus-greedy quality records attainment, selected cardinalities, a
cardinality gap only when exact proved an attaining optimum and greedy attained,
intersection/union counts, Jaccard similarity (`1` for two empty sets), margins,
evaluation counts, and both result digests.

Every subset is projected from immutable input and receives a fresh calculation
context. The logical result is one solution item, even when its selected set is
empty, so `totalResultCardinality` is `1` and the digest distinguishes attained,
unattained, and proof states. The item includes normalized baseline/result/
threshold values, selection details, responsible paths where v0 can identify
them, and proof flags.

Candidate kind `counter` remains eligible, but the frozen likelihood
recalculation recognizes only `evidence` and `objection` as evidence endpoints.
Phase 3 does not normalize the alias to `objection`; selected alias details
explicitly report that they were not recognized. This preserves rather than
hides the semantic mismatch.

## 5. `robustness-v0`

The Phase 0 semantic checkpoint is unchanged: all node, leaf, and edge kinds;
zero leaf contribution; stored posterior; maximum signed leaf-to-node log-LR;
and `exp(-abs(probability delta))`.

- One recursive memoized traversal computes each node's responsible path and
  vector once.
- Equal maximum path scores use the leaf-to-node node-ID sequence ordinal and
  then edge-ID sequence ordinal.
- Ranking is score ascending then node ID ordinal. Ranks are one-based.
- `LeastRobust` is the same first object from the already-computed ranking; it
  is not a second execution.
- Each item contains node ID/title/kind/rank, score, original and hypothetical
  probabilities, absolute delta, accumulated path log-LR and representable LR,
  ordered leaf-to-node IDs, and semantic version.
- Distribution records count, minimum, median, maximum, and mean score.
- Raw typed ranking values retain legacy numeric fidelity. A matching normalized
  JSON projection supplies compact retained items and complete-result digesting.

The recursion and documented deep-stack risk remain part of v0 at this phase.
The Phase 1 isolated worker contains a process failure; Phase 6 owns deep-chain
validation and any iterative-equivalence checkpoint.

## 6. Isolation and legacy compatibility

The production analysis worker consumes `insights-worker-protocol-v1`, accepts
one canonical request per process, dispatches the four Phase 3 operations, and
emits protocol frames only on standard output. It accepts a matching
cancellation frame concurrently with calculation. The Phase 1 supervisor owns
the hard deadline, cooperative grace period, and process-tree termination.
It advertises its configured protocol-line byte limit to the child through
`LOGICLIKELY_INSIGHTS_ANALYSIS_WORKER_MAX_PROTOCOL_LINE_BYTES`; a directly
launched worker uses the Phase 1 default. The worker checks the actual canonical
output event against that limit and applies the common whole-item prefix rule
before writing any output frame. The supervisor remains the receiving-side
protocol-bound authority.

The directly callable analyzers are deterministic cores for tests and worker
execution; exact production execution uses the isolated host.

No `/lab` route is introduced in Phase 3. Existing graph API routes remain
compatible:

- Evidence-impact and robustness DTOs are adapters over the rich versioned
  results.
- The evidence adapter retains the scalar endpoint's raw decimal/double
  precision; 12-place projection applies to rich digest items, not to the
  legacy DTO wire values.
- For evidence inputs outside the rich positive-LR DAG contract, the legacy
  route retains its pre-Phase 3 scalar behavior rather than silently narrowing
  its accepted input domain. The versioned worker remains strict.
- Least robustness is derived from the same ordinal ranking as the full result.
- `minimal-counter-set` remains the explicitly characterized
  `critical-counter-heuristic-v0` route and `{ counterNodeIds }` payload. It is
  not silently relabeled as v1.
- Nullable graph-context bodies explicitly allow an empty HTTP body. Database
  and supplied-context modes retain ordinal slug matching.
- Legacy analysis console diagnostics were removed and cancellation is threaded
  through the existing recalculation/path loops without changing golden values.

## 7. Frozen characterization digests

| Fixture | Digest |
|---|---|
| Rich strongest-path v1 | `sha256:c3841aa33fd15bc6ede2e4e5838a667bf8a613b57353aab676d7bb732f9f4f28` |
| Rich evidence-impact v0 | `sha256:de1fda3676e293004753c6b29ef6b29f9cc59d2004129af117b80d55b6dd6fb4` |
| Critical-counter v1 exact nested optimum | `sha256:25faf12463764574c1a3d7d17ba73f822e3b10789d38dc19030ad57c01214439` |
| Rich robustness v0 | `sha256:3d964b07066b5ad518dddd76fe0576fccf0c90973818b382296da100a7828ad9` |
| Frozen robustness v0 scalar ranking | `sha256:a6d2f8b34f6887a7c5332281e7db9c82912d6ae7145d0a9ff676b9bbc7daec21` |
| Frozen robustness v0 counter-only | `sha256:19887c1fd39090db890aec618fa599954bb87b24e0c7c2e45a09d7e587658929` |
| Frozen robustness v0 mixed/custom | `sha256:0971e853ace2d58a653c99116f17f218fad51c224e43f616a074927000941361` |
| Frozen critical-counter heuristic v0 defect | `sha256:84789ec23abde4f0b7eaaa2a4982598ff5c30063aee0b4ef57e3becd3fde9b60` |

Changing any Phase 3 logical item meaning, membership, ordering, or digest
projection requires a new semantic identity and new fixtures. Implementation
or isolation changes may retain the identity only when these outputs remain
equal.

## 8. Deferred questions and gates

The following remain deliberately unresolved and do not authorize Phase 4,
Phase 5, or Phase 6 work:

- The authoritative auto cutoff is calibrated in Phase 6. Until then every
  auto request must carry an explicit provisional cutoff and record it.
- Worker timeout and cancellation-grace durations are execution-policy inputs,
  not algorithm semantics; named profiles are Phase 4 work.
- Whether `counter` should become a likelihood evidence alias requires an
  explicitly versioned likelihood/critical-counter semantic decision.
- Exact and greedy intentionally expose different non-attaining selections
  under the frozen rules: exact returns the cardinality-first baseline after
  exhaustive proof, while greedy retains its strict improvements.
- Algorithm-partner acknowledgement is still required before an authoritative
  robustness baseline is promoted.
- Any support-only filtering, endpoint-kind restriction, posterior
  recomputation, formula change, or iterative implementation that changes the
  robustness digest requires a new semantic version.
- Deep-chain robustness equivalence, authoritative cutoff calibration, corpus
  fingerprint acceptance, edge-budget calibration, and baseline promotion
  remain Phase 6 checkpoints.
