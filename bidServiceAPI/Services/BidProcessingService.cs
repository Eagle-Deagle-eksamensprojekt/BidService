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
        await StartListening(stoppingToken); // Start listening for bids
    }

    public async Task StartListening(CancellationToken stoppingToken)
    {
        var queueName = _queueNameProvider.GetActiveItemId() + "bid"; // Setting the queue name

        // Ensure the queue exists
        _channel.QueueDeclare(
            queue: queueName,
            durable: false,
            exclusive: false,
            autoDelete: false,
            arguments: null
        );

        _logger.LogInformation("Listening for bids on queue {QueueName}.", queueName);

        _consumer = new EventingBasicConsumer(_channel); // Basic consumer for receiving 1 mesaage at a time
        _consumer.Received += async (model, ea) =>
        {
            try
            {
                // Deserialize incoming bid
                var body = ea.Body.ToArray(); // Get the body of the message
                var message = Encoding.UTF8.GetString(body); // Convert the body to a string
                var bid = JsonSerializer.Deserialize<Bid>(message); // Deserialize the string to a Bid object

                if (bid == null)
                {
                    _logger.LogWarning("Received a null bid. Ignoring message.");
                    return; // Ignore the message if the bid is null
                }

                _logger.LogInformation("Received bid for ItemId {ItemId}: {Amount}.", bid.ItemId, bid.Amount);

                // Validate the bid
                var isValid = await ValidateAuctionableItem(bid.ItemId!); // Validate the bid
                if (isValid == null)
                {
                    _logger.LogWarning("Bid for ItemId {ItemId} is not valid. Ignoring bid.", bid.ItemId);
                    return; // Ignore the bid if it is not valid
                }

                // Forward valid bid to auctionService queue
                PublishBidToAuctionService(bid); // Publish the bid to the auctionService queue
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error processing bid.");
            }
        };

        _channel.BasicConsume(queue: queueName, autoAck: true, consumer: _consumer); // Start consuming messages

        // Keep the service alive
        while (!stoppingToken.IsCancellationRequested) // Keep the service alive
        {
            await Task.Delay(1000, stoppingToken); // Delay for 1 second
        }
    }

    // Start listener for bid ingress
    public void StartListenerBidIngress()
    {
        if (_consumer != null)
        {
            _logger.LogWarning("Listener is already running on queue: {itemId}bid.", _queueNameProvider.GetActiveItemId());
            return; // Listener is already running
        }
 
        var cts = new CancellationTokenSource(); // Create a cancellation token source
        StartListening(cts.Token).ConfigureAwait(false); // Start listening for bids
        _logger.LogInformation("Listener started."); // Log that the listener has started
    }

    public async Task<TodaysAuctionMessage?> ValidateAuctionableItem(string itemId)
    {
        // Check cache
        if (_memoryCache.TryGetValue(itemId, out DateTimeOffset cachedEndTime)) // Check if the item is in the cache
        {
            var currentTime = DateTimeOffset.UtcNow; // Get the current time
            if (currentTime <= cachedEndTime) 
            {
                _logger.LogInformation("Item {ItemId} found in cache. Auction is valid until {AuctionEndTime}.", itemId, cachedEndTime);
                return new TodaysAuctionMessage // Return the auction details
                {
                    StartAuctionDateTime = currentTime,
                    EndAuctionDateTime = cachedEndTime
                };
            }

            _memoryCache.Remove(itemId); // Remove the item from the cache
            _logger.LogInformation("Item {ItemId} auction has expired. Removed from cache.", itemId);
        }

        // Make HTTP call to AuctionService
        var client = _httpClientFactory.CreateClient(); // Create a new HTTP client
        var url = $"{_config["AuctionServiceEndpoint"]}/auctionable/{itemId}"; // Set the URL for the HTTP call
        _logger.LogInformation("Validating item {ItemId} against auctionService at {Url}.", itemId, url);

        try
        {
            var response = await client.GetAsync(url); // Make the HTTP call
            if (!response.IsSuccessStatusCode) 
            {
                _logger.LogWarning("Item {ItemId} is not auctionable. Response status code: {StatusCode}.", itemId, response.StatusCode);
                return null;
            }

            var item = JsonSerializer.Deserialize<Auction>(await response.Content.ReadAsStringAsync()); // Deserialize the response
            if (item == null || DateTimeOffset.UtcNow > item.EndAuctionDateTime) 
            {
                _logger.LogInformation("Item {ItemId} is not auctionable or auction period has ended.", itemId);
                return null;
            }

            // Cache auction end time
            CacheAuctionEndTime(itemId, item.EndAuctionDateTime); // Cache the auction end time

            return new TodaysAuctionMessage // Return the auction details
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

    private void CacheAuctionEndTime(string itemId, DateTimeOffset endAuctionTime) // Cache the auction end time
    {
        _memoryCache.Set(itemId, endAuctionTime, new MemoryCacheEntryOptions // Set the cache entry
        {
            AbsoluteExpiration = endAuctionTime,
            Priority = CacheItemPriority.High
        });
        _logger.LogInformation("Cached auction end time for item {ItemId}. Auction valid until {AuctionEndTime}.", itemId, endAuctionTime);
    }

    private void PublishBidToAuctionService(Bid bid) // Publish the bid to the auctionService queue
    {
        var success = _rabbitMQPublisher.PublishBidToQueue(bid); // Publish the bid to the auctionService queue
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
}
