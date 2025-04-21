using System;
using BTCPayServer.Plugins.AutoForward.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace BTCPayServer.Plugins.AutoForward.Migrations
{
    [DbContext(typeof(AutoForwardDbContext))]
    [Migration("20231117154419_Init")]
    public partial class Init : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "BTCPayServer.Plugins.AutoForward");

            migrationBuilder.CreateTable(
                name: "AutoForwardDestination",
                schema: "BTCPayServer.Plugins.AutoForward",
                columns: table => new
                {
                    Id = table.Column<string>(maxLength: 50, nullable: false),
                    StoreId = table.Column<string>(maxLength: 50, nullable: false),
                    Destination = table.Column<string>(maxLength: 90, nullable: false),
                    PaymentMethod = table.Column<string>(maxLength: 50, nullable: false),
                    // Blob = table.Column<byte[]>(nullable: true)
                    Balance = table.Column<decimal>(nullable: false),
                    PayoutsAllowed = table.Column<bool>(nullable: false),
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AutoForwardDestination", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AutoForwardDestination_Stores_StoreId",
                        column: x => x.StoreId,
                        principalTable: "Stores",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });
            
            migrationBuilder.CreateIndex(
                 name: "IX_AutoForwardDestination_Destination",
                 table: "AutoForwardDestination",
                 schema: "BTCPayServer.Plugins.AutoForward",
                columns: new []{ "Destination", "PaymentMethod" },
                unique: true);

        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AutoForwardDestination",
                schema: "BTCPayServer.Plugins.AutoForward");
        }
        
    }
}
