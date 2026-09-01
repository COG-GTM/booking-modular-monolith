using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Identity.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddSchemaIsolation : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(name: "identity");

            migrationBuilder.RenameTable(name: "asp_net_users", newName: "asp_net_users", newSchema: "identity");

            migrationBuilder.RenameTable(
                name: "asp_net_user_tokens",
                newName: "asp_net_user_tokens",
                newSchema: "identity"
            );

            migrationBuilder.RenameTable(
                name: "asp_net_user_roles",
                newName: "asp_net_user_roles",
                newSchema: "identity"
            );

            migrationBuilder.RenameTable(
                name: "asp_net_user_logins",
                newName: "asp_net_user_logins",
                newSchema: "identity"
            );

            migrationBuilder.RenameTable(
                name: "asp_net_user_claims",
                newName: "asp_net_user_claims",
                newSchema: "identity"
            );

            migrationBuilder.RenameTable(name: "asp_net_roles", newName: "asp_net_roles", newSchema: "identity");

            migrationBuilder.RenameTable(
                name: "asp_net_role_claims",
                newName: "asp_net_role_claims",
                newSchema: "identity"
            );

            migrationBuilder.AlterColumn<string>(
                name: "pass_port_number",
                schema: "identity",
                table: "asp_net_users",
                type: "text",
                nullable: false,
                defaultValue: "",
                oldClrType: typeof(string),
                oldType: "text",
                oldNullable: true
            );

            migrationBuilder.AlterColumn<string>(
                name: "last_name",
                schema: "identity",
                table: "asp_net_users",
                type: "text",
                nullable: false,
                defaultValue: "",
                oldClrType: typeof(string),
                oldType: "text",
                oldNullable: true
            );

            migrationBuilder.AlterColumn<string>(
                name: "first_name",
                schema: "identity",
                table: "asp_net_users",
                type: "text",
                nullable: false,
                defaultValue: "",
                oldClrType: typeof(string),
                oldType: "text",
                oldNullable: true
            );
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameTable(name: "asp_net_users", schema: "identity", newName: "asp_net_users");

            migrationBuilder.RenameTable(
                name: "asp_net_user_tokens",
                schema: "identity",
                newName: "asp_net_user_tokens"
            );

            migrationBuilder.RenameTable(name: "asp_net_user_roles", schema: "identity", newName: "asp_net_user_roles");

            migrationBuilder.RenameTable(
                name: "asp_net_user_logins",
                schema: "identity",
                newName: "asp_net_user_logins"
            );

            migrationBuilder.RenameTable(
                name: "asp_net_user_claims",
                schema: "identity",
                newName: "asp_net_user_claims"
            );

            migrationBuilder.RenameTable(name: "asp_net_roles", schema: "identity", newName: "asp_net_roles");

            migrationBuilder.RenameTable(
                name: "asp_net_role_claims",
                schema: "identity",
                newName: "asp_net_role_claims"
            );

            migrationBuilder.AlterColumn<string>(
                name: "pass_port_number",
                table: "asp_net_users",
                type: "text",
                nullable: true,
                oldClrType: typeof(string),
                oldType: "text"
            );

            migrationBuilder.AlterColumn<string>(
                name: "last_name",
                table: "asp_net_users",
                type: "text",
                nullable: true,
                oldClrType: typeof(string),
                oldType: "text"
            );

            migrationBuilder.AlterColumn<string>(
                name: "first_name",
                table: "asp_net_users",
                type: "text",
                nullable: true,
                oldClrType: typeof(string),
                oldType: "text"
            );
        }
    }
}
