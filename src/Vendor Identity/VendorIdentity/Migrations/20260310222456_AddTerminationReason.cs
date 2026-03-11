using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VendorIdentity.Migrations
{
    /// <inheritdoc />
    public partial class AddTerminationReason : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "TerminationReason",
                schema: "vendoridentity",
                table: "Tenants",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "TerminationReason",
                schema: "vendoridentity",
                table: "Tenants");
        }
    }
}
