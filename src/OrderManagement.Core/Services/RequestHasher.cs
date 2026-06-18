using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using OrderManagement.Core.Contracts;

namespace OrderManagement.Core.Services;

public static class RequestHasher
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false
    };

    public static string HashCreateOrderRequest(CreateOrderRequest request)
    {
        var normalized = new
        {
            request.CustomerId,
            request.ShippingAddress,
            Items = request.Items
                .OrderBy(x => x.ProductId)
                .Select(x => new { x.ProductId, x.Quantity })
                .ToList()
        };

        var payload = JsonSerializer.Serialize(normalized, SerializerOptions);
        var hashBytes = SHA256.HashData(Encoding.UTF8.GetBytes(payload));
        return Convert.ToHexString(hashBytes);
    }
}
