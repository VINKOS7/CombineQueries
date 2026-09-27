using System.Data.Common;

using MediatR;
using Microsoft.EntityFrameworkCore;

using CombineQueries.Domain.Aggregates.Translator;
using CombineQueries.Domain.Aggregates.Account;

namespace CombineQueries.Api.Controllers.Translators.Handlers.Connect;

// Обработчик доменного события подключения. Единственный репозиторий — ITranslatorRepo
// (правило «один хендлер — один репо»): здесь и только здесь инкапсулирована работа с хранилищем
// транслятора, ConnectHandler о нём не знает вовсе.
//
// Уведомление результата не возвращает, поэтому добытый агрегат кладём в само событие - оттуда его
// заберёт тот, кто событие поднял. Персист по возможности: протокол работает в памяти, его отказ
// не валит connect.
public class ConnectedHandler(ITranslatorRepo translatorRepo, ILogger<ConnectedHandler> logger)
    : INotificationHandler<AccountConnected>
{
    public async Task Handle(AccountConnected notification, CancellationToken cancellationToken)
    {
        try
        {
            var translator = await translatorRepo.GetByAlphabetAsync(notification.Alphabet);

            if (translator is null)
            {
                translator = Domain.Aggregates.Translator.Translator.From(new InitCommand<char>
                {
                    Runes = Domain.Aggregates.Translator.Translator.ATRFrom(notification.Alphabet),
                    BaseForwardUrl = notification.BaseForwardUrl,
                    Alphabet = notification.Alphabet
                });

                await translatorRepo.AddAsync(translator);
                await translatorRepo.UnitOfWork.SaveChangesAsync(cancellationToken);

                logger.LogInformation("connect: new Translator persisted, ID={Id}", translator.Id);
            }

            notification.Translator = translator;

            logger.LogInformation("connect: Translator {Id} ready, {Fragments} fragments, {Hypers} hypers",
                translator.Id, translator.VirtualFragments.Count, translator.Hypers.Count);
        }
        // Только отказ БД. Ошибки кода обязаны падать, а не притворяться «базы нет».
        catch (Exception ex) when (ex is DbException or DbUpdateException)
        {
            logger.LogWarning("connect: persistence unavailable, memory only ({Kind}: {Message})", ex.GetType().Name, ex.Message);
        }
    }
}
