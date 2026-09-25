using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DmOrder.Infrastructure.Persistence.Migrations
{
    /// <summary>
    /// Adds the customer-facing order reference.
    ///
    /// Written by hand rather than left as scaffolded. The generated version added a NOT NULL column
    /// with a <c>""</c> default and then created a unique index over it, which fails the moment more
    /// than one order already exists — and would leave every historical order sharing an empty
    /// reference if it did not.
    ///
    /// So: add it nullable, backfill, tighten to NOT NULL, then index.
    /// </summary>
    public partial class AddOrderPublicReference : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "PublicReference",
                table: "orders",
                type: "character varying(24)",
                maxLength: 24,
                nullable: true);

            // Backfill.
            //
            // md5 is seeded with the row's own Id, which makes it correlated and therefore evaluated
            // per row — an uncorrelated random() subquery is free to be computed once for the whole
            // statement, which would give every order the same reference.
            //
            // Uppercase hex is a strict subset of the reference alphabet (0-9 and A-F), so these are
            // well-formed by OrderReference.IsWellFormed without needing to reproduce the full
            // alphabet in SQL.
            //
            // The prefix is the historical default. It is configurable for new orders, but a
            // migration cannot read application configuration, and a reference a customer already
            // has written down must keep working — so old and new prefixes simply coexist.
            migrationBuilder.Sql(
                """
                UPDATE orders
                SET "PublicReference" =
                    'DM-'
                    || to_char("CreatedAt", 'YYYY')
                    || '-'
                    || upper(substr(md5("Id"::text || random()::text), 1, 6))
                WHERE "PublicReference" IS NULL;
                """);

            migrationBuilder.AlterColumn<string>(
                name: "PublicReference",
                table: "orders",
                type: "character varying(24)",
                maxLength: 24,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(24)",
                oldMaxLength: 24,
                oldNullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_orders_PublicReference",
                table: "orders",
                column: "PublicReference",
                unique: true);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_orders_PublicReference",
                table: "orders");

            migrationBuilder.DropColumn(
                name: "PublicReference",
                table: "orders");
        }
    }
}
