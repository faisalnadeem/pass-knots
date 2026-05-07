using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VaultApp.Migrations
{
    /// <inheritdoc />
    public partial class PendingShareSchema : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "PendingShares",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    VaultEntryId = table.Column<int>(type: "int", nullable: false),
                    RecipientEmail = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false),
                    EncryptedPassword = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    IV = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PendingShares", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PendingShares_VaultEntries_VaultEntryId",
                        column: x => x.VaultEntryId,
                        principalTable: "VaultEntries",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_PendingShares_VaultEntryId_RecipientEmail",
                table: "PendingShares",
                columns: new[] { "VaultEntryId", "RecipientEmail" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "PendingShares");
        }
    }
}
