using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Passenger.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddPassengerUserIdAndUniquePassportNumber : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "user_id",
                table: "passenger",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000")
            );

            migrationBuilder.Sql(
                "UPDATE passenger SET passport_number = upper(btrim(passport_number)) WHERE passport_number IS NOT NULL AND passport_number <> upper(btrim(passport_number));"
            );

            migrationBuilder.CreateIndex(
                name: "ix_passenger_passport_number",
                table: "passenger",
                column: "passport_number",
                unique: true
            );

            migrationBuilder.CreateIndex(name: "ix_passenger_user_id", table: "passenger", column: "user_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(name: "ix_passenger_passport_number", table: "passenger");

            migrationBuilder.DropIndex(name: "ix_passenger_user_id", table: "passenger");

            migrationBuilder.DropColumn(name: "user_id", table: "passenger");
        }
    }
}
