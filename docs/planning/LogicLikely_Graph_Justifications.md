# Graph Justifications

## Directed Graphs

Graphs in LogicLikely are directed, meaning edges between nodes go in one direction, with a clear "to" and "from" node.

## DAGs

Directed acyclic graphs (DAGs) are directed and have no graph cycles. It would not make sense for an argument to rely on one of its branching arguments, so all graphs at LogicLikely are DAGs. Notably, this allows us to more easily make use of SSP algorithms.

## Graph Traversal

Primary methods of graph traversal, also known as exploring all nodes of a graph, are DFS and BFS, which are vertex-centric.

DFS recursively loops through all unvisited nodes neighboring the current selected node.

BFS reaches the same end result as DFS, with the added benefit that it works by traversing all nodes in a "frontier" before moving to the next one. Nodes in a frontier are all the same number of nodes away from the start node.

We will likely need to traverse graphs to see if a node is a child of another.

## Connectivity

Connectivity describes the problem of seeing whether two nodes, such as node A and node B, are connected. This is commonly done via the union-find algorithm. You can also accomplish this by performing DFS starting from node A or B and seeing if you come across the other node.

DFS naturally will explore every node in a connected component. You can also use graph contraction to see if there is a connection if you want to parallelize the process.

Connectivity checks could be important in ensuring our DAG precondition is being met.

## Cut Sets

Cut sets are created by "cutting" edges from a graph. This may partition the graph into two separate connected components, each spanning a different side of the cut. We map the nodes from these connected components to different sets.

Many algorithms, such as Dijkstra's, Boruvka's, Prim's, and Kruskal's, that we might use to create MSTs or find shortest weight paths rely on cut sets in their correctness proofs.

## Dominating Sets

Dominating sets are sets of nodes such that their combined list of neighbors, plus the nodes themselves, covers every node in the graph.

This concept will be crucial in finding the optimal premises to debunk in order to disprove an entire argument.

## Graph Resilience

Graph resilience is a measure of how many edges you can remove from a connected component before it breaks apart into different components.

Measuring resilience will be crucial in evaluating the robustness of an argument.
