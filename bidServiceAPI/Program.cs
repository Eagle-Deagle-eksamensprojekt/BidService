using Microsoft.AspNetCore.Builder;
using NLog;
using NLog.Web;
using RabbitMQ.Client;

var builder = WebApplication.CreateBuilder(args);

// NLog setup
var logger = NLog.LogManager.Setup().LoadConfigurationFromAppSettings()
        .GetCurrentClassLogger();
logger.Debug("init main");

// Add services to the container
builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();
builder.Services.AddMemoryCache();

// Registrér RabbitMQListener både som en singleton og hosted service
builder.Services.AddSingleton<RabbitMQListener>();
builder.Services.AddHostedService(sp => sp.GetRequiredService<RabbitMQListener>());
builder.Services.AddSingleton<RabbitMQPublisher>();


// NLog and HttpClient
builder.Logging.ClearProviders();
builder.Host.UseNLog();
builder.Logging.AddConsole();
builder.Services.AddHttpClient();

var app = builder.Build();

// Configure the HTTP request pipeline
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseHttpsRedirection();
app.UseAuthorization();
app.MapControllers();
app.Run();
