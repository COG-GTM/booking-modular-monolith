using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Flight.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddSchemaIsolation : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(name: "flight");

            migrationBuilder.RenameTable(name: "seat", newName: "seat", newSchema: "flight");

            migrationBuilder.RenameTable(name: "flight", newName: "flight", newSchema: "flight");

            migrationBuilder.RenameTable(name: "airport", newName: "airport", newSchema: "flight");

            migrationBuilder.RenameTable(name: "aircraft", newName: "aircraft", newSchema: "flight");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameTable(name: "seat", schema: "flight", newName: "seat");

            migrationBuilder.RenameTable(name: "flight", schema: "flight", newName: "flight");

            migrationBuilder.RenameTable(name: "airport", schema: "flight", newName: "airport");

            migrationBuilder.RenameTable(name: "aircraft", schema: "flight", newName: "aircraft");
        }
    }
}
