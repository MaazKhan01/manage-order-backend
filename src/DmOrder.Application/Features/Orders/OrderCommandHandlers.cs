using DmOrder.Application.Common.Interfaces;
using DmOrder.Application.Features.Catalogue;
using DmOrder.Domain.Exceptions;
using DmOrder.Domain.Orders;
using FluentValidation;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace DmOrder.Application.Features.Orders;

public sealed class ChangeOrderStatusHandler(
    IAppDbContext db,
    ICurrentUser currentUser,
    ILogger<ChangeOrderStatusHandler> logger)
{
    public async Task HandleAsync(
        Guid orderId,
        ChangeOrderStatusRequest request,
        CancellationToken cancellationToken)
    {
        var storeId = await StoreScope.RequireStoreIdAsync(db, currentUser, cancellationToken);
        var order = await RequireOwnOrderAsync(db, storeId, orderId, cancellationToken);

        var from = order.Status;

        // The domain decides whether the move is legal. A completed or cancelled order is a closed
        // record, and reopening it silently would let history be rewritten.
        order.ChangeStatus(request.Status);

        if (from != order.Status)
        {
            db.OrderStatusHistory.Add(OrderStatusHistory.Record(
                order.Id, from, order.Status, currentUser.RequireUserId(), request.Note));

            logger.LogInformation(
                "Order {OrderId} moved from {From} to {To}", order.Id, from, order.Status);
        }

        await db.SaveChangesAsync(cancellationToken);
    }

    internal static async Task<Order> RequireOwnOrderAsync(
        IAppDbContext db,
        Guid storeId,
        Guid orderId,
        CancellationToken cancellationToken) =>
        await db.Orders.FirstOrDefaultAsync(o => o.Id == orderId && o.StoreId == storeId, cancellationToken)
        ?? throw new NotFoundException("Order", orderId);
}

public sealed class ChangeOrderPaymentHandler(IAppDbContext db, ICurrentUser currentUser)
{
    public async Task HandleAsync(
        Guid orderId,
        ChangeOrderPaymentRequest request,
        CancellationToken cancellationToken)
    {
        var storeId = await StoreScope.RequireStoreIdAsync(db, currentUser, cancellationToken);
        var order = await ChangeOrderStatusHandler.RequireOwnOrderAsync(db, storeId, orderId, cancellationToken);

        order.ChangePaymentStatus(request.PaymentStatus);

        // The seller sets the real total once they have read the details — most custom work is
        // quoted, not priced up front.
        if (request.TotalAmount is not null)
        {
            order.SetTotal(request.TotalAmount);
        }

        await db.SaveChangesAsync(cancellationToken);
    }
}

public sealed class AddOrderNoteHandler(IAppDbContext db, ICurrentUser currentUser)
{
    public async Task<OrderNoteResponse> HandleAsync(
        Guid orderId,
        AddOrderNoteRequest request,
        CancellationToken cancellationToken)
    {
        var storeId = await StoreScope.RequireStoreIdAsync(db, currentUser, cancellationToken);
        await ChangeOrderStatusHandler.RequireOwnOrderAsync(db, storeId, orderId, cancellationToken);

        var userId = currentUser.RequireUserId();
        var note = OrderNote.Create(orderId, userId, request.Body);

        db.OrderNotes.Add(note);
        await db.SaveChangesAsync(cancellationToken);

        return new OrderNoteResponse(note.Id, note.Body, currentUser.Email ?? "You", note.CreatedAt);
    }
}

public sealed class DeleteOrderNoteHandler(IAppDbContext db, ICurrentUser currentUser)
{
    public async Task HandleAsync(Guid orderId, Guid noteId, CancellationToken cancellationToken)
    {
        var storeId = await StoreScope.RequireStoreIdAsync(db, currentUser, cancellationToken);
        await ChangeOrderStatusHandler.RequireOwnOrderAsync(db, storeId, orderId, cancellationToken);

        var note = await db.OrderNotes
            .FirstOrDefaultAsync(n => n.Id == noteId && n.OrderId == orderId, cancellationToken)
            ?? throw new NotFoundException("Note", noteId);

        // Notes are the seller's own working memory, so deleting one is theirs to do. Status history
        // is not — that stays append-only.
        db.OrderNotes.Remove(note);
        await db.SaveChangesAsync(cancellationToken);
    }
}

public sealed class ChangeOrderStatusValidator : AbstractValidator<ChangeOrderStatusRequest>
{
    public ChangeOrderStatusValidator()
    {
        RuleFor(x => x.Status).IsInEnum().WithMessage("Choose one of the available statuses.");
        RuleFor(x => x.Note).MaximumLength(500);
    }
}

public sealed class ChangeOrderPaymentValidator : AbstractValidator<ChangeOrderPaymentRequest>
{
    public ChangeOrderPaymentValidator()
    {
        RuleFor(x => x.PaymentStatus).IsInEnum().WithMessage("Choose one of the available payment states.");

        RuleFor(x => x.TotalAmount)
            .GreaterThanOrEqualTo(0).When(x => x.TotalAmount.HasValue)
            .WithMessage("A total cannot be negative.")
            .LessThan(1_000_000_000m).When(x => x.TotalAmount.HasValue)
            .WithMessage("That total is too large.");
    }
}

public sealed class AddOrderNoteValidator : AbstractValidator<AddOrderNoteRequest>
{
    public AddOrderNoteValidator()
    {
        RuleFor(x => x.Body)
            .NotEmpty().WithMessage("A note cannot be empty.")
            .MaximumLength(2000);
    }
}
