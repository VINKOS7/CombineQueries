using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CombineQueries.Api.Migrations
{
    /// <inheritdoc />
    public partial class Dictionary : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Hypers",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false),
                    TranslatorId = table.Column<Guid>(type: "uuid", nullable: false),
                    Url = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Hypers", x => new { x.TranslatorId, x.Id });
                    table.ForeignKey(
                        name: "FK_Hypers_Translators_TranslatorId",
                        column: x => x.TranslatorId,
                        principalTable: "Translators",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "VirtualFragments",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false),
                    TranslatorId = table.Column<Guid>(type: "uuid", nullable: false),
                    Text = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                    Level = table.Column<int>(type: "integer", nullable: false),
                    Jump = table.Column<int>(type: "integer", nullable: true),
                    End = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_VirtualFragments", x => new { x.TranslatorId, x.Id });
                    table.ForeignKey(
                        name: "FK_VirtualFragments_Translators_TranslatorId",
                        column: x => x.TranslatorId,
                        principalTable: "Translators",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Hypers_TranslatorId_Url",
                table: "Hypers",
                columns: new[] { "TranslatorId", "Url" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_VirtualFragments_TranslatorId_Level",
                table: "VirtualFragments",
                columns: new[] { "TranslatorId", "Level" });

            migrationBuilder.CreateIndex(
                name: "IX_VirtualFragments_TranslatorId_Text",
                table: "VirtualFragments",
                columns: new[] { "TranslatorId", "Text" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Hypers");

            migrationBuilder.DropTable(
                name: "VirtualFragments");
        }
    }
}
