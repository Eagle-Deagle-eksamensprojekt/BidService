using System.Text;
using System.Text.Json;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;
using BidServiceAPI.Models;

// Background service that listens for incoming bids on a RabbitMQ queue
public class RabbitMQPublisher : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Implement the logic to run in the background service
        while (!stoppingToken.IsCancellationRequested) 
        {
            await Task.Delay(1000, stoppingToken); // Example delay
        }
    }

    private readonly ILogger<RabbitMQPublisher> _logger;
    private readonly IConnection _connection;
    private readonly IModel _channel;
    private readonly Dictionary<string, CancellationTokenSource> _activeListeners;
    private readonly IConfiguration _config;

    public RabbitMQPublisher(ILogger<RabbitMQPublisher> logger, IConfiguration config)
    {
        _logger = logger;
        _config = config;

        var rabbitHost = _config["RABBITMQ_HOST"] ?? "localhost";
        var factory = new ConnectionFactory() { HostName = rabbitHost };

        _connection = factory.CreateConnection();
        _channel = _connection.CreateModel();
    }
    

    public async Task<bool> PublishToRabbitMQ(Bid bid)
        {

            try
            {
                // Queue name based on AuctionId
                var itemId = _config["ITEM_ID"];
                if (itemId == null)
                {
                    _logger.LogError("Failed to get ItemId from bid.");
                    return false;
                }
                var queueName = $"{itemId}Queue";

                // Declare the queue (only necessary the first time)
                _channel.QueueDeclare(
                    queue: queueName,
                    durable: false,
                    exclusive: false,
                    autoDelete: false,
                    arguments: null
                );

                // Serialize bid object to JSON
                var message = JsonSerializer.Serialize(bid);
                var body = Encoding.UTF8.GetBytes(message);

                // Publish the message to RabbitMQ
                _channel.BasicPublish(
                    exchange: "",
                    routingKey: queueName,
                    basicProperties: null,
                    body: body
                );

                _logger.LogInformation("Published bid {BidId} to RabbitMQ queue {QueueName}.", bid.Id, queueName);
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to publish bid {BidId} to RabbitMQ.", bid.Id);
                return false;
            }
        }
}

