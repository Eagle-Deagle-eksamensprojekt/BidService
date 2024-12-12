using System.Text;
using System.Text.Json;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;
using BidServiceAPI.Models;

// Background service that listens for incoming bids on a RabbitMQ queue
public class RabbitMQListener : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Implement the logic to run in the background service
        while (!stoppingToken.IsCancellationRequested) 
        {
            await Task.Delay(1000, stoppingToken); // Example delay
        }
    }

    private readonly ILogger<RabbitMQListener> _logger;
    private readonly IConnection _connection;
    private readonly IModel _channel;
    private readonly Dictionary<string, CancellationTokenSource> _activeListeners;
    private readonly IConfiguration _config;


    public RabbitMQListener(ILogger<RabbitMQListener> logger, IConfiguration config)
    {
        _logger = logger;
        _config = config;

        var rabbitHost = config["RABBITMQ_HOST"] ?? "localhost";
        var factory = new ConnectionFactory() { HostName = rabbitHost };

        _connection = factory.CreateConnection();
        _channel = _connection.CreateModel();
        _activeListeners = new Dictionary<string, CancellationTokenSource>();
    }

    // Stop listening for a specific queue
    public void StopListening(string itemId)
    {
        if (_activeListeners.TryGetValue(itemId, out var cancellationTokenSource))
        {
            cancellationTokenSource.Cancel();
            _activeListeners.Remove(itemId);
            _logger.LogInformation("Stopped listening on queue for item {ItemId}.", itemId);
        }
    }

    public async Task ListenOnQueue() // , CancellationToken token)
    {
        var queueName = _config["TodaysAuctions"];
        // Kan der sættes en timer på? Således den stopper efter en given tid?
        _channel.QueueDeclare(queue: queueName, durable: false, exclusive: false, autoDelete: false, arguments: null);

        var consumer = new EventingBasicConsumer(_channel);
        consumer.Received += (model, ea) =>
        {
            var body = ea.Body.ToArray();
            var message = Encoding.UTF8.GetString(body);
            var auction = JsonSerializer.Deserialize<Auction>(message);

            ProcessBid(auction!);
        };

        _channel.BasicConsume(queue: queueName, autoAck: true, consumer: consumer);
        _logger.LogInformation($"Started listening for active auctions on queue {queueName}.");
    }

    private void ProcessBid(Auction auction)
    {
        _logger.LogInformation($"ItemId {auction.ItemId} is on auction today.");
        // Implement logic for processing the bid
        // Her skal itemId sendes videre til RabbitMQPublisher
        // Kan der også udtrækkes hele auction objektet? Således endDate for auction kan gemmes i cash for placeBid
    }


    //     public void Dispose()
    public override void Dispose()
    {
        foreach (var cts in _activeListeners.Values)
        {
            cts.Cancel();
        }

        _channel?.Close();
        _connection?.Close();
        base.Dispose();
    }
}

