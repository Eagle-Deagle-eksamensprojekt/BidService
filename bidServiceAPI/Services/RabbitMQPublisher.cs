using System.Text;
using System.Text.Json;
using RabbitMQ.Client;
using BidServiceAPI.Models;

public class RabbitMQPublisher
{
    private readonly ILogger<RabbitMQPublisher> _logger;
    private readonly IConnection _connection;
    private readonly IModel _channel;
    private readonly QueueNameProvider _queueNameProvider;

    public RabbitMQPublisher(ILogger<RabbitMQPublisher> logger, IConfiguration config, QueueNameProvider queueNameProvider)
    {
        _logger = logger;
        _queueNameProvider = queueNameProvider;

        var rabbitHost = config["RABBITMQ_HOST"] ?? "localhost";
        var factory = new ConnectionFactory() { HostName = rabbitHost };

        _connection = factory.CreateConnection();
        _channel = _connection.CreateModel();
    }

    public bool PublishBidToQueue(Bid bid)
    {
        try
        {
            if (bid == null || string.IsNullOrWhiteSpace(bid.ItemId))
            {
                _logger.LogError("Invalid bid or missing ItemId.");
                return false;
            }

            var queueName = _queueNameProvider.GetActiveQueueName();
            //var queueItemId = _queueNameProvider.GetActiveItemId();
            if (string.IsNullOrEmpty(queueName))
            {
                _logger.LogError("No active queue name is set. Cannot publish bid.");
                return false;
            }

            _logger.LogInformation("QueueName: {QueueName} Bid ItemId: {bid.ItemId}+Queue", queueName, bid.ItemId);
            if (queueName != bid.ItemId + "Queue") // Denne skal blokere at der bliver publishet til en forkert kø
            {
                _logger.LogError("ItemId {ItemId} does not match Queue name {QueueName}. Cannot publish bid.", bid.ItemId, queueName);
                return false;
            }


            // Declare the queue if it doesn't exist
            _channel.QueueDeclare(
                queue: queueName,
                durable: false,
                exclusive: false,
                autoDelete: false,
                arguments: null
            );

            // Serialize bid to JSON
            var message = JsonSerializer.Serialize(bid);
            var body = Encoding.UTF8.GetBytes(message);

            // Publish to the queue
            _channel.BasicPublish(
                exchange: "",
                routingKey: queueName,
                basicProperties: null,
                body: body
            );

            _logger.LogInformation("Published bid with amount {Amount} for {BidId} to queue {QueueName}.", bid.Amount, bid.ItemId, queueName);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to publish bid.");
            return false;
        }
    }


    public void Dispose()
    {
        _channel?.Close();
        _connection?.Close();
    }
}
