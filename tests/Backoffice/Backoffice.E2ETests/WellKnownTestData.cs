namespace Backoffice.E2ETests;

/// <summary>
/// Well-known deterministic test data for E2E scenarios.
/// Avoids random IDs — enables stub coordination across checkout flow.
/// </summary>
internal static class WellKnownTestData
{
    internal static class AdminUsers
    {
        public static readonly Guid Alice = Guid.Parse("11111111-1111-1111-1111-111111111111");
        public const string AliceEmail = "alice.admin@crittersupply.com";
        public const string AlicePassword = "Password123!";
        public const string AliceName = "Alice Admin";

        // Product Admin users (ProductManager and CopyWriter roles)
        public static readonly Guid Bob = Guid.Parse("88888888-8888-8888-8888-888888888888");

        public static readonly Guid BobExecutive = Guid.Parse("22222222-2222-2222-2222-222222222222");
        public const string BobEmail = "bob.exec@crittersupply.com";
        public const string BobPassword = "Password123!";
        public const string BobName = "Bob Executive";

        // Multi-role admin users for Authorization scenarios
        public static readonly Guid SystemAdmin = Guid.Parse("AAAAAAAA-AAAA-AAAA-AAAA-AAAAAAAAAAAA");
        public const string SystemAdminEmail = "sysadmin@crittersupply.com";
        public const string SystemAdminRole = "system-admin";

        public static readonly Guid OperationsManager = Guid.Parse("BBBBBBBB-BBBB-BBBB-BBBB-BBBBBBBBBBBB");
        public const string OperationsManagerEmail = "opsmgr@crittersupply.com";
        public const string OperationsManagerRole = "operations-manager";

        public static readonly Guid WarehouseClerk = Guid.Parse("CCCCCCCC-CCCC-CCCC-CCCC-CCCCCCCCCCCC");
        public const string WarehouseClerkEmail = "warehouse@crittersupply.com";
        public const string WarehouseClerkRole = "warehouse-clerk";

        public static readonly Guid CustomerService = Guid.Parse("DDDDDDDD-DDDD-DDDD-DDDD-DDDDDDDDDDDD");
        public const string CustomerServiceEmail = "support@crittersupply.com";
        public const string CustomerServiceRole = "customer-service";

        public static readonly Guid CopyWriter = Guid.Parse("EEEEEEEE-EEEE-EEEE-EEEE-EEEEEEEEEEEE");
        public const string CopyWriterEmail = "copywriter@crittersupply.com";
        public const string CopyWriterRole = "copy-writer";

        public static readonly Guid PricingManager = Guid.Parse("FFFFFFFF-FFFF-FFFF-FFFF-FFFFFFFFFFFF");
        public const string PricingManagerEmail = "pricing@crittersupply.com";
        public const string PricingManagerRole = "pricing-manager";

        public static readonly Guid Executive = Guid.Parse("99999999-9999-9999-9999-999999999999");
        public const string ExecutiveEmail = "exec@crittersupply.com";
        public const string ExecutiveRole = "executive";
    }

    internal static class Customers
    {
        public static readonly Guid TestCustomer = Guid.Parse("33333333-3333-3333-3333-333333333333");
        public const string TestCustomerEmail = "test.customer@example.com";
        public const string TestCustomerName = "Test Customer";
    }

    internal static class Orders
    {
        public static readonly Guid TestOrder = Guid.Parse("44444444-4444-4444-4444-444444444444");
        public const decimal TestOrderTotal = 75.96m;
    }

    internal static class Returns
    {
        public static readonly Guid TestReturn = Guid.Parse("55555555-5555-5555-5555-555555555555");
    }

    internal static class Alerts
    {
        public static readonly Guid LowStockAlert = Guid.Parse("66666666-6666-6666-6666-666666666666");
        public const string LowStockSku = "DOG-BOWL-01";
    }

    internal static class Products
    {
        public const string CeramicDogBowl = "DOG-BOWL-01";
        public const string InteractiveCatLaser = "CAT-LASER-01";
        public const decimal CeramicDogBowlPrice = 19.99m;
        public const decimal InteractiveCatLaserPrice = 29.99m;
    }

    internal static class Listings
    {
        public static readonly Guid LiveListing = Guid.Parse("77777777-7777-7777-7777-777777777777");
        public const string LiveListingSku = "DOG-BOWL-01";
        public const string LiveListingChannel = "OWN_WEBSITE";
        public const string LiveListingProductName = "Ceramic Dog Bowl";
        public const string LiveListingStatus = "Live";

        public static readonly Guid DraftListing = Guid.Parse("77777777-7777-7777-7777-777777777778");
        public const string DraftListingSku = "CAT-LASER-01";
        public const string DraftListingChannel = "OWN_WEBSITE";
        public const string DraftListingProductName = "Interactive Cat Laser";
        public const string DraftListingStatus = "Draft";

        public static readonly Guid ReadyForReviewListing = Guid.Parse("77777777-7777-7777-7777-777777777779");
        public const string ReadyForReviewListingSku = "DOG-BOWL-02";
        public const string ReadyForReviewListingChannel = "OWN_WEBSITE";
        public const string ReadyForReviewListingProductName = "Ceramic Dog Bowl Premium";
        public const string ReadyForReviewListingStatus = "ReadyForReview";
    }
}
