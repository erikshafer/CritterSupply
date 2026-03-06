namespace Storefront.E2ETests;

/// <summary>
/// Well-known, stable test data constants for E2E scenarios.
/// These IDs are consistent across all test runs, enabling reliable test setup
/// without dynamic ID generation that creates cross-step state management problems.
///
/// Coordinate with seed data used in Customer Identity integration tests
/// and the Cycle 19 authentication seed data (alice@example.com).
/// </summary>
internal static class WellKnownTestData
{
    internal static class Customers
    {
        /// <summary>Pre-seeded customer for E2E checkout scenarios.</summary>
        public static readonly Guid Alice = Guid.Parse("11111111-1111-1111-1111-111111111111");

        public const string AliceEmail = "alice@example.com";
        public const string AlicePassword = "password123";
        public const string AliceFirstName = "Alice";
        public const string AliceLastName = "Testington";
    }

    internal static class Addresses
    {
        /// <summary>Alice's home address (saved in Customer Identity BC).</summary>
        public static readonly Guid AliceHome = Guid.Parse("22222222-2222-2222-2222-222222222222");

        /// <summary>Alice's work address (saved in Customer Identity BC).</summary>
        public static readonly Guid AliceWork = Guid.Parse("33333333-3333-3333-3333-333333333333");

        public const string AliceHomeNickname = "Home";
        public const string AliceHomeAddressLine1 = "123 Main St";
        public const string AliceHomeCity = "Seattle";
        public const string AliceHomeState = "WA";
        public const string AliceHomePostalCode = "98101";
        public const string AliceHomeCountry = "USA";
        public const string AliceHomeDisplayLine = "123 Main St, Seattle, WA 98101";

        public const string AliceWorkNickname = "Work";
        public const string AliceWorkAddressLine1 = "456 Office Blvd";
        public const string AliceWorkCity = "Seattle";
        public const string AliceWorkState = "WA";
        public const string AliceWorkPostalCode = "98102";
        public const string AliceWorkCountry = "USA";
        public const string AliceWorkDisplayLine = "456 Office Blvd, Seattle, WA 98102";
    }

    internal static class Products
    {
        public const string CeramicDogBowlSku = "DOG-BOWL-01";
        public const string CeramicDogBowlName = "Ceramic Dog Bowl";
        public const decimal CeramicDogBowlPrice = 19.99m;

        public const string InteractiveCatLaserSku = "CAT-TOY-05";
        public const string InteractiveCatLaserName = "Interactive Cat Laser";
        public const decimal InteractiveCatLaserPrice = 29.99m;
    }

    internal static class Shipping
    {
        public const string StandardMethod = "Standard";
        public const decimal StandardCost = 5.99m;
        public const string StandardDisplayName = "Standard Ground";

        public const string ExpressMethod = "Express";
        public const decimal ExpressCost = 12.99m;
        public const string ExpressDisplayName = "Express Shipping";

        public const string NextDayMethod = "NextDay";
        public const decimal NextDayCost = 24.99m;
        public const string NextDayDisplayName = "Next Day Air";
    }

    internal static class Payment
    {
        public const string ValidVisaToken = "tok_visa_test_12345";
        public const string InvalidToken = "tok_invalid";
        public const string DeclinedToken = "tok_declined_test";
    }

    internal static class Checkouts
    {
        /// <summary>Deterministic checkout ID for Alice's E2E checkout scenario.</summary>
        public static readonly Guid AliceCheckoutId = Guid.Parse("44444444-4444-4444-4444-444444444444");
    }

    /// <summary>
    /// Expected order totals for the standard E2E checkout scenario:
    /// 2x Ceramic Dog Bowl ($19.99) + 1x Interactive Cat Laser ($29.99) = $69.97
    /// </summary>
    internal static class ExpectedTotals
    {
        public const decimal Subtotal = 69.97m;
        public const decimal StandardShipping = 5.99m;
        public const decimal TotalWithStandardShipping = 75.96m;
        public const decimal ExpressShipping = 12.99m;
        public const decimal TotalWithExpressShipping = 82.96m;
    }
}
