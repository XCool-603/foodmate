using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FoodMate.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddDishCanMakeAtHome : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "can_make_at_home",
                table: "dishes",
                type: "boolean",
                nullable: false,
                defaultValue: false);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "can_make_at_home",
                table: "dishes");
        }
    }
}
