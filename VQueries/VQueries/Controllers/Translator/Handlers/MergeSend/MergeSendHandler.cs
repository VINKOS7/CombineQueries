using MediatR;

using CombineQueries.Api.Services.AFST;
using CombineQueries.Domain.Aggregates.Translator;

namespace CombineQueries.Api.Controllers.Translator.Handlers.MergeSend;

// MERGE+SEND = слепить последний кусок, разжать всё склеенное и реально форварднуть.
// Два кодека - какой именно, говорит клиент через Mode (см. CombineMode).
public class MergeSendHandler : IRequestHandler<MergeSendRequest, MergeSendResponse>
{
    private readonly ILogger<MergeSendHandler> _logger;
    private readonly IAFST _alphabetFST;
    private readonly HttpClient _httpClient;

    public MergeSendHandler(ILogger<MergeSendHandler> logger, HttpClient client, IAFST alphabetFST)
    {
        _logger = logger;
        _httpClient = client;
        _alphabetFST = alphabetFST;
    }

    public async Task<MergeSendResponse> Handle(MergeSendRequest request, CancellationToken cancellationToken)
    {
        try
        {
            if (_alphabetFST.Alphabet is null || _alphabetFST.ArenaTreeContext is null) throw new Exception("CRIT: /init не вызван");

            string alphabet = _alphabetFST.Alphabet;
            var tree = _alphabetFST.ArenaTreeContext;

            // последний кусок тоже слепляем - он часть сообщения, а не только сигнал "отправляй"
            _alphabetFST.CombineRunes.Add(request.Runes);

            int count = _alphabetFST.CombineRunes.Count;
            string forwardUrl = Decode(request.Mode, alphabet, tree);

            _logger.LogInformation($"mergesend[{request.Mode}]: кусков={count}, собран URL='{forwardUrl}'");

            _alphabetFST.CombineRunes.Clear();
            _alphabetFST.UnrunedCombine.Clear();

            var httpResponse = await _httpClient.GetAsync(forwardUrl, cancellationToken);
            string body = await httpResponse.Content.ReadAsStringAsync(cancellationToken);

            if (httpResponse.IsSuccessStatusCode) _logger.LogInformation("mergesend: форвардинг успешен");
            else _logger.LogWarning($"mergesend: целевой ресурс ответил {httpResponse.StatusCode}");

            return new MergeSendResponse { ForwardedUrl = forwardUrl, Response = body };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex.ToString());

            // чистим и на ошибке - иначе следующий мердж склеится с мусором от упавшего
            _alphabetFST.CombineRunes.Clear();

            throw;
        }
    }

    private string Decode(CombineMode mode, string alphabet, Domain.Aggregates.Translator.types.IArenaTreeRunes<char> tree)
    {
        if (mode == CombineMode.Tree)
        {
            // каждый кусок - отдельный wireValue, декодятся по одному; дерево растёт как побочный
            // эффект самого декода, поэтому доучивать его отдельно тут НЕ НАДО (иначе разъедется)
            return Domain.Aggregates.Translator.Translator.UnrunedCombineMany(alphabet, tree, _alphabetFST.CombineRunes);
        }

        // Простой режим - тождество: значение чанка это его же разряды base-alphabet, и запись
        // значения обратно теми же разрядами возвращает исходные символы. Поэтому склейка рун УЖЕ
        // есть исходный текст, гонять его через Rune/Derune не нужно.
        // Раньше тут стояла эта пара, и она ломала любую НЕЧЁТНУЮ ширину руны (runeSize=3):
        // Rune делит длину на 2 и при нечётной возвращает пустой массив -> пустой URL.
        string text = string.Concat(_alphabetFST.CombineRunes);

        // В простом режиме дерево ничем не кормится - учим его вручную на расшифрованном тексте.
        // Клиент обязан сделать РОВНО ТО ЖЕ на своём дереве, иначе tree-режим потом развалится.
        Domain.Aggregates.Translator.Translator.Learn(text, tree);

        return text;
    }
}
