using LoyaltyPoints.Api.Services;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers();
builder.Services.AddSingleton<ILoyaltyService, InMemoryLoyaltyService>();

var app = builder.Build();

app.MapControllers();

app.Run();

public partial class Program;
