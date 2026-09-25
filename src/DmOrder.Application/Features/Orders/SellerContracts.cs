using DmOrder.Domain.Orders;

namespace DmOrder.Application.Features.Orders;

// --- Requests -----------------------------------------------------------

public sealed record ChangeOrderStatusRequest(OrderStatus Status, string? Note);

public sealed record ChangeOrderPaymentRequest(PaymentStatus PaymentStatus, decimal? TotalAmount);

public sealed record AddOrderNoteRequest(string Body);

// --- Responses ----------------------------------------------------------

/// <summary>
/// A row in the seller's order list. Deliberately lighter than the detail view: enough to triage
/// what needs doing today without loading every answer of every order.
/// </summary>
public sealed record OrderListItemResponse(
    Guid Id,
    int OrderNumber,
    string Status,
    string PaymentStatus,
    decimal? TotalAmount,
    DateOnly? DeliveryDate,
    DateTimeOffset CreatedAt,
    string CustomerName,
    string CustomerPhone,
    string ProductSummary,
    int ItemCount);

public sealed record OrderFieldValueResponse(
    string Label,
    string FieldType,
    string? Value,
    string? ImageUrl);

public sealed record OrderItemResponse(
    Guid Id,
    Guid? ProductId,
    string ProductName,
    decimal? UnitPrice,
    int Quantity,
    decimal? LineTotal,
    IReadOnlyList<OrderFieldValueResponse> Answers);

public sealed record OrderStatusHistoryResponse(
    string? FromStatus,
    string ToStatus,
    string? ChangedBy,
    string? Note,
    DateTimeOffset CreatedAt);

public sealed record OrderNoteResponse(Guid Id, string Body, string Author, DateTimeOffset CreatedAt);

public sealed record OrderCustomerResponse(
    Guid Id,
    string Name,
    string Phone,
    string? Email,
    string? Address,
    int TotalOrders);

public sealed record OrderDetailResponse(
    Guid Id,
    int OrderNumber,
    /// The customer-facing reference. The seller needs it to answer "where is my order?".
    string Reference,
    string Status,
    string PaymentStatus,
    decimal? TotalAmount,
    string Currency,
    DateOnly? DeliveryDate,
    string? DeliveryAddress,
    string? CustomerNote,
    string Source,
    DateTimeOffset CreatedAt,
    bool IsFinal,
    OrderCustomerResponse Customer,
    IReadOnlyList<OrderItemResponse> Items,
    IReadOnlyList<OrderStatusHistoryResponse> History,
    IReadOnlyList<OrderNoteResponse> Notes);

/// <summary>Counts for the order list's filter tabs, so a seller sees what needs attention.</summary>
public sealed record OrderCountsResponse(
    int New,
    int Confirmed,
    int InProgress,
    int ReadyForDelivery,
    int Completed,
    int Cancelled,
    int Total);

// --- Customers ----------------------------------------------------------

public sealed record CustomerListItemResponse(
    Guid Id,
    string Name,
    string Phone,
    string? Email,
    int OrderCount,
    DateTimeOffset? LastOrderAt,
    decimal? TotalSpent);

public sealed record CustomerOrderSummaryResponse(
    Guid Id,
    int OrderNumber,
    string Status,
    decimal? TotalAmount,
    DateTimeOffset CreatedAt);

public sealed record CustomerDetailResponse(
    Guid Id,
    string Name,
    string Phone,
    string? Email,
    string? Address,
    string Currency,
    DateTimeOffset CreatedAt,
    int OrderCount,
    decimal? TotalSpent,
    IReadOnlyList<CustomerOrderSummaryResponse> Orders);
