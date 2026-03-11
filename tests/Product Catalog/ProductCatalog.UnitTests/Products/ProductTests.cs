namespace ProductCatalog.UnitTests.Products;

/// <summary>
/// Unit tests for the <see cref="Product"/> document model.
/// Covers the <see cref="Product.Create"/>, <see cref="Product.Update"/>,
/// <see cref="Product.ChangeStatus"/>, <see cref="Product.SoftDelete"/>,
/// and <see cref="Product.AssignToVendor"/> methods.
/// </summary>
public class ProductTests
{
    // ---------------------------------------------------------------------------
    // Helpers
    // ---------------------------------------------------------------------------

    private static Product BuildProduct(
        string sku = "DOG-FOOD-5LB",
        string name = "Premium Dog Food 5lb",
        string description = "High quality dog food",
        string category = "Dog Food") =>
        Product.Create(sku, name, description, category);

    // ---------------------------------------------------------------------------
    // Product.Create() — field mapping
    // ---------------------------------------------------------------------------

    /// <summary>Create sets the SKU from the provided string.</summary>
    [Fact]
    public void Create_Sets_Sku_From_String()
    {
        var product = Product.Create("CAT-FOOD-3LB", "Cat Food 3lb", "Tasty cat food", "Cat Food");

        product.Sku.Value.ShouldBe("CAT-FOOD-3LB");
    }

    /// <summary>Create sets Id to the string representation of the SKU.</summary>
    [Fact]
    public void Create_Sets_Id_To_Sku_String()
    {
        var product = Product.Create("CAT-FOOD-3LB", "Cat Food 3lb", "Tasty cat food", "Cat Food");

        product.Id.ShouldBe("CAT-FOOD-3LB");
    }

    /// <summary>Create sets the product name.</summary>
    [Fact]
    public void Create_Sets_Name()
    {
        var product = BuildProduct(name: "Organic Bird Seed Mix");

        product.Name.Value.ShouldBe("Organic Bird Seed Mix");
    }

    /// <summary>Create sets description.</summary>
    [Fact]
    public void Create_Sets_Description()
    {
        var product = BuildProduct(description: "100% natural ingredients");

        product.Description.ShouldBe("100% natural ingredients");
    }

    /// <summary>Create sets category with trimming applied.</summary>
    [Fact]
    public void Create_Sets_Category_With_Trimming()
    {
        var product = Product.Create("FISH-001", "Goldfish Food", "Flakes", "  Fish Supplies  ");

        product.Category.ShouldBe("Fish Supplies");
    }

    /// <summary>A new product always starts in the Active status.</summary>
    [Fact]
    public void Create_Sets_Status_To_Active()
    {
        var product = BuildProduct();

        product.Status.ShouldBe(ProductStatus.Active);
    }

    /// <summary>A new product is not deleted.</summary>
    [Fact]
    public void Create_Sets_IsDeleted_To_False()
    {
        var product = BuildProduct();

        product.IsDeleted.ShouldBeFalse();
    }

    /// <summary>Optional fields default to null when not provided.</summary>
    [Fact]
    public void Create_Optional_Fields_Default_To_Null()
    {
        var product = BuildProduct();

        product.LongDescription.ShouldBeNull();
        product.Subcategory.ShouldBeNull();
        product.Brand.ShouldBeNull();
        product.Dimensions.ShouldBeNull();
        product.UpdatedAt.ShouldBeNull();
        product.VendorTenantId.ShouldBeNull();
        product.AssignedBy.ShouldBeNull();
        product.AssignedAt.ShouldBeNull();
    }

    /// <summary>Collections default to empty when not provided.</summary>
    [Fact]
    public void Create_Collections_Default_To_Empty()
    {
        var product = BuildProduct();

        product.Images.ShouldBeEmpty();
        product.Tags.ShouldBeEmpty();
    }

    /// <summary>A newly created product is not in a terminal state.</summary>
    [Fact]
    public void Create_IsTerminal_Is_False()
    {
        var product = BuildProduct();

        product.IsTerminal.ShouldBeFalse();
    }

    /// <summary>Providing an invalid SKU to Create throws an exception.</summary>
    [Fact]
    public void Create_InvalidSku_Throws_ArgumentException()
    {
        Should.Throw<ArgumentException>(() =>
            Product.Create("invalid sku!", "Dog Food", "Desc", "Category"));
    }

    /// <summary>Providing an invalid product name to Create throws an exception.</summary>
    [Fact]
    public void Create_InvalidName_Throws_ArgumentException()
    {
        Should.Throw<ArgumentException>(() =>
            Product.Create("DOG-001", "##Invalid##", "Desc", "Category"));
    }

    // ---------------------------------------------------------------------------
    // Product.Update()
    // ---------------------------------------------------------------------------

