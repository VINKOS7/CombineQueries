using System.Security.Cryptography;

using MediatR;

using CombineQueries.Domain.Aggregates.Account;

namespace CombineQueries.Api.Controllers.Accounts.Handlers.Add;

// Единственный репозиторий - IAccountRepo (правило «один хендлер — один репо»).
public class AddHandler(IAccountRepo accountRepo, IConfiguration configuration, ILogger<AddHandler> logger) : IRequestHandler<AddRequest, AddResponse>
{
    // Алфавит токена ровно тот, что принимает Account.IsToken.
    private const string Alphabet = "abcdefghijklmnopqrstuvwxyz0123456789";

    private const int Generated = 16;

    public async Task<AddResponse> Handle(AddRequest request, CancellationToken cancellationToken)
    {
        MasterOnly.Ensure(request, configuration);

        if (string.IsNullOrEmpty(request.Token)) request.Token = Generate();

        if (!Account.IsToken(request.Token)) throw new Exception("domain error: token rejected");

        // Токен - ключ входа, дубль пустил бы два мира под одним. Индекс это тоже ловит, но
        // внятная ошибка лучше, чем DbUpdateException из глубины.
        if (await accountRepo.GetIdByTokenAsync(request.Token) != Guid.Empty)
            throw new Exception("domain error: token already taken");

        var account = Account.From(request);

        await accountRepo.AddAsync(account);
        await accountRepo.UnitOfWork.SaveEntitiesAsync(cancellationToken);

        logger.LogInformation("accounts: added {Id} ({Name})", account.Id, account.Name);

        return new AddResponse { Id = account.Id, Token = account.Token, Name = account.Name };
    }

    // RandomNumberGenerator, а не Random: токен это секрет, предсказуемый генератор его обесценивает.
    private static string Generate()
    {
        var token = new char[Generated];

        for (int i = 0; i < Generated; i++) token[i] = Alphabet[RandomNumberGenerator.GetInt32(Alphabet.Length)];

        return new string(token);
    }
}
