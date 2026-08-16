Feature: Browser performance harness

  Scenario: Rendering a bounded analysis result with timing evidence
    Given a bounded browser performance result fixture is available
    When I run the bounded result browser journey
    Then the browser performance journey should succeed
    And it should expose incremental result-render timing evidence
    And it should preserve complete cardinality while bounding mounted rows
