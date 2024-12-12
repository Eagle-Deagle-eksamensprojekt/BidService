using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using System;
using System.Threading.Tasks;
using BidServiceAPI.Models;
using System.Text.Json;
using RabbitMQ.Client;
using System.Text;
using System.Diagnostics;
using Microsoft.Extensions.Caching.Memory;



namespace BidService.Controllers
{
    [ApiController]
    [Route("[controller]")]
    public class BidController : ControllerBase
    {
        private readonly ILogger<BidController> _logger;
        private readonly IHttpClientFactory _httpClientFactory;
        private readonly IConnectionFactory _connectionFactory;
        private readonly IConfiguration _config;

        private readonly RabbitMQListener _rabbitMQListener;
        private readonly RabbitMQPublisher _rabbitMQPublisher;
        public BidController(ILogger<BidController> logger, IConfiguration config, IHttpClientFactory httpClientFactory, IConnectionFactory connectionFactory, RabbitMQListener rabbitMQListener, RabbitMQPublisher rabbitMQPublisher)

        private readonly IMemoryCache _memoryCache;
        public BidController(ILogger<BidController> logger, IConfiguration config, IHttpClientFactory httpClientFactory, IConnectionFactory connectionFactory, IMemoryCache memoryCache)

        {
            _logger = logger;
            _config = config;
            _httpClientFactory = httpClientFactory;
            _connectionFactory = connectionFactory;

            _rabbitMQListener = rabbitMQListener;
            _rabbitMQPublisher = rabbitMQPublisher;
        }

        /// <summary>
        /// Hent version af Service
        /// </summary>
        [HttpGet("version")]
        public async Task<Dictionary<string,string>> GetVersion()

