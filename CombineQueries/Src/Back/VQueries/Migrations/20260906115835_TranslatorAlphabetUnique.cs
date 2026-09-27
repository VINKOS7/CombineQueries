using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CombineQueries.Api.Migrations
{
    /// <inheritdoc />
    public partial class TranslatorAlphabetUnique : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "IX_Translators_Alphabet",
                table: "Translators",
                column: "Alphabet",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Translators_Alphabet",
                table: "Translators");
        }
    }
}
