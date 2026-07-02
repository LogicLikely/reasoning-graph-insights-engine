# Structural Insights Engine Implementation Plan

## Epic 1 - Graph Storage and Data Model

### Story: Define Backend Graph Schema

**Description**

Create the database structure required to represent the reasoning graph.

The graph should support:

- Claim nodes
- Evidence nodes
- Relationships between nodes
- Support edges
- Counter edges
- Claim dependency relationships

Determine whether graph relationships require a new table structure.

**Estimate:** Medium

**Acceptance Criteria**

- Claims can be stored
- Evidence can be stored
- Connections between nodes can be stored
- Edge type is represented
- Graph can be reconstructed from database data

### Story: Implement Graph Repository Layer

**Description**

Create backend repository methods for interacting with graph data.

Required functionality:

- Retrieve graph
- Retrieve individual claims
- Retrieve evidence
- Add/remove nodes
- Add/remove connections

**Estimate:** Medium

**Acceptance Criteria**

- Backend can load graph structure
- Backend can update graph structure
- Existing APIs use repository layer

## Epic 2 - Evidence Cache System

### Story: Create Claim Evidence Cache

**Description**

Create a cache that stores all evidence connected to each claim.

The cache should allow algorithms to quickly answer:

> What evidence affects this claim?

Example:

```text
Claim A -> Evidence 1, Evidence 2, Evidence 3
```

**Estimate:** Medium

**Acceptance Criteria**

- Cache table is created
- Claim ID maps to connected evidence IDs
- Multiple claims can reference the same evidence
- Algorithms can retrieve evidence without graph traversal

### Story: Implement Cache Generation Algorithm

**Description**

Build the process that calculates the evidence connected to each claim.

The algorithm should:

- Traverse claim relationships
- Collect supporting evidence
- Collect counter evidence
- Store results in cache

**Estimate:** Medium/Large

**Acceptance Criteria**

- Cache can be rebuilt from graph structure
- Nested claims include evidence from child claims
- Duplicate evidence is handled correctly

## Epic 3 - Odds Update Algorithm

### Story: Implement Claim Confidence Calculation

**Description**

Create the core odds update algorithm.

Given:

- Current claim confidence
- Supporting evidence
- Counter evidence

Calculate updated claim confidence.

**Estimate:** Medium

**Acceptance Criteria**

- Evidence increases/decreases confidence correctly
- Multiple evidence values combine correctly
- Unit tests verify expected calculations

### Story: Connect Odds Algorithm to Evidence Cache

**Description**

Allow confidence calculations to automatically use cached evidence.

Flow:

```text
Claim ID
  ↓
Get cached evidence
  ↓
Apply odds update
  ↓
Return updated confidence
```

**Estimate:** Medium

**Acceptance Criteria**

- Algorithm works from only claim ID input
- Evidence lookup uses cache
- Confidence results can be stored

## Epic 4 - Critical Counter Analysis

### Story: Implement Brute Force Critical Counter Algorithm

**Description**

Create a correctness-focused implementation that finds the optimal set of counterarguments needed to weaken a claim.

This will be used to validate faster algorithms.

**Estimate:** Large

**Acceptance Criteria**

- Algorithm finds optimal counter set
- Works on small graphs
- Results can be compared against greedy solution

### Story: Implement Greedy Critical Counter Algorithm

**Description**

Create optimized algorithm for finding high-impact counterarguments.

Process:

- Rank available counters
- Apply strongest counters first
- Stop when claim confidence falls below threshold

**Estimate:** Medium

**Acceptance Criteria**

- Counters are ranked by impact
- Algorithm returns minimal discovered counter set
- Runtime is compared against brute force

## Epic 5 - Evidence Impact Ranking

### Story: Implement Supporting Evidence Impact Ranking

**Description**

Determine which supporting evidence contributes most to a claim.

Simulate removing evidence and measure confidence change.

**Estimate:** Medium

**Acceptance Criteria**

- Supporting evidence is ranked
- Higher impact evidence appears first
- Results include confidence change amount

### Story: Implement Counter Evidence Impact Ranking

**Description**

Determine which counter evidence most reduces claim confidence.

**Estimate:** Medium

**Acceptance Criteria**

- Counter evidence is ranked
- Strongest counter evidence appears first
- Results include confidence impact

## Epic 6 - Fragile Claim Detection

### Story: Define Claim Fragility Metric

**Description**

Create rules for determining whether a claim is fragile.

Possible factors:

- Low confidence
- Dependence on small number of evidence nodes
- Easily changed by counterarguments

**Estimate:** Medium

**Acceptance Criteria**

- Fragility calculation is documented
- Algorithm inputs are defined
- Output score is defined

### Story: Implement Weakest Claims Algorithm

**Description**

Analyze the graph and identify claims most vulnerable to changing.

**Estimate:** Medium

**Acceptance Criteria**

- Multiple claims can be analyzed
- Claims are ranked by fragility
- Results identify areas needing stronger evidence
