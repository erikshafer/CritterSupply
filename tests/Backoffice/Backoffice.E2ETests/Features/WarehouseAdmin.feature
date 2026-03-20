Feature: Warehouse Admin
  As a Warehouse Clerk
  I want to manage inventory through the Backoffice
  So that I can maintain accurate stock levels

  Background:
    Given the Backoffice application is running
    And stub inventory has SKU "KIBBLE-001" with 50 available and 10 reserved
    And stub inventory has SKU "TREATS-002" with 5 available and 0 reserved
    And stub inventory has SKU "BOWLS-003" with 0 available and 0 reserved

  Scenario: Warehouse Clerk can browse inventory list
    Given admin user "WarehouseClerk" exists with email "warehouse@crittersupply.com" and role "warehouse-clerk"
    And I am logged in as "warehouse@crittersupply.com"
    When I navigate to the inventory list
    Then I should see the inventory table
    And I should see SKU "KIBBLE-001" in the inventory list
    And I should see SKU "TREATS-002" in the inventory list
    And I should see SKU "BOWLS-003" in the inventory list

  Scenario: Warehouse Clerk can filter inventory by SKU
    Given admin user "WarehouseClerk" exists with email "warehouse@crittersupply.com" and role "warehouse-clerk"
    And I am logged in as "warehouse@crittersupply.com"
    When I navigate to the inventory list
    And I search inventory for "KIBBLE"
    Then I should see SKU "KIBBLE-001" in the inventory list
    And I should not see SKU "TREATS-002" in the inventory list

  Scenario: Warehouse Clerk can navigate to inventory edit page
    Given admin user "WarehouseClerk" exists with email "warehouse@crittersupply.com" and role "warehouse-clerk"
    And I am logged in as "warehouse@crittersupply.com"
    When I navigate to the inventory list
    And I click on SKU "KIBBLE-001" in the inventory list
    Then I should be on the inventory edit page for "KIBBLE-001"
    And I should see the available quantity is "50"
    And I should see the reserved quantity is "10"
    And I should see the total quantity is "60"

  Scenario: Warehouse Clerk can adjust inventory for cycle count
    Given admin user "WarehouseClerk" exists with email "warehouse@crittersupply.com" and role "warehouse-clerk"
    And I am logged in as "warehouse@crittersupply.com"
    When I navigate to the inventory edit page for SKU "KIBBLE-001"
    And I set the adjustment quantity to "5"
    And I select the adjustment reason "Cycle Count"
    And I submit the inventory adjustment
    Then I should see the inventory success message "Inventory adjusted by 5 (Cycle Count)"
    And the available quantity should be updated to "55"

  Scenario: Warehouse Clerk can adjust inventory for damage (negative)
    Given admin user "WarehouseClerk" exists with email "warehouse@crittersupply.com" and role "warehouse-clerk"
    And I am logged in as "warehouse@crittersupply.com"
    When I navigate to the inventory edit page for SKU "KIBBLE-001"
    And I set the adjustment quantity to "-3"
    And I select the adjustment reason "Damage"
    And I submit the inventory adjustment
    Then I should see the inventory success message "Inventory adjusted by -3 (Damage)"
    And the available quantity should be updated to "47"

  Scenario: Warehouse Clerk can receive inbound stock
    Given admin user "WarehouseClerk" exists with email "warehouse@crittersupply.com" and role "warehouse-clerk"
    And I am logged in as "warehouse@crittersupply.com"
    When I navigate to the inventory edit page for SKU "TREATS-002"
    And I set the receive quantity to "20"
    And I set the receive source to "Acme Pet Supplies"
    And I submit the stock receipt
    Then I should see the inventory success message "Received 20 units from Acme Pet Supplies"
    And the available quantity should be updated to "25"

  Scenario: Adjustment button is disabled when quantity is zero and no reason selected
    Given admin user "WarehouseClerk" exists with email "warehouse@crittersupply.com" and role "warehouse-clerk"
    And I am logged in as "warehouse@crittersupply.com"
    When I navigate to the inventory edit page for SKU "KIBBLE-001"
    Then the adjust submit button should be disabled

  Scenario: Receive button is disabled when quantity is zero
    Given admin user "WarehouseClerk" exists with email "warehouse@crittersupply.com" and role "warehouse-clerk"
    And I am logged in as "warehouse@crittersupply.com"
    When I navigate to the inventory edit page for SKU "KIBBLE-001"
    Then the receive submit button should be disabled

  Scenario: Session expired redirects to login during inventory edit
    Given admin user "WarehouseClerk" exists with email "warehouse@crittersupply.com" and role "warehouse-clerk"
    And I am logged in as "warehouse@crittersupply.com"
    When I navigate to the inventory edit page for SKU "KIBBLE-001"
    And my session expires
    And I set the adjustment quantity to "5"
    And I select the adjustment reason "Cycle Count"
    And I submit the inventory adjustment
    Then I should be redirected to the login page

  Scenario: SystemAdmin can access warehouse admin pages
    Given admin user "SystemAdmin" exists with email "sysadmin@crittersupply.com" and role "system-admin"
    And I am logged in as "sysadmin@crittersupply.com"
    When I navigate to the inventory list
    Then I should see the inventory table
    When I click on SKU "KIBBLE-001" in the inventory list
    Then I should be on the inventory edit page for "KIBBLE-001"
    And I should see the adjust inventory form
    And I should see the receive stock form
