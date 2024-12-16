using Microsoft.AspNetCore.Builder;
using NLog;
using NLog.Web;
using RabbitMQ.Client;

var builder = WebApplication.CreateBuilder(args);

// Disable HTTPS Redirection Middleware
builder.Services.Configure<Microsoft.AspNetCore.HttpsPolicy.HttpsRedirectionOptions>(options =>
{
    options.HttpsPort = null; // Remove the HTTPS port
});


// NLog setup
var logger = NLog.LogManager.Setup().LoadConfigurationFromAppSettings()
        .GetCurrentClassLogger();
logger.Debug("init main");

// Add services to the container
builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();
builder.Services.AddMemoryCache();

// Registrer RabbitMQListener som singleton, så den kan injiceres i controlleren
builder.Services.AddSingleton<QueueNameProvider>();
builder.Services.AddSingleton<RabbitMQListener>(); // <-- Registrér som singleton
builder.Services.AddSingleton<RabbitMQPublisher>();

// Registrer BidProcessingService som Singleton i stedet for HostedService
builder.Services.AddSingleton<BidProcessingService>();



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
