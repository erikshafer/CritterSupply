Feature: Shipment Dispatch and Tracking
  As a fulfillment operations system and customer experience system
  I want to correctly dispatch shipments to carriers and track them to delivery
  So that customers receive their orders and are informed at every meaningful step

  # Remaster Note (ADR 0059): Dispatch and tracking events use the Shipment aggregate.
  # Events in this file (ShippingLabelGenerated through ShipmentDelivered) live on the
  # Shipment stream. ShipmentDispatched is replaced by the more precise
  # ShipmentHandedToCarrier event (physical custody transfer to carrier).
  # ShipmentDeliveryFailed is replaced by the explicit delivery attempt chain:
  # DeliveryAttemptFailed(1/2/3) → ReturnToSenderInitiated.
  # The Shipment stream also holds routing events (FulfillmentRequested,
  # FulfillmentCenterAssigned) from the intake flow.

  Background:
    Given the fulfillment system is operational
    And the carrier integration APIs are available:
      | Carrier | Service Types                               |
      | UPS     | Ground, 2-Day, Overnight, SurePost          |
      | FedEx   | Ground, 2-Day, Overnight, SmartPost         |
      | USPS    | Priority Mail, Ground Advantage, First Class|
    And the tracking event polling interval is 15 minutes
    And the lost-in-transit threshold is 5 business days without a carrier scan


  # ============================================================
  # HAPPY PATH: Label → Stage → Carrier → Delivered
  # ============================================================

  Scenario: Order is labeled, staged, handed to carrier, and delivered successfully
    Given order "CS-2026-020100" has completed packing at NJ FC
    And the shipment contains:
      | SKU           | Product Name                        | Quantity |
      | DOG-FOOD-40LB | Hill's Science Diet Dog Food 40lb   | 1        |
    And the destination is "789 Maple Ave, Richmond, VA 23220"
    And the requested shipping service is "UPS Ground"
    When the rate shopping engine queries UPS, FedEx, and USPS carrier APIs
    Then UPS Ground is selected based on lowest cost for the shipping zone
    And a DIM weight calculation is applied: actual 40lb, DIM 38lb, billable weight 40lb
    And the domain event "ShippingLabelGenerated" is appended with:
      | Field          | Value                          |
      | Carrier        | UPS                            |
      | Service        | Ground                         |
      | BillableWeight | 40lb                           |
      | LabelZPL       | [ZPL label data]               |
    And the domain event "TrackingNumberAssigned" is appended with tracking number "1Z999AA10123456784"
    And the integration event "TrackingNumberAssigned" is published to the Orders BC message bus
    And the Customer Experience BC receives the tracking number and displays it in the order tracking UI
    And the order tracking UI now shows "Your order has shipped! Track with UPS: 1Z999AA10123456784"
    And the domain event "ShipmentManifested" is appended to the shipment stream
    When the package is moved to the UPS staging lane at the NJ FC outbound dock
    Then the domain event "PackageStagedForPickup" is appended with carrier "UPS" and pickup window "2:00 PM – 3:00 PM ET"
    When the UPS driver arrives and scans the manifest at NJ FC at 2:14 PM ET
    Then the domain event "CarrierPickupConfirmed" is appended with:
      | Field          | Value              |
      | Carrier        | UPS                |
      | DriverScan     | true               |
      | PickupTime     | 14:14 ET           |
    And the domain event "ShipmentHandedToCarrier" is appended to the shipment stream
    And the integration event "ShipmentHandedToCarrier" is published to the Orders BC
    When the UPS carrier system records a facility scan at the Edison, NJ hub at 4:32 PM ET
    Then the domain event "ShipmentInTransit" is appended
    And the order tracking UI shows "In Transit — Last scan: Edison, NJ hub"
    When the UPS carrier system records an out-for-delivery scan at 8:17 AM the following morning
    Then the domain event "OutForDelivery" is appended
    And the order tracking UI shows "Out for Delivery"
    When the UPS carrier system records a delivery confirmation at 11:43 AM
    Then the domain event "ShipmentDelivered" is appended to the shipment stream
    And the integration event "ShipmentDelivered" is published to the Orders BC
    And the customer receives an email: "Your order has arrived! 📦"
    And the order tracking UI shows "Delivered — June 3, 2026 at 11:43 AM"
    And the shipment stream is in terminal state "Delivered"


  # ============================================================
  # TRACKING NUMBER NOT AVAILABLE UNTIL LABEL GENERATED
  # ============================================================

  Scenario: Customer checks order status before label is generated — no tracking number shown
    Given order "CS-2026-021000" has been placed and confirmed
    And the order is assigned to OH FC
    And the order contains:
      | SKU           | Product Name              | Quantity |
      | CAT-TOY-LASER | PetSafe FroliCat Bolt     | 1        |
    And the work order has been created but packing has not yet completed
    When the customer views their order status in the Customer Experience UI
    Then the order tracking UI shows:
      | Section          | Content                                              |
      | Order Status     | "We've received your order and are preparing it"     |
      | Tracking Section | Progress bar with "Preparing" step highlighted       |
      | Tracking Number  | Not displayed (hidden until TrackingNumberAssigned)  |
      | Track Button     | Not displayed                                        |
    And no tracking number field is rendered in the DOM
    When packing completes at OH FC
    And the domain event "ShippingLabelGenerated" is appended
    And the domain event "TrackingNumberAssigned" is appended with tracking number "794644774000"
    And the SignalR hub pushes the "TrackingNumberAssigned" event to the customer's browser session
    Then the order tracking UI updates in real time to show:
      | Section          | Content                                               |
      | Order Status     | "Your order has shipped!"                             |
      | Tracking Number  | "794644774000"                                        |
      | Track Button     | Displayed — links to FedEx tracking page              |


  # ============================================================
  # SAD PATH: Carrier Pickup Missed
  # ============================================================

  Scenario: FedEx driver does not arrive for scheduled pickup at WA FC — alternate carrier arranged
    Given order "CS-2026-022300" has been labeled, manifested, and staged at WA FC
    And the order contains:
      | SKU             | Product Name              | Quantity |
      | BIRD-SEED-20LB  | Wild Bird Seed Mix 20lb   | 2        |
    And the shipment is staged in the FedEx lane at WA FC outbound dock
    And the scheduled FedEx pickup window is 12:00 PM – 1:00 PM PT
    And WA FC operating hours are 6:00 AM – 10:00 PM PT
    When the clock reaches 1:00 PM PT and no FedEx driver has arrived
    Then the domain event "CarrierPickupMissed" is appended with:
      | Field              | Value                       |
      | Carrier            | FedEx                       |
      | ScheduledWindow    | 12:00 PM – 1:00 PM PT       |
      | ActualArrival      | None                        |
      | DetectedAt         | 13:00 PT                    |
    And the WA FC dock supervisor "SUP-Kim" is alerted immediately via WMS
    And the domain event "CarrierRelationsEscalated" is appended
    When dock supervisor "SUP-Kim" contacts FedEx carrier relations at 1:05 PM PT
    Then FedEx reports a driver capacity issue and cannot pick up until tomorrow
    And the WA FC supervisor determines UPS has available capacity today
    And UPS Ground is arranged as the alternate carrier
    And the domain event "AlternateCarrierArranged" is appended with:
      | Field              | Value        |
      | OriginalCarrier    | FedEx        |
      | AlternateCarrier   | UPS          |
      | PickupRescheduled  | Same day     |
    And the shipping label is voided for FedEx
    And the domain event "ShippingLabelVoided" is appended
    And a new UPS label is generated for the same destination
    And the domain event "ShippingLabelGenerated" is appended with carrier "UPS"
    And the domain event "TrackingNumberAssigned" is appended with the new UPS tracking number
    And the customer receives a notification: "Your shipment tracking number has been updated."
    When the UPS driver arrives at 3:45 PM PT and scans the manifest
    Then the domain event "CarrierPickupConfirmed" is appended
    And the domain event "ShipmentHandedToCarrier" is appended
    And delivery date is recalculated and remains within the original delivery promise


  # ============================================================
  # SAD PATH: Ghost Shipment (Carrier Took But Did Not Scan)
  # ============================================================

  Scenario: Carrier picks up package but does not record a facility scan within 24 hours — ghost shipment
    Given order "CS-2026-023500" was handed to UPS at NJ FC at 2:10 PM ET on Monday
    And the domain event "ShipmentHandedToCarrier" is appended with tracking number "1Z888BB10987654321"
    And the order contains:
      | SKU            | Product Name                    | Quantity |
      | REPTILE-HEAT   | Zoo Med ReptiTherm Under Tank Heater | 1   |
    When 24 hours pass without any carrier scan recorded for tracking number "1Z888BB10987654321"
    Then the domain event "GhostShipmentDetected" is appended with:
      | Field           | Value                             |
      | TrackingNumber  | 1Z888BB10987654321                |
      | HoursWithoutScan| 24                                |
      | DetectedAt      | Tuesday 2:10 PM ET                |
    And the fulfillment operations team is alerted
    And CritterSupply contacts UPS carrier relations to locate the package
    When UPS confirms the package was missed at the Newark hub sort and is now being processed
    And UPS records a facility scan at the Edison, NJ hub at 6:14 PM ET Tuesday
    Then the domain event "ShipmentInTransit" is appended
    And the ghost shipment flag is resolved
    And no customer notification was sent (resolved within tolerance window)
    And the delivery date is assessed — if delayed past original promise, customer is notified proactively


  # ============================================================
  # SAD PATH: Lost in Transit → Carrier Trace → Reship
  # ============================================================

  Scenario: Shipment has no carrier scan for 5 business days — lost in transit — reship dispatched immediately
    Given order "CS-2026-024700" was handed to USPS at NJ FC on Monday June 1
    And the domain event "ShipmentHandedToCarrier" is appended with tracking number "9400111899223066784512"
    And the last recorded carrier scan was "Accepted at USPS Origin Facility" on Monday June 1 at 3:45 PM ET
    And the order contains:
      | SKU              | Product Name                            | Quantity |
      | SMALL-PET-WHEEL  | Kaytee Silent Spinner Wheel 8.5-inch    | 1        |
    When 5 business days pass (through Monday June 8) without any additional carrier scan
    Then the domain event "ShipmentLostInTransit" is appended with:
      | Field              | Value                          |
      | TrackingNumber     | 9400111899223066784512         |
      | LastScanDate       | Monday June 1                  |
      | BusinessDaysNoScan | 5                              |
      | Carrier            | USPS                           |
    And the domain event "CarrierTraceOpened" is appended with:
      | Field              | Value                                   |
      | Carrier            | USPS                                    |
      | TraceWindowDays    | 15                                      |
      | TraceReferenceNum  | TRACE-USPS-2026-0608-001                |
    And CritterSupply does not wait for the trace to resolve before reshipping
    And a new fulfillment request is created for order "CS-2026-024700"
    And the domain event "ReshipmentCreated" is appended with:
      | Field               | Value                    |
      | OriginalShipmentId  | shipment-024700-A        |
      | NewShipmentId       | shipment-024700-B        |
      | Reason              | LostInTransit            |
    And the customer receives an email:
      """
      Subject: We're reshipping your order — no action needed

      We've been unable to locate your shipment with USPS (tracking: 9400111899223066784512).

      We've sent you a replacement at no charge:
        Kaytee Silent Spinner Wheel 8.5-inch

      New tracking: [new tracking number once labeled]

      If your original package does arrive, you're welcome to keep it.
      """
    And the original shipment stream is marked "Lost — Replacement Shipped"
    And the order tracking UI shows both shipment entries:
      | Shipment      | Status                       |
      | Original      | Lost — Replacement Shipped   |
      | Replacement   | In progress                  |
    When the USPS trace period of 15 days expires without locating the package
    Then the domain event "CarrierClaimFiled" is appended
    And the carrier claim window of 15 business days is tracked


  # ============================================================
  # SAD PATH: Delivery Failure → Retry → Return to Sender
  # ============================================================

  Scenario: Three failed delivery attempts result in return to sender and reship-or-refund decision
    Given order "CS-2026-025900" was dispatched via UPS Ground to "456 Oak St, Denver, CO 80203"
    And the domain event "ShipmentHandedToCarrier" is appended with tracking number "1Z777CC10112233445"
    And the order contains:
      | SKU           | Product Name              | Quantity |
      | DOG-HARNESS-M | Ruffwear Front Range Harness Medium | 1 |
    When UPS records a delivery exception on Tuesday at 11:22 AM MT
    Then the delivery exception code is "NI" (No one home / No indirect delivery accepted)
    And the domain event "DeliveryAttemptFailed" is appended with:
      | Field          | Value             |
      | AttemptNumber  | 1                 |
      | ExceptionCode  | NI                |
      | ExceptionDesc  | No one home       |
      | AttemptDate    | Tuesday           |
    And the customer receives an email:
      """
      Subject: Delivery attempt for your CritterSupply order

      UPS attempted to deliver your package today but was unable to complete delivery.
      They will try again tomorrow.

      [Schedule redelivery with UPS My Choice]
      [Redirect to UPS Access Point pickup location]
      """
    When UPS records a second delivery exception on Wednesday at 10:55 AM MT
    Then the domain event "DeliveryAttemptFailed" is appended with attempt number 2
    And the customer receives a second email with escalated options including hold at UPS facility
    When UPS records a third delivery exception on Thursday at 11:08 AM MT
    Then the domain event "DeliveryAttemptFailed" is appended with attempt number 3
    And UPS initiates return-to-sender after the third failed attempt
    And the domain event "ReturnToSenderInitiated" is appended with:
      | Field              | Value                       |
      | Carrier            | UPS                         |
      | TotalAttempts      | 3                           |
      | EstimatedRTSDays   | 5 to 10 business days       |
    And the customer receives an email:
      """
      Subject: Your package is being returned to us

      After three delivery attempts, UPS is returning your package to our fulfillment center.
      This typically takes 5–10 business days.

      Once we receive it, we'll contact you about reshipping to a new address or issuing a full refund.
      """
    When the package arrives at NJ FC after 7 business days
    Then the domain event "ReturnReceived" is appended
    And a CS task is created to contact the customer about reship vs. refund


  # ============================================================
  # COMPENSATION: "Carrier Says Delivered" — First Offense Reship
  # ============================================================

  Scenario: Customer disputes delivery — first offense — reship issued without fraud review
    Given order "CS-2026-026800" shows "Delivered" status in the carrier system
    And the domain event "ShipmentDelivered" is appended with tracking number "794899526780"
    And the delivery confirmation timestamp is "June 5, 2026 at 2:34 PM"
    And the delivery confirmation photo shows a front porch at a different address
    And the order contains:
      | SKU           | Product Name                           | Quantity |
      | CAT-FOOD-HILL | Hill's Prescription Diet c/d Cat Food | 1        |
    And customer "sarah.chen@example.com" has no prior delivery disputes in the past 12 months
    When customer "sarah.chen@example.com" contacts CS via the "I didn't receive this" workflow
    Then the CS system checks the customer's dispute history
    And the dispute count for customer "sarah.chen@example.com" in the past 12 months is 0
    And this is classified as a first offense
    And the domain event "DeliveryDisputed" is appended with:
      | Field            | Value                        |
      | OrderId          | CS-2026-026800               |
      | CustomerId       | sarah.chen@example.com       |
      | OffenseNumber    | 1                            |
      | Resolution       | ReshippedNoQuestionsAsked    |
    And the CS agent selects "Reship — First Offense" without requiring fraud review
    And the domain event "ReshipmentCreated" is appended
    And the customer receives an email:
      """
      Subject: We're reshipping your order

      We're sorry your order didn't arrive as expected.

      We've sent a replacement at no charge:
        Hill's Prescription Diet c/d Cat Food

      New tracking: [new tracking number]
      """
    And the original order history shows the dispute and resolution with offense count 1


  # ============================================================
  # MULTI-FC: Split Order — Both Shipments Delivered
  # ============================================================

  Scenario: Order split across NJ FC and WA FC — both shipments delivered successfully
    Given order "CS-2026-027500" was placed containing items from two different FC inventory pools
    And the order contains:
      | SKU            | Product Name                         | Quantity | Fulfilling FC |
      | DOG-FOOD-40LB  | Hill's Science Diet Dog Food 40lb    | 1        | NJ FC         |
      | AQUARIUM-55GAL | Fluval 55-Gallon Aquarium Kit        | 1        | WA FC         |
    And the domain event "OrderSplitIntoShipments" was appended at order routing time with:
      | Shipment ID          | FC    | SKU            |
      | shipment-027500-A    | NJ FC | DOG-FOOD-40LB  |
      | shipment-027500-B    | WA FC | AQUARIUM-55GAL |
    And the Customer Experience UI shows "Your order ships in 2 groups"
    When shipment "shipment-027500-A" completes packing and labeling at NJ FC
    Then the domain event "TrackingNumberAssigned" is appended to "shipment-027500-A" with tracking "1Z-NJ-027500-A"
    And the order tracking UI shows:
      | Shipment Group | Status              | Tracking          |
      | 1 of 2 (NJ FC) | Label Created       | 1Z-NJ-027500-A    |
      | 2 of 2 (WA FC) | In Preparation      | Not yet available |
    When shipment "shipment-027500-B" completes packing and labeling at WA FC
    Then the domain event "TrackingNumberAssigned" is appended to "shipment-027500-B" with tracking "1Z-WA-027500-B"
    And the order tracking UI shows:
      | Shipment Group | Status         | Tracking          |
      | 1 of 2 (NJ FC) | In Transit     | 1Z-NJ-027500-A    |
      | 2 of 2 (WA FC) | Label Created  | 1Z-WA-027500-B    |
    When UPS delivers shipment "shipment-027500-A" on June 3
    Then the domain event "ShipmentDelivered" is appended to "shipment-027500-A"
    And the order tracking UI shows:
      | Shipment Group | Status              | Delivered Date |
      | 1 of 2 (NJ FC) | ✅ Delivered        | June 3, 2026   |
      | 2 of 2 (WA FC) | 🚚 In Transit       | Est. June 5    |
    And the overall order status is still "Partially Delivered" — not "Delivered"
    When FedEx delivers shipment "shipment-027500-B" on June 5
    Then the domain event "ShipmentDelivered" is appended to "shipment-027500-B"
    And the overall order status transitions to "Delivered"
    And the customer receives an email: "Your complete order has arrived! 📦"
    And the order tracking UI shows:
      | Shipment Group | Status        | Delivered Date |
      | 1 of 2 (NJ FC) | ✅ Delivered  | June 3, 2026   |
      | 2 of 2 (WA FC) | ✅ Delivered  | June 5, 2026   |


  # ============================================================
  # MULTI-FC: Split Order — One Shipment Lost, One Delivered
  # ============================================================

  Scenario: Split order where second shipment is lost in transit — reship dispatched for lost shipment only
    Given order "CS-2026-028200" was split across NJ FC and OH FC
    And the order contains:
      | SKU            | Product Name                           | Quantity | Fulfilling FC | Shipment ID       |
      | DOG-FOOD-40LB  | Hill's Science Diet Dog Food 40lb      | 1        | NJ FC         | shipment-028200-A |
      | CAT-SCRATCHER  | PetFusion Ultimate Cat Scratcher Post  | 1        | OH FC         | shipment-028200-B |
    And the domain event "OrderSplitIntoShipments" was appended at routing time
    And both shipments were handed to carriers:
      | Shipment          | Carrier | Tracking           | Handed Over  |
      | shipment-028200-A | UPS     | 1Z-NJ-028200-A     | Monday June 1|
      | shipment-028200-B | FedEx   | 794-OH-028200-B    | Monday June 1|
    When UPS delivers shipment "shipment-028200-A" on Wednesday June 3
    Then the domain event "ShipmentDelivered" is appended to "shipment-028200-A"
    And the overall order status is "Partially Delivered"
    When 5 business days pass without a FedEx scan on "shipment-028200-B" (through Monday June 8)
    Then the domain event "ShipmentLostInTransit" is appended to "shipment-028200-B"
    And the domain event "CarrierTraceOpened" is appended for "shipment-028200-B"
    And a new fulfillment request is created for "shipment-028200-B" line items only
    And the domain event "ReshipmentCreated" is appended to "shipment-028200-B" stream
    And the customer receives an email:
      """
      Subject: Update on part of your order CS-2026-028200

      Good news: Your Hill's Science Diet Dog Food 40lb was delivered on June 3.

      Unfortunately, we've lost track of your other shipment (FedEx tracking: 794-OH-028200-B):
        PetFusion Ultimate Cat Scratcher Post

      We're reshipping it immediately at no charge.
      New tracking: [new tracking number]
      """
    And the CS view of order "CS-2026-028200" shows both shipments with their independent statuses
    And the CS view flags that "shipment-028200-B" requires holistic review alongside "shipment-028200-A"
