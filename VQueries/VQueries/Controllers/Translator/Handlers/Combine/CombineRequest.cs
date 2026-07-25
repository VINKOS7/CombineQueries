using MediatR;
using Newtonsoft.Json;

namespace CombineQueries.Api.Controllers.Translator.Handlers.Combine;

public record CombineRequest : IRequest<CombineResponse>
{
    // сырые wire-руны одного куска (WireSize разрядов), без разбора query на пары ключ-значение
    [JsonProperty("runes")] public string Runes { get; set; }
}
