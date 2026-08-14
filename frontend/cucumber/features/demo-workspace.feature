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

  Scenario: Switching between standard and compact graph views
    Given a graph workspace is available
    When I view the graph workspace
    Then the standard graph view should be selected
    When I switch to the compact graph view
    Then I should see the compact graph canvas
    When I expand the compact graph to the viewport
    Then the compact graph should fill the viewport
    When I restore the compact graph size
    And I switch to the standard graph view
    Then I should see the standard graph canvas
    And standard support and rebut edges should retain different colors
