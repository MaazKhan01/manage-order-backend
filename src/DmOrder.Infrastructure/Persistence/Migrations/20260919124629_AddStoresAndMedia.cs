using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DmOrder.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddStoresAndMedia : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "media_assets",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    StoreId = table.Column<Guid>(type: "uuid", nullable: false),
                    StorageKey = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                    ContentType = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    SizeBytes = table.Column<long>(type: "bigint", nullable: false),
                    Width = table.Column<int>(type: "integer", nullable: true),
                    Height = table.Column<int>(type: "integer", nullable: true),
                    OriginalFileName = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    UploadedByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    Purpose = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_media_assets", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "stores",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    OwnerUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    Slug = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    Description = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    ContactPhone = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    WhatsApp = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    ContactEmail = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    InstagramUrl = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    FacebookUrl = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    TiktokUrl = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    AddressText = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    City = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: true),
                    Country = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: true),
                    LogoMediaId = table.Column<Guid>(type: "uuid", nullable: true),
                    CoverMediaId = table.Column<Guid>(type: "uuid", nullable: true),
                    SeoTitle = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: true),
                    SeoDescription = table.Column<string>(type: "character varying(320)", maxLength: 320, nullable: true),
                    Currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    IsPublished = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    PublishedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_stores", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "store_themes",
                columns: table => new
                {
                    StoreId = table.Column<Guid>(type: "uuid", nullable: false),
                    PrimaryColor = table.Column<string>(type: "character varying(7)", maxLength: 7, nullable: false),
                    AccentColor = table.Column<string>(type: "character varying(7)", maxLength: 7, nullable: false),
                    BackgroundColor = table.Column<string>(type: "character varying(7)", maxLength: 7, nullable: false),
                    FontChoice = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    ButtonStyle = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    LayoutVariant = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    BackgroundMediaId = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_store_themes", x => x.StoreId);
                    table.ForeignKey(
                        name: "FK_store_themes_stores_StoreId",
                        column: x => x.StoreId,
                        principalTable: "stores",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_media_assets_StorageKey",
                table: "media_assets",
                column: "StorageKey",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_media_assets_StoreId",
                table: "media_assets",
                column: "StoreId");

            migrationBuilder.CreateIndex(
                name: "IX_media_assets_StoreId_Purpose",
                table: "media_assets",
                columns: new[] { "StoreId", "Purpose" });

            migrationBuilder.CreateIndex(
                name: "IX_stores_OwnerUserId",
                table: "stores",
                column: "OwnerUserId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_stores_Slug",
                table: "stores",
                column: "Slug",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_stores_Slug_IsPublished_IsActive",
                table: "stores",
                columns: new[] { "Slug", "IsPublished", "IsActive" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "media_assets");

            migrationBuilder.DropTable(
                name: "store_themes");

            migrationBuilder.DropTable(
                name: "stores");
        }
    }
}
