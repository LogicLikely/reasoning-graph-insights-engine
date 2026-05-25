Feature: Graph details panel

  Scenario: Viewing details for a selected graph node
    Given a graph node is selected
    When I view the graph details panel
    Then I should see the selected node title

  Scenario: Viewing the panel before a node is selected
    Given no graph node is selected
    When I view the graph details panel
    Then I should see guidance to select a node
