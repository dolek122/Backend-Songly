using Npgsql;
using Songly.Common.Persistence;

namespace Songly.Common.Tests;

/// <summary>The URL form hosting platforms give out (Render's <c>connectionString</c>), and Npgsql's own form.</summary>
public sealed class PostgresUrlTests
{
    [Theory]
    [InlineData("postgresql://songly:secret@dpg-abc-a:5432/songly")]
    [InlineData("postgres://songly:secret@dpg-abc-a/songly")]
    public void A_url_becomes_npgsqls_form(string url)
    {
        var builder = new NpgsqlConnectionStringBuilder(PostgresUrl.ToConnectionString(url));

        Assert.Equal("dpg-abc-a", builder.Host);
        Assert.Equal(5432, builder.Port);
        Assert.Equal("songly", builder.Database);
        Assert.Equal("songly", builder.Username);
        Assert.Equal("secret", builder.Password);
    }

    /// <summary>A generated password may hold any character; in a URL the special ones come percent-encoded.</summary>
    [Fact]
    public void Percent_encoded_credentials_are_decoded()
    {
        var builder = new NpgsqlConnectionStringBuilder(PostgresUrl.ToConnectionString("postgresql://so%40ngly:p%3Ass%40w%2Fd@host:6543/my%20db"));

        Assert.Equal("so@ngly", builder.Username);
        Assert.Equal("p:ss@w/d", builder.Password);
        Assert.Equal(6543, builder.Port);
        Assert.Equal("my db", builder.Database);
    }

    [Fact]
    public void Options_after_the_question_mark_are_kept()
    {
        var builder = new NpgsqlConnectionStringBuilder(PostgresUrl.ToConnectionString("postgresql://u:p@host/db?sslmode=require"));

        Assert.Equal(SslMode.Require, builder.SslMode);
    }

    [Fact]
    public void Npgsqls_own_form_is_left_as_it_is()
    {
        const string connectionString = "Host=localhost;Port=5432;Database=songly;Username=songly;Password=songly";

        Assert.Equal(connectionString, PostgresUrl.ToConnectionString(connectionString));
    }
}
