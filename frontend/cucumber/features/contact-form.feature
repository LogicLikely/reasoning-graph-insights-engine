Feature: Contact form

  Scenario: Required fields are validated
    Given the contact form is empty
    When I try to submit the contact form
    Then I should see that name is required

  Scenario: Invalid email is rejected
    Given the contact form has an invalid email address
    When I try to submit the contact form
    Then I should see that the email address is invalid

  Scenario: Valid contact form can be submitted
    Given the contact form has valid values
    When I submit the contact form
    Then I should see a successful submission message

  Scenario: Validation messages appear when clicking submit on an empty form
    Given the contact form is empty
    When I try to submit the contact form
    Then I should see that name is required
