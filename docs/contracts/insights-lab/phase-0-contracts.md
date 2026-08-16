# Insights Lab Phase 0 contracts

**Contract status:** Frozen for Phase 0; reconciled by Phase 3.5

**Contract family:** `insights-lab-v1`

**Scope:** Specifications, executable contract helpers, and characterization only

This document turns Phase 0 of the Insights Lab plan into an implementation
contract. It does not add benchmark persistence, workers, instrumentation,
GraphMap integration, replacement algorithms, runners, or Lab routes. Those
remain Phases 1 through 6.

## 1. Operation registry

Registry order is stable and is used anywhere operations need a deterministic
display or serialization order.

| Order | Operation key | Semantic identity | Exposure | Initial result surface |
|---:|---|---|---|---|
| 1 | `graph.catalog` | `graph-catalog-v1` | Benchmark diagnostic | Timing and count summary |
| 2 | `graph.fetch` | `graph-fetch-v1` | Benchmark diagnostic | Graph and payload summary |
| 3 | `graph.search` | `graph-search-v1` | Benchmark diagnostic | Match/union counts and optional projection |
| 4 | `path.strongest` | `strongest-path-v1` | Analysis and benchmark | Summary, ranked ordered paths, optional graph context |
| 5 | `path.single-pair` | `single-pair-path-v0` | Benchmark diagnostic | Diagnostic result and timing |
| 6 | `evidence.impact-ranking` | `evidence-impact-v0` | Analysis and benchmark | Summary, distribution, deterministic top 100 |
| 7 | `counter.critical-set` | `critical-counter-v1` | Analysis and benchmark | Selected counters, quality, optional graph context |
| 8 | `node.robustness` | `robustness-v0` | Analysis and benchmark | Least-robust summary, distribution, deterministic top 100 |
| 9 | `likelihood.recalculate` | `likelihood-recalculate-v0` | Benchmark diagnostic | Before/after likelihood summary |

`critical-counter-heuristic-v0` names the existing minimal-counter endpoint for
characterization. It is deliberately not the semantic identity of the planned
`counter.critical-set` operation.

`strongest-path-scalar-v0` names the existing scalar strongest-path behavior
for characterization. It is deliberately not the semantic identity of the
planned rich `path.strongest` operation.

### Semantic identity rules

A semantic identity has the form `<family>-v<N>`, where `N` is a non-negative
integer. The complete string is persisted; it is not inferred from an assembly
or package version.

- Increment `N` when observable meaning changes: eligibility, mathematical
  formula, threshold behavior, direction, path choice, tie-breaking, ordering,
  canonical result membership, or interpretation of a field.
- Keep the identity when an implementation, caching strategy, traversal, or
  isolation mechanism changes but the same logical inputs retain the same
  canonical output and digest.
- Adding optional envelope metadata does not change an algorithm identity.
  Removing or reinterpreting a field changes the export schema and may also
  require a new algorithm identity.
- A changed identity is incompatible with prior runs by default. Compatibility
  is never inferred merely because two versions happen to produce one equal
  digest.
- `v0` is a frozen characterization, not permission to change behavior silently.

### Deterministic algorithm ordering

- Strongest path v1 orders by absolute accumulated log-LR descending, then by
  signed accumulated log-LR descending (so an equal-magnitude support path wins
  over a counter path, matching the current scalar tie), then by the ordered
  node-ID sequence and ordered edge-ID sequence using ordinal comparison.
- Evidence impact v0 orders each supporting/counter partition by absolute raw
  probability delta descending, then node ID ordinal.
- Critical-counter v1 uses the objective in section 2.
- Robustness v0 orders score ascending, then node ID ordinal. “Least robust” is
  the first row of that one ranking.

For a succeeded result, operations without strategies use `null`/`null`;
single-pair `used` equals its requested `minimum` or `maximum`; critical-counter
`exact` and `greedy` use the requested strategy, while `auto` resolves `used`
to `exact` or `greedy`. Non-succeeded results may preserve an invalid raw
requested value for validation reporting and may not yet have a used strategy.

## 2. Critical-counter v1

### Target and candidates

- The target is any existing node that is not itself an eligible counter.
- A candidate ID is unique under ordinal comparison, is not the target, exists
  in the graph, has kind `objection` or the legacy alias `counter`
  case-insensitively, and has at least one directed structural path to the target
  following edges from `From` to `To`.
- Candidate reachability considers all edge kinds. Every edge must have a finite
  likelihood ratio greater than zero. The complete operation input must be a
  directed acyclic graph; any directed cycle is a validation failure because
  the operation does not guess at cyclic likelihood semantics.
