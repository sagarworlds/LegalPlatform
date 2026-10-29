using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace ServiceEndpoints.Tests.Support;

/// <summary>
/// A real relational database (in-memory SQLite) for a service's DbContext. The EF InMemory provider would hide the
/// bugs these tests guard against: it does not enforce NOT NULL, unique indexes or insert ordering.
/// Each <see cref="NewContext"/> is a fresh context over the same database, like one context per HTTP request.
/// </summary>
public sealed class SqliteDatabase<TContext> : IDisposable where TContext : DbContext
{
    private readonly SqliteConnection _connection;
    private readonly DbContextOptions<TContext> _options;

    public SqliteDatabase()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();
        _options = new DbContextOptionsBuilder<TContext>().UseSqlite(_connection).Options;

        using var context = NewContext();
        context.Database.EnsureCreated();
    }

    public TContext NewContext() => (TContext)Activator.CreateInstance(typeof(TContext), _options)!;

    public void Dispose() => _connection.Dispose();
}
