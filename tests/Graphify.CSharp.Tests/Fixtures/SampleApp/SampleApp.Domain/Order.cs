namespace SampleApp.Domain;

public interface IOrderRepository
{
    Task SaveAsync(Order order);
}

public sealed class Order
{
    public required string Id { get; init; }
}
