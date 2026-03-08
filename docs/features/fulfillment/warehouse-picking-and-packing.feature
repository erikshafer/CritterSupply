Feature: Warehouse Picking and Packing
  As a fulfillment operations system
  I want to correctly pick, verify, and pack customer orders
  So that shipments leave the fulfillment center accurately and on time

  Background:
    Given the fulfillment system is operational
    And the following fulfillment centers are active:
      | FC ID | Name              | Location    | Timezone |
      | NJ-FC | NJ Fulfillment    | Newark, NJ  | Eastern  |
      | OH-FC | OH Fulfillment    | Columbus, OH| Eastern  |
      | WA-FC | WA Fulfillment    | Kent, WA    | Pacific  |
      | TX-FC | TX 3PL Partner    | Dallas, TX  | Central  |
    And pick/pack SLA thresholds are configured:
      | Order Type          | SLA Hours |
      | Standard            | 4         |
      | Expedited           | 2         |
      | Hazmat              | 6         |
      | 3PL                 | 8         |
    And SLA escalation rules are active:
      | Threshold | Alert Recipient   |
      | 50%       | FC Supervisor     |
      | 75%       | Operations Manager|
      | 100%      | Operations Manager + urgent flag |


  # ============================================================
  # HAPPY PATH: Standard Discrete Pick and Pack
  # ============================================================

  Scenario: Standard order is picked and packed successfully using discrete pick strategy
    Given a fulfillment request has been received for order "CS-2026-001122"
    And the order is assigned to NJ FC
    And the order contains the following line items:
      | SKU            | Product Name                          | Quantity | Bin Location |
      | DOG-FOOD-40LB  | Hill's Science Diet Adult Dog Food 40lb | 1      | A-12-03      |
      | CAT-TOY-LASER  | PetSafe FroliCat Bolt Laser Cat Toy   | 2        | C-04-07      |
    And all bin locations are stocked with sufficient quantity
    And a picker "P-Martinez" is available with an RF scanner at NJ FC
    When the WMS batches the shipment into pick wave "WAVE-NJ-20260601-001"
    Then the domain event "WaveReleased" is appended to the shipment stream
    And a pick list is assigned to picker "P-Martinez"
    And the domain event "PickListAssigned" is appended to the shipment stream
    When picker "P-Martinez" begins picking at bin "A-12-03"
    Then the domain event "PickStarted" is appended to the shipment stream
    When picker "P-Martinez" scans SKU "DOG-FOOD-40LB" at bin "A-12-03"
    Then the domain event "ItemPicked" is appended for SKU "DOG-FOOD-40LB" with quantity 1
    When picker "P-Martinez" scans SKU "CAT-TOY-LASER" at bin "C-04-07" twice
    Then the domain event "ItemPicked" is appended for SKU "CAT-TOY-LASER" with quantity 2
    And the domain event "PickCompleted" is appended to the shipment stream
    And the shipment status transitions to "PickCompleted"
    When the items arrive at pack station "PS-04" at NJ FC
    Then the domain event "PackingStarted" is appended to the shipment stream
    When the packer scans SKU "DOG-FOOD-40LB" at the pack station
    Then the domain event "ItemVerifiedAtPack" is appended for SKU "DOG-FOOD-40LB"
    When the packer scans SKU "CAT-TOY-LASER" twice at the pack station
    Then the domain event "ItemVerifiedAtPack" is appended for SKU "CAT-TOY-LASER" with quantity 2
    And the system selects carton size "C3" (12x12x10 inches) based on dimensional weight
    And the carton is sealed with void fill
    Then the domain event "PackingCompleted" is appended to the shipment stream
    And the shipment status transitions to "PackCompleted"
    And the elapsed pick/pack time is within the 4-hour standard SLA


  # ============================================================
  # BATCH PICKING: Multiple Orders in One Wave
  # ============================================================

  Scenario: Batch pick wave combines three orders for efficiency at OH FC
    Given the following fulfillment requests have been received and assigned to OH FC:
      | Order ID      | SKU            | Product Name                         | Quantity | Bin Location |
      | CS-2026-002001| CAT-FOOD-WET-24| Fancy Feast Wet Cat Food 24-Pack     | 1        | B-08-02      |
      | CS-2026-002002| CAT-FOOD-WET-24| Fancy Feast Wet Cat Food 24-Pack     | 2        | B-08-02      |
      | CS-2026-002003| DOG-TREAT-ZUKE | Zuke's Mini Naturals Dog Treats 6oz  | 3        | D-11-05      |
    And picker "P-Thompson" is available at OH FC
    When the WMS batches orders "CS-2026-002001", "CS-2026-002002", and "CS-2026-002003" into batch wave "WAVE-OH-20260601-007"
    Then the domain event "WaveReleased" is appended to each shipment stream
    And a batch pick list is assigned to picker "P-Thompson" covering all three orders
    And the domain event "PickListAssigned" is appended to each shipment stream
    When picker "P-Thompson" arrives at bin "B-08-02" and picks 3 units of "CAT-FOOD-WET-24" into the batch cart
    Then the system records the pick as serving orders "CS-2026-002001" (1 unit) and "CS-2026-002002" (2 units)
    And the domain event "ItemPicked" is appended for each order with the correct quantities
    When picker "P-Thompson" arrives at bin "D-11-05" and picks 3 units of "DOG-TREAT-ZUKE"
    Then the domain event "ItemPicked" is appended for order "CS-2026-002003" with quantity 3
    And the domain event "PickCompleted" is appended to all three shipment streams
    When the batch cart items are sorted at the pack station induction point
    Then order "CS-2026-002001" receives 1 unit of "CAT-FOOD-WET-24"
    And order "CS-2026-002002" receives 2 units of "CAT-FOOD-WET-24"
    And order "CS-2026-002003" receives 3 units of "DOG-TREAT-ZUKE"
    And the domain event "PackingStarted" is appended to each shipment stream


  # ============================================================
  # SAD PATH: Short Pick — Alternative Bin Found
  # ============================================================

  Scenario: Short pick detected at primary bin but alternative bin resolves the shortage
    Given a fulfillment request has been received for order "CS-2026-003301"
    And the order is assigned to WA FC
    And the order contains:
      | SKU            | Product Name                          | Quantity | Primary Bin | Alternative Bin |
      | FISH-FOOD-API  | API Tropical Flakes Fish Food 5.3oz   | 2        | F-03-08     | F-03-09         |
    And bin "F-03-08" contains only 1 unit of "FISH-FOOD-API"
    And bin "F-03-09" contains 3 units of "FISH-FOOD-API"
    And picker "P-Nguyen" is assigned to the pick wave at WA FC
    When picker "P-Nguyen" scans bin "F-03-08" for SKU "FISH-FOOD-API"
    Then picker "P-Nguyen" picks 1 unit successfully from bin "F-03-08"
    When picker "P-Nguyen" attempts to pick the second unit from bin "F-03-08"
    Then the WMS detects a quantity shortage at bin "F-03-08"
    And the domain event "ShortPickDetected" is appended to shipment stream for order "CS-2026-003301"
    And the RF scanner displays "SHORT PICK — Checking alternative bins..."
    When the WMS identifies bin "F-03-09" as containing sufficient stock
    Then the RF scanner directs picker "P-Nguyen" to bin "F-03-09"
    And the domain event "PickResumed" is appended to the shipment stream
    When picker "P-Nguyen" picks 1 unit of "FISH-FOOD-API" from bin "F-03-09"
    Then the domain event "ItemPicked" is appended for SKU "FISH-FOOD-API" with total quantity 2
    And the domain event "PickCompleted" is appended to the shipment stream
    And the shipment proceeds to packing without delay
    And no customer notification is sent (short pick resolved transparently)


  # ============================================================
  # SAD PATH: Short Pick — Emergency Re-Route to Alternate FC
  # ============================================================

  Scenario: Short pick with no stock at assigned FC triggers emergency re-route to OH FC
    Given a fulfillment request has been received for order "CS-2026-004500"
    And the order is assigned to NJ FC
    And the order contains:
      | SKU            | Product Name                            | Quantity |
      | DOG-BED-ORTHO  | Furhaven Orthopedic Dog Bed Large       | 1        |
    And NJ FC has 0 units of "DOG-BED-ORTHO" in all bin locations (including cycle count verified)
    And OH FC has 3 units of "DOG-BED-ORTHO" available
    And picker "P-Garcia" is assigned to the pick wave at NJ FC
    When picker "P-Garcia" arrives at the assigned bin location for "DOG-BED-ORTHO"
    Then the bin is empty
    And the WMS confirms zero units across all NJ FC bin locations for SKU "DOG-BED-ORTHO"
    And the domain event "ShortPickDetected" is appended to the shipment stream
    And the domain event "PickExceptionRaised" is appended with reason "NoStockAtAssignedFC"
    When the Order Routing Engine evaluates alternate FCs for SKU "DOG-BED-ORTHO"
    Then OH FC is identified as having sufficient stock
    And the Order Routing Engine determines OH FC meets the delivery date commitment
    And the domain event "ShipmentRerouted" is appended with new FC assignment "OH-FC"
    And a new work order is created at OH FC for order "CS-2026-004500"
    And the domain event "WorkOrderCreated" is appended to the shipment stream at OH FC
    And no customer notification is sent (reroute is transparent)
    And the original NJ FC work order is closed


  # ============================================================
  # SAD PATH: Short Pick → Backorder (No Stock at Any FC)
  # ============================================================

  Scenario: Short pick with no stock at any FC results in backorder and customer notification
    Given a fulfillment request has been received for order "CS-2026-005700"
    And the order is assigned to NJ FC
    And the order contains:
      | SKU               | Product Name                               | Quantity |
      | REPTILE-LAMP-UVB  | Zoo Med ReptiSun 10.0 UVB Lamp T8 24-inch  | 1        |
    And NJ FC has 0 units of "REPTILE-LAMP-UVB"
    And OH FC has 0 units of "REPTILE-LAMP-UVB"
    And WA FC has 0 units of "REPTILE-LAMP-UVB"
    And TX FC (3PL) has 0 units of "REPTILE-LAMP-UVB"
    When the short pick exception is raised at NJ FC
    Then the domain event "ShortPickDetected" is appended to the shipment stream
    And the domain event "PickExceptionRaised" is appended with reason "NoStockAtAssignedFC"
    When the Order Routing Engine checks all FCs including TX 3PL
    Then all FCs report zero available stock for "REPTILE-LAMP-UVB"
    And the domain event "BackorderCreated" is appended to the shipment stream
    And the Inventory BC is notified to flag SKU "REPTILE-LAMP-UVB" for replenishment
    And the customer "CS-2026-005700" receives an email notification:
      """
      Subject: Update on your CritterSupply order

      We're sorry — one item in your order is temporarily out of stock:
        Zoo Med ReptiSun 10.0 UVB Lamp T8 24-inch

      We'll ship it as soon as it's available.
      You can cancel this item at any time from your order history.
      """
    And the order tracking UI shows the "REPTILE-LAMP-UVB" line item status as "Temporarily out of stock"


  # ============================================================
  # SAD PATH: Weight Discrepancy at Pack Station
  # ============================================================

  Scenario: Weight discrepancy detected at pack station triggers exception workflow
    Given a fulfillment request has been received for order "CS-2026-006100"
    And the order is assigned to OH FC
    And the order contains:
      | SKU             | Product Name                          | Quantity | Expected Weight (oz) |
      | CAT-LITTER-40LB | Fresh Step Clumping Cat Litter 40lb   | 1        | 643                  |
    And the item has been successfully picked and conveyed to pack station "PS-07" at OH FC
    And the domain event "PackingStarted" is appended to the shipment stream
    When the packer scans SKU "CAT-LITTER-40LB" at the pack station
    Then the domain event "ItemVerifiedAtPack" is appended
    When the packer places the item on the pack station scale
    Then the scale reads 428 oz (actual) versus 643 oz (expected)
    And the weight variance is 33% which exceeds the 5% tolerance threshold
    And the domain event "PackDiscrepancyDetected" is appended with details:
      | Field              | Value                         |
      | SKU                | CAT-LITTER-40LB               |
      | ExpectedWeightOz   | 643                           |
      | ActualWeightOz     | 428                           |
      | VariancePercent    | 33                            |
      | DiscrepancyType    | WeightMismatch                |
    And the pack station display shows "WEIGHT DISCREPANCY — Supervisor required"
    And the FC Supervisor "SUP-Johnson" is alerted via WMS notification
    When Supervisor "SUP-Johnson" inspects the item and determines the bag has a tear and lost product
    Then the supervisor records disposition "ItemDamaged_ReplacementRequired"
    And a replacement unit of "CAT-LITTER-40LB" is retrieved from bin "G-02-11"
    And the domain event "PickResumed" is appended for the replacement pick
    And the domain event "ItemPicked" is appended for the replacement unit
    When the replacement unit passes the weight check at 641 oz (within 5% tolerance)
    Then packing resumes normally
    And the domain event "PackingCompleted" is appended to the shipment stream


  # ============================================================
  # SPECIAL HANDLING: Temperature-Sensitive Items (Cold Packs)
  # ============================================================

  Scenario: Temperature-sensitive raw pet food order requires cold pack at NJ FC
    Given a fulfillment request has been received for order "CS-2026-007200"
    And the order is assigned to NJ FC
    And the order contains:
      | SKU               | Product Name                              | Quantity | Temp Sensitive | Storage Zone |
      | RAW-DOG-FOOD-2LB  | Stella & Chewy's Frozen Raw Patties 2lb   | 3        | Yes            | Cold Storage |
    And the shipping service is "Standard Ground" (requiring 24-hour cold pack)
    And cold packs are stocked and available at NJ FC pack station "PS-COLD-01"
    When the WMS routes the pick list for order "CS-2026-007200" to the cold storage zone
    And picker "P-Williams" retrieves 3 units of "RAW-DOG-FOOD-2LB" from the cold storage zone at 38°F
    Then the domain event "ItemPicked" is appended with metadata "TemperatureSensitive: true"
    And the domain event "PickCompleted" is appended to the shipment stream
    When the items are conveyed to cold pack station "PS-COLD-01"
    Then the domain event "PackingStarted" is appended
    When the packer scans all 3 units of "RAW-DOG-FOOD-2LB"
    Then each unit is verified via SVP (scan-verify-pack)
    And the domain event "ItemVerifiedAtPack" is appended for each unit
    When the packer adds a 24-hour cold pack rated for ground shipping transit
    Then the domain event "ColdPackApplied" is appended with:
      | Field                | Value                    |
      | ColdPackType         | 24-hour gel pack         |
      | ShippingService      | Standard Ground          |
      | MaxTransitHours      | 24                       |
      | PackStationVerified  | PS-COLD-01               |
    And the carton is sealed with insulated liner
    And the domain event "PackingCompleted" is appended to the shipment stream
    And the carton is flagged "PERISHABLE — KEEP REFRIGERATED" for staging
    And the elapsed pick/pack time is within the 4-hour standard SLA


  # ============================================================
  # SPECIAL HANDLING: Hazmat Items (Ground-Only Enforcement)
  # ============================================================

  Scenario: Hazmat flea treatment order enforces ground-only shipping and 6-hour SLA
    Given a fulfillment request has been received for order "CS-2026-008400"
    And the order is assigned to NJ FC
    And the order contains:
      | SKU                 | Product Name                              | Quantity | Hazmat Class       |
      | FLEA-FRONTLINE-6PK  | Frontline Plus Flea & Tick Treatment 6-Pack | 1      | ORM-D (Limited Qty)|
    And the customer originally selected "FedEx 2-Day Air" as the shipping service
    When the order enters the work order creation step
    Then the domain event "HazmatItemFlagged" is appended for SKU "FLEA-FRONTLINE-6PK" with hazmat class "ORM-D"
    And the domain event "HazmatShippingRestrictionApplied" is appended with:
      | Field                 | Value                               |
      | RestrictedServices    | Air — all air services              |
      | PermittedServices     | Ground only (UPS Ground, FedEx Ground, USPS Ground Advantage) |
      | Reason                | ORM-D / Limited Quantity air exclusion |
    And the shipping service is automatically downgraded from "FedEx 2-Day Air" to "FedEx Ground"
    And the customer receives an email notification:
      """
      Subject: Shipping method update for order CS-2026-008400

      One item in your order contains restricted materials that cannot ship via air:
        Frontline Plus Flea & Tick Treatment 6-Pack

      Your shipment has been switched to ground shipping at no additional charge.
      New estimated delivery: [ground delivery date].
      """
    And the pick/pack SLA for this order is set to 6 hours (hazmat SLA)
    When the WMS assigns the pick list with hazmat designation
    Then the domain event "PickListAssigned" is appended with "HazmatDesignated: true"
    And picker "P-Rodriguez" retrieves "FLEA-FRONTLINE-6PK" from the hazmat-designated bin "H-01-03"
    And the domain event "ItemPicked" is appended with metadata "HazmatClass: ORM-D"
    And the domain event "PickCompleted" is appended
    When the item arrives at hazmat pack station "PS-HZ-01"
    Then the domain event "PackingStarted" is appended
    And the packer applies required ORM-D marking to the outer carton
    And the packer verifies the package does not exceed Limited Quantity weight limits
    When the packer completes the SVP scan
    Then the domain event "ItemVerifiedAtPack" is appended
    And the domain event "PackingCompleted" is appended
    And the shipment is staged in the hazmat-designated staging lane
    And the elapsed pick/pack time is within the 6-hour hazmat SLA


  # ============================================================
  # SLA ESCALATION: Expedited Order Approaching Breach
  # ============================================================

  Scenario: Expedited order SLA escalates to operations manager when 75% threshold reached
    Given a fulfillment request has been received for order "CS-2026-009900"
    And the order type is "Expedited"
    And the order is assigned to OH FC
    And the pick/pack SLA for expedited orders is 2 hours
    And the work order was created at 9:00 AM Eastern (within OH FC operating hours)
    And the order contains:
      | SKU           | Product Name                          | Quantity |
      | DOG-BOWL-SS   | Stainless Steel Dog Bowl 64oz         | 1        |
    When 1 hour and 30 minutes have elapsed since work order creation (75% of 2-hour SLA)
    And the pick list has not yet been assigned
    Then the domain event "SLAEscalationRaised" is appended with:
      | Field            | Value              |
      | OrderId          | CS-2026-009900     |
      | OrderType        | Expedited          |
      | SLAHours         | 2                  |
      | ElapsedMinutes   | 90                 |
      | ThresholdPercent | 75                 |
      | AlertedRole      | OperationsManager  |
    And the Operations Manager "MGR-Chen" receives a WMS alert:
      """
      EXPEDITED ORDER SLA WARNING
      Order CS-2026-009900 — 75% of 2-hour SLA elapsed
      Status: PickListAssigned not yet received
      Action required: Assign picker immediately
      """
    When 2 hours have elapsed since work order creation (100% SLA breach)
    And the pick list is still not assigned
    Then the domain event "SLABreached" is appended with threshold "100%"
    And Operations Manager "MGR-Chen" receives an urgent alert
    And the customer is placed on a 2-hour hold notification queue


  # ============================================================
  # PACK STATION: No Valid Carton Size Available
  # ============================================================

  Scenario: Pack station cannot find a valid carton for oversized item — discrepancy raised
    Given a fulfillment request has been received for order "CS-2026-010200"
    And the order is assigned to WA FC
    And the order contains:
      | SKU              | Product Name                              | Quantity | Dimensions (L x W x H inches) |
      | AQUARIUM-55GAL   | Fluval 55-Gallon Aquarium Complete Kit    | 1        | 49 x 24 x 21                  |
    And WA FC pack stations carry carton sizes up to 36 x 24 x 24 inches
    When the item arrives at pack station "PS-09" at WA FC after picking
    Then the domain event "PackingStarted" is appended
    When the packer scans SKU "AQUARIUM-55GAL" and attempts carton selection
    Then the system determines no available carton can accommodate a 49-inch dimension
    And the domain event "PackDiscrepancyDetected" is appended with:
      | Field              | Value                             |
      | SKU                | AQUARIUM-55GAL                    |
      | DiscrepancyType    | NoValidCartonSize                 |
      | ItemDimensions     | 49x24x21 inches                   |
      | MaxCartonAvailable | 36x24x24 inches                   |
    And the pack station supervisor is notified
    When the supervisor determines the item ships as SIOC (Ships In Own Container)
    Then the SIOC shipping method is applied to the shipment
    And the outer carton requirement is waived
    And carrier-compliant shipping label placement is confirmed on the original manufacturer packaging
    And the domain event "PackingCompleted" is appended with "PackingMethod: SIOC"
