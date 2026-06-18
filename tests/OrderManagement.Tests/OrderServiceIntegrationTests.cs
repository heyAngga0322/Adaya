using Microsoft.EntityFrameworkCore;
using OrderManagement.Core;
using OrderManagement.Core.Contracts;
using OrderManagement.Core.Data;
using OrderManagement.Core.Domain;
using OrderManagement.Core.Domain.Enums;
using OrderManagement.Core.Exceptions;
using OrderManagement.Core.Services;
using OrderManagement.Tests.Infrastructure;

namespace OrderManagement.Tests;

public class OrderStatusTransitionTests
{
    [Theory]
    [InlineData(OrderStatus.Pending, OrderStatus.Confirmed, true)]
    [InlineData(OrderStatus.Pending, OrderStatus.Cancelled, true)]
    [InlineData(OrderStatus.Confirmed, OrderStatus.Shipped, true)]
    [InlineData(OrderStatus.Confirmed, OrderStatus.Cancelled, true)]
    [InlineData(OrderStatus.Shipped, OrderStatus.Delivered, true)]
    [InlineData(OrderStatus.Pending, OrderStatus.Shipped, false)]
    [InlineData(OrderStatus.Delivered, OrderStatus.Cancelled, false)]
    [InlineData(OrderStatus.Cancelled, OrderStatus.Pending, false)]
    public void CanTransition_ReturnsExpectedResult(OrderStatus current, OrderStatus next, bool expected)
    {
        var result = OrderStatusTransitions.CanTransition(current, next);
        Assert.Equal(expected, result);
    }

    [Theory]
    [InlineData(OrderStatus.Pending, true)]
    [InlineData(OrderStatus.Confirmed, true)]
    [InlineData(OrderStatus.Shipped, false)]
    [InlineData(OrderStatus.Delivered, false)]
    [InlineData(OrderStatus.Cancelled, false)]
    public void CanCancel_ReturnsExpectedResult(OrderStatus status, bool expected)
    {
        Assert.Equal(expected, OrderStatusTransitions.CanCancel(status));
    }
}

public class OrderServiceIntegrationTests(PostgreSqlTestDatabase database) : IClassFixture<PostgreSqlTestDatabase>, IAsyncLifetime
{
    private static readonly Guid ProductXId = Guid.Parse("11111111-1111-1111-1111-111111111101");
    private static readonly Guid CustomerId = Guid.Parse("22222222-2222-2222-2222-222222222201");

    private AppDbContext _dbContext = null!;
    private OrderService _orderService = null!;

    public async Task InitializeAsync()
    {
        _dbContext = await database.CreateDbContextAsync();
        await database.ResetAsync(_dbContext);
        _orderService = database.CreateOrderService(_dbContext);
    }

    public async Task DisposeAsync()
    {
        await _dbContext.DisposeAsync();
    }

    [Fact]
    public async Task CreateOrder_WithValidRequest_CreatesPendingOrder()
    {
        var order = await _orderService.CreateOrderAsync(
            Guid.NewGuid().ToString(),
            CreateSampleOrderRequest(quantity: 2),
            CancellationToken.None);

        Assert.Equal(OrderStatus.Pending, order.Status);
        Assert.Single(order.Items);
    }

    [Fact]
    public async Task CreateOrder_WithSameIdempotencyKey_ReturnsSameOrder()
    {
        var request = CreateSampleOrderRequest(quantity: 1);
        var idempotencyKey = Guid.NewGuid().ToString();

        var firstOrder = await _orderService.CreateOrderAsync(idempotencyKey, request, CancellationToken.None);
        var secondOrder = await _orderService.CreateOrderAsync(idempotencyKey, request, CancellationToken.None);

        Assert.Equal(firstOrder.Id, secondOrder.Id);
    }

    [Fact]
    public async Task CreateOrder_WithInsufficientStock_ThrowsUnprocessableException()
    {
        var exception = await Assert.ThrowsAsync<UnprocessableException>(() =>
            _orderService.CreateOrderAsync(
                Guid.NewGuid().ToString(),
                CreateSampleOrderRequest(quantity: 100),
                CancellationToken.None));

        Assert.Equal(HttpStatusCodes.UnprocessableEntity, exception.StatusCode);
    }

