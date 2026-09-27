using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CustomerManager.Infrastructure.Persistence.Migrations
{
    /// <summary>
    /// Fixes found in code review:
    ///  1. CustomerCode widened from KH-0001 to KH-000001 so codes keep sorting
    ///     correctly as strings (and inserting in order into the clustered
    ///     index) past 9999.
    ///  2. CustomerCodeSequence restarted after the highest existing code. The
    ///     sequence was created with START WITH 1 on databases that already had
    ///     customers from the old MAX+1 generator, so the first N creates after
    ///     that migration collided with existing codes.
    ///  3. IX_Customers_IsDeleted (keyed on a single constant value, never used)
    ///     replaced by IX_Customers_CreatedAt_Active for the default list query.
    ///  4. Account-wide lockout columns dropped — lockout is now tracked per
    ///     (username, client IP) in memory, see MemoryLoginAttemptTracker.
    /// </summary>
    public partial class ReviewFixes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // (1) Numeric KH- codes shorter than 6 digits → zero-padded to 6.
            //     Includes soft-deleted rows (raw SQL bypasses the query filter).
            migrationBuilder.Sql("""
                UPDATE dbo.Customers
                SET CustomerCode = N'KH-' + RIGHT(REPLICATE(N'0', 6) + SUBSTRING(CustomerCode, 4, 20), 6)
                WHERE CustomerCode LIKE N'KH-%'
                  AND LEN(CustomerCode) < 9
                  AND TRY_CAST(SUBSTRING(CustomerCode, 4, 20) AS BIGINT) IS NOT NULL;
                """);

            // (2) ALTER SEQUENCE ... RESTART WITH only accepts a constant, hence
            //     dynamic SQL. On an empty database this restarts at 1 (no-op).
            migrationBuilder.Sql("""
                DECLARE @next BIGINT = ISNULL((
                    SELECT MAX(TRY_CAST(SUBSTRING(CustomerCode, 4, 20) AS BIGINT))
                    FROM dbo.Customers
                    WHERE CustomerCode LIKE N'KH-%'), 0) + 1;

                DECLARE @sql NVARCHAR(200) =
                    N'ALTER SEQUENCE dbo.CustomerCodeSequence RESTART WITH ' + CAST(@next AS NVARCHAR(20));
                EXEC sp_executesql @sql;
                """);

            // (3)
            migrationBuilder.DropIndex(
                name: "IX_Customers_IsDeleted",
                table: "Customers");

            migrationBuilder.CreateIndex(
                name: "IX_Customers_CreatedAt_Active",
                table: "Customers",
                columns: new[] { "CreatedAt" },
                descending: new[] { true },
                filter: "[IsDeleted] = 0");

            // (4)
            migrationBuilder.DropColumn(
                name: "FailedLoginAttempts",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "LockedOutUntil",
                table: "Users");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "FailedLoginAttempts",
                table: "Users",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<DateTime>(
                name: "LockedOutUntil",
                table: "Users",
                type: "datetime2",
                nullable: true);

            migrationBuilder.DropIndex(
                name: "IX_Customers_CreatedAt_Active",
                table: "Customers");

            migrationBuilder.CreateIndex(
                name: "IX_Customers_IsDeleted",
                table: "Customers",
                column: "IsDeleted",
                filter: "[IsDeleted] = 0");

            // Codes are intentionally not shortened back: KH-000123 → KH-0123
            // could collide once codes exceed 9999, and the old format is the bug.
        }
    }
}
