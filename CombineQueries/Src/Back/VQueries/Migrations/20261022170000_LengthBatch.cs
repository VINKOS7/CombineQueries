using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CombineQueries.Api.Migrations
{
    // Потолок длины url уходит из схемы в /connect (lengthBatch): колонки становятся text, а
    // уникальность handle<->url держится на md5 - строка btree-индекса Postgres не длиннее ~2,7 КБ.
    /// <inheritdoc />
    public partial class LengthBatch : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Hypers_TranslatorId_Url",
                table: "Hypers");

            migrationBuilder.AlterColumn<string>(
                name: "Url",
                table: "Hypers",
                type: "text",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(2048)",
                oldMaxLength: 2048);

            migrationBuilder.AlterColumn<string>(
                name: "Url",
                table: "Chains",
                type: "text",
                nullable: true,
                oldClrType: typeof(string),
                oldType: "character varying(2048)",
                oldMaxLength: 2048,
                oldNullable: true);

            migrationBuilder.AddColumn<string>(
                name: "UrlHash",
                table: "Hypers",
                type: "text",
                nullable: true,
                computedColumnSql: "md5(\"Url\")",
                stored: true);

            migrationBuilder.CreateIndex(
                name: "IX_Hypers_TranslatorId_UrlHash",
                table: "Hypers",
                columns: new[] { "TranslatorId", "UrlHash" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Hypers_TranslatorId_UrlHash",
                table: "Hypers");

            migrationBuilder.DropColumn(
                name: "UrlHash",
                table: "Hypers");

            migrationBuilder.AlterColumn<string>(
                name: "Url",
                table: "Chains",
                type: "character varying(2048)",
                maxLength: 2048,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "text",
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "Url",
                table: "Hypers",
                type: "character varying(2048)",
                maxLength: 2048,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "text");

            migrationBuilder.CreateIndex(
                name: "IX_Hypers_TranslatorId_Url",
                table: "Hypers",
                columns: new[] { "TranslatorId", "Url" },
                unique: true);
        }
    }
}
