using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CombineQueries.Api.Migrations
{
    /// <summary>
    /// Один хайпер ТОЛЬКО для dev - ровно то, что показывает дамп.
    ///
    /// В dev гиперизация живёт в ОЗУ сервера: новые цепочки в БД не уезжают (hypers=off), поэтому
    /// без этой строки прыгать было бы не по чему и персист остался бы невидим. С ней клиент на
    /// connect получает сид с одним адресом, которого он никогда не собирал, и тот уходит в два
    /// запроса с первого раза; всё дальнейшее - уже динамика в памяти.
    ///
    /// Цепочка из ОДНОГО шага: фрагмент 18 сидового словаря это "dummyjson.com/carts/", а "5"
    /// уезжает хвостом, хвост в цепочку не входит. Литералами, как и словарь: применённая
    /// миграция обязана давать один и тот же результат при любом прогоне.
    /// </summary>
    public partial class DevChainSeed : Migration
    {
        // Тот же транслятор, что завёл DevFragmentsSeed: цепочка ссылается на его словарь.
        private static readonly Guid Translator = new("3f2a51c6-9b47-4d18-a0e5-6c7d81f4b2a9");

        private const int Node = 0;
        private const string Step = "f18";
        private const string Url = "dummyjson.com/carts/5";

        private static readonly DateTime Stamp = new(2026, 9, 7, 0, 0, 0, DateTimeKind.Utc);

        private static bool IsDevelopment =>
            string.Equals(Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT"), "Development", StringComparison.OrdinalIgnoreCase);

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            if (!IsDevelopment) return;

            migrationBuilder.InsertData(
                table: "Chains",
                columns: ["TranslatorId", "Id", "ParentId", "Step", "Url", "CreatedAt", "UpdatedAt"],
                values: [Translator, Node, null, Step, Url, Stamp, Stamp]);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            if (!IsDevelopment) return;

            migrationBuilder.DeleteData(
                table: "Chains",
                keyColumns: ["TranslatorId", "Id"],
                keyValues: [Translator, Node]);
        }
    }
}
