# Insights Algorithm Research and Complexity Concepts

## Set Cover Problem

- Consider a Venn diagram with multiple sets (all subsets of the universe set $S$) overlapping with certain elements. A set cover would be the smallest sub-collection of $S$ whose union equals the universe $S$.

![Venn diagram showing overlapping sets for set cover](./insights_algorithm_research_assets/page1_image1.png)

- Example: If $S = \{\{1, 2, 3\}, \{2, 4\}, \{3, 4\}, \{4, 5\}\}$ and $U = \{1, 2, 3, 4, 5\}$, then a valid set cover would be $\{\{1, 2, 3\}, \{4, 5\}\}$.
- There are two main kinds of set cover problems: the decision problem and the optimization problem. Given a universe $U$ and a family of subsets $S$:
  - **Decision problem:** Input is a pair $(U, S)$ and an integer $k$. The question is whether there is a set cover of size $k$ or less.
  - **Optimization problem:** Input is a pair $(U, S)$ and the goal is to find the set cover that uses the fewest sets.
- The decision version is NP-complete, while the optimization problem is NP-hard.
- **Weighted set cover problem:** Each set is assigned a positive weight representing its cost, and the goal is to find the smallest-weight cover.
- **Capacitated set cover problem:** Each set $s \in S$ is associated with a capacity $c_s$. The capacity represents the number of elements that can count toward the total used in the cover set. For example, if a set has 10 elements but $c_s = 3$, only 3 elements can be used at once. The goal is to find the most efficient combination of sets to obtain full coverage of $U$.
- **Fractional set cover:** ?
- The greedy algorithm for weighted set cover is guaranteed to be at most a factor of $H(n)$ times the optimal solution.

## Hitting Set

- While mathematically equivalent, the hitting set problem and the set cover problem are framed from opposite perspectives.
- In the hitting set problem, you are given a collection of groups and want to pick the fewest number of elements such that you select at least one representative for each group.
- You are essentially creating the universal set that is given in the set cover problem.

## NP-Hardness

- NP = non-deterministic polynomial time.
- NP-hard problems have no known deterministic polynomial-time solution.
- Solving one NP-hard problem efficiently solves all of them and proves $P = NP$.
- As the size of input $n$ grows, the time to find the exact solution grows exponentially, such as $2^n$ or $n!$.
- Knapsack is technically NP-hard because the dynamic programming solution has a complexity of $O(nW)$, where $n$ is input size and $W$ is the capacity of the sack.
- Examples of NP-hard problems:
  - Knapsack optimization problems
  - Integer programming
  - Traveling salesman optimization problem
  - Minimum vertex cover
  - Maximum clique
  - Longest simple path
  - Graph coloring; an application: register allocation in compilers

## Brute Force Algorithms

- Make a list of every possible state for a situation.
- Loop through the list and do a calculation on each element until a viable solution is found.
- Slow, but can be made faster through the use of effective data structures.
- Can often be the starting point for an algorithm that can be built off of later.
- Can be helpful if algorithmic complexity does not matter too much and you want an exact solution.
- Will brute-force solutions on small graphs for LogicLikely.

## Greedy Approximation Algorithms

- May not come up with the optimal solution, but is usually close to it.
- Each step of the algorithm focuses on maximizing some metric locally, without regard for past or future decisions.
- Used in dynamic programming, Dijkstra's algorithm, BFS, Prim's algorithm, Kruskal's algorithm, etc.
- Will use a greedy algorithm for larger graphs in LogicLikely, trading accuracy for speed.
