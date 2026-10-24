using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace delivery_schedule_management.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class DeliveryDayMenu : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "RecipeRef",
                table: "DeliveryDays");

            migrationBuilder.AddColumn<int>(
                name: "DayNumber",
                table: "DeliveryDays",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            // Backfill existing days. Days are inserted in plan order when a calendar is created, so
            // their id order within a calendar IS the plan-day order. (Deriving it from Date would be
            // wrong for rescheduled days.) The old RecipeRef values were mock fixtures, so nothing
            // meaningful is lost by dropping that column; resolved days get a menu on the next run
            // only if they are still Scheduled.
            migrationBuilder.Sql("""
                UPDATE "DeliveryDays" AS d
                SET "DayNumber" = n.rn
                FROM (
                    SELECT "Id", ROW_NUMBER() OVER (PARTITION BY "DeliveryCalendarId" ORDER BY "Id") AS rn
                    FROM "DeliveryDays"
                ) AS n
                WHERE d."Id" = n."Id";
                """);

            migrationBuilder.CreateTable(
                name: "DeliveryDayMenuItems",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    MealTime = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    MealTimeOrder = table.Column<int>(type: "integer", nullable: false),
                    RecipeId = table.Column<Guid>(type: "uuid", nullable: false),
                    RecipeName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Rations = table.Column<int>(type: "integer", nullable: false),
                    DeliveryDayId = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DeliveryDayMenuItems", x => x.Id);
                    table.ForeignKey(
                        name: "FK_DeliveryDayMenuItems_DeliveryDays_DeliveryDayId",
                        column: x => x.DeliveryDayId,
                        principalTable: "DeliveryDays",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_DeliveryDayMenuItems_DeliveryDayId",
                table: "DeliveryDayMenuItems",
                column: "DeliveryDayId");

            migrationBuilder.CreateIndex(
                name: "IX_DeliveryDayMenuItems_RecipeId",
                table: "DeliveryDayMenuItems",
                column: "RecipeId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "DeliveryDayMenuItems");

            migrationBuilder.DropColumn(
                name: "DayNumber",
                table: "DeliveryDays");

            migrationBuilder.AddColumn<string>(
                name: "RecipeRef",
                table: "DeliveryDays",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);
        }
    }
}
