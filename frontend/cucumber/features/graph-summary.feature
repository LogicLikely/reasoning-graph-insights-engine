Feature: Graph summary

  Scenario: Viewing a summary of the current graph
    Given a graph summary is available
    When I view the graph summary
    Then I should see the graph summary counts
