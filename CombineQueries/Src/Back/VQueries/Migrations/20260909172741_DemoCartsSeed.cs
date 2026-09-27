using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CombineQueries.Api.Migrations
{
    /// <summary>
    /// Соседи посеянного carts/5 - carts/1, carts/2 и carts/3.
    ///
    /// Нужны первому шагу демки. Прыжок отдаёт СЕМЬЮ: сам узел и его соседей по родителю, у
    /// которых общая вся дорога, кроме последнего куска. У carts/5 соседей не было вовсе, поэтому
    /// один запрос возвращал один адрес - и «четыре разных урла за один запрос» показать было не
    /// на чем.
    ///
    /// Ни родитель, ни шаг тут не зашиты: и то, и другое берётся у САМОГО carts/5. Его шаг - это
    /// остаток адреса после того, что накрыл родитель: в dev словарь накрывает "dummyjson.com/carts/"
    /// и остаётся "r5", на релизе словарь пуст и весь адрес едет текстом. Меняем в этом шаге
    /// последний символ - и получаем брата, разобранного ровно так же, как разобрал бы сервер.
    /// </summary>
    public partial class DemoCartsSeed : Migration
    {
        // Продолжаем нумерацию семьи из DemoChainSeed (900-903).
        public const int First = 904;

        public const string Sibling = "dummyjson.com/carts/5";

        public static readonly string[] Urls =
        [
            "dummyjson.com/carts/1",
            "dummyjson.com/carts/2",
            "dummyjson.com/carts/3"
        ];

        private static readonly DateTime Stamp = new(2026, 9, 9, 0, 0, 0, DateTimeKind.Utc);

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            for (int at = 0; at < Urls.Length; at++) migrationBuilder.Sql(Insert(First + at, Urls[at], Stamp));
        }

        // Брат посеянного адреса: тот же родитель, тот же шаг с точностью до последнего символа.
        public static string Insert(int id, string url, DateTime stamp) =>
            $"""
            insert into "Chains" ("TranslatorId", "Id", "ParentId", "Step", "Url", "CreatedAt", "UpdatedAt")
            select c."TranslatorId",
                   {id},
                   c."ParentId",
                   left(c."Step", length(c."Step") - 1) || '{url[^1]}',
                   '{url}',
                   '{stamp:O}',
                   '{stamp:O}'
            from "Chains" c
            where c."Url" = '{Sibling}'
            limit 1
            on conflict do nothing;
            """;

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            for (int at = 0; at < Urls.Length; at++)
                migrationBuilder.Sql($"""delete from "Chains" where "Id" = {First + at};""");
        }
    }
}
