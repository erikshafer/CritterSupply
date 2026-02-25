using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CustomerIdentity.Migrations
{
    /// <inheritdoc />
    public partial class AddPasswordAndSeedTestUsers : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Password",
                schema: "customeridentity",
                table: "Customers",
                type: "text",
                nullable: true);

            // Seed test users for development (alice@critter.test, bob@critter.test, charlie@critter.test)
            // Password is "password" for all users (plaintext - dev mode only)
            // Using unique GUIDs to avoid conflicts with existing data
            migrationBuilder.InsertData(
                schema: "customeridentity",
                table: "Customers",
                columns: new[] { "Id", "Email", "FirstName", "LastName", "Password", "CreatedAt" },
                values: new object[,]
                {
                    { Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"), "alice@critter.test", "Alice", "Anderson", "password", DateTimeOffset.UtcNow },
                    { Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb"), "bob@critter.test", "Bob", "Builder", "password", DateTimeOffset.UtcNow },
                    { Guid.Parse("cccccccc-cccc-cccc-cccc-cccccccccccc"), "charlie@critter.test", "Charlie", "Chen", "password", DateTimeOffset.UtcNow }
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Remove seeded test users
            migrationBuilder.DeleteData(
                schema: "customeridentity",
                table: "Customers",
                keyColumn: "Id",
                keyValues: new object[]
                {
                    Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"),
                    Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb"),
                    Guid.Parse("cccccccc-cccc-cccc-cccc-cccccccccccc")
                });

            migrationBuilder.DropColumn(
                name: "Password",
                schema: "customeridentity",
                table: "Customers");
        }
    }
}
