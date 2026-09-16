using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace QuotesApi.Shared.Migrations.SqlServer.Migrations
{
    /// <inheritdoc />
    public partial class AddCollectionOwnerNameUniqueIndex : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "IX_Collections_OwnerId_Name",
                table: "Collections",
                columns: new[] { "OwnerId", "Name" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Collections_OwnerId_Name",
                table: "Collections");
        }
    }
}
