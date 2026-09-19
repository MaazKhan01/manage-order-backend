using DmOrder.Domain.Exceptions;
using DmOrder.Domain.Orders;

namespace DmOrder.Domain.Tests.Orders;

public class OrderStatusTests
{
    private static Order CreateOrder() =>
        Order.Create(Guid.CreateVersion7(), Guid.CreateVersion7(), 1, null, null, null, null);

    [Fact]
    public void NewOrderStartsUnpaidAndUnactioned()
    {
        var order = CreateOrder();

        order.Status.ShouldBe(OrderStatus.New);
        order.PaymentStatus.ShouldBe(PaymentStatus.Unpaid);
    }

    [Theory]
    [InlineData(OrderStatus.Confirmed)]
    [InlineData(OrderStatus.InProgress)]
    [InlineData(OrderStatus.ReadyForDelivery)]
    [InlineData(OrderStatus.Completed)]
    [InlineData(OrderStatus.Cancelled)]
    public void AnOpenOrderCanMoveAnywhere(OrderStatus target)
    {
        // Skipping ahead is allowed: a seller who finishes a small order in an hour should not have
        // to click through every stage to say so.
        var order = CreateOrder();

        Should.NotThrow(() => order.ChangeStatus(target));
        order.Status.ShouldBe(target);
    }

    [Fact]
    public void AnOpenOrderCanMoveBackwards()
    {
        var order = CreateOrder();
        order.ChangeStatus(OrderStatus.ReadyForDelivery);

        // Mistakes happen while the work is still open.
        Should.NotThrow(() => order.ChangeStatus(OrderStatus.InProgress));
    }

    [Theory]
    [InlineData(OrderStatus.Completed)]
    [InlineData(OrderStatus.Cancelled)]
    public void AClosedOrderCannotBeReopened(OrderStatus finalStatus)
    {
        // A completed or cancelled order is a closed record. Reopening it silently would let history
        // be rewritten after the fact.
        var order = CreateOrder();
        order.ChangeStatus(finalStatus);

        Should.Throw<BusinessRuleException>(() => order.ChangeStatus(OrderStatus.InProgress));
        order.Status.ShouldBe(finalStatus);
    }

    [Fact]
    public void SettingTheSameStatusIsAllowedAndChangesNothing()
    {
        var order = CreateOrder();
        order.ChangeStatus(OrderStatus.Completed);

        // Idempotent: a double-click must not be an error.
        Should.NotThrow(() => order.ChangeStatus(OrderStatus.Completed));
    }

    [Fact]
    public void RejectsAStatusCastFromAnArbitraryInteger() =>
        Should.Throw<BusinessRuleException>(() => CreateOrder().ChangeStatus((OrderStatus)99));

    [Fact]
    public void CompletedAndCancelledAreFinal()
    {
        OrderStatus.Completed.IsFinal().ShouldBeTrue();
        OrderStatus.Cancelled.IsFinal().ShouldBeTrue();
        OrderStatus.New.IsFinal().ShouldBeFalse();
        OrderStatus.ReadyForDelivery.IsFinal().ShouldBeFalse();
    }

    [Fact]
    public void TotalIsNullWhenNothingWasPriced()
    {
        // Zero would read as "free", which is a different and wrong statement.
        var order = CreateOrder();
        order.AddItem(null, "Custom Cake", null, 1);

        order.TotalAmount.ShouldBeNull();
    }

    [Fact]
    public void TotalSumsPricedLines()
    {
        var order = CreateOrder();
        order.AddItem(null, "Cake", 4500m, 2);

        order.TotalAmount.ShouldBe(9000m);
    }

    [Fact]
    public void RejectsAQuantityBelowOne() =>
        Should.Throw<BusinessRuleException>(() => CreateOrder().AddItem(null, "Cake", null, 0));

    [Fact]
    public void RejectsANegativeTotal() =>
        Should.Throw<BusinessRuleException>(() => CreateOrder().SetTotal(-1m));

    [Fact]
    public void SellerCanSetATotalOnAnUnpricedOrder()
    {
        // Most custom work is quoted after the seller reads the details.
        var order = CreateOrder();
        order.AddItem(null, "Custom Lehenga", null, 1);

        order.SetTotal(25_000m);

        order.TotalAmount.ShouldBe(25_000m);
    }
}

public class CustomerTests
{
    [Theory]
    [InlineData("0300 123 4567", "03001234567")]
    [InlineData("0300-123-4567", "03001234567")]
    [InlineData("(0300) 1234567", "03001234567")]
    [InlineData("+92 300 1234567", "+923001234567")]
    public void PhoneNumbersAreNormalisedForMatching(string input, string expected)
    {
        // "0300 123 4567" and "03001234567" must be the same customer, or a seller ends up with
        // duplicate records for one person.
        Customer.NormalisePhone(input).ShouldBe(expected);
    }

    [Fact]
    public void TheLeadingPlusIsKept()
    {
        // It is the difference between a local and an international number, so stripping it would
        // merge two different people.
        Customer.NormalisePhone("+923001234567").ShouldNotBe(Customer.NormalisePhone("923001234567"));
    }

    [Fact]
    public void BlanksFromALaterOrderDoNotEraseWhatIsAlreadyKnown()
    {
        var customer = Customer.Create(
            Guid.CreateVersion7(), "Ayesha", "03001234567", "ayesha@example.com", "12 Gulberg");

        // Skipping the email field on a second order is not a request to forget the first one.
        customer.UpdateFromOrder("Ayesha Khan", null, null);

        customer.Name.ShouldBe("Ayesha Khan");
        customer.Email.ShouldBe("ayesha@example.com");
        customer.AddressText.ShouldBe("12 Gulberg");
    }

    [Fact]
    public void RequiresAPhoneNumber() =>
        Should.Throw<BusinessRuleException>(
            () => Customer.Create(Guid.CreateVersion7(), "Ayesha", "  ", null, null));
}
