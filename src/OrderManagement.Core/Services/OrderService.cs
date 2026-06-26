using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using OrderManagement.Core.Contracts;
using OrderManagement.Core.Data;
using OrderManagement.Core.Domain;
using OrderManagement.Core.Domain.Entities;
using OrderManagement.Core.Domain.Enums;
using OrderManagement.Core.Exceptions;
using Npgsql;

namespace OrderManagement.Core.Services;

public interface IOrderService
{
    Task<OrderResponse> CreateOrderAsync(string idempotencyKey, CreateOrderRequest request, CancellationToken cancellationToken);
    Task<OrderResponse> GetOrderAsync(Guid orderId, CancellationToken cancellationToken);
    Task<PagedOrdersResponse> ListOrdersAsync(
        OrderStatus? status,
        Guid? customerId,
        DateTime? from,
        DateTime? to,
        int page,
        int pageSize,
        CancellationToken cancellationToken);
    Task<OrderResponse> UpdateOrderStatusAsync(Guid orderId, OrderStatus newStatus, uint rowVersion, CancellationToken cancellationToken);
    Task<OrderResponse> CancelOrderAsync(Guid orderId, uint rowVersion, CancellationToken cancellationToken);
}

public sealed class OrderService(AppDbContext dbContext, ILogger<OrderService> logger) : IOrderService
{
    public async Task<OrderResponse> CreateOrderAsync(
        string idempotencyKey,
        CreateOrderRequest request,
        CancellationToken cancellationToken)
    {
        ValidateCreateRequest(request);
        logger.LogInformation(
            "Processing create order request for customer {CustomerId} with idempotency key {IdempotencyKey}",
            request.CustomerId,
            idempotencyKey);

        var requestHash = RequestHasher.HashCreateOrderRequest(request);
        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);

        try
        {
            var existingRecord = await TryAcquireIdempotencyRecordAsync(
                idempotencyKey,
                requestHash,
                cancellationToken);

            if (existingRecord?.OrderId is Guid existingOrderId)
            {
                await transaction.CommitAsync(cancellationToken);
                logger.LogInformation("Returning existing order {OrderId} for idempotency key {IdempotencyKey}", existingOrderId, idempotencyKey);
                return await GetOrderInternalAsync(existingOrderId, cancellationToken)
                    ?? throw new NotFoundException($"Order '{existingOrderId}' was not found.");
            }

            var idempotencyRecord = existingRecord ?? await CreateProcessingRecordAsync(idempotencyKey, requestHash, cancellationToken);
            var products = await LoadProductsForOrderAsync(request, cancellationToken);

            foreach (var item in request.Items)
            {
                var deducted = await DeductStockAsync(item.ProductId, item.Quantity, cancellationToken);
                if (!deducted)
                {
                    throw new UnprocessableException(
                        $"Insufficient stock for product '{products[item.ProductId].Name}'. Requested {item.Quantity}, available {products[item.ProductId].StockQuantity}.");
                }
            }

            var now = DateTime.UtcNow;
            var order = new Order
            {
                Id = Guid.NewGuid(),
                CustomerId = request.CustomerId,
                ShippingAddress = request.ShippingAddress.Trim(),
                Status = OrderStatus.Pending,
                CreatedAt = now,
                UpdatedAt = now,
                Items = request.Items.Select(item => new OrderItem
                {
                    Id = Guid.NewGuid(),
                    ProductId = item.ProductId,
                    Quantity = item.Quantity,
                    UnitPrice = products[item.ProductId].Price
                }).ToList()
            };

            dbContext.Orders.Add(order);

            idempotencyRecord.OrderId = order.Id;
            idempotencyRecord.Status = IdempotencyStatus.Completed;
            idempotencyRecord.CompletedAt = now;

            await dbContext.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);

