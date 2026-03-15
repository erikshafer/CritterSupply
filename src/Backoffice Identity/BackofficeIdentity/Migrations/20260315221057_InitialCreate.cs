using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BackofficeIdentity.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "backofficeidentity");

            migrationBuilder.CreateTable(
                name: "Users",
                schema: "backofficeidentity",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Email = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    PasswordHash = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    FirstName = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    LastName = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Role = table.Column<int>(type: "integer", nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    LastLoginAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    DeactivatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    DeactivationReason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    RefreshToken = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    RefreshTokenExpiresAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Users", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Users_Email",
                schema: "backofficeidentity",
                table: "Users",
                column: "Email",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Users_RefreshToken",
                schema: "backofficeidentity",
                table: "Users",
                column: "RefreshToken",
                unique: true,
                filter: "\"RefreshToken\" IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Users",
                schema: "backofficeidentity");
        }
    }
}
