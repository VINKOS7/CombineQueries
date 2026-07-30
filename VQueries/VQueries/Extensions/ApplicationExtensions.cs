using CombineQueries.Api.Services.AFST;
using CombineQueries.Api.Services.Forwarder;

namespace CombineQueries.Api.Extensions;

public static class ApplicationExtensions
{
    public static IServiceCollection ConfigureApplicationServices(this IServiceCollection services, IConfiguration configuration)
    {
        // Было AddScoped<HttpClient> - новый клиент на каждый HTTP-запрос. Ломается не DIP, а сокеты:
        // каждый инстанс держит СВОЙ пул соединений, при Dispose они уходят в TIME_WAIT на ~2 минуты
        // и порты кончаются раньше, чем освобождаются (socket exhaustion).
        //
        // Шарить надо HttpMessageHandler - пулом соединений владеет он, а не HttpClient. Поэтому не
        // синглтон, а AddHttpClient: фабрика держит handler общим и РОТИРУЕТ его раз в 2 минуты.
        // Вечный синглтон ротации не делает и залипает на первом DNS-ответе намертво - для
        // форвардера по произвольным чужим доменам это реальная беда, а не теоретическая.
        services.AddHttpClient<IForwarder, Forwarder>(client =>
        {
            // Дефолт 100 секунд: мёртвый целевой хост держал бы наш запрос всё это время,
            // а Udon отвалится по своему таймауту сильно раньше и будет ждать впустую.
            client.Timeout = TimeSpan.FromSeconds(15);

            // Тело чужого ответа читается в строку целиком - без потолка одна жирная ссылка
            // сажает процесс по памяти. 1 МБ с запасом: Udon столько всё равно не проглотит.
            client.MaxResponseContentBufferSize = 1024 * 1024;
        });

        // ТОЛЬКО Singleton. AFST держит состояние МЕЖДУ запросами (алфавит, дерево, буфер склейки),
        // а Scoped создаёт новый экземпляр на каждый HTTP-запрос - тогда /init ставит контекст и он
        // тут же теряется, и первый же /m/ падает с "CRIT: /init не вызван".
        services.AddSingleton<ISpeech, AFST>();

        return services;
    }
}