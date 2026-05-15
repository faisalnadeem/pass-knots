using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VaultApp.Migrations
{
    /// <inheritdoc />
    public partial class PendingShareCodeColumns : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_PendingShares_VaultEntryId_RecipientEmail",
                table: "PendingShares");

            migrationBuilder.AlterColumn<string>(
                name: "RecipientEmail",
                table: "PendingShares",
                type: "nvarchar(256)",
                maxLength: 256,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "nvarchar(256)",
                oldMaxLength: 256);

            migrationBuilder.AddColumn<DateTime>(
                name: "ExpiresAt",
                table: "PendingShares",
                type: "datetime2",
                nullable: false,
                defaultValue: new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified));

            migrationBuilder.AddColumn<bool>(
                name: "IsConsumed",
                table: "PendingShares",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "ShareCode",
                table: "PendingShares",
                type: "nvarchar(32)",
                maxLength: 32,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SharedWithUserId",
                table: "PendingShares",
                type: "nvarchar(450)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "UserId",
                table: "PendingShares",
                type: "nvarchar(450)",
                nullable: true);

            migrationBuilder.Sql(@"
                UPDATE p
                SET p.UserId = v.OwnerId,
                    p.ExpiresAt = DATEADD(day, 30, p.CreatedAt)
                FROM PendingShares p
                INNER JOIN VaultEntries v ON v.Id = p.VaultEntryId;

                DELETE FROM PendingShares
                WHERE UserId IS NULL OR LTRIM(RTRIM(UserId)) = '';
            ");

            migrationBuilder.AlterColumn<string>(
                name: "UserId",
                table: "PendingShares",
                type: "nvarchar(450)",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "nvarchar(450)",
                oldNullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_PendingShares_ShareCode",
                table: "PendingShares",
                column: "ShareCode",
                unique: true,
                filter: "[ShareCode] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_PendingShares_SharedWithUserId",
                table: "PendingShares",
                column: "SharedWithUserId");

            migrationBuilder.CreateIndex(
                name: "IX_PendingShares_UserId",
                table: "PendingShares",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_PendingShares_VaultEntryId_RecipientEmail",
                table: "PendingShares",
                columns: new[] { "VaultEntryId", "RecipientEmail" },
                unique: true,
                filter: "[RecipientEmail] IS NOT NULL");

            migrationBuilder.AddForeignKey(
                name: "FK_PendingShares_AspNetUsers_SharedWithUserId",
                table: "PendingShares",
                column: "SharedWithUserId",
                principalTable: "AspNetUsers",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_PendingShares_AspNetUsers_UserId",
                table: "PendingShares",
                column: "UserId",
                principalTable: "AspNetUsers",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_PendingShares_AspNetUsers_SharedWithUserId",
                table: "PendingShares");

            migrationBuilder.DropForeignKey(
                name: "FK_PendingShares_AspNetUsers_UserId",
                table: "PendingShares");

            migrationBuilder.DropIndex(
                name: "IX_PendingShares_ShareCode",
                table: "PendingShares");

            migrationBuilder.DropIndex(
                name: "IX_PendingShares_SharedWithUserId",
                table: "PendingShares");

            migrationBuilder.DropIndex(
                name: "IX_PendingShares_UserId",
                table: "PendingShares");

            migrationBuilder.DropIndex(
                name: "IX_PendingShares_VaultEntryId_RecipientEmail",
                table: "PendingShares");

            migrationBuilder.DropColumn(
                name: "ExpiresAt",
                table: "PendingShares");

            migrationBuilder.DropColumn(
                name: "IsConsumed",
                table: "PendingShares");

            migrationBuilder.DropColumn(
                name: "ShareCode",
                table: "PendingShares");

            migrationBuilder.DropColumn(
                name: "SharedWithUserId",
                table: "PendingShares");

            migrationBuilder.DropColumn(
                name: "UserId",
                table: "PendingShares");

            migrationBuilder.AlterColumn<string>(
                name: "RecipientEmail",
                table: "PendingShares",
                type: "nvarchar(256)",
                maxLength: 256,
                nullable: false,
                defaultValue: "",
                oldClrType: typeof(string),
                oldType: "nvarchar(256)",
                oldMaxLength: 256,
                oldNullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_PendingShares_VaultEntryId_RecipientEmail",
                table: "PendingShares",
                columns: new[] { "VaultEntryId", "RecipientEmail" },
                unique: true);
        }
    }
}
