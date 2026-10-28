using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace delivery_schedule_management.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class DeliveryRouteLinks : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "DeliveryRouteLinks",
                columns: table => new
                {
                    DeliveryId = table.Column<Guid>(type: "uuid", nullable: false),
                    RouteId = table.Column<Guid>(type: "uuid", nullable: false),
                    DayId = table.Column<int>(type: "integer", nullable: false),
                    LinkedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DeliveryRouteLinks", x => x.DeliveryId);
                });

            migrationBuilder.CreateIndex(
                name: "IX_DeliveryRouteLinks_DayId",
                table: "DeliveryRouteLinks",
                column: "DayId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "DeliveryRouteLinks");
        }
    }
}
