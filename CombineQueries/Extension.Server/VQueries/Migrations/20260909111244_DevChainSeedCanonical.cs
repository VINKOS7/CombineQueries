using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CombineQueries.Api.Migrations
{
    /// <summary>
    /// Приводит dev-посев к КАНОНУ.
    ///
    /// Путь в дереве теперь считается разбором адреса текущим словарём, а не тем, чем его
    /// продиктовали: хвост перестал быть отдельным шагом, непокрытый остаток едет рунным шагом с
    /// разжатым текстом. Для dummyjson.com/carts/5 это f18 -> r5, а посев клал f18 -> t5.
    ///
    /// Разойдись они - живая сборка того же адреса завела бы второй путь к нему, то есть дубль,
    /// и поиск по каноническому пути посев бы не нашёл.
    /// </summary>
    public partial class DevChainSeedCanonical : Migration
    {
        private static readonly Guid Translator = new("3f2a51c6-9b47-4d18-a0e5-6c7d81f4b2a9");

        private static bool IsDevelopment =>
            string.Equals(Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT"), "Development", StringComparison.OrdinalIgnoreCase);

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            if (!IsDevelopment) return;

            migrationBuilder.UpdateData(
                table: "Chains",
                keyColumns: ["TranslatorId", "Id"],
                keyValues: [Translator, 1],
                column: "Step",
                value: "r5");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            if (!IsDevelopment) return;

            migrationBuilder.UpdateData(
                table: "Chains",
                keyColumns: ["TranslatorId", "Id"],
                keyValues: [Translator, 1],
                column: "Step",
                value: "t5");
        }
    }
}
