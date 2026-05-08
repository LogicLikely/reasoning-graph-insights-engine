# Demo Page UI Requirements

## Purpose

The Demo page presents a reasoning graph in a way that helps a user explore how an overall idea is supported, challenged, and evidenced.

The page should let a user:

- see the full graph structure at a glance
- understand the role of each node in the graph
- inspect more detail for any selected node
- quickly compare the graph as a whole with the currently selected item

## Page Structure

The Demo page is organized into three main areas:

1. A page header that identifies the page as the interactive graph demo
2. A main graph area that displays the reasoning graph itself
3. A right-side panel area that shows details and graph-level summary information

The graph should remain the primary focus of the page.

## Graph Area

The main graph area displays a connected network of reasoning nodes and directional edges.

The graph should visually communicate that:

- the graph has a central topic or root idea
- claims can support that root idea
- premises and evidence can support claims
- counterpoints can challenge claims or premises

The graph should be readable without requiring the user to understand any technical graph terminology.

## Node Types

The graph uses distinct node types so the user can quickly understand what role each item plays.

The current node types are:

- Root: the main idea or top-level conclusion
- Claim: a major supporting statement connected to the root
- Premise: a supporting reason underneath a claim
- Evidence: a supporting item that adds observable or recorded support
- Counter: a challenging or opposing point

Each node type should be visually recognizable at a glance.

## What Each Node Displays

Each node card should display a short, scannable summary rather than every available detail.

Each node shows:

- a leading symbol at the start of the title to indicate the type of node
- a short title
- a single key metric beneath the title

The symbols currently communicate:

- `🌍` for the root node
- `🌿` for a claim
- `🌱` for a premise
- `🔬` for evidence

Counter nodes should be visually distinct through a faint red tint applied to the node background.

### Title Rules

The title shown on the node card should stay compact and readable.

- If the title is short enough, it should display in full
- If the title is too long, it should be truncated on the node card and end with an ellipsis
- The layout should assume the title may wrap to two lines
- Node cards should maintain a consistent height, even when title length varies

### Metric Rules

The single metric shown on the node card depends on the node type.

- Root and Claim nodes show `Importance`
- Premise and Counter nodes show `Confidence`
- Evidence nodes show `Score`

These metrics are intended to help the user compare nodes quickly without opening the side panel.

## What Edges Display

Edges connect related nodes and indicate the direction of the relationship.

Each edge should:

- connect one node to another with a directional arrow
- show whether the relationship is supportive or rebutting

The current relationship labels are:

- `Support`
- `Rebut`

Edges help the user understand how ideas connect, but they should remain visually lighter than the nodes so the graph itself stays readable.

## Hover Interaction

When the user hovers over a node, a tooltip should appear.

The tooltip should:

- display the longer explanatory body text for that node
- be easy to read
- appear above surrounding graph elements so it is not hidden behind other selected or nearby nodes

This interaction is meant to provide fast context without changing the current selection.

Hovering over a node should not update the side panel.

## Click Interaction

When the user clicks a node, that node becomes the selected node.

Clicking a node should:

- visually indicate which node is selected
- update the Node Details panel on the right side of the page
- keep the rest of the graph visible so the user can retain context

Clicking a different node should replace the previous selection.

If no node is selected, the Node Details panel should show an empty-state prompt inviting the user to select a node.

## Right-Side Panel Area

The right side of the page contains two separate panels that should remain visually distinct from one another.

The two panels are:

- Node Details
- Graph Overview

These panels should scroll together as one sidebar unit rather than overlapping or moving independently.

## Node Details Panel

The Node Details panel explains the currently selected node in more depth.

When a node is selected, this panel should display:

- the node title
- the node type
- the node identifier
- the longer body text
- any available metadata such as category, tags, prior, confidence, weight, or importance
- evidence-specific details when applicable, including evidence type, score, and rationale

When no node is selected, this panel should display an instructional empty state telling the user to click a node to inspect it.

The Node Details panel is the primary place for detailed, per-node inspection.

## Graph Overview Panel

The Graph Overview panel describes the graph as a whole rather than the selected node.

This panel should display:

- the graph title
- the graph description
- the total number of nodes
- the total number of edges
- the fixture or graph identifier currently being shown

The `Nodes` and `Edges` metrics should appear side by side for quick scanning.

This panel gives the user orientation and context even before any node is selected.

## Loading and Error States

The Demo page should clearly communicate when graph data is not yet ready or could not be loaded.

### Loading State

While the graph is being prepared, the page should show:

- a loading message
- a short plain-language explanation that the graph is being fetched and laid out

### Error State

If the graph cannot be loaded, the page should show:

- an error message
- a short explanation that the graph is unavailable
- a retry action

## Overall Experience Goals

The Demo page should feel:

- visually readable
- exploratory
- understandable to non-technical users
- structured around progressive disclosure

At a glance, the user should understand the shape of the reasoning. With hover, they should get quick context. With click, they should get full node detail.
