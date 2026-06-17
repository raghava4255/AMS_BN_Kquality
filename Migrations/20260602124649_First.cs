using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ams.Migrations
{
    /// <inheritdoc />
    public partial class First : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "IsActive",
                table: "Users",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "ClockInAddress",
                table: "AttendanceLogs",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<double>(
                name: "ClockInLat",
                table: "AttendanceLogs",
                type: "float",
                nullable: true);

            migrationBuilder.AddColumn<double>(
                name: "ClockInLng",
                table: "AttendanceLogs",
                type: "float",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ClockOutAddress",
                table: "AttendanceLogs",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<double>(
                name: "ClockOutLat",
                table: "AttendanceLogs",
                type: "float",
                nullable: true);

            migrationBuilder.AddColumn<double>(
                name: "ClockOutLng",
                table: "AttendanceLogs",
                type: "float",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "IsActive",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "ClockInAddress",
                table: "AttendanceLogs");

            migrationBuilder.DropColumn(
                name: "ClockInLat",
                table: "AttendanceLogs");

            migrationBuilder.DropColumn(
                name: "ClockInLng",
                table: "AttendanceLogs");

            migrationBuilder.DropColumn(
                name: "ClockOutAddress",
                table: "AttendanceLogs");

            migrationBuilder.DropColumn(
                name: "ClockOutLat",
                table: "AttendanceLogs");

            migrationBuilder.DropColumn(
                name: "ClockOutLng",
                table: "AttendanceLogs");
        }
    }
}
