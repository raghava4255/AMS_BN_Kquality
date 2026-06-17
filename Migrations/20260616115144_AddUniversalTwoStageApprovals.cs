using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ams.Migrations
{
    /// <inheritdoc />
    public partial class AddUniversalTwoStageApprovals : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "HrApprovalStatus",
                table: "ShiftRequests",
                type: "nvarchar(50)",
                maxLength: 50,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "HrApproverSignature",
                table: "ShiftRequests",
                type: "nvarchar(max)",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "TlApprovalStatus",
                table: "ShiftRequests",
                type: "nvarchar(50)",
                maxLength: 50,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "TlApproverSignature",
                table: "ShiftRequests",
                type: "nvarchar(max)",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "HrApprovalStatus",
                table: "FlexyHourRequests",
                type: "nvarchar(50)",
                maxLength: 50,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "HrApproverSignature",
                table: "FlexyHourRequests",
                type: "nvarchar(max)",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "TlApprovalStatus",
                table: "FlexyHourRequests",
                type: "nvarchar(50)",
                maxLength: 50,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "TlApproverSignature",
                table: "FlexyHourRequests",
                type: "nvarchar(max)",
                nullable: false,
                defaultValue: "");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "HrApprovalStatus",
                table: "ShiftRequests");

            migrationBuilder.DropColumn(
                name: "HrApproverSignature",
                table: "ShiftRequests");

            migrationBuilder.DropColumn(
                name: "TlApprovalStatus",
                table: "ShiftRequests");

            migrationBuilder.DropColumn(
                name: "TlApproverSignature",
                table: "ShiftRequests");

            migrationBuilder.DropColumn(
                name: "HrApprovalStatus",
                table: "FlexyHourRequests");

            migrationBuilder.DropColumn(
                name: "HrApproverSignature",
                table: "FlexyHourRequests");

            migrationBuilder.DropColumn(
                name: "TlApprovalStatus",
                table: "FlexyHourRequests");

            migrationBuilder.DropColumn(
                name: "TlApproverSignature",
                table: "FlexyHourRequests");
        }
    }
}
