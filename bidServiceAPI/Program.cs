using System.Text;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Builder;
using Microsoft.IdentityModel.Tokens;
using NLog;
using NLog.Web;
using RabbitMQ.Client;
using VaultSharp;
using VaultSharp.V1.AuthMethods.Token;


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

// Vault-integration
var vaultToken = Environment.GetEnvironmentVariable("VAULT_TOKEN") 
                 ?? throw new Exception("Vault token not found");
var vaultUrl = Environment.GetEnvironmentVariable("VAULT_URL") 
               ?? "http://vault:8200"; // Standard Vault URL

var authMethod = new TokenAuthMethodInfo(vaultToken);
var vaultClientSettings = new VaultClientSettings(vaultUrl, authMethod);
var vaultClient = new VaultClient(vaultClientSettings);

var kv2Secret = await vaultClient.V1.Secrets.KeyValue.V2.ReadSecretAsync(path: "Secrets", mountPoint: "secret");
var jwtSecret = kv2Secret.Data.Data["jwtSecret"]?.ToString() ?? throw new Exception("jwtSecret not found in Vault.");
var jwtIssuer = kv2Secret.Data.Data["jwtIssuer"]?.ToString() ?? throw new Exception("jwtIssuer not found in Vault.");


// Register JWT authentication
builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidateAudience = true,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            ValidIssuer = jwtIssuer,
            ValidAudience = "http://localhost", // Tilpas efter behov
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtSecret))
        };
    });

builder.Services.AddAuthorization();

var app = builder.Build();

// Configure the HTTP request pipeline
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseHttpsRedirection();
app.UseAuthentication(); // Auhorization
app.UseAuthorization();
app.MapControllers();
app.Run();
