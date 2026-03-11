namespace VendorPortal.E2ETests;

/// <summary>
/// Well-known, stable test data constants for Vendor Portal E2E scenarios.
/// These match the seed data created by VendorIdentitySeedData.cs (auto-seeded in Development).
///
/// The "Acme Pet Supplies" tenant with 3 demo users is the canonical test context.
/// </summary>
internal static class WellKnownVendorTestData
{
    /// <summary>
    /// Acme Pet Supplies tenant — the single test tenant seeded by VendorIdentitySeedData.
    /// </summary>
    internal static class Tenant
    {
        public static readonly Guid AcmeTenantId = Guid.Parse("00000000-0000-0000-0000-000000000001");
        public const string AcmeTenantName = "Acme Pet Supplies";
    }

    /// <summary>
    /// Demo users seeded by VendorIdentitySeedData — password for all: "password"
    /// </summary>
    internal static class Users
    {
        public const string AdminEmail = "admin@acmepets.test";
        public const string CatalogManagerEmail = "catalog@acmepets.test";
        public const string ReadOnlyEmail = "readonly@acmepets.test";
        public const string SharedPassword = "password";

        public const string AdminFirstName = "Alice";
        public const string CatalogManagerFirstName = "Bob";
        public const string ReadOnlyFirstName = "Carol";
    }

    /// <summary>
    /// Test SKU for change request scenarios.
    /// </summary>
    internal static class Products
    {
        public const string TestSku = "DOG-BOWL-01";
    }
}
