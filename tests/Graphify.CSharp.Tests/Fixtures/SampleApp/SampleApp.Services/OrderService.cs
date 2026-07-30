using SampleApp.Domain;

namespace SampleApp.Services;

public sealed class OrderService
{
    private readonly IOrderRepository _repository;

    public OrderService(IOrderRepository repository)
    {
        _repository = repository;
    }

    public async Task CreateOrderAsync(string id)
    {
        var order = new Order { Id = id };
        await _repository.SaveAsync(order).ConfigureAwait(false);
    }
}
