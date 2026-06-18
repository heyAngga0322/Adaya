using OrderManagement.Core.Contracts;
using OrderManagement.Core.Domain.Enums;
using OrderManagement.Core.Exceptions;
using OrderManagement.Core.Services;

namespace OrderManagement.Api.Endpoints;

public static class OrderEndpoints
{
    public const string IdempotencyHeaderName = "Idempotency-Key";

    public static RouteGroupBuilder MapOrderEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/orders")
            .WithTags("Orders");

        group.MapPost("/", CreateOrderAsync)
            .WithName("CreateOrder")
            .WithSummary("Create a new order")
            .Produces<OrderResponse>(StatusCodes.Status201Created)
            .Produces<ErrorResponse>(StatusCodes.Status400BadRequest)
            .Produces<ErrorResponse>(StatusCodes.Status409Conflict)
            .Produces<ErrorResponse>(StatusCodes.Status422UnprocessableEntity);

        group.MapGet("/{orderId:guid}", GetOrderAsync)
            .WithName("GetOrder")
            .Produces<OrderResponse>()
            .Produces<ErrorResponse>(StatusCodes.Status404NotFound);

        group.MapGet("/", ListOrdersAsync)
            .WithName("ListOrders")
            .Produces<PagedOrdersResponse>();

        group.MapPatch("/{orderId:guid}/status", UpdateOrderStatusAsync)
            .WithName("UpdateOrderStatus")
            .Produces<OrderResponse>()
            .Produces<ErrorResponse>(StatusCodes.Status409Conflict)
            .Produces<ErrorResponse>(StatusCodes.Status422UnprocessableEntity);

        group.MapPost("/{orderId:guid}/cancel", CancelOrderAsync)
            .WithName("CancelOrder")
            .Produces<OrderResponse>()
            .Produces<ErrorResponse>(StatusCodes.Status409Conflict);

        return group;
    }

    private static async Task<IResult> CreateOrderAsync(
        CreateOrderRequest request,
        HttpRequest httpRequest,
        IOrderService orderService,
        CancellationToken cancellationToken)
    {
        if (!httpRequest.Headers.TryGetValue(IdempotencyHeaderName, out var idempotencyKeyValues) ||
            string.IsNullOrWhiteSpace(idempotencyKeyValues.FirstOrDefault()))
        {
            throw new ValidationException($"{IdempotencyHeaderName} header is required.");
        }

        var idempotencyKey = idempotencyKeyValues.ToString().Trim();
        if (idempotencyKey.Length > 128)
        {
            throw new ValidationException($"{IdempotencyHeaderName} must be at most 128 characters.");
        }

        var order = await orderService.CreateOrderAsync(idempotencyKey, request, cancellationToken);
        return Results.Created($"/api/orders/{order.Id}", order);
    }

    private static async Task<IResult> GetOrderAsync(
        Guid orderId,
        IOrderService orderService,
        CancellationToken cancellationToken)
    {
        var order = await orderService.GetOrderAsync(orderId, cancellationToken);
        return Results.Ok(order);
    }

    private static async Task<IResult> ListOrdersAsync(
        OrderStatus? status,
        Guid? customerId,
        DateTime? from,
        DateTime? to,
        int page,
        int pageSize,
        IOrderService orderService,
        CancellationToken cancellationToken)
    {
        page = page <= 0 ? 1 : page;
        pageSize = pageSize <= 0 ? 20 : pageSize;

        var result = await orderService.ListOrdersAsync(status, customerId, from, to, page, pageSize, cancellationToken);
        return Results.Ok(result);
    }

    private static async Task<IResult> UpdateOrderStatusAsync(
        Guid orderId,
        UpdateOrderStatusRequest request,
        HttpRequest httpRequest,
        IOrderService orderService,
        CancellationToken cancellationToken)
    {
        var rowVersion = ParseRowVersion(httpRequest);
        var order = await orderService.UpdateOrderStatusAsync(orderId, request.Status, rowVersion, cancellationToken);
        return Results.Ok(order);
    }

    private static async Task<IResult> CancelOrderAsync(
        Guid orderId,
        HttpRequest httpRequest,
        IOrderService orderService,
        CancellationToken cancellationToken)
    {
        var rowVersion = ParseRowVersion(httpRequest);
        var order = await orderService.CancelOrderAsync(orderId, rowVersion, cancellationToken);
        return Results.Ok(order);
    }

    private static uint ParseRowVersion(HttpRequest httpRequest)
    {
        if (!httpRequest.Headers.TryGetValue("If-Match", out var values) ||
            !uint.TryParse(values.FirstOrDefault(), out var rowVersion))
        {
            throw new ValidationException("If-Match header with the current order row version is required.");
        }

        return rowVersion;
    }
}
