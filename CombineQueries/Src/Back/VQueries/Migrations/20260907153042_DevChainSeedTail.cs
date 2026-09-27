using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CombineQueries.Api.Migrations
{
    /// <summary>
    /// Тот же dev-хайпер, но цепочкой НОВОЙ формы: хвост входит в путь последним шагом.
    ///
    /// Пока лист хранил только combine-часть, адрес был неоднозначен - comments/1 и comments/2
    /// сходились в один узел и перетирали друг друга, поэтому прыжок не мог форвардить сам и стоил
    /// двух запросов. Теперь лист это ВЕСЬ адрес, и /h/ отдаёт результат за один запрос.
    ///
    /// Для dummyjson.com/carts/5 путь такой: f18 ("dummyjson.com/carts/") -> t5 (хвост "5").
    /// Адрес висит на втором узле, прыжок клиента идёт именно в него.
    /// </summary>
    public partial class DevChainSeedTail : Migration
    {
        private static readonly Guid Translator = new("3f2a51c6-9b47-4d18-a0e5-6c7d81f4b2a9");

        private const string Url = "dummyjson.com/carts/5";

        private static readonly DateTime Stamp = new(2026, 9, 7, 0, 0, 0, DateTimeKind.Utc);

        private static bool IsDevelopment =>
            string.Equals(Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT"), "Development", StringComparison.OrdinalIgnoreCase);

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            if (!IsDevelopment) return;

            // Старый посев формы «адрес на combine-узле» больше не годится.
            migrationBuilder.DeleteData(table: "Chains", keyColumns: ["TranslatorId", "Id"], keyValues: [Translator, 0]);

            migrationBuilder.InsertData(
                table: "Chains",
                columns: ["TranslatorId", "Id", "ParentId", "Step", "Url", "CreatedAt", "UpdatedAt"],
                values: new object[,]
                {
                    { Translator, 0, null, "f18", null, Stamp, Stamp },
                    { Translator, 1, 0, "t5", Url, Stamp, Stamp }
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            if (!IsDevelopment) return;

            migrationBuilder.DeleteData(table: "Chains", keyColumns: ["TranslatorId", "Id"], keyValues: [Translator, 1]);
            migrationBuilder.DeleteData(table: "Chains", keyColumns: ["TranslatorId", "Id"], keyValues: [Translator, 0]);

            migrationBuilder.InsertData(
                table: "Chains",
                columns: ["TranslatorId", "Id", "ParentId", "Step", "Url", "CreatedAt", "UpdatedAt"],
                values: [Translator, 0, null, "f18", Url, Stamp, Stamp]);
        }
    }
}
