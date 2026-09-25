using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DmOrder.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddSubscriptions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "subscriptions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    StoreId = table.Column<Guid>(type: "uuid", nullable: false),
                    Status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    TrialStartedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    TrialEndsAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    Provider = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: true),
                    ProviderCustomerId = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: true),
                    ProviderSubscriptionId = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: true),
                    CurrentPeriodStart = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    CurrentPeriodEnd = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    CancelledAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_subscriptions", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_subscriptions_Status_TrialEndsAt",
                table: "subscriptions",
                columns: new[] { "Status", "TrialEndsAt" });

            migrationBuilder.CreateIndex(
                name: "IX_subscriptions_StoreId",
                table: "subscriptions",
                column: "StoreId",
                unique: true);

            // Backfill: every existing store gets the same month everyone else gets, measured from
            // when the store was created rather than from today. A store created five weeks ago has
            // therefore already used its trial, which is the honest reading — not a fresh month
            // granted for having been early.
            //
            // The 30 days is a literal because a migration cannot read Plans:TrialDays. It matches
            // the default; a deployment that has changed that setting should adjust this first.
            migrationBuilder.Sql(
                """
                INSERT INTO subscriptions
                    ("Id", "StoreId", "Status", "TrialStartedAt", "TrialEndsAt", "CreatedAt", "UpdatedAt")
                SELECT
                    gen_random_uuid(),
                    s."Id",
                    CASE
                        WHEN s."CreatedAt" + interval '30 days' > now() THEN 'Trialing'
                        ELSE 'TrialExpired'
                    END,
                    s."CreatedAt",
                    s."CreatedAt" + interval '30 days',
                    now(),
                    now()
                FROM stores s
                WHERE NOT EXISTS (
                    SELECT 1 FROM subscriptions sub WHERE sub."StoreId" = s."Id"
                );
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "subscriptions");
        }
    }
}
