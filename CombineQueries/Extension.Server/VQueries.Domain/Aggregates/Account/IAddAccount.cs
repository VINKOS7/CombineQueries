namespace CombineQueries.Domain.Aggregates.Account;

public interface IAddAccount
{
    public string Token { get; set; }

    public string? Name { get; set; }

    public string? Description { get; set; }
}
