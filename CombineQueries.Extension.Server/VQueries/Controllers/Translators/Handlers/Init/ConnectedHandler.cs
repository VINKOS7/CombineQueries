using MediatR;

using CombineQueries.Domain.Aggregates.Translator;
using CombineQueries.Domain.Aggregates.Account;

namespace CombineQueries.Api.Controllers.Translators.Handlers.Init;

// Обработчик доменного события connect. Единственный репозиторий — ITranslatorRepo
// (правило «один хендлер — один репо»). Обеспечивает Translator под алфавит. Best-effort:
// протокол работает в памяти, поэтому отказ персиста не валит connect.
public class ConnectedHandler(ITranslatorRepo translatorRepo, ILogger<ConnectedHandler> logger)
    : INotificationHandler<AccountConnected>
{
    public async Task Handle(AccountConnected notification, CancellationToken cancellationToken)
    {
        try
        {
            if (await translatorRepo.GetIdByAlphabetAsync(notification.Alphabet) != Guid.Empty) return;

            var translator = Domain.Aggregates.Translator.Translator.From(new InitCommand<char>
            {
                Runes = Domain.Aggregates.Translator.Translator.ATRFrom(notification.Alphabet),
                BaseForwardUrl = notification.BaseForwardUrl,
                Alphabet = notification.Alphabet
            });

            await translatorRepo.AddAsync(translator);
            await translatorRepo.UnitOfWork.SaveChangesAsync(cancellationToken);

            logger.LogInformation($"connect: new Translator persisted, ID={translator.Id}");
        }
        catch (Exception ex)
        {
            logger.LogWarning($"connect: persistence unavailable, memory only ({ex.GetType().Name}: {ex.Message})");
        }
    }
}
