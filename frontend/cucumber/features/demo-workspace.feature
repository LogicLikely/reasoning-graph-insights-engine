Feature: Demo workspace

  Scenario: Viewing the graph workspace
    Given a graph workspace is available
    When I view the graph workspace
    Then I should see the graph workspace title

  Scenario: Viewing the workspace while graph data is loading
    Given the graph is loading
    When I view the graph workspace
    Then I should see that the graph is loading

  Scenario: Recovering after the graph fails to load
    Given the graph fails to load initially
    When I view the graph workspace
    And I retry loading the graph
    Then I should be able to continue into the graph workspace
