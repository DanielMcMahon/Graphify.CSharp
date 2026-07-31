using SampleApp.Domain;

namespace SampleApp.Services;

public sealed class InMemoryOrderRepository : IOrderRepository
{
    public Task SaveAsync(Order order) => Task.CompletedTask;
}