- Eligibility does not require a positive marginal improvement. Exact may
  inspect an eligible but unhelpful candidate; greedy must stop when none of the
  remaining candidates improves the target.

### Removal and application

Let `C` be the complete eligible candidate set and `S` the selected subset.

1. The no-counter baseline is the graph induced by all nodes except `C`.
   Removing a candidate removes the node and every incident edge.
2. Applying `S` restores those candidate nodes, then restores only original
   edges whose two endpoints are active. Non-candidate descendants remain in
   the base graph and reconnect naturally when their selected candidate is
   restored. Shared non-candidate context is retained once.
3. Every evaluated subset starts from the same immutable input graph and builds
   a fresh calculation context. It uses the versioned likelihood-recalculation
   semantics on that context. It must not leave excluded candidates traversable
   or manually add a selected candidate's contribution a second time.
4. Stored posterior values are inputs only where the versioned likelihood
   contract explicitly uses them; mutation from one evaluated subset never
   leaks into another.

These rules intentionally differ from `critical-counter-heuristic-v0`, whose
ID-only exclusion leaves counter nodes in traversal and can double-count a
later selected counter.

### Threshold and result objective

- Default threshold: target log-odds `-1`.
- Attainment: `resultingLogOdds <= thresholdLogOdds`, equivalently resulting
  probability at or below the threshold probability.
- An already-attaining baseline returns the empty set.
- Among attaining results, compare lexicographically:
  1. fewer selected counters;
  2. larger below-threshold margin (`threshold - resultingLogOdds`);
  3. the ascending ordinal sequence of selected node IDs.
- Exact reports whether optimal cardinality was proven. Greedy never uses
  “minimal” or “optimal” unless independently proven. Auto records requested
  strategy, actual strategy, candidate count, configured cutoff, and selection
  reason.
- A completed search that cannot attain the threshold is a succeeded execution
  with `thresholdAttained: false`, not a failure. The same objective order is
  retained: fewer selected counters, then greater margin, then the ascending
  ordinal node-ID sequence. Never describe a non-attaining set as minimal or
  optimal. Timeout, cancellation, crash, and validation failure remain distinct
  outcomes.

The numeric auto cutoff is intentionally not frozen here; Phase 6 calibrates it.

## 3. Frozen current algorithm behavior

### `robustness-v0` semantic checkpoint

The implementation checkpoint answers are frozen as follows:

| Question | Frozen v0 answer |
|---|---|
| Ranked node kinds | Every graph node, regardless of kind |
| Leaf/path endpoint kinds | Every structural leaf (a node with no child edge), regardless of kind |
| Edge kinds | Every structural edge; `support`, `rebut`, and other labels are not filtered |
| Path value | Maximum accumulated `ln(edge likelihood ratio)` from a structural leaf to the node |
| Leaf evidence contribution | Omitted; a leaf contributes zero before edge weights |
| Posterior source | The node's stored posterior log-odds, not a graph-wide recomputation |
| Hypothetical posterior | Stored posterior log-odds minus the selected path log-LR |
| Score | `exp(-abs(sigmoid(storedPosterior) - sigmoid(hypotheticalPosterior)))` |
| Range | Approximately `exp(-1)` (`0.367879...`) through `1` |
| Ordering | Score ascending, then node ID ordinal |
| Least robust | First row of the single graph-wide ranking |
| Equal-log-LR reported path | Maximum log-LR, then leaf-to-node node-ID sequence ordinal, then edge-ID sequence ordinal |
| Structural safety | Recursive and cycle-rejecting; sufficiently deep chains may overflow the process stack |

Support-only, counter-only, and mixed paths therefore differ only through their
numeric edge likelihood ratios. An iterative implementation may retain
`robustness-v0` only when it preserves the frozen golden output digest. Any
change to the table above requires a new semantic identity.

The accumulated path log-LR is authoritative. A helper vector exposes the
accumulated LR as a decimal when representable and explicit `null` when
exponentiation would overflow or underflow that representation; this does not
invalidate the probability or robustness-score calculation.

The code-derived checkpoint is complete for freezing v0. Algorithm-partner
acknowledgement is still required before an authoritative robustness baseline
is promoted; it does not permit v0 behavior to drift in the meantime.

### Other current behavior

- `strongest-path-scalar-v0` characterization returns scalar accumulated log
  scores. It selects the value farthest from neutral; equal absolute magnitudes
  select the greater signed value. It does not yet return an ordered path.
