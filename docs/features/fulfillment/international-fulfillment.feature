Feature: International Fulfillment
  As a fulfillment operations system
  I want to correctly route, document, and ship international orders to Canada and the UK
  So that customers outside the US receive their orders with accurate customs handling and duties

  Background:
    Given the fulfillment system is operational
    And the following international hubs are active:
      | Hub ID       | Name             | Location        | Timezone | Countries Served |
      | TORONTO-HUB  | Toronto Hub      | Toronto, ON     | Eastern  | Canada           |
      | BHAM-HUB     | Birmingham Hub   | Birmingham, UK  | GMT/BST  | United Kingdom   |
    And the carrier cutoff times for international hubs are:
      | Hub           | Cutoff Time | Timezone |
      | Toronto Hub   | 11:00 AM    | Eastern  |
      | Birmingham Hub| 10:00 AM    | GMT/BST  |
    And international carriers are configured:
      | Carrier          | Hubs Served              |
      | DHL Express      | Toronto Hub, Birmingham Hub |
      | FedEx International | Toronto Hub, Birmingham Hub |
    And the landed cost engine is configured with Avalara AvaTax for Canada and Zonos for UK
    And the USMCA de minimis threshold for Canada is CAD $150.00
    And all Canada orders use DDP (Delivered Duty Paid) by default
    And all UK orders use DDP (Delivered Duty Paid) by default


  # ============================================================
  # HAPPY PATH: Canada Order — Toronto Hub — Customs Cleared — Delivered
  # ============================================================

  Scenario: Canada order routed to Toronto Hub clears customs and is delivered successfully
    Given a customer in Toronto, Ontario has placed order "CS-2026-050100"
    And the shipping destination is "221 King St W, Toronto, ON M5H 1K4, Canada"
    And the order contains:
      | SKU            | Product Name                         | Quantity | Unit Price USD | HS Code        | Country of Origin |
      | DOG-FOOD-40LB  | Hill's Science Diet Dog Food 40lb    | 1        | 49.99          | 2309.10.00     | USA               |
    And the order total is USD $49.99 (CAD equivalent approximately $67.50 — below USMCA de minimis)
    And landed cost has been calculated at checkout:
      | Component          | Amount    |
      | Subtotal           | USD $49.99|
      | Shipping (DHL)     | USD $18.50|
      | Estimated Duties   | USD $0.00 (USMCA exemption)|
      | GST/HST            | USD $7.25 |
      | Order Total        | USD $75.74|
    When the Order Routing Engine processes the Canada order
    Then the routing engine determines the destination is Canada
    And the domain event "ShipmentAssigned" is appended with fulfilling hub "TORONTO-HUB"
    And the work order is created at Toronto Hub
    And the domain event "WorkOrderCreated" is appended to the shipment stream
    When the pick and pack workflow completes at Toronto Hub
    Then the domain event "PickCompleted" is appended
    And the domain event "PackingCompleted" is appended
    When the labeling step runs for the international shipment
    Then the domain event "ShippingLabelGenerated" is appended with carrier "DHL Express"
    And the domain event "TrackingNumberAssigned" is appended with a DHL tracking number
    And the domain event "ShipmentManifested" is appended
    When the customs documentation step is triggered for the Canada order
    Then the domain event "CustomsDocumentationPrepared" is appended with:
      | Field                  | Value                               |
      | DocumentType           | Commercial Invoice                  |
      | HSCode                 | 2309.10.00                          |
      | DeclaredValueUSD       | 49.99                               |
      | CountryOfOrigin        | USA                                 |
      | Description            | Dog food, prepared pet food         |
      | DutyTreatment          | DDP                                 |
    And the domain event "USMCACertificateOfOriginIssued" is appended because:
      | Field                  | Value                               |
      | ProductOrigin          | USA                                 |
      | DeclaredValueCAD       | 67.50                               |
      | DeMinimisThresholdCAD  | 150.00                              |
      | Qualifies              | true                                |
    And the shipment is staged for DHL pickup at Toronto Hub
    And the domain event "PackageStagedForPickup" is appended
    When the DHL driver scans the manifest at Toronto Hub before 11:00 AM ET cutoff
    Then the domain event "CarrierPickupConfirmed" is appended
    And the domain event "ShipmentHandedToCarrier" is appended
    When DHL records a facility scan at Toronto Hub sort center
    Then the domain event "ShipmentInTransit" is appended
    And the order tracking UI shows a "Customs Clearance" step (displayed for international orders only)
    When CBSA (Canada Border Services Agency) clears the shipment
    Then the domain event "CustomsHoldReleased" is NOT appended because no hold occurred
    And the domain event "OutForDelivery" is appended (local Canadian carrier handoff)
    When the shipment is delivered to "221 King St W, Toronto"
    Then the domain event "ShipmentDelivered" is appended
    And the customer receives an email: "Your order has arrived in Canada! 📦"
    And the order tracking UI shows "Delivered" with the Canadian delivery timestamp


  # ============================================================
  # HAPPY PATH: UK Order — Birmingham Hub — Domestic UK Delivery
  # ============================================================

  Scenario: UK order routed to Birmingham Hub is distributed domestically without per-order customs complexity
    Given a customer in London, England has placed order "CS-2026-051200"
    And the shipping destination is "14 Baker Street, London, W1U 7BJ, United Kingdom"
    And the order contains:
      | SKU           | Product Name                             | Quantity | Unit Price USD | HS Code     |
      | CAT-TOY-LASER | PetSafe FroliCat Bolt Laser Cat Toy      | 1        | 24.99          | 9503.00.00  |
      | DOG-LEAD-6FT  | Ruffwear Flat Out Leash 6ft Red          | 1        | 34.99          | 6217.10.00  |
    And landed cost has been calculated at checkout via Zonos:
      | Component          | Amount    |
      | Subtotal           | USD $59.98|
      | Shipping (FedEx)   | USD $22.00|
      | UK VAT (20%)       | USD $11.99|
      | UK Import Duty     | USD $3.60 |
      | Order Total        | USD $97.57|
    When the Order Routing Engine processes the UK order
    Then the routing engine determines the destination is United Kingdom
    And the domain event "ShipmentAssigned" is appended with fulfilling hub "BHAM-HUB"
    And the domain event "WorkOrderCreated" is appended at Birmingham Hub
    And the Birmingham Hub processes the order as a UK domestic distribution from existing hub stock
    And no per-order customs documentation is required for the individual parcel
    Because the bulk inventory was already imported commercially to Birmingham Hub
    When pick and pack completes at Birmingham Hub
    Then the domain event "PackingCompleted" is appended
    When the labeling step runs
    Then FedEx International is selected as the carrier for UK domestic delivery
    And the domain event "ShippingLabelGenerated" is appended with carrier "FedEx"
    And the domain event "TrackingNumberAssigned" is appended
    And the package is staged and handed to FedEx before the 10:00 AM GMT cutoff
    And the domain event "ShipmentHandedToCarrier" is appended
    When FedEx delivers the package to "14 Baker Street, London"
    Then the domain event "ShipmentDelivered" is appended
    And the customer receives an email: "Your order has arrived! 📦"
    And the order total charged was USD $97.57 with duties pre-paid (DDP)


  # ============================================================
  # SAD PATH: Customs Hold — Documentation Error — Corrected and Released
  # ============================================================

  Scenario: Canada order held at customs due to missing HS code — corrected entry submitted — released
    Given order "CS-2026-052300" was dispatched from Toronto Hub to "88 Dundas St, Ottawa, ON K1P 5G6, Canada"
    And the domain event "ShipmentHandedToCarrier" is appended with DHL tracking "JD014600004540101"
    And the order contains:
      | SKU              | Product Name                               | Quantity | Declared Value USD |
      | REPTILE-LAMP-UVB | Zoo Med ReptiSun 10.0 UVB Lamp T8 24-inch  | 2        | 29.99 each         |
    And the commercial invoice was prepared with HS code "8539.39.00" (incorrect — should be "8543.70.90")
    And the shipment is in transit
    When CBSA places a hold on the shipment and reports a documentation error to DHL
    Then DHL reports the customs hold to the CritterSupply integration endpoint
    And the domain event "CustomsHoldInitiated" is appended with:
      | Field              | Value                                      |
      | TrackingNumber     | JD014600004540101                          |
      | HoldReason         | DocumentationError                         |
      | HoldDetail         | HS code 8539.39.00 rejected; correction required |
      | Authority          | CBSA — Canada Border Services Agency       |
      | EstimatedResolution| 2 to 5 business days                       |
    And the customer receives an email:
      """
      Subject: Customs review — your order to Canada

      Your international order (DHL: JD014600004540101) is currently being reviewed by
      Canada Border Services Agency (CBSA).

      This is a routine documentation review and typically resolves within 2–5 business days.
      No action is required from you.

      We'll update you as soon as it clears.
      """
    And the order tracking UI shows "Customs Review" step with status "Pending" and estimated range "2–5 business days"
    When CritterSupply's customs broker submits a corrected commercial invoice with HS code "8543.70.90"
    Then CBSA accepts the corrected entry within 3 business days
    And DHL reports customs clearance to CritterSupply
    And the domain event "CustomsHoldReleased" is appended with:
      | Field              | Value                                 |
      | ResolutionDays     | 3                                     |
      | CorrectedHSCode    | 8543.70.90                            |
    And the order tracking UI updates the "Customs Review" step to "Cleared ✅"
    And shipment resumes transit to the Ottawa destination
    And the domain event "OutForDelivery" is appended when on the local delivery vehicle
    And the domain event "ShipmentDelivered" is appended upon delivery


  # ============================================================
  # SAD PATH: Prohibited Item Seized by Customs — Refund Issued
  # ============================================================

  Scenario: UK order containing prohibited item is seized by HMRC — refund issued to customer
    Given a customer in Manchester, UK placed order "CS-2026-053400"
    And the shipping destination is "52 Oxford Road, Manchester, M1 5NH, United Kingdom"
    And the order contains:
      | SKU                   | Product Name                                   | Quantity | Declared Value USD |
      | FLEA-SPRAY-HOUSEHOLD  | Raid Flea Killer Household Spray 16oz          | 1        | 9.99               |
      | CAT-FOOD-PREMIUM      | Royal Canin Indoor Cat Food 7lb                | 1        | 39.99              |
    And "FLEA-SPRAY-HOUSEHOLD" contains DEET-based propellants restricted from UK import for residential use
    And the order was dispatched from Birmingham Hub
    And the domain event "ShipmentHandedToCarrier" was appended with FedEx tracking "7723456789012"
    When HMRC (UK customs authority) inspects the shipment and identifies the prohibited aerosol
    Then HMRC seizes the "FLEA-SPRAY-HOUSEHOLD" item
    And FedEx reports the seizure to CritterSupply's integration endpoint
    And the domain event "ProhibitedItemSeized" is appended with:
      | Field              | Value                                     |
      | TrackingNumber     | 7723456789012                             |
      | SeizedSKU          | FLEA-SPRAY-HOUSEHOLD                      |
      | SeizureAuthority   | HMRC (His Majesty's Revenue and Customs)  |
      | Reason             | Prohibited aerosol — UK residential restriction |
      | Resolution         | Seizure permanent; item not returned      |
    And the customer receives an email:
      """
      Subject: Update on part of your CritterSupply order

      We regret to inform you that one item in your order was seized by UK customs authorities
      and cannot be released:

        Raid Flea Killer Household Spray 16oz — £9.99 equivalent

      We've issued a full refund of £9.99 for this item.

      The rest of your order (Royal Canin Indoor Cat Food 7lb) is proceeding normally
      and will be delivered separately.
      """
    And the domain event "RefundIssued" is appended for the seized SKU with amount USD $9.99
    And the domain event "CarrierClaimFiled" is appended for the seized item
    And the remaining item "CAT-FOOD-PREMIUM" continues in transit and is delivered normally
    And the shipping label for "CAT-FOOD-PREMIUM" remains active on the original tracking number
    And the "FLEA-SPRAY-HOUSEHOLD" SKU is flagged in the product catalog with restriction "Cannot ship to United Kingdom"
    And future UK checkout attempts for "FLEA-SPRAY-HOUSEHOLD" are blocked at the cart level


  # ============================================================
  # SAD PATH: DDU Duty Refusal — Package RTS — Reship or Refund
  # ============================================================

  Scenario: Canadian customer refuses DDU import duties — package returned to sender — reship offered
    Given CritterSupply has exceptionally processed order "CS-2026-054500" under DDU terms
    Because the customer's destination postal code was in a remote area where DDP is unavailable
    And the shipping destination is "Whitehorse, YT Y1A 2B3, Canada"
    And the order contains:
      | SKU             | Product Name                          | Quantity | Declared Value USD |
      | DOG-HARNESS-M   | Ruffwear Front Range Harness Medium   | 1        | 74.95              |
    And the commercial invoice clearly states "DDU — Import duties payable by recipient"
    And the estimated Canadian import duty is CAD $8.50
    And the order was dispatched from Toronto Hub with FedEx International tracking "7788990011223"
    When FedEx attempts delivery and the Canada Post carrier requires CAD $8.50 duty payment from the customer
    Then the customer refuses to pay the duty
    And FedEx records a delivery exception code "DUTY_REFUSED"
    And FedEx reports the DDU refusal to CritterSupply's integration endpoint
    And the domain event "DutyRefused" is appended with:
      | Field                | Value                              |
      | TrackingNumber       | 7788990011223                      |
      | DutyAmountCAD        | 8.50                               |
      | DutyAmountUSD        | 6.29                               |
      | RefusalReason        | CustomerDeclinedPayment            |
      | Carrier              | FedEx International                |
    And the domain event "ReturnToSenderInitiated" is appended
    And the customer receives an email:
      """
      Subject: Your order is being returned — import duty declined

      Your order (FedEx: 7788990011223) was returned because the import duty of CAD $8.50
      was not accepted at delivery.

      We have two options for you:

      Option 1: Reship with pre-paid duties (DDP)
        We'll absorb the import duties and reship your order.
        No additional charge to you.
        [Confirm Reship with DDP]

      Option 2: Full refund
        We'll refund your original order total in full.
        [Request Refund]

      Please respond within 7 days.
      """
    When the customer selects "Confirm Reship with DDP" within 3 days
    Then a new fulfillment request is created with DDP terms
    And the domain event "ReshipmentCreated" is appended with:
      | Field           | Value                          |
      | Reason          | DDUDutyRefusal_ReshippedAsDDP  |
      | DutyAbsorbed    | true                           |
      | DutyAmountUSD   | 6.29                           |
    And the duty cost is absorbed by CritterSupply
    And the new shipment is processed through Toronto Hub with corrected DDP commercial documentation


  # ============================================================
  # SAD PATH: Hazmat Item Blocked from International Shipping
  # ============================================================

  Scenario: Customer attempts to ship hazmat flea treatment to Canada — blocked at routing and checkout
    Given a customer in Vancouver, BC has added items to their cart:
      | SKU                  | Product Name                                  | Quantity |
      | FLEA-FRONTLINE-6PK   | Frontline Plus Flea & Tick Treatment 6-Pack   | 1        |
      | DOG-TREAT-ZUKE       | Zuke's Mini Naturals Dog Treats 6oz           | 2        |
    And "FLEA-FRONTLINE-6PK" is classified as ORM-D (Limited Quantity — flea treatment containing permethrin)
    And Canada restricts ORM-D permethrin-based flea treatments from cross-border import
    And the customer's shipping destination is "1234 Granville St, Vancouver, BC V6Z 1L6, Canada"
    When the customer proceeds to checkout and the address is entered as Canadian
    Then the domain event "HazmatItemBlockedFromInternational" is appended for "FLEA-FRONTLINE-6PK" with:
      | Field              | Value                                          |
      | SKU                | FLEA-FRONTLINE-6PK                             |
      | HazmatClass        | ORM-D                                          |
      | DestinationCountry | Canada                                         |
      | BlockReason        | ORM-D permethrin restricted from Canadian import |
    And the checkout form shows an inline error for "FLEA-FRONTLINE-6PK":
      """
      This item (Frontline Plus Flea & Tick Treatment) cannot ship to Canada
      due to import restrictions on restricted materials.
      Remove this item to continue checkout.
      """
    And the "Continue to Payment" button is disabled until "FLEA-FRONTLINE-6PK" is removed
    And "DOG-TREAT-ZUKE" remains in the cart and can proceed to checkout normally
    When the customer removes "FLEA-FRONTLINE-6PK" from the cart
    Then the checkout can proceed with "DOG-TREAT-ZUKE" only
    And the order for "DOG-TREAT-ZUKE" is routed to Toronto Hub normally
    And the PDP for "FLEA-FRONTLINE-6PK" displays "Cannot ship to Canada — restricted material" when the customer's location is detected as Canada


  # ============================================================
  # USMCA: US-Origin Item Qualifies for Reduced Duties to Canada
  # ============================================================

  Scenario: US-manufactured dog food qualifies for USMCA certificate reducing Canadian import duties
    Given a customer in Calgary, AB has placed order "CS-2026-056700"
    And the shipping destination is "1000 Bow Valley Trail, Calgary, AB T1W 1P6, Canada"
    And the order contains:
      | SKU            | Product Name                          | Quantity | Unit Price USD | Country of Origin | HS Code    |
      | DOG-FOOD-40LB  | Hill's Science Diet Dog Food 40lb     | 2        | 49.99          | USA               | 2309.10.00 |
    And the order subtotal is USD $99.98
    And the CAD equivalent is approximately CAD $135.00 (below USMCA de minimis of CAD $150)
    When the landed cost calculation runs for the Canadian order
    Then the USMCA exemption is identified because:
      | Condition                    | Met  |
      | Product manufactured in USA  | Yes  |
      | Declared value below CAD $150| Yes  |
    And the landed cost calculation produces:
      | Component          | Amount    |
      | Subtotal           | USD $99.98|
      | Shipping (DHL)     | USD $28.00|
      | Canadian Duties    | USD $0.00 (USMCA exempt)|
      | GST/HST (5%)       | USD $5.00 |
      | Order Total        | USD $132.98|
    And the checkout order summary displays:
      | Line Item                    | Amount    |
      | Subtotal                     | $99.98    |
      | Shipping                     | $28.00    |
      | Import Duties (USMCA exempt) | $0.00     |
      | GST/HST                      | $5.00     |
      | Total                        | $132.98   |
    And the tooltip on "Import Duties (USMCA exempt)" reads "This product qualifies for zero duty under the Canada-US-Mexico Agreement (USMCA)."
    When the customs documentation step runs at Toronto Hub
    Then the domain event "USMCACertificateOfOriginIssued" is appended with:
      | Field               | Value                          |
      | Qualifier           | Manufactured in USA            |
      | DeclaredValueCAD    | 135.00                         |
      | ThresholdCAD        | 150.00                         |
      | DutiesOwed          | 0.00                           |
      | CertificateType     | USMCA (CUSMA) Certificate of Origin |
    And the USMCA certificate is attached to the commercial invoice submitted to CBSA
    And the shipment clears customs without any duty payment


  # ============================================================
  # HAPPY PATH: Landed Cost Calculated at Checkout (DDP)
  # ============================================================

  Scenario: UK customer sees full DDP landed cost at checkout before placing order
    Given a customer in Edinburgh, Scotland is building a cart at CritterSupply
    And the customer's shipping address is "5 Royal Mile, Edinburgh, EH1 2PB, United Kingdom"
    And the cart contains:
      | SKU              | Product Name                         | Quantity | Unit Price USD |
      | CAT-FOOD-ROYAL7  | Royal Canin Indoor Cat Food 7lb      | 2        | 39.99          |
      | CAT-TOY-LASER    | PetSafe FroliCat Bolt Laser Cat Toy  | 1        | 24.99          |
    When the customer enters the UK shipping destination at checkout Step 1
    Then the Zonos landed cost engine calculates duties and taxes in real time:
      | Component               | Calculation                              | Amount    |
      | Subtotal                | 2 × $39.99 + 1 × $24.99                 | USD $104.97|
      | UK Import Duty (cat food)| 2309.10.00 tariff @ 4.0%               | USD $3.20 |
      | UK Import Duty (cat toy) | 9503.00.00 tariff @ 0%                 | USD $0.00 |
      | UK VAT (20%)            | Applied to subtotal + duty + shipping    | USD $22.64|
      | International Shipping  | FedEx International Economy             | USD $26.50|
      | Order Total (DDP)       | All duties and taxes pre-paid            | USD $157.31|
    And the checkout order summary displays all components as named line items:
      | Line Item          | Amount    |
      | Subtotal           | $104.97   |
      | UK Import Duties   | $3.20     |
      | UK VAT             | $22.64    |
      | Shipping           | $26.50    |
      | Total              | $157.31   |
    And the order summary includes the notice:
      """
      All duties and taxes are pre-paid (DDP). No additional charges at delivery.
      """
    And the "UK Import Duties" line item has a tooltip: "Calculated using Zonos. Final duties determined by HMRC."
    When the customer reviews and places the order
    Then the order total of USD $157.31 is charged at the time of order placement
    And the commercial invoice prepared at Birmingham Hub reflects:
      | Field              | Value                                   |
      | DutyTreatment      | DDP — Delivered Duty Paid               |
      | DeclaredValueUSD   | 104.97                                  |
      | DutiesPrePaid      | 3.20                                    |
      | VATPrePaid         | 22.64                                   |
    And no additional charges are collected at UK delivery
    And the customer receives no duty invoice from FedEx at the door
