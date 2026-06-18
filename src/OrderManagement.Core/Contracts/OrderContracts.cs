using OrderManagement.Core.Domain.Enums;

namespace OrderManagement.Core.Contracts;

public sealed record CreateOrderItemRequest(Guid ProductId, int Quantity);

public sealed record CreateOrderRequest(
    Guid CustomerId,
    IReadOnlyList<CreateOrderItemRequest> Items,
    string ShippingAddress);

public sealed record UpdateOrderStatusRequest(OrderStatus Status);

public sealed record OrderItemResponse(
    Guid ProductId,
    string ProductName,
    int Quantity,
    decimal UnitPrice,
    decimal LineTotal);

public sealed record OrderResponse(
    Guid Id,
    Guid CustomerId,
    string ShippingAddress,
    OrderStatus Status,
    DateTime CreatedAt,
    DateTime UpdatedAt,
    uint RowVersion,
    IReadOnlyList<OrderItemResponse> Items,
    decimal TotalAmount);

public sealed record PagedOrdersResponse(
    IReadOnlyList<OrderResponse> Items,
    int Page,
    int PageSize,
    int TotalCount,
    int TotalPages);

public sealed record ErrorResponse(
    string ErrorCode,
    string Message,
    string? CorrelationId,
    IDictionary<string, string[]>? ValidationErrors = null);
