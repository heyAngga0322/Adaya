using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using OrderManagement.Core.Data;

#nullable disable

namespace OrderManagement.Core.Data.Migrations;

[DbContext(typeof(AppDbContext))]
[Migration("20250617000000_InitialCreate")]
public partial class InitialCreate
{
    protected override void BuildTargetModel(ModelBuilder modelBuilder)
    {
        modelBuilder
            .HasAnnotation("ProductVersion", "10.0.0")
            .HasAnnotation("Relational:MaxIdentifierLength", 63);

        modelBuilder.UseIdentityByDefaultColumns();

        modelBuilder.Entity("OrderManagement.Api.Domain.Entities.IdempotencyRecord", b =>
        {
            b.Property<Guid>("Id").HasColumnType("uuid");
            b.Property<DateTime?>("CompletedAt").HasColumnType("timestamp with time zone");
            b.Property<DateTime>("CreatedAt").HasColumnType("timestamp with time zone");
            b.Property<string>("Key").IsRequired().HasMaxLength(128).HasColumnType("character varying(128)");
            b.Property<Guid?>("OrderId").HasColumnType("uuid");
            b.Property<string>("RequestHash").IsRequired().HasMaxLength(128).HasColumnType("character varying(128)");
            b.Property<int>("Status").HasColumnType("integer");
            b.HasKey("Id");
            b.HasIndex("Key").IsUnique();
            b.ToTable("IdempotencyRecords");
        });

        modelBuilder.Entity("OrderManagement.Api.Domain.Entities.Order", b =>
        {
            b.Property<Guid>("Id").HasColumnType("uuid");
            b.Property<DateTime>("CreatedAt").HasColumnType("timestamp with time zone");
            b.Property<Guid>("CustomerId").HasColumnType("uuid");
            b.Property<string>("ShippingAddress").IsRequired().HasMaxLength(500).HasColumnType("character varying(500)");
            b.Property<string>("Status").IsRequired().HasMaxLength(20).HasColumnType("character varying(20)");
            b.Property<DateTime>("UpdatedAt").HasColumnType("timestamp with time zone");
            b.Property<uint>("RowVersion").IsConcurrencyToken().ValueGeneratedOnAddOrUpdate().HasColumnType("xid").HasColumnName("xmin");
            b.HasKey("Id");
            b.HasIndex("CreatedAt");
            b.HasIndex("CustomerId");
            b.HasIndex("Status");
            b.ToTable("Orders");
        });

        modelBuilder.Entity("OrderManagement.Api.Domain.Entities.OrderItem", b =>
        {
            b.Property<Guid>("Id").HasColumnType("uuid");
            b.Property<Guid>("OrderId").HasColumnType("uuid");
            b.Property<Guid>("ProductId").HasColumnType("uuid");
            b.Property<int>("Quantity").HasColumnType("integer");
            b.Property<decimal>("UnitPrice").HasPrecision(18, 2).HasColumnType("numeric(18,2)");
            b.HasKey("Id");
            b.HasIndex("OrderId");
            b.HasIndex("ProductId");
            b.ToTable("OrderItems");
        });

        modelBuilder.Entity("OrderManagement.Api.Domain.Entities.Product", b =>
        {
            b.Property<Guid>("Id").HasColumnType("uuid");
            b.Property<string>("Name").IsRequired().HasMaxLength(200).HasColumnType("character varying(200)");
            b.Property<decimal>("Price").HasPrecision(18, 2).HasColumnType("numeric(18,2)");
            b.Property<int>("StockQuantity").HasColumnType("integer");
            b.HasKey("Id");
            b.ToTable("Products");
        });

        modelBuilder.Entity("OrderManagement.Api.Domain.Entities.OrderItem", b =>
        {
            b.HasOne("OrderManagement.Api.Domain.Entities.Order", "Order")
                .WithMany("Items")
                .HasForeignKey("OrderId")
                .OnDelete(DeleteBehavior.Cascade)
                .IsRequired();
            b.HasOne("OrderManagement.Api.Domain.Entities.Product", "Product")
                .WithMany()
                .HasForeignKey("ProductId")
                .OnDelete(DeleteBehavior.Restrict)
                .IsRequired();
            b.Navigation("Order");
            b.Navigation("Product");
        });

        modelBuilder.Entity("OrderManagement.Api.Domain.Entities.Order", b =>
        {
            b.Navigation("Items");
        });
    }
}