            _memoryCache = memoryCache;
        }/*

        {
            var properties = new Dictionary<string, string>();
            var assembly = typeof(Program).Assembly;

            properties.Add("service", "OrderService");
            var ver = FileVersionInfo.GetVersionInfo(
                typeof(Program).Assembly.Location).ProductVersion ?? "N/A";
            properties.Add("version", ver);
            
            var hostName = System.Net.Dns.GetHostName();
            var ips = await System.Net.Dns.GetHostAddressesAsync(hostName);
            var ipa = ips.First().MapToIPv4().ToString() ?? "N/A";
            properties.Add("ip-address", ipa);
            
            return properties;
        }

        [HttpPost]
        public async Task<IActionResult> PlaceBid([FromBody] Bid newBid)
        {
            _logger.LogInformation("Placing bid on item {ItemId} for {Amount:C}.", newBid.ItemId, newBid.Amount);

            try
            {
                // Tjek om item er auctionable
                var auctionDetails = await IsItemAuctionable(newBid.ItemId);
                if (auctionDetails == null)
                {
                    _logger.LogWarning("Item {ItemId} does not exist or is not auctionable.", newBid.ItemId);
                    return BadRequest("Item is not auctionable or does not exist.");
                }

                var now = DateTimeOffset.UtcNow;
                if (now < auctionDetails.StartAuctionDateTime || now > auctionDetails.EndAuctionDateTime)
                {
                    _logger.LogWarning("Item {ItemId} is not auctionable at {CurrentTime}. Auction is valid from {Start} to {End}.", 
                        newBid.ItemId, now, auctionDetails.StartAuctionDateTime, auctionDetails.EndAuctionDateTime);
                    return BadRequest($"Item is not auctionable. Valid auction period: {auctionDetails.StartAuctionDateTime} to {auctionDetails.EndAuctionDateTime}");
                }

                _logger.LogInformation("Item {ItemId} is auctionable. Proceeding with bid.", newBid.ItemId);

                // Publish bid to RabbitMQ
                var published = await PublishToRabbitMQ(newBid);
                if (!published)
                {
                    _logger.LogError("Failed to publish bid for {ItemId} to RabbitMQ.", newBid.ItemId);
                    return StatusCode(500, "Failed to publish bid.");
                }


            // Publish bid to RabbitMQ
            var published = await _rabbitMQPublisher.PublishToRabbitMQ(newBid);
            if (!published)

                _logger.LogInformation("Bid for {ItemId} published successfully to RabbitMQ.", newBid.ItemId);
                return Ok(newBid); // Returner det nye bud
            }

            {
                _logger.LogError(ex, "An error occurred while placing bid for item {ItemId}.", newBid.ItemId);
                return StatusCode(500, "An error occurred while placing the bid.");
            }
        }



        private async Task<AuctionDetails?> IsItemAuctionable(string itemId)
        {
            // Check if the item's auction end time is in the cache
            if (_memoryCache.TryGetValue(itemId, out DateTimeOffset cachedAuctionEndTime))
            {
                var currentTime = DateTimeOffset.UtcNow;
                if (currentTime <= cachedAuctionEndTime)
                {
                    _logger.LogInformation("Item {ItemId} found in cache. Auction is valid until {AuctionEndTime}.", itemId, cachedAuctionEndTime);
                    return new AuctionDetails
                    {
                        StartAuctionDateTime = currentTime, // Dummy, as it's not cached
                        EndAuctionDateTime = cachedAuctionEndTime
                    };
                }

                // Remove expired cache
                _memoryCache.Remove(itemId);
                _logger.LogInformation("Item {ItemId} auction has expired. Removed from cache.", itemId);
                return null;
            }

            // Construct the request URL to check auctionable status
            var existsUrl = $"{_config["AuctionServiceEndpoint"]}/auctionable/{itemId}";
            _logger.LogInformation("Checking if item is auctionable at: {ExistsUrl}", existsUrl);

            var client = _httpClientFactory.CreateClient();
            HttpResponseMessage response;

            try
            {
                client.DefaultRequestHeaders.Add("Accept", "application/json");
                response = await client.GetAsync(existsUrl);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error while checking if item is auctionable.");
                return null; // Assume item is not auctionable on error
            }

            if (response.IsSuccessStatusCode)
            {
                // Deserialize the response into an `Item` object
                string responseContent = await response.Content.ReadAsStringAsync();
                var item = JsonSerializer.Deserialize<Item>(responseContent);

                if (item == null)
                {
                    _logger.LogWarning("Failed to deserialize item {ItemId} auctionable status.", itemId);
                    return null;
                }

                var now = DateTimeOffset.UtcNow;

                // Check if the current time is within the auction period
                if (now >= item.StartAuctionDateTime && now <= item.EndAuctionDateTime)
                {
                    _logger.LogInformation("Item {ItemId} is auctionable between {StartDate} and {EndDate}.", 
                        itemId, item.StartAuctionDateTime, item.EndAuctionDateTime);

                    // Cache the auction end time with an expiration
                    CacheAuctionEndTime(itemId, item.EndAuctionDateTime);
                    return new AuctionDetails
                    {
                        StartAuctionDateTime = item.StartAuctionDateTime,
                        EndAuctionDateTime = item.EndAuctionDateTime
                    };
                }

                _logger.LogInformation("Item {ItemId} is not auctionable. Current time: {CurrentTime}, StartDate: {StartDate}, EndDate: {EndDate}.",
                    itemId, now, item.StartAuctionDateTime, item.EndAuctionDateTime);
                return null;
            }

            _logger.LogWarning("Failed to determine if item {ItemId} is auctionable. Response status code: {StatusCode}.", itemId, response.StatusCode);
            return null; // Default to not auctionable if status is unknown
        }


        // Method to cache the auction end time
        private void CacheAuctionEndTime(string itemId, DateTimeOffset endAuctionTime)
        {
            var cacheExpiryOptions = new MemoryCacheEntryOptions
            {
                AbsoluteExpiration = endAuctionTime, // Expire the cache when the auction ends
                SlidingExpiration = null, // No sliding expiration since the auction end time is fixed
                Priority = CacheItemPriority.High
            };
            _memoryCache.Set(itemId, endAuctionTime, cacheExpiryOptions);
            _logger.LogInformation("Cached auction end time for item {ItemId}. Auction valid until {AuctionEndTime}.", itemId, endAuctionTime);
        }


        // simpel get for at teste om den kan hente bool på auctionable item
        // tager både itemid og datetime som parameter
        [HttpGet("auctionable/{itemId}")]
        public async Task<IActionResult> GetAuctionableItem(string itemId)
        {
            var item = await IsItemAuctionable(itemId);
            if (item == null)
            {
                return Ok(null); // Returnerer null, hvis ingen items findes
            }

            return Ok(item); // Returnerer 200 OK med items
        }


        // Her skal der implementeres en metode til at poste et bud til rabbitMQ
        // I metoden skal ovenstående metode kaldes for at tjekke om item er auctionable
        // Dertil skal der også valideres om det nye bud er højere end det nuværende højeste bud


    }
}