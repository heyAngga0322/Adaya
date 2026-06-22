using Microsoft.EntityFrameworkCore;
using OrderManagement.Core.Domain.Entities;

namespace OrderManagement.Core.Data;

public class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
    public DbSet<Product> Products => Set<Product>();
    public DbSet<Order> Orders => Set<Order>();
    public DbSet<OrderItem> OrderItems => Set<OrderItem>();
    public DbSet<IdempotencyRecord> IdempotencyRecords => Set<IdempotencyRecord>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Product>(entity =>
        {
            entity.HasKey(product => product.Id);
            entity.Property(product => product.Name).HasMaxLength(200).IsRequired();
            entity.Property(product => product.Price).HasPrecision(18, 2);
        });

        modelBuilder.Entity<Order>(entity =>
        {
            entity.HasKey(order => order.Id);
            entity.Property(order => order.ShippingAddress).HasMaxLength(500).IsRequired();
            entity.Property(order => order.Status).HasConversion<string>().HasMaxLength(20);
            entity.Property(order => order.CreatedAt)
                .HasColumnType("timestamp with time zone")
                .HasDefaultValueSql("NOW()");
            entity.Property(order => order.RowVersion)
                .IsRowVersion()
                .HasColumnName("xmin")
                .HasColumnType("xid");
            entity.HasIndex(order => order.CustomerId);
            entity.HasIndex(order => order.Status);
            entity.HasIndex(order => order.CreatedAt);
        });

        modelBuilder.Entity<OrderItem>(entity =>
        {
            entity.HasKey(orderItem => orderItem.Id);
            entity.Property(orderItem => orderItem.UnitPrice).HasPrecision(18, 2);
            entity.HasOne(orderItem => orderItem.Order)
                .WithMany(order => order.Items)
                .HasForeignKey(orderItem => orderItem.OrderId)
                .OnDelete(DeleteBehavior.Cascade);
            entity.HasOne(orderItem => orderItem.Product)
                .WithMany()
                .HasForeignKey(orderItem => orderItem.ProductId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<IdempotencyRecord>(entity =>
        {
            entity.HasKey(record => record.Id);
            entity.Property(record => record.Key).HasMaxLength(128).IsRequired();
            entity.Property(record => record.RequestHash).HasMaxLength(128).IsRequired();
            entity.Property(record => record.CreatedAt)
                .HasColumnType("timestamp with time zone")
                .HasDefaultValueSql("NOW()");
            entity.HasIndex(record => record.Key).IsUnique();
        });
    }
}