- Evidence-impact v0 recalculates the target baseline, subtracts each evidence
  node's strongest scalar path contribution, partitions by contribution sign,
  and applies the ordering in section 1. It does not yet expose all rich fields.
- Critical-counter heuristic v0 uses a hard-coded `-1` threshold, legacy
  priority calculation, and incomplete removal semantics. Its golden fixture
  is evidence of the baseline defect, not the v1 contract.
- The legacy standalone least-robust service method recomputes the full ranking
  and resolves equal scores by graph insertion order. That wrapper is not an
  authoritative `robustness-v0` result surface; the frozen contract uses one
  score-then-ordinal ranking and derives “least robust” from its first row.
- Likelihood recalculation v0 uses the current graph-calculation semantics and
  deterministic distance then ordinal node ordering.

## 4. Run and result contracts

The JSON export schema identity is `insights-run-export-v1`. One export contains
one complete run manifest, zero or more samples, and zero or more compact
outputs.

The checked-in export example uses an explicitly illustrative three-node graph;
it is a schema/digest fixture, not a generated stress-catalog run or an
authoritative performance baseline.

### Status and failure

Execution status is exactly one of `queued`, `running`, `succeeded`, `failed`,
`timed-out`, `cancelled`, `crashed`, or `skipped`.

Validation failure is represented by execution status `failed` and failure kind
`validation`. Other failure kinds distinguish execution failure, timeout,
cancellation, crash, and skip.

### Retention and paths

- `totalResultCardinality` describes the complete logical result.
- `items` contains at most the first 100 items in the operation's deterministic
  order.
- `resultDigest` covers the complete logical result before top-100 retention.
- Summary and distribution data are compact. A full-result artifact reference
  is optional.
- An ordered path stores node IDs and edge IDs in traversal order plus its
  accumulated score. Ordered paths are not silently truncated.

### Canonical JSON and digests

- Serialize as UTF-8 JSON without a byte-order mark or insignificant
  whitespace.
- Sort object member names using ordinal comparison. Preserve array order.
- Include explicit nulls for contract fields; do not alternate between omitted
  and null forms in material covered by a digest.
- Normalize integral and decimal numbers to their shortest invariant
  mathematical representation. Normalize remaining finite floating-point
  values to a round-trip invariant representation; normalize negative zero to
  zero and exponent spelling consistently.
- Before an algorithm's floating-derived logical result values enter a result
  digest, project them to 12 fractional decimal places with midpoint-to-even
  rounding. Exact counts, identifiers, and canonical input decimals are not
  rounded by this rule.
- Preserve string code points exactly; producers are responsible for any
  domain-level Unicode normalization before constructing the contract.
- Date-time contract fields use an ISO 8601 offset form with seconds, optional
  fractional seconds, and an explicit `+HH:MM` or `-HH:MM` offset; UTC is
  written as `+00:00`, not `Z`.
- Enum tokens are exact lowercase kebab-case strings. Numeric, differently
  cased, and unknown enum forms are rejected, as are unmapped object members.
- Hash the canonical bytes with SHA-256 and encode lowercase hexadecimal with
  the prefix `sha256:`.

Canonical parameters and complete logical results use the same algorithm.
Changing field order cannot change a digest; changing array order or a logical
value must change it.

## 5. Run compatibility

Default comparison is compatible only when every value below matches exactly
using ordinal string comparison:

1. scenario key;
2. operation key;
3. dataset/input fingerprint;
4. algorithm semantic identity;
5. canonical parameter digest;
6. environment profile;
7. build mode;
8. measurement unit contract.

The evaluator returns every mismatch rather than only the first. The UI and
future runners must reject or visibly label an incompatible pair; equality of
result digests cannot override incompatible identity.

No Phase 0 output is an authoritative performance baseline. Promotion still
requires the accepted 100K corpus fingerprint, `ll-arm64-mac-primary`, and the
later authoritative profile.

## 6. Deferred questions and gates

The following are deliberately unresolved by Phase 0 and must not be guessed
or used to widen this implementation:

- the calibrated `auto` candidate cutoff (Phase 6);
- the accepted 100K dataset/corpus fingerprint and first authoritative baseline
  (Phase 6);
- whether a future robustness version should restrict ranked kinds, endpoints,
  or edge kinds, or recompute posterior odds (requires a new semantic identity);
- algorithm-partner acknowledgement of the frozen robustness-v0 checkpoint
  before baseline promotion.

Phase 0 does not create benchmark history, so none can be declared
authoritative by this work.
