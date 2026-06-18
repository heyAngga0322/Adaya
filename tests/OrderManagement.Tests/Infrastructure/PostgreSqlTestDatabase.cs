using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using OrderManagement.Core.Data;
using OrderManagement.Core.Services;
using Testcontainers.PostgreSql;

namespace OrderManagement.Tests.Infrastructure;

public sealed class PostgreSqlTestDatabase : IAsyncLifetime
{
    private readonly PostgreSqlContainer? _postgres;
    private readonly string? _externalConnectionString;

    public PostgreSqlTestDatabase()
    {
        _externalConnectionString = Environment.GetEnvironmentVariable("TEST_DATABASE_CONNECTION_STRING");

        if (string.IsNullOrWhiteSpace(_externalConnectionString) &&
            string.Equals(Environment.GetEnvironmentVariable("USE_TESTCONTAINERS"), "true", StringComparison.OrdinalIgnoreCase))
        {
            _postgres = new PostgreSqlBuilder()
                .WithImage("postgres:17")
                .WithDatabase("ordermanagement_test")
                .WithUsername("postgres")
                .WithPassword("postgres")
                .Build();
        }
    }

    public string ConnectionString =>
        _postgres?.GetConnectionString()
        ?? _externalConnectionString
        ?? "Host=localhost;Port=5432;Database=ordermanagement;Username=postgres;Password=postgres";

    public async Task InitializeAsync()
    {
        if (_postgres is not null)
        {
            await _postgres.StartAsync();
        }
    }

    public async Task DisposeAsync()
    {
        if (_postgres is not null)
        {
            await _postgres.DisposeAsync();
        }
    }

    public async Task<AppDbContext> CreateDbContextAsync()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql(ConnectionString)
            .Options;

        var dbContext = new AppDbContext(options);
        await dbContext.Database.MigrateAsync();
        return dbContext;
    }

    public async Task ResetAsync(AppDbContext dbContext)
    {
        dbContext.ChangeTracker.Clear();
        await dbContext.Database.ExecuteSqlRawAsync("""
            TRUNCATE TABLE "OrderItems", "Orders", "IdempotencyRecords", "Products" RESTART IDENTITY CASCADE;
            """);
        await DatabaseSeeder.SeedAsync(dbContext);
    }

    public OrderService CreateOrderService(AppDbContext dbContext) =>
        new(dbContext, NullLogger<OrderService>.Instance);
}
