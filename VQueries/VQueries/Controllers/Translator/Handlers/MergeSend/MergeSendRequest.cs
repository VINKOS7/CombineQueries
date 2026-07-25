using MediatR;
using Newtonsoft.Json;

namespace CombineQueries.Api.Controllers.Translator.Handlers.MergeSend;

// Каким кодеком закодирован ВЕСЬ накопленный буфер. Решает клиент (он единственный видит и текст,
// и своё дерево, поэтому может честно померить оба варианта и выбрать плотнейший), сервер только
// подчиняется - так режимы гарантированно не разъезжаются.
public enum CombineMode
{
    Simple = 0, // 1-кратное сжатие: пара рун = один id = пара исходных символов
    Tree = 1    // дерево: wireValue = nodeId*(L+1)+symbolIndex, один запрос = целый выученный кусок
}

public record MergeSendRequest : IRequest<MergeSendResponse>
{
    [JsonProperty("runes")] public string Runes { get; set; }

    [JsonProperty("mode")] public CombineMode Mode { get; set; }
}
