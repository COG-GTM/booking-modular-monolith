using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Flight.Data.Migrations
{
    /// <inheritdoc />
    public partial class audit_user_id_guid : Migration
    {
        // Audit user ids are now Guids (the token "sub" claim). Existing bigint values were always 0 and carry no
        // attribution, and PostgreSQL has no bigint -> uuid cast, so the columns are converted to NULL.

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("ALTER TABLE aircraft ALTER COLUMN created_by TYPE uuid USING NULL;");
            migrationBuilder.Sql("ALTER TABLE aircraft ALTER COLUMN last_modified_by TYPE uuid USING NULL;");
            migrationBuilder.Sql("ALTER TABLE airport ALTER COLUMN created_by TYPE uuid USING NULL;");
            migrationBuilder.Sql("ALTER TABLE airport ALTER COLUMN last_modified_by TYPE uuid USING NULL;");
            migrationBuilder.Sql("ALTER TABLE flight ALTER COLUMN created_by TYPE uuid USING NULL;");
            migrationBuilder.Sql("ALTER TABLE flight ALTER COLUMN last_modified_by TYPE uuid USING NULL;");
            migrationBuilder.Sql("ALTER TABLE seat ALTER COLUMN created_by TYPE uuid USING NULL;");
            migrationBuilder.Sql("ALTER TABLE seat ALTER COLUMN last_modified_by TYPE uuid USING NULL;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("ALTER TABLE aircraft ALTER COLUMN created_by TYPE bigint USING NULL;");
            migrationBuilder.Sql("ALTER TABLE aircraft ALTER COLUMN last_modified_by TYPE bigint USING NULL;");
            migrationBuilder.Sql("ALTER TABLE airport ALTER COLUMN created_by TYPE bigint USING NULL;");
            migrationBuilder.Sql("ALTER TABLE airport ALTER COLUMN last_modified_by TYPE bigint USING NULL;");
            migrationBuilder.Sql("ALTER TABLE flight ALTER COLUMN created_by TYPE bigint USING NULL;");
            migrationBuilder.Sql("ALTER TABLE flight ALTER COLUMN last_modified_by TYPE bigint USING NULL;");
            migrationBuilder.Sql("ALTER TABLE seat ALTER COLUMN created_by TYPE bigint USING NULL;");
            migrationBuilder.Sql("ALTER TABLE seat ALTER COLUMN last_modified_by TYPE bigint USING NULL;");
        }
    }
}
