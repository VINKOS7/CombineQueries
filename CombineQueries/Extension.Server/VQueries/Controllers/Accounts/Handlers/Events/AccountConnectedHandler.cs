using CombineQueries.Api.Controllers.Accounts.Handlers.Code;
using CombineQueries.Api.Services.Persist;
using CombineQueries.Api.Services.Speech;
using CombineQueries.Domain.Aggregates.Account.Events;
using CombineQueries.Domain.Aggregates.Translator;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Data.Common;

namespace CombineQueries.Api.Controllers.Accounts.Handlers.Events;

// Вторая половина connect: всё, что касается ЧУЖОГО агрегата - Translator.
//
// Раньше это лежало в ConnectHandler, и тот держал сразу два репозитория. Теперь связь идёт
// доменным событием: аккаунт подключился -> кто-то обеспечивает под него транслятор. Событие
// синхронное (SaveEntitiesAsync дожидается обработчиков), поэтому connect продолжает работу уже
// с прогретым словарём и отвечает клиенту тем, что здесь наработано.
//
// Репозиторий берём из провайдера, а не из конструктора: обработчик доменного события не должен
// тянуть за собой инфраструктуру чужого агрегата как собственную зависимость.
public class AccountConnectedHandler(IServiceProvider provider, ISpeech speech, IConfiguration configuration, IWebHostEnvironment environment, ILogger<AccountConnectedHandler> logger)
    : INotificationHandler<AccountConnected>
{
    public async Task Handle(AccountConnected notification, CancellationToken cancellationToken)
    {
        // Аргументов у события нет: алфавит и базовый адрес уже стоят в контексте - их выставил
        // connect до того, как аккаунт поднял событие.
        if (speech.Alphabet is null) return;

        var repo = provider.GetRequiredService<ITranslatorRepo>();

        var translator = await TranslatorOf(repo, cancellationToken);

        // Сброс идёт ДО заливки: иначе Warm тут же вернул бы забытое обратно из персиста.
        await ForgetHypers(repo, translator, cancellationToken);

        Warm(translator);

        // Прогрев идёт последним: Warm поднимает дерево из БД через Forget, и нагретое до него
        // просто исчезло бы. Греем один раз на мастера - отметку снимает его авторизация.
        int warmed = speech.Preheat();

        if (warmed > 0) logger.LogInformation("connect: preheated {Warmed} hypers from urlRequests", warmed);
    }

    private async Task<Translator?> TranslatorOf(ITranslatorRepo repo, CancellationToken cancellationToken)
    {
        try
        {
            var translator = await repo.GetByAlphabetAsync(speech.Alphabet!);

            if (translator is not null) return translator;

            translator = Translator.For(speech.Alphabet!, speech.BaseForwardUrl);

            await repo.AddAsync(translator);
            await repo.UnitOfWork.SaveChangesAsync(cancellationToken);

            logger.LogInformation("connect: new Translator persisted, ID={Id}", translator.Id);

            return translator;
        }
        // Только отказ БД. Ошибки кода обязаны падать, а не притворяться «базы нет».
        catch (Exception ex) when (PersistFailure.Unavailable(ex))
        {
            logger.LogWarning("connect: persistence unavailable, memory only ({Kind}: {Message})", ex.GetType().Name, ex.Message);

            return null;
        }
    }

    // Забывает хайперы и в рантайме, и в персисте - иначе они вернулись бы на ближайшем connect.
    // Только Development: на релизе это стирало бы то, что накопил живой мир.
    private async Task ForgetHypers(ITranslatorRepo repo, Translator? translator, CancellationToken cancellationToken)
    {
        if (!speech.ResetHypers || !environment.IsDevelopment()) return;

        int forgotten = speech.HyperUrls.Count;

        speech.ForgetHypers();

        // Что забывать в персисте, решает сам агрегат: посев цепочек он оставляет, иначе прыгать
        // станет не по чему. Накопленное этим прогоном живёт в памяти, его ForgetHypers уже стёр.
        if (translator is not null && translator.Forget() > 0)
        {
            forgotten += translator.Hypers.Count;

            await repo.UnitOfWork.SaveEntitiesAsync(cancellationToken);
        }

        logger.LogInformation("connect: {Forgotten} hypers forgotten (dev reset)", forgotten);
    }

    // Тёплый словарь из персиста в рантайм. Адрес и handle - это индексы, поэтому строго по
    // возрастанию, дырки Restore добьёт сам.
    private void Warm(Translator? translator)
    {
        if (translator is null) return;

        var learned = translator.LearnedOrdered();
        var remembered = translator.RememberedOrdered();

        var fragments = new List<FragmentSeed>(learned.Count);

        foreach (var (id, text) in learned) fragments.Add(new FragmentSeed(id, text));

        var hypers = new List<HyperSeed>(remembered.Count);

        foreach (var (handle, url) in remembered) hypers.Add(new HyperSeed(handle, url));

        // Дерево цепочек из персиста: номера узлов сохраняются, иначе выданные прыжки протухнут.
        // Поднимаем ВСЕГДА - hypers=off гасит появление новых цепочек, а не чтение накопленных.
        var chains = translator.Grown();

        speech.RestoreChains(chains);

        speech.Restore(fragments, hypers);

        logger.LogInformation("connect: restored {Fragments} fragments, {Hypers} hypers, {Chains} chain nodes", fragments.Count, hypers.Count, chains.Count);
    }
}
