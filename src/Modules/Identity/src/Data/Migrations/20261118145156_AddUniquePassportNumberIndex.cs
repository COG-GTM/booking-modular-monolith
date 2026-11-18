using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Identity.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddUniquePassportNumberIndex : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                "UPDATE asp_net_users SET pass_port_number = upper(btrim(pass_port_number)) WHERE pass_port_number IS NOT NULL AND pass_port_number <> upper(btrim(pass_port_number));"
            );

            migrationBuilder.CreateIndex(
                name: "PassportNumberIndex",
                table: "asp_net_users",
                column: "pass_port_number",
                unique: true
            );
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(name: "PassportNumberIndex", table: "asp_net_users");
        }
    }
}
