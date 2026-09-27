using System.Data.Common;

using MediatR;
using Microsoft.EntityFrameworkCore;

using CombineQueries.Api.Services.Speech;
using CombineQueries.Domain.Aggregates.Translator;

namespace CombineQueries.Api.Controllers.Translators.Handlers.Reset;

// Забывает хайперы - и в рантайме, и в персисте. Нужен для повторяемых прогонов теста: собранный
// один раз url дальше уходит одним запросом /h/, и второй прогон уже ничего не измеряет.
//
// ТОЛЬКО Development: на релизе это стирало бы накопленное живым миром.
public class ResetHandler(ITranslatorRepo translatorRepo, ISpeech speech, IWebHostEnvironment environment, ILogger<ResetHandler> logger)
    : IRequestHandler<ResetRequest, ResetResponse>
{
    public async Task<ResetResponse> Handle(ResetRequest request, CancellationToken cancellationToken)
    {
        if (!environment.IsDevelopment()) throw new Exception("domain error: hyper reset is development-only");

        int forgotten = speech.HyperUrls.Count;

        speech.ForgetHypers();

        try
        {
            if (speech.Alphabet is not null)
            {
                var translator = await translatorRepo.GetByAlphabetAsync(speech.Alphabet);

                if (translator is not null && translator.Hypers.Count > 0)
                {
                    // Чистим и в БД, иначе они вернутся тёплыми на ближайшем connect.
                    translator.Hypers.Clear();

                    await translatorRepo.UnitOfWork.SaveEntitiesAsync(cancellationToken);
                }
            }
        }
        catch (Exception ex) when (ex is DbException or DbUpdateException)
        {
            logger.LogWarning("reset: persistence unavailable, memory only ({Kind}: {Message})", ex.GetType().Name, ex.Message);
        }

        logger.LogInformation("reset: {Forgotten} hypers forgotten", forgotten);

        return new ResetResponse { Forgotten = forgotten };
    }
}
