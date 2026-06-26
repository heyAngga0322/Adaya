# OrderManagement Project Summary

## Overview
OrderManagement is a prototype REST API built with **ASP.NET Core (.NET 10)** for managing orders, inventory, and order status changes. The project emphasizes:

- safe idempotent order creation
- concurrent stock deduction without negative inventory
- optimistic concurrency for order status updates
- structured logging and correlation IDs
- deployability with Docker and PostgreSQL

## Project Structure

- `src/OrderManagement.Api/`
  - ASP.NET Core minimal API host
  - application endpoints and middleware
  - Serilog logging configuration
  - database migration and seeding on startup
- `src/OrderManagement.Core/`
  - domain models, EF Core DbContext, and service logic
  - request/response contracts
  - concurrency and idempotency support
- `tests/OrderManagement.Tests/`
  - integration tests for service behaviors
  - PostgreSQL test database support

## Key Components

### API Endpoints

- `POST /api/orders`
  - creates a new order
  - requires header `Idempotency-Key`
- `GET /api/orders/{orderId}`
  - retrieves an order by ID
- `GET /api/orders`
  - lists orders with optional filter and pagination
- `PATCH /api/orders/{orderId}/status`
  - updates order status
  - requires header `If-Match` with current `RowVersion`
- `POST /api/orders/{orderId}/cancel`
  - cancels an order and restores stock
  - requires header `If-Match`

### Data Contracts

- `CreateOrderRequest`
  - `CustomerId`
  - `Items` list of `CreateOrderItemRequest`
  - `ShippingAddress`
- `UpdateOrderStatusRequest`
  - `Status`
- `OrderResponse`
  - includes order metadata, `RowVersion`, item details, and total amount
- `ErrorResponse`
  - `ErrorCode`, `Message`, optional `CorrelationId`, and validation details

### Domain Behavior

- `OrderStatus` values:
  - `Pending`
  - `Confirmed`
  - `Shipped`
  - `Delivered`
  - `Cancelled`

- Allowed transitions:
  - `Pending` → `Confirmed` or `Cancelled`
  - `Confirmed` → `Shipped` or `Cancelled`
  - `Shipped` → `Delivered`
  - `Delivered` and `Cancelled` are terminal states

## Concurrency and Idempotency

### Idempotency

Order creation is guarded with:

- header `Idempotency-Key`
- `IdempotencyRecords` table
- request payload hashing via `RequestHasher`
- unique constraint on `IdempotencyRecords.Key`

If the same key and payload are submitted again, the service returns the existing order.

### Stock Deduction

Stock deduction uses EF Core `ExecuteUpdateAsync` with a conditional update:

- only subtract stock when `StockQuantity >= quantity`
- prevents negative inventory under concurrency
- returns failure if a product does not have enough stock

### Optimistic Concurrency

- `Order.RowVersion` is mapped to PostgreSQL `xmin`
- client must provide `If-Match` header with row version for status changes and cancel operations
- concurrency conflicts return `409 Conflict`

## Middleware and Observability

- `CorrelationIdMiddleware`
  - reads `X-Correlation-Id` header or generates one
  - adds correlation ID to response headers and Serilog context
- `ExceptionHandlingMiddleware`
  - converts `AppException` to structured error responses
  - logs handled warnings and unhandled errors
- Serilog is configured with:
  - console sink
  - environment and thread enrichment
  - request logging with correlation and request context

## Deployment

### Docker Support

- `docker-compose.yml` defines:
  - `postgres` service running PostgreSQL 17
  - `ordermanagement-api` service built from `src/OrderManagement.Api/Dockerfile`
- API container runs on port `5000` mapped to container port `80`
- database connection is configured with `ConnectionStrings__DefaultConnection` using Docker service name `postgres`

### Dockerfile

- multi-stage build using `mcr.microsoft.com/dotnet/sdk:10.0`
- publishes the API to `/app/publish`
- runtime image is `mcr.microsoft.com/dotnet/aspnet:10.0`

### Running

From the repository root:

```bash
docker compose up -d --build
```

API should become available at `http://localhost:5000`.

## Testing

Run the test suite with:

```bash
dotnet test OrderManagement.slnx
```

The repository also includes a Postman collection at `OrderManagement.postman_collection.json` for API verification.

## Notes

- `appsettings.json` currently points to PostgreSQL on `localhost`; Docker deployment overrides this with a Docker network connection string.
- There is an existing `System.Security.Cryptography.Xml` vulnerability warning from NuGet during restore, but it does not prevent build or test execution.

## Useful Files

- `src/OrderManagement.Api/Program.cs`
- `src/OrderManagement.Api/Endpoints/OrderEndpoints.cs`
- `src/OrderManagement.Api/Middleware/ExceptionHandlingMiddleware.cs`
- `src/OrderManagement.Api/Middleware/CorrelationIdMiddleware.cs`
- `src/OrderManagement.Core/Services/OrderService.cs`
- `src/OrderManagement.Core/Data/AppDbContext.cs`
- `docker-compose.yml`
- `src/OrderManagement.Api/Dockerfile`
- `OrderManagement.postman_collection.json`