    [Fact]
    public async Task CancelOrder_RestoresStock()
    {
        var createdOrder = await _orderService.CreateOrderAsync(
            Guid.NewGuid().ToString(),
            CreateSampleOrderRequest(quantity: 5),
            CancellationToken.None);

        var cancelledOrder = await _orderService.CancelOrderAsync(
            createdOrder.Id,
            createdOrder.RowVersion,
            CancellationToken.None);

        Assert.Equal(OrderStatus.Cancelled, cancelledOrder.Status);

        var product = await _dbContext.Products.SingleAsync(x => x.Id == ProductXId);
        Assert.Equal(15, product.StockQuantity);
    }

    [Fact]
    public async Task ConcurrentStockDeduction_OnlyOneOrderSucceeds()
    {
        await database.ResetAsync(_dbContext);

        var product = await _dbContext.Products.SingleAsync(x => x.Id == ProductXId);
        product.StockQuantity = 15;
        await _dbContext.SaveChangesAsync();

        var request = CreateSampleOrderRequest(quantity: 10);
        var tasks = Enumerable.Range(0, 2)
            .Select(async _ =>
            {
                await using var dbContext = await database.CreateDbContextAsync();
                var service = database.CreateOrderService(dbContext);

                try
                {
                    await service.CreateOrderAsync(Guid.NewGuid().ToString(), request, CancellationToken.None);
                    return true;
                }
                catch (UnprocessableException)
                {
                    return false;
                }
            })
            .ToArray();

        var results = await Task.WhenAll(tasks);

        Assert.Equal(1, results.Count(x => x));
        Assert.Equal(1, results.Count(x => !x));

        await using var verifyContext = await database.CreateDbContextAsync();
        var finalProduct = await verifyContext.Products.SingleAsync(x => x.Id == ProductXId);
        Assert.Equal(5, finalProduct.StockQuantity);
    }

    [Fact]
    public async Task ConcurrentStatusUpdate_OnlyOneUpdateWins()
    {
        var createdOrder = await _orderService.CreateOrderAsync(
            Guid.NewGuid().ToString(),
            CreateSampleOrderRequest(quantity: 1),
            CancellationToken.None);

        var confirmedOrder = await _orderService.UpdateOrderStatusAsync(
            createdOrder.Id,
            OrderStatus.Confirmed,
            createdOrder.RowVersion,
            CancellationToken.None);

        var tasks = new[]
        {
            Task.Run(async () =>
            {
                await using var dbContext = await database.CreateDbContextAsync();
                var service = database.CreateOrderService(dbContext);
                return await TryUpdateAsync(() =>
                    service.UpdateOrderStatusAsync(
                        confirmedOrder.Id,
                        OrderStatus.Shipped,
                        confirmedOrder.RowVersion,
                        CancellationToken.None));
            }),
            Task.Run(async () =>
            {
                await using var dbContext = await database.CreateDbContextAsync();
                var service = database.CreateOrderService(dbContext);
                return await TryUpdateAsync(() =>
                    service.CancelOrderAsync(
                        confirmedOrder.Id,
                        confirmedOrder.RowVersion,
                        CancellationToken.None));
            })
        };

        var results = await Task.WhenAll(tasks);

        Assert.Equal(1, results.Count(x => x));
        Assert.Equal(1, results.Count(x => !x));
    }

    [Fact]
    public async Task ConcurrentIdempotentCreate_CreatesSingleOrder()
    {
        var request = CreateSampleOrderRequest(quantity: 1);
        var idempotencyKey = Guid.NewGuid().ToString();

        var tasks = Enumerable.Range(0, 10)
            .Select(async _ =>
            {
                await using var dbContext = await database.CreateDbContextAsync();
                var service = database.CreateOrderService(dbContext);
                return await service.CreateOrderAsync(idempotencyKey, request, CancellationToken.None);
            })
            .ToArray();

        var orders = await Task.WhenAll(tasks);

        Assert.Single(orders.Select(x => x.Id).Distinct());

        await using var verifyContext = await database.CreateDbContextAsync();
        var orderCount = await verifyContext.Orders.CountAsync();
        Assert.Equal(1, orderCount);
    }

    private static CreateOrderRequest CreateSampleOrderRequest(int quantity) =>
        new(
            CustomerId,
            [new CreateOrderItemRequest(ProductXId, quantity)],
            "Jl. Contoh No. 123, Bandung");

    private static async Task<bool> TryUpdateAsync(Func<Task<OrderResponse>> action)
    {
        try
        {
            await action();
            return true;
        }
        catch (ConflictException)
        {
            return false;
        }
    }
}
