using Newtonsoft.Json;

namespace CombineQueries.Api.Controllers.Accounts.Handlers;

// Запросы управления аккаунтами: раздают и отзывают КЛЮЧИ ВХОДА, поэтому требуют мастер-токен.
// Держим его в самом запросе, а не в контроллере: сверка это решение, а не транспорт.
public interface IMasterOnly
{
    string? Master { get; set; }
}

public static class MasterOnly
{
    // Мастер - это Auth:Token из конфигурации. Не задан - управление выключено целиком: раздатчик
    // токенов, открытый по умолчанию, это дыра, а не удобство.
    public static void Ensure(IMasterOnly request, IConfiguration configuration)
    {
        string configured = configuration["Auth:Token"] ?? "";

        if (configured.Length == 0) throw new Exception("auth error: account management is disabled (Auth:Token is not set)");

        if (request.Master != configured) throw new Exception("auth error: master token rejected");
    }
}
