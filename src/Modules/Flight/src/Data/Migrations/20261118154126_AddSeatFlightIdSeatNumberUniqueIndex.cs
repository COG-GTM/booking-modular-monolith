using Flight.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Flight.Data.Migrations
{
    [DbContext(typeof(FlightDbContext))]
    [Migration("20261118154126_AddSeatFlightIdSeatNumberUniqueIndex")]
    public partial class AddSeatFlightIdSeatNumberUniqueIndex : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "ix_seat_flight_id_seat_number",
                table: "seat",
                columns: new[] { "flight_id", "seat_number" },
                unique: true
            );
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(name: "ix_seat_flight_id_seat_number", table: "seat");
        }
    }
}
