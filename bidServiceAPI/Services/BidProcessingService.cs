using System.Text;
using System.Text.Json;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;
using BidServiceAPI.Models;
using Microsoft.Extensions.Caching.Memory;

public class BidProcessingService : BackgroundService
{
    private readonly ILogger<BidProcessingService> _logger;
    private readonly RabbitMQPublisher _rabbitMQPublisher;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IMemoryCache _memoryCache;

    private readonly IConnection _connection;
    private readonly IModel _channel;
    private readonly IConfiguration _config;
    private readonly QueueNameProvider _queueNameProvider;
    private EventingBasicConsumer? _consumer;

    public BidProcessingService(
        ILogger<BidProcessingService> logger,
        RabbitMQPublisher rabbitMQPublisher,
        IConfiguration config,
        QueueNameProvider queueNameProvider,
        IHttpClientFactory httpClientFactory,
        IMemoryCache memoryCache)
    {
        _logger = logger;
        _rabbitMQPublisher = rabbitMQPublisher;
        _config = config;
        _queueNameProvider = queueNameProvider;
        _httpClientFactory = httpClientFactory;
        _memoryCache = memoryCache;

        var rabbitHost = _config["RABBITMQ_HOST"] ?? "localhost";
        var factory = new ConnectionFactory { HostName = rabbitHost };

        _connection = factory.CreateConnection();
        _channel = _connection.CreateModel();
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await StartListening(stoppingToken);
    }

    public async Task StartListening(CancellationToken stoppingToken)
    {
        var queueName = _queueNameProvider.GetActiveItemId() + "bid";

        // Ensure the queue exists
        _channel.QueueDeclare(
            queue: queueName,
            durable: false,
            exclusive: false,
            autoDelete: false,
            arguments: null
        );

        _logger.LogInformation("Listening for bids on queue {QueueName}.", queueName);

        _consumer = new EventingBasicConsumer(_channel);
        _consumer.Received += async (model, ea) =>
        {
            try
            {
                // Deserialize incoming bid
                var body = ea.Body.ToArray();
                var message = Encoding.UTF8.GetString(body);
                var bid = JsonSerializer.Deserialize<Bid>(message);

                if (bid == null)
                {
                    _logger.LogWarning("Received a null bid. Ignoring message.");
                    return;
                }

                _logger.LogInformation("Received bid for ItemId {ItemId}: {Amount}.", bid.ItemId, bid.Amount);

                // Validate the bid
                var isValid = await ValidateAuctionableItem(bid.ItemId!);
                if (isValid == null)
                {
                    _logger.LogWarning("Bid for ItemId {ItemId} is not valid. Ignoring bid.", bid.ItemId);
                    return;
                }

                // Forward valid bid to auctionService queue
                PublishBidToAuctionService(bid);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error processing bid.");
            }
        };

        _channel.BasicConsume(queue: queueName, autoAck: true, consumer: _consumer);

        // Keep the service alive
        while (!stoppingToken.IsCancellationRequested)
        {
            await Task.Delay(1000, stoppingToken);
        }
    }

    // Start listener for bid ingress
    public void StartListenerBidIngress()
    {
        if (_consumer != null)
        {
            _logger.LogWarning("Listener is already running on queue: {itemId}bid.", _queueNameProvider.GetActiveItemId());
            return;
        }

        var cts = new CancellationTokenSource();
        StartListening(cts.Token).ConfigureAwait(false);
        _logger.LogInformation("Listener started.");
    }

    public async Task<TodaysAuctionMessage?> ValidateAuctionableItem(string itemId)
    {
        // Check cache
        if (_memoryCache.TryGetValue(itemId, out DateTimeOffset cachedEndTime))
        {
            var currentTime = DateTimeOffset.UtcNow;
            if (currentTime <= cachedEndTime)
            {
                _logger.LogInformation("Item {ItemId} found in cache. Auction is valid until {AuctionEndTime}.", itemId, cachedEndTime);
                return new TodaysAuctionMessage
                {
                    StartAuctionDateTime = currentTime,
                    EndAuctionDateTime = cachedEndTime
                };
            }

            _memoryCache.Remove(itemId);
            _logger.LogInformation("Item {ItemId} auction has expired. Removed from cache.", itemId);
        }

        // Make HTTP call to AuctionService
        var client = _httpClientFactory.CreateClient();
        var url = $"{_config["AuctionServiceEndpoint"]}/auctionable/{itemId}";
        _logger.LogInformation("Validating item {ItemId} against auctionService at {Url}.", itemId, url);

        try
        {
            var response = await client.GetAsync(url);
            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("Item {ItemId} is not auctionable. Response status code: {StatusCode}.", itemId, response.StatusCode);
                return null;
            }

            var item = JsonSerializer.Deserialize<Auction>(await response.Content.ReadAsStringAsync());
            if (item == null || DateTimeOffset.UtcNow > item.EndAuctionDateTime)
            {
                _logger.LogInformation("Item {ItemId} is not auctionable or auction period has ended.", itemId);
                return null;
            }

            // Cache auction end time
            CacheAuctionEndTime(itemId, item.EndAuctionDateTime);

            return new TodaysAuctionMessage
            {
                StartAuctionDateTime = item.StartAuctionDateTime,
                EndAuctionDateTime = item.EndAuctionDateTime
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error validating item {ItemId} auctionable status.", itemId);
            return null;
        }
    }

    private void CacheAuctionEndTime(string itemId, DateTimeOffset endAuctionTime)
    {
        _memoryCache.Set(itemId, endAuctionTime, new MemoryCacheEntryOptions
        {
            AbsoluteExpiration = endAuctionTime,
            Priority = CacheItemPriority.High
        });
        _logger.LogInformation("Cached auction end time for item {ItemId}. Auction valid until {AuctionEndTime}.", itemId, endAuctionTime);
    }

    private void PublishBidToAuctionService(Bid bid)
    {
        var success = _rabbitMQPublisher.PublishBidToQueue(bid);
        if (success)
        {
            _logger.LogInformation("Published bid for ItemId {ItemId} to AuctionService queue.", bid.ItemId);
        }
        else
        {
            _logger.LogError("Failed to publish bid for ItemId {ItemId} to AuctionService queue.", bid.ItemId);
        }
    }

    public override void Dispose()
    {
        _channel?.Close();
        _connection?.Close();
        base.Dispose();
    }
/*
    public class AuctionDetails
    {
        public DateTimeOffset StartAuctionDateTime { get; set; }
        public DateTimeOffset EndAuctionDateTime { get; set; }
    }*/
}
