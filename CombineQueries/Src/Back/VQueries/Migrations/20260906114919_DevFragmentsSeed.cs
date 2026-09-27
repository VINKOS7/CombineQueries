using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CombineQueries.Api.Migrations
{
    /// <summary>
    /// Тестовый словарь ТОЛЬКО для dev. На релизе таблица обязана быть пустой и наполняться через
    /// /preheat, поэтому строки идут не через HasData (та уехала бы в прод вместе со схемой), а
    /// отдельной миграцией, которая вне Development не вставляет ничего.
    ///
    /// Строки - куски URL публичного API dummyjson (тот самый сайт, на который ходит клиентский
    /// тест), нарезанные по границам сегментов и отранжированные по выгоде:
    /// score = (длина / RuneSize - 1) * в скольких URL встречается. Ровно так их разложит и /preheat.
    /// Литералами, а не генератором: применённая миграция обязана остаться неизменной.
    /// </summary>
    public partial class DevFragmentsSeed : Migration
    {
        // Транслятор ищется по алфавиту (GetIdByAlphabetAsync), поэтому сид кладёт ту же строку, что
        // приедет в /init с клиента - иначе connect завёл бы ВТОРОЙ транслятор рядом с сидовым.
        private const string Alphabet = "abcdefghijklmnopqrstuvwxyz0123456789-._~:/?#[]@!$&'()*+,;=%";

        private static readonly Guid Translator = new("3f2a51c6-9b47-4d18-a0e5-6c7d81f4b2a9");

        // Размеры ровно те, что /init выставляет в dev (Dev:DfaSize / Dev:PageCount). Взяты так,
        // чтобы 1024 строки легли во ВСЕ уровни сразу: потолок L3 = 16*8 = 128, дальше Infinite,
        // который достаётся Развязкой-3 (адресуемо 128*64 = 8192, с запасом).
        // Адреса при этом остаются < клиентского dfaSize=1024, поэтому адресация не расходится.
        private const int DfaSize = 16;
        private const int Capacity = 128;

        // Уровни числами (FragmentLevel) = сколько развязок нужно, чтобы назвать строку.
        // Ноль особый: финитного адреса нет, достаётся якорем + Развязкой-3.
        private const int L2 = 1;
        private const int L3 = 2;
        private const int Infinite = 0;

        // Дата сида фиксированная: миграция обязана давать один и тот же результат при любом прогоне.
        private static readonly DateTime Stamp = new(2026, 9, 6, 0, 0, 0, DateTimeKind.Utc);

        private static bool IsDevelopment =>
            string.Equals(Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT"), "Development", StringComparison.OrdinalIgnoreCase);

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            if (!IsDevelopment) return;

            migrationBuilder.InsertData(
                table: "Translators",
                columns: ["Id", "Alphabet", "BaseForwardUrl", "CreatedAt", "Description", "Name", "UpdatedAt"],
                values: [Translator, Alphabet, "https://dummyjson.com", Stamp, "dev seed", "dummyjson", Stamp]);

            var rows = new object[Texts.Length, 8];

            for (int id = 0; id < Texts.Length; id++)
            {
                bool infinite = id >= Capacity;

                // Цепь «заёма»: бесконечная строка занимает адрес у финитной (якорь = id % Capacity),
                // следующее звено лежит ровно через ёмкость. End держит готовый результат, чтобы
                // tail не проходил цепь заново.
                bool linked = infinite && id + Capacity < Texts.Length;

                rows[id, 0] = Translator;
                rows[id, 1] = id;
                rows[id, 2] = Stamp;
                rows[id, 3] = infinite ? (object)Texts[id] : null;
                rows[id, 4] = linked ? (object)(id + Capacity) : null;
                rows[id, 5] = infinite ? Infinite : id < DfaSize ? L2 : L3;
                rows[id, 6] = Texts[id];
                rows[id, 7] = Stamp;
            }

            migrationBuilder.InsertData(
                table: "VirtualFragments",
                columns: ["TranslatorId", "Id", "CreatedAt", "End", "Jump", "Level", "Text", "UpdatedAt"],
                values: rows);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            if (!IsDevelopment) return;

            for (int id = 0; id < Texts.Length; id++)
                migrationBuilder.DeleteData(
                    table: "VirtualFragments",
                    keyColumns: ["TranslatorId", "Id"],
                    keyValues: [Translator, id]);

            migrationBuilder.DeleteData(table: "Translators", keyColumn: "Id", keyValue: Translator);
        }

        // Порядок = адрес: индекс в массиве и есть Id строки. Первые - самые выгодные.
        private static readonly string[] Texts =
        [
        "dummyjson.com/", "dummyjson.com", "dummyjson.", "dummyjson",
        "dummyjson.com/users", "dummyjson.com/comments", "dummyjson.com/posts", "dummyjson.com/recipes",
        "dummyjson.com/products", "dummyjson.com/todos", "dummyjson.com/carts", "dummyjson.com/users/",
        "dummyjson.com/quotes", "dummyjson.com/posts/", "dummyjson.com/comments/", "dummyjson.com/recipes/",
        "dummyjson.com/products/", "dummyjson.com/todos/", "dummyjson.com/carts/", ".com/comments",
        "com/comments", ".com/recipes", ".com/products", "com/products",
        "dummyjson.com/quotes/", ".com/users", "com/users", "/comments",
        "dummyjson.com/products?limit=", "dummyjson.com/comments?limit=", "dummyjson.com/products?limit", "dummyjson.com/comments?limit",
        "dummyjson.com/recipes?limit=", "dummyjson.com/quotes?limit=", "dummyjson.com/recipes?limit", ".com/posts",
        "com/posts", "?limit=", "limit=", "?limit",
        "dummyjson.com/products?", "dummyjson.com/comments?", "dummyjson.com/recipes?", "dummyjson.com/quotes?",
        "dummyjson.com/carts?limit=", "dummyjson.com/users?limit=", "dummyjson.com/posts?limit=", "dummyjson.com/quotes?limit",
        "dummyjson.com/todos?limit=", "dummyjson.com/carts?limit", "dummyjson.com/users?limit", "dummyjson.com/posts?limit",
        "dummyjson.com/todos?limit", ".com/todos", "com/todos", ".com/carts",
        "com/carts", ".com/users/", "com/users/", "com/recipes",
        "/products", "&skip=", ".com/comments/", "com/comments/",
        "dummyjson.com/carts?", "dummyjson.com/users?", "dummyjson.com/posts?", "dummyjson.com/todos?",
        ".com/recipes/", "com/recipes/", ".com/products/", "com/products/",
        ".com/quotes", "com/quotes", ".com/posts/", "com/posts/",
        "?limit=100&skip=", "?limit=10&skip=", "?limit=20&skip=", "?limit=30&skip=",
        "?limit=100&skip", "limit=100&skip=", ".com/products?limit=", ".com/comments?limit=",
        "com/products?limit=", ".com/products?limit", ".com/comments?limit", "com/comments?limit=",
        ".com/recipes?limit=", "com/products?limit", "com/comments?limit", ".com/quotes?limit=",
        ".com/recipes?limit", "com/recipes?limit=", "dummyjson.com/comments/post/", "dummyjson.com/comments/post",
        ".com/quotes/", "/users", "/posts", "/search?q=",
        "/search?q", "search?q=", ".com/todos/", "com/todos/",
        ".com/carts/", "com/carts/", "/comments/", "comments/",
        "dummyjson.com/carts/user/", "dummyjson.com/posts/user/", "dummyjson.com/todos/user/", "dummyjson.com/carts/user",
        "dummyjson.com/posts/user", "dummyjson.com/todos/user", "/todos", "comments",
        "/carts", ".com/carts?limit=", ".com/users?limit=", ".com/posts?limit=",
        "com/quotes?limit=", ".com/quotes?limit", ".com/todos?limit=", "com/recipes?limit",
        "/products?limit=", "com/carts?limit=", ".com/carts?limit", "com/users?limit=",
        ".com/users?limit", ".com/posts?limit", "com/posts?limit=", "/comments?limit=",
        "com/quotes?limit", ".com/todos?limit", "com/todos?limit=", "/products?limit",
        "products?limit=", "com/carts?limit", "com/users?limit", "com/posts?limit",
        "/comments?limit", "comments?limit=", "com/todos?limit", "/recipes?limit=",
        "/recipes/", "?limit=5&skip=", "?limit=10&skip", "limit=10&skip=",
        "limit=20&skip=", "?limit=20&skip", "?limit=30&skip", "limit=30&skip=",
        "limit=100&skip", "?limit=5&skip", "limit=5&skip=", "limit=10&skip",
        "limit=20&skip", "limit=30&skip", "limit=5&skip", "/products/",
        "products/", ".com/products?", ".com/comments?", "com/products?",
        "com/comments?", ".com/recipes?", ".com/quotes?", "com/recipes?",
        "/users/", "users/", "/recipes", "recipes",
        "products", "?limit=100", "?limit=10", "?limit=20",
        "?limit=30", "limit=100", "com/quotes/", ".com/comments/post/",
        "com/comments/post/", ".com/comments/post", "products?limit", "comments?limit",
        "/quotes?limit=", "/recipes?limit", "recipes?limit=", "/carts?limit=",
        "/users?limit=", "/posts?limit=", "quotes?limit=", "/quotes?limit",
        "/todos?limit=", "recipes?limit", "/carts?limit", "carts?limit=",
        "/users?limit", "users?limit=", "/posts?limit", "posts?limit=",
        "quotes?limit", "/todos?limit", "todos?limit=", "dummyjson.com/products/category",
        "/quotes", "quotes", "/posts/", "posts/",
        "?limit=100&", "?limit=10&", "?limit=20&", "?limit=30&",
        "=100&skip=", "limit=100&", "?limit=5&", "limit=10&",
        "=10&skip=", "limit=20&", "=20&skip=", "=30&skip=",
        "limit=30&", "100&skip=", "=100&skip", "dummyjson.com/products/search?q=",
        "dummyjson.com/comments/search?q=", "dummyjson.com/products/category/", "dummyjson.com/products/search?q", "dummyjson.com/comments/search?q",
        "dummyjson.com/recipes/search?q=", "dummyjson.com/products/search?", "dummyjson.com/comments/search?", "dummyjson.com/quotes/search?q=",
        "dummyjson.com/recipes/search?q", ".com/carts?", ".com/users?", ".com/posts?",
        "com/quotes?", ".com/todos?", "/products?", "com/carts?",
        "com/users?", "com/posts?", "/comments?", "com/todos?",
        "products?", "comments?", "/recipes?", "com/comments/post",
        ".com/carts/user/", ".com/posts/user/", ".com/todos/user/", ".com/carts/user",
        "com/carts/user/", ".com/posts/user", "com/posts/user/", ".com/todos/user",
        "com/todos/user/", "/comments/post/", "dummyjson.com/products/search", "dummyjson.com/carts/search?q=",
        "dummyjson.com/users/search?q=", "dummyjson.com/posts/search?q=", "dummyjson.com/comments/search", "dummyjson.com/quotes/search?q",
        "dummyjson.com/todos/search?q=", "dummyjson.com/recipes/search?", "dummyjson.com/carts/search?q", "dummyjson.com/users/search?q",
        "dummyjson.com/posts/search?q", "dummyjson.com/quotes/search?", "dummyjson.com/todos/search?q", "dummyjson.com/recipes/search",
        "dummyjson.com/carts/search?", "dummyjson.com/users/search?", "dummyjson.com/posts/search?", "dummyjson.com/quotes/search",
        "dummyjson.com/todos/search?", "?select=firstName,lastName", "?select=title,description", "select=firstName,lastName",
        "select=title,description", "search?q", "/search?", "/search",
        "search?", "search", "dummyjson.com/auth/", "dummyjson.com/auth",
        "/todos/", "todos/", "/carts/", "carts/",
        "carts?limit", "users?limit", "posts?limit", "todos?limit",
        "recipes/", "dummyjson.com/carts/search", "dummyjson.com/users/search", "dummyjson.com/posts/search",
        "dummyjson.com/todos/search", "?sortBy=title&order=asc", "sortBy=title&order=asc", ".com/products/category",
        "com/products/category", "?select=title,", "?select=title", "select=title,",
        "select=title", "dummyjson.com/products?limit=100", "dummyjson.com/comments?limit=100", "dummyjson.com/products?limit=10",
        "dummyjson.com/products?limit=20", "dummyjson.com/products?limit=30", "dummyjson.com/comments?limit=10", "dummyjson.com/comments?limit=20",
        "dummyjson.com/comments?limit=30", "dummyjson.com/recipes?limit=100", "dummyjson.com/products?limit=5", "dummyjson.com/comments?limit=5",
        "dummyjson.com/quotes?limit=100", "dummyjson.com/recipes?limit=10", "dummyjson.com/recipes?limit=20", "dummyjson.com/recipes?limit=30",
        "com/carts/user", "com/posts/user", "com/todos/user", "comments/post/",
        "/comments/post", "comments/post", "/carts/user/", "/posts/user/",
        "/todos/user/", "/user/", "dummyjson.com/products?limit=100&skip=", "dummyjson.com/comments?limit=100&skip=",
        "dummyjson.com/products?limit=10&skip=", "dummyjson.com/products?limit=20&skip=", "dummyjson.com/products?limit=30&skip=", "dummyjson.com/products?limit=100&skip",
        "dummyjson.com/comments?limit=10&skip=", "dummyjson.com/comments?limit=20&skip=", "dummyjson.com/comments?limit=30&skip=", "dummyjson.com/comments?limit=100&skip",
        "dummyjson.com/recipes?limit=100&skip=", "dummyjson.com/products?limit=5&skip=", "dummyjson.com/products?limit=10&skip", "dummyjson.com/products?limit=20&skip",
        "dummyjson.com/products?limit=30&skip", "dummyjson.com/comments?limit=5&skip=", "dummyjson.com/comments?limit=10&skip", "dummyjson.com/comments?limit=20&skip",
        "dummyjson.com/comments?limit=30&skip", "dummyjson.com/quotes?limit=100&skip=", "dummyjson.com/recipes?limit=10&skip=", "dummyjson.com/recipes?limit=20&skip=",
        "dummyjson.com/recipes?limit=30&skip=", "dummyjson.com/recipes?limit=100&skip", "dummyjson.com/recipes/tag/", "dummyjson.com/recipes/tag",
        ".com/products/search?q=", ".com/comments/search?q=", ".com/products/category/", "com/products/search?q=",
        ".com/products/search?q", ".com/comments/search?q", "com/comments/search?q=", ".com/recipes/search?q=",
        "com/products/category/", ".com/products/search?", "com/products/search?q", "com/comments/search?q",
        ".com/comments/search?", ".com/quotes/search?q=", "com/recipes/search?q=", ".com/recipes/search?q",
        "dummyjson.com/products?limit=5&skip", "dummyjson.com/carts?limit=100&skip=", "dummyjson.com/users?limit=100&skip=", "dummyjson.com/posts?limit=100&skip=",
        "dummyjson.com/comments?limit=5&skip", "dummyjson.com/quotes?limit=10&skip=", "dummyjson.com/quotes?limit=20&skip=", "dummyjson.com/quotes?limit=30&skip=",
        "dummyjson.com/quotes?limit=100&skip", "dummyjson.com/todos?limit=100&skip=", "dummyjson.com/recipes?limit=5&skip=", "dummyjson.com/recipes?limit=10&skip",
        "dummyjson.com/recipes?limit=20&skip", "dummyjson.com/recipes?limit=30&skip", "dummyjson.com/carts?limit=10&skip=", "dummyjson.com/carts?limit=20&skip=",
        "dummyjson.com/carts?limit=30&skip=", "dummyjson.com/carts?limit=100&skip", "dummyjson.com/users?limit=10&skip=", "dummyjson.com/users?limit=20&skip=",
        "dummyjson.com/users?limit=30&skip=", "dummyjson.com/users?limit=100&skip", "dummyjson.com/posts?limit=10&skip=", "dummyjson.com/posts?limit=20&skip=",
        "dummyjson.com/posts?limit=30&skip=", "dummyjson.com/posts?limit=100&skip", "dummyjson.com/quotes?limit=5&skip=", "dummyjson.com/quotes?limit=10&skip",
        "dummyjson.com/quotes?limit=20&skip", "dummyjson.com/quotes?limit=30&skip", "dummyjson.com/todos?limit=10&skip=", "dummyjson.com/todos?limit=20&skip=",
        "dummyjson.com/todos?limit=30&skip=", "dummyjson.com/todos?limit=100&skip", "dummyjson.com/recipes?limit=5&skip", "dummyjson.com/products?limit=100&",
        "dummyjson.com/carts?limit=5&skip=", "dummyjson.com/carts?limit=10&skip", "dummyjson.com/carts?limit=20&skip", "dummyjson.com/carts?limit=30&skip",
        "dummyjson.com/users?limit=5&skip=", "dummyjson.com/users?limit=10&skip", "dummyjson.com/users?limit=20&skip", "dummyjson.com/users?limit=30&skip",
        "dummyjson.com/posts?limit=5&skip=", "dummyjson.com/posts?limit=10&skip", "dummyjson.com/posts?limit=20&skip", "dummyjson.com/posts?limit=30&skip",
        "dummyjson.com/comments?limit=100&", "dummyjson.com/quotes?limit=5&skip", "dummyjson.com/todos?limit=5&skip=", "dummyjson.com/todos?limit=10&skip",
        "dummyjson.com/todos?limit=20&skip", "dummyjson.com/todos?limit=30&skip", "dummyjson.com/carts?limit=100", "dummyjson.com/users?limit=100",
        "dummyjson.com/posts?limit=100", "dummyjson.com/quotes?limit=10", "dummyjson.com/quotes?limit=20", "dummyjson.com/quotes?limit=30",
        "dummyjson.com/todos?limit=100", "dummyjson.com/recipes?limit=5", "dummyjson.com/carts?limit=10", "dummyjson.com/carts?limit=20",
        "dummyjson.com/carts?limit=30", "dummyjson.com/users?limit=10", "dummyjson.com/users?limit=20", "dummyjson.com/users?limit=30",
        "dummyjson.com/posts?limit=10", "dummyjson.com/posts?limit=20", "dummyjson.com/posts?limit=30", "dummyjson.com/quotes?limit=5",
        "dummyjson.com/todos?limit=10", "dummyjson.com/todos?limit=20", "dummyjson.com/todos?limit=30", "dummyjson.com/carts?limit=5",
        "dummyjson.com/users?limit=5", "dummyjson.com/posts?limit=5", "dummyjson.com/todos?limit=5", "/search?q=Margherita",
        "?sortBy=title&order=", "search?q=Margherita", "?select=title,price", "=firstName,lastName",
        "?sortBy=title&order", "sortBy=title&order=", "?limit=100&skip=10", "?limit=100&skip=20",
        "?limit=100&skip=50", "select=title,price", "=title,description", "?select=firstName,",
        "firstName,lastName", "sortBy=title&order", "/products/category", "?limit=5",
        "&skip=10", "&skip=20", "&skip=50", "limit=10",
        "limit=20", "limit=30", "limit=5", "&skip=0",
        "skip=10", "skip=20", "skip=50", "skip=0",
        "/quotes/", "quotes/", "dummyjson.com/products?limit=10&", "dummyjson.com/products?limit=20&",
        "dummyjson.com/products?limit=30&", "dummyjson.com/carts?limit=5&skip", "dummyjson.com/users?limit=5&skip", "dummyjson.com/posts?limit=5&skip",
        "dummyjson.com/comments?limit=10&", "dummyjson.com/comments?limit=20&", "dummyjson.com/comments?limit=30&", "dummyjson.com/todos?limit=5&skip",
        "dummyjson.com/recipes?limit=100&", "dummyjson.com/recipes/meal-type/", "dummyjson.com/products?limit=5&", "dummyjson.com/comments?limit=5&",
        "dummyjson.com/quotes?limit=100&", "dummyjson.com/recipes?limit=10&", "dummyjson.com/recipes?limit=20&", "dummyjson.com/recipes?limit=30&",
        "dummyjson.com/recipes/meal-type", "dummyjson.com/products?select=", "dummyjson.com/carts?limit=100&", "dummyjson.com/users?limit=100&",
        "dummyjson.com/posts?limit=100&", "dummyjson.com/comments?select=", "dummyjson.com/quotes?limit=10&", "dummyjson.com/quotes?limit=20&",
        "dummyjson.com/quotes?limit=30&", "dummyjson.com/todos?limit=100&", "dummyjson.com/recipes?limit=5&", "com/products/search?",
        ".com/products/search", ".com/carts/search?q=", ".com/users/search?q=", ".com/posts/search?q=",
        "com/comments/search?", ".com/comments/search", "com/quotes/search?q=", ".com/quotes/search?q",
        ".com/todos/search?q=", ".com/recipes/search?", "com/recipes/search?q", "com/products/search",
        "/products/search?q=", "com/carts/search?q=", ".com/carts/search?q", ".com/users/search?q",
        "com/users/search?q=", ".com/posts/search?q", "com/posts/search?q=", "com/comments/search",
        "/comments/search?q=", "com/quotes/search?q", ".com/quotes/search?", ".com/todos/search?q",
        "com/todos/search?q=", "com/recipes/search?", ".com/recipes/search", "/products/category/",
        "/products/search?q", "products/search?q=", "com/carts/search?q", ".com/carts/search?",
        "com/users/search?q", ".com/users/search?", ".com/posts/search?", "com/posts/search?q",
        "/comments/search?q", "comments/search?q=", ".com/quotes/search", "com/quotes/search?",
        "com/todos/search?q", ".com/todos/search?", "com/recipes/search", "/recipes/search?q=",
        "products/category/", ".com/products?limit=100&skip=", "dummyjson.com/products?select", "dummyjson.com/carts?limit=10&",
        "dummyjson.com/carts?limit=20&", "dummyjson.com/carts?limit=30&", "dummyjson.com/users?limit=10&", "dummyjson.com/users?limit=20&",
        "dummyjson.com/users?limit=30&", "dummyjson.com/posts?limit=10&", "dummyjson.com/posts?limit=20&", "dummyjson.com/posts?limit=30&",
        ".com/comments?limit=100&skip=", "dummyjson.com/comments?select", "dummyjson.com/quotes?limit=5&", "dummyjson.com/todos?limit=10&",
        "dummyjson.com/todos?limit=20&", "dummyjson.com/todos?limit=30&", "dummyjson.com/recipes?select=", ".com/products?limit=10&skip=",
        ".com/products?limit=20&skip=", ".com/products?limit=30&skip=", "com/products?limit=100&skip=", ".com/products?limit=100&skip",
        "dummyjson.com/carts?limit=5&", "dummyjson.com/users?limit=5&", "dummyjson.com/posts?limit=5&", ".com/comments?limit=10&skip=",
        ".com/comments?limit=20&skip=", ".com/comments?limit=30&skip=", ".com/comments?limit=100&skip", "com/comments?limit=100&skip=",
        "dummyjson.com/quotes?select=", "dummyjson.com/todos?limit=5&", ".com/recipes?limit=100&skip=", "dummyjson.com/recipes?select",
        ".com/products?limit=5&skip=", ".com/products?limit=10&skip", "com/products?limit=10&skip=", "com/products?limit=20&skip=",
        ".com/products?limit=20&skip", ".com/products?limit=30&skip", "com/products?limit=30&skip=", "com/products?limit=100&skip",
        "dummyjson.com/carts?select=", "dummyjson.com/users?select=", "dummyjson.com/posts?select=", ".com/comments?limit=5&skip=",
        ".com/comments?limit=10&skip", "com/comments?limit=10&skip=", ".com/comments?limit=20&skip", "com/comments?limit=20&skip=",
        ".com/comments?limit=30&skip", "com/comments?limit=30&skip=", "com/comments?limit=100&skip", ".com/quotes?limit=100&skip=",
        "dummyjson.com/quotes?select", "dummyjson.com/todos?select=", ".com/recipes?limit=10&skip=", ".com/recipes?limit=20&skip=",
        ".com/recipes?limit=30&skip=", "com/recipes?limit=100&skip=", ".com/recipes?limit=100&skip", "dummyjson.com/recipes/meal-",
        "?limit=10&skip=10", "?limit=10&skip=20", "?limit=10&skip=50", "?limit=20&skip=10",
        "?limit=20&skip=20", "?limit=20&skip=50", "?limit=30&skip=10", "?limit=30&skip=20",
        "?limit=30&skip=50", "?limit=100&skip=0", "limit=100&skip=10", "limit=100&skip=20",
        "limit=100&skip=50", "title,description", "?select=firstName", "select=firstName,",
        "products/category", "/search?q=laptop", "?limit=5&skip=10", "?limit=5&skip=20",
        "?limit=5&skip=50", "?limit=10&skip=0", "limit=10&skip=10", "limit=10&skip=20",
        "limit=10&skip=50", "?limit=20&skip=0", "limit=20&skip=10", "limit=20&skip=20",
        "limit=20&skip=50", "?limit=30&skip=0", "limit=30&skip=10", "limit=30&skip=20",
        "limit=30&skip=50", "limit=100&skip=0", "?select=id,title", "select=firstName",
        "=title&order=asc", "/search?q=phone", "search?q=laptop", "/search?q=cream",
        "/search?q=shirt", "?limit=5&skip=0", "limit=5&skip=10", "limit=5&skip=20",
        "limit=5&skip=50", "limit=10&skip=0", "limit=20&skip=0", "limit=30&skip=0",
        "select=id,title", "title&order=asc", "limit=5&", "=5&skip=",
        "=10&skip", "10&skip=", "=20&skip", "20&skip=",
        "=30&skip", "30&skip=", "100&skip", "?select=",
        "5&skip=", "=5&skip", "10&skip", "20&skip",
        "30&skip", "select=", "?select", "5&skip",
        "select", "/quotes?", "recipes?", "/carts?",
        "/users?", "/posts?", "quotes?", "/todos?",
        "carts?", "users?", "posts?", "todos?",
        ".com/products?limit=100", ".com/comments?limit=100", ".com/products?limit=10", ".com/products?limit=20",
        ".com/products?limit=30", "com/products?limit=100", ".com/comments?limit=10", ".com/comments?limit=20",
        ".com/comments?limit=30", "com/comments?limit=100", ".com/recipes?limit=100", ".com/products?limit=5",
        "com/products?limit=10", "com/products?limit=20", "com/products?limit=30", ".com/comments?limit=5",
        "com/comments?limit=10", "com/comments?limit=20", "com/comments?limit=30", ".com/quotes?limit=100",
        ".com/recipes?limit=10", ".com/recipes?limit=20", ".com/recipes?limit=30", "com/recipes?limit=100",
        "/carts/user", "carts/user/", "/posts/user", "posts/user/",
        "/todos/user", "todos/user/", "carts/user", "posts/user",
        "todos/user", "com/products?limit=5&skip=", ".com/products?limit=5&skip", "com/products?limit=10&skip",
        "com/products?limit=20&skip", "com/products?limit=30&skip", ".com/carts?limit=100&skip=", "dummyjson.com/carts?select",
        ".com/users?limit=100&skip=", "dummyjson.com/users?select", ".com/posts?limit=100&skip=", "dummyjson.com/posts?select",
        "com/comments?limit=5&skip=", ".com/comments?limit=5&skip", "com/comments?limit=10&skip", "com/comments?limit=20&skip",
        "com/comments?limit=30&skip", ".com/quotes?limit=10&skip=", ".com/quotes?limit=20&skip=", ".com/quotes?limit=30&skip=",
        ".com/quotes?limit=100&skip", "com/quotes?limit=100&skip=", ".com/todos?limit=100&skip=", "dummyjson.com/todos?select",
        ".com/recipes?limit=5&skip=", ".com/recipes?limit=10&skip", "com/recipes?limit=10&skip=", ".com/recipes?limit=20&skip",
        "com/recipes?limit=20&skip=", "com/recipes?limit=30&skip=", ".com/recipes?limit=30&skip", "com/recipes?limit=100&skip",
        "dummyjson.com/recipes/meal", "com/products?limit=5&skip", "/products?limit=100&skip=", ".com/carts?limit=10&skip=",
        ".com/carts?limit=20&skip=", ".com/carts?limit=30&skip=", ".com/carts?limit=100&skip", "com/carts?limit=100&skip=",
        ".com/users?limit=10&skip=", ".com/users?limit=20&skip=", ".com/users?limit=30&skip=", "com/users?limit=100&skip=",
        ".com/users?limit=100&skip", ".com/posts?limit=10&skip=", ".com/posts?limit=20&skip=", ".com/posts?limit=30&skip=",
        "com/posts?limit=100&skip=", ".com/posts?limit=100&skip", "com/comments?limit=5&skip", "/comments?limit=100&skip=",
        ".com/quotes?limit=5&skip=", "com/quotes?limit=10&skip=", ".com/quotes?limit=10&skip", "com/quotes?limit=20&skip=",
        ".com/quotes?limit=20&skip", ".com/quotes?limit=30&skip", "com/quotes?limit=30&skip=", "com/quotes?limit=100&skip",
        ".com/todos?limit=10&skip=", ".com/todos?limit=20&skip=", ".com/todos?limit=30&skip=", ".com/todos?limit=100&skip",
        "com/todos?limit=100&skip=", "com/recipes?limit=5&skip=", ".com/recipes?limit=5&skip", "com/recipes?limit=10&skip",
        "com/recipes?limit=20&skip", "com/recipes?limit=30&skip", "/products?limit=10&skip=", "/products?limit=20&skip=",
        "/products?limit=30&skip=", "/products?limit=100&skip", ".com/products?limit=100&", "products?limit=100&skip=",
        ".com/carts?limit=5&skip=", "com/carts?limit=10&skip=", ".com/carts?limit=10&skip", ".com/carts?limit=20&skip",
        "com/carts?limit=20&skip=", "com/carts?limit=30&skip=", ".com/carts?limit=30&skip", "com/carts?limit=100&skip",
        ".com/users?limit=5&skip=", "com/users?limit=10&skip=", ".com/users?limit=10&skip", "com/users?limit=20&skip=",
        ".com/users?limit=20&skip", "com/users?limit=30&skip=", ".com/users?limit=30&skip", "com/users?limit=100&skip",
        ".com/posts?limit=5&skip=", "com/posts?limit=10&skip=", ".com/posts?limit=10&skip", "com/posts?limit=20&skip=",
        ".com/posts?limit=20&skip", "com/posts?limit=30&skip=", ".com/posts?limit=30&skip", "com/posts?limit=100&skip",
        "/comments?limit=10&skip=", "/comments?limit=20&skip=", "/comments?limit=30&skip=", "comments?limit=100&skip=",
        "/comments?limit=100&skip", ".com/comments?limit=100&", "com/quotes?limit=5&skip=", ".com/quotes?limit=5&skip",
        "com/quotes?limit=10&skip", "com/quotes?limit=20&skip", "com/quotes?limit=30&skip", ".com/todos?limit=5&skip=",
        ".com/todos?limit=10&skip", "com/todos?limit=10&skip=", ".com/todos?limit=20&skip", "com/todos?limit=20&skip=",
        ".com/todos?limit=30&skip", "com/todos?limit=30&skip=", "com/todos?limit=100&skip", "com/recipes?limit=5&skip",
        "/recipes?limit=100&skip=", "/products/search?", "products/search?q", ".com/carts/search",
        "com/carts/search?", "com/users/search?", ".com/users/search", ".com/posts/search",
        "com/posts/search?", "/comments/search?", "comments/search?q", "/quotes/search?q=",
        "com/quotes/search", ".com/todos/search", "com/todos/search?", "/recipes/search?q",
        "recipes/search?q=", "/products/search", "products/search?", "/carts/search?q=",
        "com/carts/search", "/users/search?q=", "com/users/search", "com/posts/search",
        "/posts/search?q=", "/comments/search", "comments/search?", "/quotes/search?q",
        "quotes/search?q=", "/todos/search?q=", "com/todos/search", "recipes/search?q",
        "/recipes/search?", "products/search", "/carts/search?q", "carts/search?q=",
        "users/search?q=", "/users/search?q", "/posts/search?q", "posts/search?q=",
        "comments/search", "/quotes/search?", "quotes/search?q", "/todos/search?q",
        "todos/search?q=", "/recipes/search", "recipes/search?", "com/products?limit=5",
        ".com/carts?limit=100", ".com/users?limit=100", ".com/posts?limit=100", "com/comments?limit=5",
        ".com/quotes?limit=10", ".com/quotes?limit=20", ".com/quotes?limit=30", "com/quotes?limit=100",
        ".com/todos?limit=100", ".com/recipes?limit=5", "com/recipes?limit=10", "com/recipes?limit=20",
        "com/recipes?limit=30", "/products?limit=100", ".com/carts?limit=10", ".com/carts?limit=20",
        ".com/carts?limit=30", "com/carts?limit=100", ".com/users?limit=10", ".com/users?limit=20",
        ".com/users?limit=30", "com/users?limit=100", ".com/posts?limit=10", ".com/posts?limit=20",
        ".com/posts?limit=30", "com/posts?limit=100", "/comments?limit=100", ".com/quotes?limit=5",
        "com/quotes?limit=10", "com/quotes?limit=20", "com/quotes?limit=30", ".com/todos?limit=10",
        ".com/todos?limit=20", ".com/todos?limit=30", "com/todos?limit=100", "com/recipes?limit=5",
        "/products?limit=10", "/products?limit=20", "/products?limit=30", "products?limit=100",
        ".com/carts?limit=5", "com/carts?limit=10", "com/carts?limit=20", "com/carts?limit=30",
        ".com/users?limit=5", "com/users?limit=10", "com/users?limit=20", "com/users?limit=30",
        ".com/posts?limit=5", "com/posts?limit=10", "com/posts?limit=20", "com/posts?limit=30",
        "/comments?limit=10", "/comments?limit=20", "/comments?limit=30", "comments?limit=100",
        "com/quotes?limit=5", ".com/todos?limit=5", "com/todos?limit=10", "com/todos?limit=20",
        "com/todos?limit=30", "/recipes?limit=100", "/products?limit=5&skip=", "/products?limit=10&skip",
        ".com/products?limit=10&", "products?limit=10&skip=", ".com/products?limit=20&", "products?limit=20&skip=",
        "/products?limit=20&skip", "/products?limit=30&skip", "products?limit=30&skip=", ".com/products?limit=30&",
        "com/products?limit=100&", "products?limit=100&skip", ".com/carts?limit=5&skip", "com/carts?limit=5&skip=",
        "com/carts?limit=10&skip", "com/carts?limit=20&skip", "com/carts?limit=30&skip", "com/users?limit=5&skip=",
        ".com/users?limit=5&skip", "com/users?limit=10&skip", "com/users?limit=20&skip", "com/users?limit=30&skip",
        ".com/posts?limit=5&skip", "com/posts?limit=5&skip=", "com/posts?limit=10&skip", "com/posts?limit=20&skip",
        "com/posts?limit=30&skip", "/comments?limit=5&skip=", ".com/comments?limit=10&", "comments?limit=10&skip=",
        "/comments?limit=10&skip", "comments?limit=20&skip=", ".com/comments?limit=20&", "/comments?limit=20&skip",
        ".com/comments?limit=30&", "/comments?limit=30&skip", "comments?limit=30&skip=", "comments?limit=100&skip",
        "com/comments?limit=100&", "com/quotes?limit=5&skip", "/quotes?limit=100&skip=", "com/todos?limit=5&skip=",
        ".com/todos?limit=5&skip", "com/todos?limit=10&skip", "com/todos?limit=20&skip", "com/todos?limit=30&skip",
        "/recipes?limit=10&skip=", "/recipes?limit=20&skip=", "/recipes?limit=30&skip=", "/recipes?limit=100&skip",
        ".com/recipes?limit=100&", "recipes?limit=100&skip=", ".com/recipes/meal-type/", "/products?limit=5&skip",
        ".com/products?limit=5&", "products?limit=5&skip=", "com/products?limit=10&", "products?limit=10&skip"
        ];
    }
}
