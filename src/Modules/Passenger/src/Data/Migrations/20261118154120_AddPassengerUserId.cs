using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Passenger.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddPassengerUserId : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "user_id",
                table: "passenger",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "ix_passenger_user_id",
                table: "passenger",
                column: "user_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_passenger_user_id",
                table: "passenger");

            migrationBuilder.DropColumn(
                name: "user_id",
                table: "passenger");
        }
    }
}
