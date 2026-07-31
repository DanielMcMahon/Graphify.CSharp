using SampleApp.Domain;
using SampleApp.Services;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddScoped<IOrderRepository, InMemoryOrderRepository>();
builder.Services.AddScoped<OrderService>();
var app = builder.Build();
app.Run();
