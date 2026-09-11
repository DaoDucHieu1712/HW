public class OrderCancellationTests
{
    [Fact]
    public void Cancel_BeforeShipping_MarksTheOrderCancelled()
    {
        var order = new OrderBuilder().WithStatus(OrderStatus.Placed).Build();

        var result = order.Cancel("customer changed their mind");

        Assert.True(result.IsSuccess);
        Assert.Equal(OrderStatus.Cancelled, order.Status);
    }

    [Fact]
    public void Cancel_AfterShipping_IsRefusedAndLeavesTheOrderUntouched()
    {
        var order = new OrderBuilder().WithStatus(OrderStatus.Shipped).Build();

        var result = order.Cancel("too late");

        Assert.True(result.IsFailure);
        Assert.Equal(OrderStatus.Shipped, order.Status);   // no partial mutation
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public void Cancel_WithoutAReason_IsRefused(string? reason)
    {
        var order = new OrderBuilder().WithStatus(OrderStatus.Placed).Build();

        Assert.True(order.Cancel(reason!).IsFailure);
    }
}
