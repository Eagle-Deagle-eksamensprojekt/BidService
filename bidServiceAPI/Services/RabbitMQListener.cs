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
    private string? _activeItemEndDate;
    private readonly BidProcessingService _bidProcessingService;

    public RabbitMQListener(ILogger<RabbitMQListener> logger, IConfiguration config, QueueNameProvider queueNameProvider, BidProcessingService bidProcessingService)
    {
        _logger = logger;
        _config = config;
        _queueNameProvider = queueNameProvider;
        _bidProcessingService = bidProcessingService;

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

            // Start auction logic at 7:05
            if (now.Hour == 7 && now.Minute == 5 && _activeItemId == null)
            {
                _logger.LogInformation("Starting auction for the day...");
                StartAuction();
            }

            // Stop auction logic at 16:00
            if (now.Hour == 16 && now.Minute == 0 && _activeItemId != null)
            {
                _logger.LogInformation("Stopping auction for the day...");
                StopAuction();
            }

            await Task.Delay(TimeSpan.FromMinutes(1), stoppingToken); // Wait 1 minute
        }
    }

    public void StartAuction()
    {
        var queueName = _config["TodaysAuctionsRabbitQueue"] ?? "TodaysAuctions"; // Get the queue name

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

        // Deserialiser beskeden som AuctionMessage
        var message = Encoding.UTF8.GetString(result.Body.ToArray()); // Konverter beskeden til en streng
        var auctionMessage = JsonSerializer.Deserialize<TodaysAuctionMessage>(message); // Deserialiser strengen til en TodaysAuctionMessage

        if (auctionMessage == null || string.IsNullOrWhiteSpace(auctionMessage.ItemId)) // Hvis deserialiseringen fejler eller ItemId mangler
        {
            _logger.LogError("Failed to deserialize auction message or ItemId is missing.");
            return;
        }

        _activeItemId = auctionMessage.ItemId; // Sæt aktivt ItemId
        _activeItemEndDate = auctionMessage.EndAuctionDateTime.ToString(); // Sæt aktivt ItemEndDate
        _logger.LogInformation("Auction started for ItemId {ItemId}.", _activeItemId); 

        // Declare queue for bids to auctionService
        var bidQueueName = $"{_activeItemId}Queue"; // Opret kønavn
        _channel.QueueDeclare(
            queue: bidQueueName,
            durable: false,
            exclusive: false,
            autoDelete: false,
            arguments: null
        );

        // Gem kønavnet og itemId i QueueNameProvider
        _queueNameProvider.SetActiveQueueName(bidQueueName); // Sæt kønavn i QueueNameProvider
        var bidItemID = _activeItemId; // Gem aktivt ItemId
        _queueNameProvider.SetActiveItemId(bidItemID); // Sæt aktivt ItemId i QueueNameProvider

        //Opret kø for bid ingress
        StartQueueForBidIngress(); // Start kø for bid ingress
        _bidProcessingService.StartListenerBidIngress(); // Start listener for bid ingress

        _logger.LogInformation("Bid queue {QueueName} declared for ItemId {ItemId}.", bidQueueName, _activeItemId);
    }
 
    // Gemmer aktivt ItemId så det kan hentes i controller
    public string GetActiveItemId()
    {
        return _activeItemId ?? "No active ItemId"; // Returnerer aktivt ItemId eller "No active ItemId"
    }


    private void StopAuction()
    {
        if (_activeItemId == null)
        {
            _logger.LogWarning("No active auction to stop.");
            return; // Returner, hvis der ikke er nogen aktiv auktion
        }

        var bidQueueName = $"{_activeItemId}Queue"; // Opret kønavn
        _logger.LogInformation("Stopping auction for ItemId {ItemId}. Deleting queue {QueueName}.", _activeItemId, bidQueueName);

        // Slet køen, hvis det ønskes
        _channel.QueueDelete(bidQueueName); 

        // Nulstil aktiv auktion
        _activeItemId = null;

        // Ryd kønavn i QueueNameProvider
        _queueNameProvider.SetActiveQueueName(null!);
    }

    // Declare queue for bids incomming from ingress
    public void StartQueueForBidIngress()
    {
        var queueName = $"{_activeItemId}bid"; // Opret kønavn

        _channel.QueueDeclare(
            queue: queueName,
            durable: false,
            exclusive: false,
            autoDelete: false,
            arguments: null
        );

        _logger.LogInformation("Bid queue {QueueName} created for ItemId {ItemId}.", queueName, _activeItemId);
    }

    public override void Dispose()
    {
        _channel?.Close();
        _connection?.Close();
        base.Dispose();
    }
}
