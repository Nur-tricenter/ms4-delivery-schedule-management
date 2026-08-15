using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace delivery_schedule_management.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "DeliveryCalendars",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    PatientId = table.Column<Guid>(type: "uuid", nullable: false),
                    ContractId = table.Column<Guid>(type: "uuid", nullable: false),
                    PlanId = table.Column<Guid>(type: "uuid", nullable: false),
                    DurationDays = table.Column<int>(type: "integer", nullable: false),
                    StartDate = table.Column<DateOnly>(type: "date", nullable: false),
                    Status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    Created = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    LastModified = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DeliveryCalendars", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ProductionOrders",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    ProductionDate = table.Column<DateOnly>(type: "date", nullable: false),
                    DeliveryDate = table.Column<DateOnly>(type: "date", nullable: false),
                    GeneratedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    Status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    Created = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    LastModified = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ProductionOrders", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "DeliveryDays",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Date = table.Column<DateOnly>(type: "date", nullable: false),
                    Status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    AddressStreet = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    AddressCity = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: true),
                    AddressReferences = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    AddressNotes = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    AddressLatitude = table.Column<double>(type: "double precision", nullable: true),
                    AddressLongitude = table.Column<double>(type: "double precision", nullable: true),
                    DeliveryHour = table.Column<TimeOnly>(type: "time without time zone", nullable: true),
                    RecipeRef = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    Portions = table.Column<int>(type: "integer", nullable: true),
                    EvidenceRecordedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    EvidencePhotoUrl = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    EvidenceSignatureUrl = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    DeliveryCalendarId = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DeliveryDays", x => x.Id);
                    table.ForeignKey(
                        name: "FK_DeliveryDays_DeliveryCalendars_DeliveryCalendarId",
                        column: x => x.DeliveryCalendarId,
                        principalTable: "DeliveryCalendars",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ProductionOrderLines",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    DeliveryDayId = table.Column<int>(type: "integer", nullable: false),
                    PatientId = table.Column<Guid>(type: "uuid", nullable: false),
                    AddressStreet = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    AddressCity = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    AddressReferences = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    AddressNotes = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    AddressLatitude = table.Column<double>(type: "double precision", nullable: false),
                    AddressLongitude = table.Column<double>(type: "double precision", nullable: false),
                    Latitude = table.Column<double>(type: "double precision", nullable: false),
                    Longitude = table.Column<double>(type: "double precision", nullable: false),
                    PlanRef = table.Column<Guid>(type: "uuid", nullable: false),
                    Hour = table.Column<TimeOnly>(type: "time without time zone", nullable: true),
                    ProductionOrderId = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ProductionOrderLines", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ProductionOrderLines_ProductionOrders_ProductionOrderId",
                        column: x => x.ProductionOrderId,
                        principalTable: "ProductionOrders",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_DeliveryCalendars_ContractId",
                table: "DeliveryCalendars",
                column: "ContractId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_DeliveryDays_DeliveryCalendarId",
                table: "DeliveryDays",
                column: "DeliveryCalendarId");

            migrationBuilder.CreateIndex(
                name: "IX_ProductionOrderLines_ProductionOrderId",
                table: "ProductionOrderLines",
                column: "ProductionOrderId");

            migrationBuilder.CreateIndex(
                name: "IX_ProductionOrders_DeliveryDate",
                table: "ProductionOrders",
                column: "DeliveryDate",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "DeliveryDays");

            migrationBuilder.DropTable(
                name: "ProductionOrderLines");

            migrationBuilder.DropTable(
                name: "DeliveryCalendars");

            migrationBuilder.DropTable(
                name: "ProductionOrders");
        }
    }
}
