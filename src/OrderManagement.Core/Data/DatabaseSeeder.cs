using Microsoft.EntityFrameworkCore;
using OrderManagement.Core.Domain.Entities;

namespace OrderManagement.Core.Data;

public static class DatabaseSeeder
{
    public static async Task SeedAsync(AppDbContext dbContext, CancellationToken cancellationToken = default)
    {
        if (await dbContext.Products.AnyAsync(cancellationToken))
        {
            return;
        }

        dbContext.Products.AddRange(
            new Product
            {
                Id = Guid.Parse("11111111-1111-1111-1111-111111111101"),
                Name = "Product X",
                StockQuantity = 15,
                Price = 100m
            },
            new Product
            {
                Id = Guid.Parse("11111111-1111-1111-1111-111111111102"),
                Name = "Product Y",
                StockQuantity = 50,
                Price = 25.5m
            },
            new Product
            {
                Id = Guid.Parse("11111111-1111-1111-1111-111111111103"),
                Name = "Product Z",
                StockQuantity = 100,
                Price = 10m
            });

        await dbContext.SaveChangesAsync(cancellationToken);
    }
}