            logger.LogInformation("Created order {OrderId} for customer {CustomerId}", order.Id, order.CustomerId);
            return MapOrder(order);
        }
        catch (DbUpdateException ex) when (IsUniqueViolation(ex))
        {
            logger.LogWarning(
                ex,
                "Unique constraint violation while creating order with idempotency key {IdempotencyKey}",
                idempotencyKey);

            await transaction.RollbackAsync(cancellationToken);
            dbContext.ChangeTracker.Clear();

            var completedOrderId = await WaitForCompletedIdempotencyOrderAsync(idempotencyKey, requestHash, cancellationToken);
            if (completedOrderId is null)
            {
                throw new ConflictException("Another request with the same idempotency key is still being processed.");
            }

            return await GetOrderInternalAsync(completedOrderId.Value, cancellationToken)
                ?? throw new NotFoundException($"Order '{completedOrderId}' was not found.");
        }
        catch (Exception ex)
        {
            await transaction.RollbackAsync(cancellationToken);
            logger.LogError(
                ex,
                "Failed to create order for idempotency key {IdempotencyKey}",
                idempotencyKey);
            throw;
        }
    }

    public async Task<OrderResponse> GetOrderAsync(Guid orderId, CancellationToken cancellationToken)
    {
        var order = await GetOrderInternalAsync(orderId, cancellationToken);
        if (order is null)
        {
            throw new NotFoundException($"Order '{orderId}' was not found.");
        }

        return order;
    }

    public async Task<PagedOrdersResponse> ListOrdersAsync(
        OrderStatus? status,
        Guid? customerId,
        DateTime? from,
        DateTime? to,
        int page,
        int pageSize,
        CancellationToken cancellationToken)
    {
        if (page < 1)
        {
            throw new ValidationException("Page must be greater than or equal to 1.");
        }

        if (pageSize is < 1 or > 100)
        {
            throw new ValidationException("PageSize must be between 1 and 100.");
        }

        var query = dbContext.Orders
            .AsNoTracking()
            .Include(x => x.Items)
            .ThenInclude(x => x.Product)
            .AsQueryable();

        if (status.HasValue)
        {
            query = query.Where(x => x.Status == status.Value);
        }

        if (customerId.HasValue)
        {
            query = query.Where(x => x.CustomerId == customerId.Value);
        }

        if (from.HasValue)
        {
            query = query.Where(x => x.CreatedAt >= from.Value);
        }

        if (to.HasValue)
        {
            query = query.Where(x => x.CreatedAt <= to.Value);
        }

        var totalCount = await query.CountAsync(cancellationToken);
        var orders = await query
            .OrderByDescending(x => x.CreatedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(cancellationToken);

        return new PagedOrdersResponse(
            orders.Select(MapOrder).ToList(),
            page,
            pageSize,
            totalCount,
            (int)Math.Ceiling(totalCount / (double)pageSize));
    }

    public async Task<OrderResponse> UpdateOrderStatusAsync(
        Guid orderId,
        OrderStatus newStatus,
        uint rowVersion,
        CancellationToken cancellationToken)
    {
        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);

        var order = await dbContext.Orders
            .Include(ord => ord.Items)
            .ThenInclude(ord => ord.Product)
            .FirstOrDefaultAsync(ord => ord.Id == orderId, cancellationToken);

        if (order is null)
        {
            throw new NotFoundException($"Order '{orderId}' was not found.");
        }

        dbContext.Entry(order).Property(x => x.RowVersion).OriginalValue = rowVersion;

        if (OrderStatusTransitions.IsTerminal(order.Status))
        {
            throw new ConflictException($"Order is in terminal state '{order.Status}' and cannot be updated.");
        }

        if (!OrderStatusTransitions.CanTransition(order.Status, newStatus))
        {
            throw new UnprocessableException($"Invalid status transition from '{order.Status}' to '{newStatus}'.");
        }

        logger.LogInformation(
            "Updating order {OrderId} status from {CurrentStatus} to {NewStatus}",
            orderId,
            order.Status,
            newStatus);

        order.Status = newStatus;
        order.UpdatedAt = DateTime.UtcNow;

        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            logger.LogInformation("Updated order {OrderId} status to {Status}", orderId, newStatus);
            return MapOrder(order);
        }
        catch (DbUpdateConcurrencyException ex)
        {
            logger.LogWarning(
                ex,
                "Concurrency conflict while updating order {OrderId} status to {Status} with row version {RowVersion}",
                orderId,
                newStatus,
                rowVersion);

            throw new ConflictException("Order was modified by another request. Refresh and retry.");
        }
        catch (Exception ex)
        {
            logger.LogError(
                ex,
                "Failed to update order {OrderId} status to {Status}",
                orderId,
                newStatus);
            throw;
        }
    }

    public async Task<OrderResponse> CancelOrderAsync(Guid orderId, uint rowVersion, CancellationToken cancellationToken)
    {
        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);

        var order = await dbContext.Orders
            .Include(x => x.Items)
            .ThenInclude(x => x.Product)
            .FirstOrDefaultAsync(x => x.Id == orderId, cancellationToken);

        if (order is null)
        {
            throw new NotFoundException($"Order '{orderId}' was not found.");
        }

        dbContext.Entry(order).Property(x => x.RowVersion).OriginalValue = rowVersion;

        if (!OrderStatusTransitions.CanCancel(order.Status))
        {
            throw new ConflictException($"Order in status '{order.Status}' cannot be cancelled.");
        }

        logger.LogInformation(
            "Cancelling order {OrderId} currently in status {CurrentStatus} for customer {CustomerId}",
            orderId,
            order.Status,
            order.CustomerId);

        foreach (var item in order.Items)
        {
            await RestoreStockAsync(item.ProductId, item.Quantity, cancellationToken);
        }

        order.Status = OrderStatus.Cancelled;
        order.UpdatedAt = DateTime.UtcNow;

        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            logger.LogInformation("Cancelled order {OrderId}", orderId);
            return MapOrder(order);
        }
        catch (DbUpdateConcurrencyException ex)
        {
            logger.LogWarning(
                ex,
                "Concurrency conflict while cancelling order {OrderId} with row version {RowVersion}",
                orderId,
                rowVersion);

            throw new ConflictException("Order was modified by another request. Refresh and retry.");
        }
        catch (Exception ex)
        {
            logger.LogError(
                ex,
                "Failed to cancel order {OrderId}",
                orderId);
            throw;
        }
    }

    private static void ValidateCreateRequest(CreateOrderRequest request)
    {
        if (request.CustomerId == Guid.Empty)
        {
            throw new ValidationException("CustomerId is required.");
        }

        if (string.IsNullOrWhiteSpace(request.ShippingAddress))
        {
            throw new ValidationException("ShippingAddress is required.");
        }

        if (request.Items is null || request.Items.Count == 0)
        {
            throw new ValidationException("At least one order item is required.");
        }

        foreach (var item in request.Items)
        {
            if (item.ProductId == Guid.Empty)
            {
                throw new ValidationException("ProductId is required for all items.");
            }

            if (item.Quantity <= 0)
            {
                throw new ValidationException("Quantity must be greater than zero.");
            }
        }
    }

    private async Task<Dictionary<Guid, Product>> LoadProductsForOrderAsync(
        CreateOrderRequest request,
        CancellationToken cancellationToken)
    {
        var productIds = request.Items.Select(product => product.ProductId).Distinct().ToList();
        var products = await dbContext.Products
            .Where(product => productIds.Contains(product.Id))
            .ToDictionaryAsync(product => product.Id, cancellationToken);

        if (products.Count != productIds.Count)
        {
            var missing = productIds.Except(products.Keys).First();
            throw new NotFoundException($"Product '{missing}' was not found.");
        }

        return products;
    }

    private async Task<bool> DeductStockAsync(Guid productId, int quantity, CancellationToken cancellationToken)
    {
        var affectedRows = await dbContext.Products
            .Where(product => product.Id == productId && product.StockQuantity >= quantity)
            .ExecuteUpdateAsync(
                setters => setters.SetProperty(product => product.StockQuantity, product => product.StockQuantity - quantity),
                cancellationToken);

        return affectedRows == 1;
    }

    private async Task RestoreStockAsync(Guid productId, int quantity, CancellationToken cancellationToken)
    {
        var affectedRows = await dbContext.Products
            .Where(product => product.Id == productId)
            .ExecuteUpdateAsync(
                setters => setters.SetProperty(product => product.StockQuantity, product => product.StockQuantity + quantity),
                cancellationToken);

        if (affectedRows != 1)
        {
            throw new NotFoundException($"Product '{productId}' was not found while restoring stock.");
        }
    }

    private async Task<IdempotencyRecord?> TryAcquireIdempotencyRecordAsync(
        string idempotencyKey,
        string requestHash,
        CancellationToken cancellationToken)
    {
        var record = await dbContext.IdempotencyRecords
            .FirstOrDefaultAsync(idempotencyRecord => idempotencyRecord.Key == idempotencyKey, cancellationToken);

        if (record is null)
        {
            return null;
        }

        if (!string.Equals(record.RequestHash, requestHash, StringComparison.Ordinal))
        {
            throw new ConflictException("Idempotency key was already used with a different request payload.");
        }

        if (record.Status == IdempotencyStatus.Completed && record.OrderId.HasValue)
        {
            return record;
        }

        var completedOrderId = await WaitForCompletedIdempotencyOrderAsync(idempotencyKey, requestHash, cancellationToken);
        if (completedOrderId is null)
        {
            throw new ConflictException("Another request with the same idempotency key is still being processed.");
        }

        return await dbContext.IdempotencyRecords
            .FirstAsync(x => x.Key == idempotencyKey, cancellationToken);
    }

    private async Task<IdempotencyRecord> CreateProcessingRecordAsync(
        string idempotencyKey,
        string requestHash,
        CancellationToken cancellationToken)
    {
        var record = new IdempotencyRecord
        {
            Id = Guid.NewGuid(),
            Key = idempotencyKey,
            RequestHash = requestHash,
            Status = IdempotencyStatus.Processing,
            CreatedAt = DateTime.UtcNow
        };

        dbContext.IdempotencyRecords.Add(record);
        await dbContext.SaveChangesAsync(cancellationToken);
        return record;
    }

    private async Task<Guid?> WaitForCompletedIdempotencyOrderAsync(
        string idempotencyKey,
        string requestHash,
        CancellationToken cancellationToken)
    {
        for (var attempt = 0; attempt < 20; attempt++)
        {
            var record = await dbContext.IdempotencyRecords
                .AsNoTracking()
                .FirstOrDefaultAsync(x => x.Key == idempotencyKey, cancellationToken);

            if (record is null)
            {
                return null;
            }

            if (!string.Equals(record.RequestHash, requestHash, StringComparison.Ordinal))
            {
                throw new ConflictException("Idempotency key was already used with a different request payload.");
            }

            if (record.Status == IdempotencyStatus.Completed && record.OrderId.HasValue)
            {
                return record.OrderId;
            }

            await Task.Delay(50, cancellationToken);
        }

        return null;
    }

    private async Task<OrderResponse?> GetOrderInternalAsync(Guid orderId, CancellationToken cancellationToken)
    {
        var order = await dbContext.Orders
            .AsNoTracking()
            .Include(x => x.Items)
            .ThenInclude(x => x.Product)
            .FirstOrDefaultAsync(x => x.Id == orderId, cancellationToken);

        return order is null ? null : MapOrder(order);
    }

    private static OrderResponse MapOrder(Order order)
    {
        var items = order.Items
            .Select(item => new OrderItemResponse(
                item.ProductId,
                item.Product?.Name ?? string.Empty,
                item.Quantity,
                item.UnitPrice,
                item.UnitPrice * item.Quantity))
            .ToList();

        return new OrderResponse(
            order.Id,
            order.CustomerId,
            order.ShippingAddress,
            order.Status,
            order.CreatedAt,
            order.UpdatedAt,
            order.RowVersion,
            items,
            items.Sum(x => x.LineTotal));
    }

    private static bool IsUniqueViolation(DbUpdateException exception) =>
        exception.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation };
}
