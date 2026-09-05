using MediatR;

namespace CombineQueries.Domain.Aggregates.Account;

public record AccountConnected(string Alphabet, string BaseForwardUrl) : INotification;
