# Graph Engine Purpose

## The problem

LogicLikely is a platform that not only visualizes arguments in a way that breaks them down and makes them easier to digest but also analyses them and produces meaningful insights. An important part of this mission is finding the smallest set of counterarguments required to disrupt a claim, and the Graph Insights Engine I am proposing will solve this NP hard graph problem.

## Why is a difficult/interesting problem

Identifying the minimum effective rebuttal set capable of disrupting the overall argument structure is an NP hard graph problem by nature: meaning there is no known algorithm that can guarantee an optimal solution as in polynomial time as the input size increases. The insights engine will address this challenge by dynamically switching between an exact solver (meant for small graph sizes) and a greedy approximation solver (meant for large graph sizes) after a certain granularity.

## What insights will be provided

- Critical premise identification
- Counterargument impact ranking
- Structural vulnerability scoring
- Resilience comparisons between graph types

## Out of Scope (What We Are NOT Building)

**Natural Language Processing (NLP):** The engine will not read essays or text to determine if an argument makes sense. We assume the data injected into the graph is already structured and valid.

**Production Database Integration:** This is a standalone prototype engine; it will not hook into LogicLikely’s live, primary production databases.

**Dynamic Argument Editing:** The UI is for visualizing and analyzing pre-existing or synthetically generated graphs, not a collaborative, live editing suite.
