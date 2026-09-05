using Dotseed.Domain;

namespace CombineQueries.Domain.Aggregates.Account;

public class Account : Entity, IAggregateRoot
{
    public new Guid Id { get; set; } = Guid.NewGuid();
    public required string Token { get; set; }
    public string? Name { get; set; }
    public string? Description { get; set; }
    public bool Active { get; set; } = true;

    public const int TokenMin = 8;
    public const int TokenMax = 64;

    public static Account From(IAddAccount command) => new()
    {
        Token = command.Token,

        Name = command.Name ?? string.Empty,
        Description = command.Description ?? string.Empty
    };

    public static bool IsToken(string token)
    {
        if (token.Length < TokenMin || token.Length > TokenMax) return false;

        foreach (char c in token) if (!char.IsAsciiLetterOrDigit(c) && c != '-' && c != '_') return false;

        return true;
    }
}
