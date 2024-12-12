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

    public RabbitMQListener(ILogger<RabbitMQListener> logger, IConfiguration config)
    {
        _logger = logger;
        _config = config;

        var rabbitHost = config["RABBITMQ_HOST"] ?? "localhost";
        var factory = new ConnectionFactory() { HostName = rabbitHost };

        _connection = factory.CreateConnection();
        _channel = _connection.CreateModel();
    }

    protected override Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Kør lytteren på en separat opgave
        Task.Run(() => ListenForAuctions(stoppingToken), stoppingToken);
        return Task.CompletedTask;
    }

    private void ListenForAuctions(CancellationToken token)
    {
        var auctionQueueName = _config["TodaysAuctionsRabbitQueue"] ?? "TodaysAuctions";

        _channel.QueueDeclare(
            queue: auctionQueueName,
            durable: false,
            exclusive: false,
            autoDelete: false,
            arguments: null
        );

        var consumer = new EventingBasicConsumer(_channel);
        consumer.Received += (model, ea) =>
        {
            var body = ea.Body.ToArray();
            var message = Encoding.UTF8.GetString(body);

            var auction = JsonSerializer.Deserialize<Auction>(message);
            if (auction != null)
            {
                _logger.LogInformation("Received auction for ItemId {ItemId}.", auction.Id);
                ProcessAuction(auction);
            }
        };

        _channel.BasicConsume(queue: auctionQueueName, autoAck: true, consumer: consumer);
        _logger.LogInformation("Started listening for auctions on queue {QueueName}.", auctionQueueName);

        // Forbliv i live, så længe token ikke er annulleret
        while (!token.IsCancellationRequested)
        {
            Thread.Sleep(1000); // Undgå unødvendig CPU-brug
        }
    }

    private void ProcessAuction(Auction auction)
    {
        var queueName = $"{auction.Id}Queue";
        _channel.QueueDeclare(
            queue: queueName,
            durable: false,
            exclusive: false,
            autoDelete: false,
            arguments: null
        );

        _logger.LogInformation("Queue {QueueName} created for ItemId {ItemId}.", queueName, auction.Id);
    }

    public override void Dispose()
    {
        _channel?.Close();
        _connection?.Close();
        base.Dispose();
    }
}
