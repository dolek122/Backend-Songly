using Npgsql;

namespace Songly.Common.Persistence;

/// <summary>
/// Accepts the database address as a URL too — <c>postgresql://user:password@host:5432/database</c>, the form
/// hosting platforms hand out (Render's <c>connectionString</c> property among them). Npgsql only parses its own
/// <c>Key=Value;</c> form; without this, the URL would have to be taken apart by hand into five settings.
/// </summary>
internal static class PostgresUrl
{
    /// <summary>A URL becomes Npgsql's form; anything else is returned as it is.</summary>
    public static string ToConnectionString(string value)
    {
        if (!Uri.TryCreate(value, UriKind.Absolute, out var url) || url.Scheme is not ("postgres" or "postgresql"))
        {
            return value;
        }

        var builder = new NpgsqlConnectionStringBuilder
        {
            Host = url.Host,
            Port = url.IsDefaultPort || url.Port <= 0 ? 5432 : url.Port,
            Database = Uri.UnescapeDataString(url.AbsolutePath.TrimStart('/')),
        };

        // user:password, each percent-encoded — a password with "@" or ":" in it is legal.
        var credentials = url.UserInfo.Split(':', 2);
        if (credentials[0].Length > 0)
        {
            builder.Username = Uri.UnescapeDataString(credentials[0]);
        }

        if (credentials.Length > 1)
        {
            builder.Password = Uri.UnescapeDataString(credentials[1]);
        }

        // Options after "?" (sslmode=require) are Npgsql settings under libpq's names; Npgsql knows the common
        // ones and rejects what it does not, rather than ignoring it.
        foreach (var option in url.Query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var pair = option.Split('=', 2);
            builder[Uri.UnescapeDataString(pair[0])] = pair.Length > 1 ? Uri.UnescapeDataString(pair[1]) : "";
        }

        return builder.ConnectionString;
    }
}
