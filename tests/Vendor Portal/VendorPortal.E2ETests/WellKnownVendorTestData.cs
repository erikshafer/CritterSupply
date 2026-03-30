namespace VendorPortal.E2ETests;

/// <summary>
/// Well-known, stable test data constants for Vendor Portal E2E scenarios.
/// These match the seed data created by VendorIdentitySeedData.cs (auto-seeded in Development).
///
/// HearthHound Nutrition Co. is the default happy-path vendor with all three roles.
/// Source: docs/domain/vendors/vendor-catalog.md
/// </summary>
internal static class WellKnownVendorTestData
{
    /// <summary>
    /// HearthHound Nutrition Co. — the default test tenant seeded by VendorIdentitySeedData.
    /// </summary>
    internal static class Tenant
    {
        public static readonly Guid HearthHoundTenantId = Guid.Parse("10000000-0000-0000-0000-000000000101");
        public const string HearthHoundTenantName = "HearthHound Nutrition Co.";
    }

    /// <summary>
    /// Demo users seeded by VendorIdentitySeedData — password for all: "Dev@123!"
    /// </summary>
    internal static class Users
    {
        public const string AdminEmail = "mkerr@hearthhound.com";
        public const string CatalogManagerEmail = "jpike@hearthhound.com";
        public const string ReadOnlyEmail = "esuarez@hearthhound.com";
        public const string SharedPassword = "Dev@123!";

        public const string AdminFirstName = "Melissa";
        public const string CatalogManagerFirstName = "Jordan";
        public const string ReadOnlyFirstName = "Elena";
    }

    /// <summary>
    /// Test SKU for change request scenarios.
    /// </summary>
    internal static class Products
    {
        public const string TestSku = "DOG-BOWL-01";
    }
}
