namespace CombineQueries.Api.Controllers.Translator.Handlers;

internal static class RawQuery
{
    public static string Arg(string? query)
    {
        string raw = query ?? string.Empty;
        int eq = raw.IndexOf('=');

        return eq < 0 ? string.Empty : raw.Substring(eq + 1);
    }

    public static int Int(string? query) => int.TryParse(Arg(query), out int v) ? v : -1;
}
