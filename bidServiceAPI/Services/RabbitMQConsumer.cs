using System.Text;
using System.Text.Json;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;
using BidServiceAPI.Models;

public class RabbitMQListener : BackgroundService
{
    private readonly ILogger<RabbitMQListener> _logger;
    private readonly IConnection _connection;
    private readonly IModel _channel;
    private readonly IConfiguration _config;
    private readonly QueueNameProvider _queueNameProvider; // For at dele kønavnet
    private string? _activeItemId;

    public RabbitMQListener(ILogger<RabbitMQListener> logger, IConfiguration config, QueueNameProvider queueNameProvider)
    {
        _logger = logger;
        _config = config;
        _queueNameProvider = queueNameProvider;

        var rabbitHost = config["RABBITMQ_HOST"] ?? "localhost";
        var factory = new ConnectionFactory { HostName = rabbitHost };

        _connection = factory.CreateConnection();
        _channel = _connection.CreateModel();
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            var now = DateTime.UtcNow;

            // Start auction logic at 7:00
            if (now.Hour == 7 && now.Minute == 0 && _activeItemId == null)
            {
                _logger.LogInformation("Starting auction for the day...");
                StartAuction();
            }

            // Stop auction logic at 18:00
            if (now.Hour == 18 && now.Minute == 0 && _activeItemId != null)
            {
                _logger.LogInformation("Stopping auction for the day...");
                StopAuction();
            }

            await Task.Delay(TimeSpan.FromMinutes(1), stoppingToken);
        }
    }

    public void StartAuction()
    {
        var queueName = _config["TodaysAuctionsRabbitQueue"] ?? "TodaysAuctions";

        // Ensure the queue exists
        _channel.QueueDeclare(
            queue: queueName,
            durable: false,
            exclusive: false,
            autoDelete: false,
            arguments: null
        );

        // Fetch a single message
        var result = _channel.BasicGet(queueName, autoAck: true); // Henter én besked fra køen
        if (result == null)
        {
            _logger.LogWarning("No auctions available in the queue.");
            return;
        }
                                                                        
        var message = Encoding.UTF8.GetString(result.Body.ToArray());
        var auction = JsonSerializer.Deserialize<Auction>(message);

        if (auction == null)
        {
            _logger.LogError("Failed to deserialize auction message.");
            return;
        }

        _activeItemId = auction.Id;
        _logger.LogInformation("Auction started for ItemId {ItemId}.", _activeItemId);

        // Declare queue for bids
        var bidQueueName = $"{_activeItemId}Queue";
        _channel.QueueDeclare(
            queue: bidQueueName,
            durable: false,
            exclusive: false,
            autoDelete: false,
            arguments: null
        );

        // Gem kønavnet i QueueNameProvider
        _queueNameProvider.SetActiveQueueName(bidQueueName);

        _logger.LogInformation("Bid queue {QueueName} declared for ItemId {ItemId}.", bidQueueName, _activeItemId);
    }   


    private void StopAuction()
    {
        if (_activeItemId == null)
        {
            _logger.LogWarning("No active auction to stop.");
            return;
        }

        var bidQueueName = $"{_activeItemId}Queue";
        _logger.LogInformation("Stopping auction for ItemId {ItemId}. Deleting queue {QueueName}.", _activeItemId, bidQueueName);

        // Slet køen, hvis det ønskes
        _channel.QueueDelete(bidQueueName);

        // Nulstil aktiv auktion
        _activeItemId = null;

        // Ryd kønavn i QueueNameProvider
        _queueNameProvider.SetActiveQueueName(null);
    }

    public override void Dispose()
    {
        _channel?.Close();
        _connection?.Close();
        base.Dispose();
    }
}
