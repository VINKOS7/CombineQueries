using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CombineQueries.Api.Migrations
{
    /// <summary>
    /// Посев демки: СЕМЬЯ из четырёх адресов, а не одиночка.
    ///
    /// Одного адреса (DevChainSeed) хватало показать, что прыжок переживает рестарт, и только.
    /// Демка показывает две вещи, которых на одном адресе не видно:
    ///
    ///   - пачка: четыре номера идут ПОДРЯД, поэтому все четыре адреса уезжают одним запросом;
    ///   - голова: у семьи общее начало и разные последние сегменты, поэтому по одному известному
    ///     адресу сервер называет остальные.
    ///
    /// Подряд номера идут не случайно: каждый адрес семьи - отдельная цепочка от корня в один шаг,
    /// значит каждый добавляет ровно один узел, и номера ложатся вплотную. Ломать порядок вставки
    /// нельзя, на нём держится пачка.
    ///
    /// Шаг цепочки зависит от того, чем адрес разбирается, а это разное в dev и на релизе: в dev
    /// словарь посеян и накрывает каждый адрес целиком, на релизе он пуст и наполняется прогревом.
    /// Поэтому шаг не зашит литералом, а считается на месте - куском, если такой кусок есть, иначе
    /// текстом. Посев обязан совпасть с тем, как сервер разберёт адрес сам: разойдутся - к одному
    /// адресу появится второй путь, то есть дубль, и посев по нему не найдётся.
    ///
    /// По той же причине не зашит и владелец: в dev это транслятор из DevFragmentsSeed, на релизе -
    /// тот, что завёл первый /connect. Берём первый по дате; нет ни одного - посев не ложится и
    /// ждёт `dotnet run --project VQueries.Dump -- seed` после первого подключения.
    /// </summary>
    public partial class DemoChainSeed : Migration
    {
        // Номера листов. Начинаем с 900, а не с нуля: нулевые заняты и посевом одного адреса, и
        // тем, что сервер успел накопить сам. Занятый номер уронил бы миграцию по ключу.
        public const int First = 900;

        public static readonly string[] Urls =
        [
            "dummyjson.com/comments",
            "dummyjson.com/products",
            "dummyjson.com/recipes",
            "dummyjson.com/quotes"
        ];

        private static readonly DateTime Stamp = new(2026, 9, 9, 0, 0, 0, DateTimeKind.Utc);

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            for (int at = 0; at < Urls.Length; at++) migrationBuilder.Sql(Insert(First + at, Urls[at], Stamp));
        }

        // Одна строка посева. Владелец и шаг берутся из самой базы, поэтому запрос одинаково верен
        // и в dev, и на релизе; on conflict - чтобы занятый номер не ронял миграцию на середине.
        public static string Insert(int id, string url, DateTime stamp) =>
            $"""
            insert into "Chains" ("TranslatorId", "Id", "ParentId", "Step", "Url", "CreatedAt", "UpdatedAt")
            select t."Id",
                   {id},
                   null,
                   coalesce((select 'f' || f."Id" from "VirtualFragments" f
                             where f."TranslatorId" = t."Id" and f."Text" = '{url}' limit 1), 'r{url}'),
                   '{url}',
                   '{stamp:O}',
                   '{stamp:O}'
            from "Translators" t
            order by t."CreatedAt"
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
