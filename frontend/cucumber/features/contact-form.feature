Feature: Contact form

  Scenario: Required fields are validated
    Given the contact form is empty
    When I try to submit the contact form
    Then I should see that name is required
