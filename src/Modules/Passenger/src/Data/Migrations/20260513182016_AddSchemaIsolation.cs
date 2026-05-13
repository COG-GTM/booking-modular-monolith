using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Passenger.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddSchemaIsolation : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(name: "passenger");

            migrationBuilder.RenameTable(name: "passenger", newName: "passenger", newSchema: "passenger");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameTable(name: "passenger", schema: "passenger", newName: "passenger");
        }
    }
}
