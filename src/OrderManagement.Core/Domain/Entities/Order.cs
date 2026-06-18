using OrderManagement.Core.Domain.Enums;

namespace OrderManagement.Core.Domain.Entities;

public class Order
{
    public Guid Id { get; set; }
    public Guid CustomerId { get; set; }
    public string ShippingAddress { get; set; } = string.Empty;
    public OrderStatus Status { get; set; } = OrderStatus.Pending;
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
    public uint RowVersion { get; set; }

    public ICollection<OrderItem> Items { get; set; } = new List<OrderItem>();
}
