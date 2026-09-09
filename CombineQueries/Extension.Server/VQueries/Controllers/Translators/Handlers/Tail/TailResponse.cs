using Newtonsoft.Json;

using CombineQueries.Api.Services.Outbox;

using CombineQueries.Api.Services.Speech;

namespace CombineQueries.Api.Controllers.Translators.Handlers.Tail;

public record TailResponse
{
    [JsonProperty("runes")] public int Runes { get; set; }
    [JsonProperty("forwardedUrl")] public string? ForwardedUrl { get; set; }
    // Тело больше не едет здесь: наружу сервер ходит в фон, а результат приезжает ДОЛГОМ.
    // Успел к этому ответу - лежит в ready, не успел - придёт со следующим запросом.
    [JsonProperty("ready")] public IReadOnlyList<Delivery>? Ready { get; set; }

    // Сколько адресов ещё в полёте: значит будет и следующий довесок.
    [JsonProperty("pending")] public int Pending { get; set; }
    [JsonProperty("handle")] public int Handle { get; set; } = -1;
    // Покрытие: chunks - куски, ушедшие рунами (фрагмент не нашёлся), остальное - по уровням.
    // Partial это как раз chunks > 0.
    [JsonProperty("chunks")] public int Chunks { get; set; }
    [JsonProperty("l2")] public int L2 { get; set; }
    [JsonProperty("l3")] public int L3 { get; set; }
    [JsonProperty("infinite")] public int Infinite { get; set; }

    // На что прыгать в следующий раз: leaf - весь этот адрес, prefix - его общее начало с уже
    // известными (shared - длина этого начала в кусках). -1 значит «прыгать некуда».
    [JsonProperty("leaf")] public int Leaf { get; set; } = -1;
    [JsonProperty("prefix")] public int Prefix { get; set; } = -1;
    [JsonProperty("shared")] public int Shared { get; set; }

    // Хайпер-дерево: сколько цепочек знает сервер и во сколько узлов они уложились.
    [JsonProperty("chains")] public int Chains { get; set; }
    [JsonProperty("nodes")] public int Nodes { get; set; }

    [JsonProperty("assemblyMs")] public long AssemblyMs { get; set; }
    [JsonProperty("forwardMs")] public long ForwardMs { get; set; }

    // Новые адресуемые фрагменты, выученные из ЭТОГО URL. Клиент кладёт их в свой словарь
    // и со следующего раза зовёт /f/<id> вместо рун. Пусто, если ничего не доросло.
    [JsonProperty("fragments")] public IReadOnlyList<FragmentSeed>? Fragments { get; set; }
}
