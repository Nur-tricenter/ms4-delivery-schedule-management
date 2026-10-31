using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace delivery_schedule_management.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class AsyncMessaging : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "OutboxMessages",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Type = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    Destination = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    Payload = table.Column<string>(type: "jsonb", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    PublishedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OutboxMessages", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "PlanDayMenus",
                columns: table => new
                {
                    PlanId = table.Column<Guid>(type: "uuid", nullable: false),
                    DayNumber = table.Column<int>(type: "integer", nullable: false),
                    SnapshotAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PlanDayMenus", x => new { x.PlanId, x.DayNumber });
                });

            migrationBuilder.CreateTable(
                name: "PlanDayMenuItems",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    MealTime = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    MealTimeOrder = table.Column<int>(type: "integer", nullable: false),
                    RecipeId = table.Column<Guid>(type: "uuid", nullable: false),
                    RecipeName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Rations = table.Column<int>(type: "integer", nullable: false),
                    DayNumber = table.Column<int>(type: "integer", nullable: false),
                    PlanId = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PlanDayMenuItems", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PlanDayMenuItems_PlanDayMenus_PlanId_DayNumber",
                        columns: x => new { x.PlanId, x.DayNumber },
                        principalTable: "PlanDayMenus",
                        principalColumns: new[] { "PlanId", "DayNumber" },
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_OutboxMessages_PublishedAt",
                table: "OutboxMessages",
                column: "PublishedAt");

            migrationBuilder.CreateIndex(
                name: "IX_PlanDayMenuItems_PlanId_DayNumber",
                table: "PlanDayMenuItems",
                columns: new[] { "PlanId", "DayNumber" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "OutboxMessages");

            migrationBuilder.DropTable(
                name: "PlanDayMenuItems");

            migrationBuilder.DropTable(
                name: "PlanDayMenus");
        }
    }
}
