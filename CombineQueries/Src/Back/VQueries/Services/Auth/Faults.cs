namespace CombineQueries.Api.Services.Auth;

// Переводит доменные отказы в коды HTTP. Контроллеры тонкие, а хендлеры бросают - без этого любой
// отказ авторизации уезжал клиенту как 500, то есть «сервер сломался» вместо «тебя не пустили».
//
// Различаем по префиксу сообщения, который уже принят в хендлерах:
//   auth error:   -> 403, ключ не подошёл или управление выключено
//   domain error: -> 400, запрос сам по себе неверный
public class Faults
{
    private readonly RequestDelegate _next;
    private readonly ILogger<Faults> _logger;

    public Faults(RequestDelegate next, ILogger<Faults> logger)
    {
        _next = next;
        _logger = logger;
    }

    public async Task Invoke(HttpContext context)
    {
        try
        {
            await _next(context);
        }
        catch (Exception ex) when (Code(ex.Message) is int code)
        {
            _logger.LogWarning("fault: {Path} -> {Code} ({Message})", context.Request.Path.Value, code, ex.Message);

            // Заголовки уже ушли - менять статус поздно, остаётся только оборвать.
            if (context.Response.HasStarted) throw;

            context.Response.StatusCode = code;

            await context.Response.WriteAsync(ex.Message);
        }
    }

    private static int? Code(string message) => true switch
    {
        _ when message.StartsWith("auth error:") => StatusCodes.Status403Forbidden,
        _ when message.StartsWith("domain error:") => StatusCodes.Status400BadRequest,
        _ => null
    };
}