    /// <summary>Updating the name changes only the Name field.</summary>
    [Fact]
    public void Update_Name_Changes_Name_Only()
    {
        var product = BuildProduct(name: "Old Name");

        var updated = product.Update(name: "New Premium Dog Food");

        updated.Name.Value.ShouldBe("New Premium Dog Food");
        updated.Sku.ShouldBe(product.Sku);
        updated.Status.ShouldBe(product.Status);
    }

    /// <summary>Update sets UpdatedAt to a non-null value.</summary>
    [Fact]
    public void Update_Sets_UpdatedAt()
    {
        var product = BuildProduct();

        var updated = product.Update(description: "Updated description");

        updated.UpdatedAt.ShouldNotBeNull();
    }

    /// <summary>Omitting fields in Update preserves existing values.</summary>
    [Fact]
    public void Update_Null_Fields_Preserve_Existing_Values()
    {
        var original = Product.Create(
            "REPTILE-001",
            "Reptile Habitat Kit",
            "Complete starter kit",
            "Reptile Supplies",
            brand: "ZooMed");

        var updated = original.Update(description: "Updated desc");

        updated.Brand.ShouldBe("ZooMed");
        updated.Category.ShouldBe("Reptile Supplies");
    }

    // ---------------------------------------------------------------------------
    // Product.ChangeStatus()
    // ---------------------------------------------------------------------------

    /// <summary>ChangeStatus updates the Status field.</summary>
    [Fact]
    public void ChangeStatus_Updates_Status()
    {
        var product = BuildProduct();

        var updated = product.ChangeStatus(ProductStatus.Discontinued);

        updated.Status.ShouldBe(ProductStatus.Discontinued);
    }

    /// <summary>ChangeStatus to Discontinued makes the product terminal.</summary>
    [Fact]
    public void ChangeStatus_Discontinued_Makes_IsTerminal_True()
    {
        var product = BuildProduct();

        var updated = product.ChangeStatus(ProductStatus.Discontinued);

        updated.IsTerminal.ShouldBeTrue();
    }

    /// <summary>ChangeStatus sets UpdatedAt.</summary>
    [Fact]
    public void ChangeStatus_Sets_UpdatedAt()
    {
        var product = BuildProduct();

        var updated = product.ChangeStatus(ProductStatus.Active);

        updated.UpdatedAt.ShouldNotBeNull();
    }

    // ---------------------------------------------------------------------------
    // Product.SoftDelete()
    // ---------------------------------------------------------------------------

    /// <summary>SoftDelete sets IsDeleted to true.</summary>
    [Fact]
    public void SoftDelete_Sets_IsDeleted_True()
    {
        var product = BuildProduct();

        var deleted = product.SoftDelete();

        deleted.IsDeleted.ShouldBeTrue();
    }

    /// <summary>A soft-deleted product is terminal.</summary>
    [Fact]
    public void SoftDelete_Makes_IsTerminal_True()
    {
        var product = BuildProduct();

        var deleted = product.SoftDelete();

        deleted.IsTerminal.ShouldBeTrue();
    }

    /// <summary>SoftDelete sets UpdatedAt.</summary>
    [Fact]
    public void SoftDelete_Sets_UpdatedAt()
    {
        var product = BuildProduct();

        var deleted = product.SoftDelete();

        deleted.UpdatedAt.ShouldNotBeNull();
    }

    // ---------------------------------------------------------------------------
    // Product.AssignToVendor()
    // ---------------------------------------------------------------------------

    /// <summary>AssignToVendor sets VendorTenantId.</summary>
    [Fact]
    public void AssignToVendor_Sets_VendorTenantId()
    {
        var product = BuildProduct();
        var vendorId = Guid.NewGuid();
        var assignedAt = DateTimeOffset.UtcNow;

        var assigned = product.AssignToVendor(vendorId, "admin", assignedAt);

        assigned.VendorTenantId.ShouldBe(vendorId);
    }

    /// <summary>AssignToVendor sets AssignedBy.</summary>
    [Fact]
    public void AssignToVendor_Sets_AssignedBy()
    {
        var product = BuildProduct();

        var assigned = product.AssignToVendor(Guid.NewGuid(), "system-admin", DateTimeOffset.UtcNow);

        assigned.AssignedBy.ShouldBe("system-admin");
    }

    /// <summary>AssignToVendor sets AssignedAt and UpdatedAt to the provided timestamp.</summary>
    [Fact]
    public void AssignToVendor_Sets_AssignedAt_And_UpdatedAt()
    {
        var product = BuildProduct();
        var timestamp = new DateTimeOffset(2025, 8, 1, 12, 0, 0, TimeSpan.Zero);

        var assigned = product.AssignToVendor(Guid.NewGuid(), "admin", timestamp);

        assigned.AssignedAt.ShouldBe(timestamp);
        assigned.UpdatedAt.ShouldBe(timestamp);
    }
}
