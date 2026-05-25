Feature: Graph details panel story
  To quickly verify the graph UI in isolation
  As a frontend developer
  I want to open a Storybook graph story in a browser

  Scenario: Viewing the default graph details panel story
    Given Storybook is running for the frontend
    When I open the GraphDetailsPanel default story
    Then I should see the selected node title
