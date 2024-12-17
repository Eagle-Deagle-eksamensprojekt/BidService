using Microsoft.AspNetCore.Mvc;
using BidServiceAPI.Models;
using System.Text.Json;
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
        private readonly IConfiguration _config;
        private readonly RabbitMQPublisher _rabbitMQPublisher; 
        private readonly IMemoryCache _memoryCache;
        private readonly RabbitMQListener _rabbitMQListener;
        private readonly BidProcessingService _BidProcessingService;
        public BidController(ILogger<BidController> logger, 
        IConfiguration config, 
        IHttpClientFactory httpClientFactory, 
        RabbitMQPublisher rabbitMQPublisher, 
        IMemoryCache memoryCache, 
        RabbitMQListener rabbitMQListener,
        BidProcessingService BidProcessingService)
        {
            _logger = logger;
            _config = config;
            _httpClientFactory = httpClientFactory;
            _memoryCache = memoryCache;
            _rabbitMQPublisher = rabbitMQPublisher;
            _rabbitMQListener = rabbitMQListener;
            _BidProcessingService = BidProcessingService;
        }

        /// <summary>
        /// Hent version af Service
        /// </summary>
        [HttpGet("version")]
        public async Task<Dictionary<string,string>> GetVersion()
        {
            var properties = new Dictionary<string, string>();

            var ver = FileVersionInfo.GetVersionInfo(
                typeof(Program).Assembly.Location).ProductVersion ?? "N/A";
            properties.Add("version", ver);

            return properties;
        }



        [HttpPost]
        public async Task<IActionResult> PlaceBid([FromBody] Bid newBid)
        {
            // Her får vi itemId fra headeren, som blev sendt af Nginx
            //var itemId = Request.Headers["X-Item-ID"].ToString() ?? newBid.ItemId;
            //var itemId = newBid.ItemId;
            string itemId;
            if (Request.Headers["X-Item-ID"].ToString() == "") // if for at tjekke om der er et itemid i headeren fra nginx // ved ikke om det header halløj virker
            { 
                _logger.LogInformation("No item ID found in header. Using item ID from bid.");
                itemId = newBid.ItemId!;
            } else {
                _logger.LogInformation("Item ID found in header. Using item ID from header.");
                itemId = Request.Headers["X-Item-ID"].ToString();
            }

            _logger.LogInformation("Placing bid on item {ItemId} for {Amount:C}.", itemId, newBid.Amount);

            try
            {
                // Tjek om item er auctionable
                var auctionDetails = await _BidProcessingService.ValidateAuctionableItem(itemId!);
                if (itemId != newBid.ItemId)
                {
                    _logger.LogWarning("ItemId {ItemId} does not match bid ItemId {BidItemId}. Cannot place bid.", itemId, newBid.ItemId);
                    return BadRequest("ItemId does not match bid ItemId.");
                }
        
                if (auctionDetails == null)
                {
                    _logger.LogWarning("Item {ItemId} does not exist or is not auctionable.", itemId);
                    return BadRequest("Item is not auctionable or does not exist.");
                }

                var now = DateTimeOffset.UtcNow;
                if (now < auctionDetails.StartAuctionDateTime || now > auctionDetails.EndAuctionDateTime)
                {
                    _logger.LogWarning("Item {ItemId} is not auctionable at {CurrentTime}. Auction is valid from {Start} to {End}.", 
                        itemId, now, auctionDetails.StartAuctionDateTime, auctionDetails.EndAuctionDateTime);
                    return BadRequest($"Item is not auctionable. Valid auction period: {auctionDetails.StartAuctionDateTime} to {auctionDetails.EndAuctionDateTime}");
                }

                _logger.LogInformation("Item {ItemId} is auctionable. Proceeding with bid.", itemId);

                // Publish bid to RabbitMQ
                var published = _rabbitMQPublisher.PublishBidToQueue(newBid);
                if (!published)
                {
                    _logger.LogError("Failed to publish bid to RabbitMQ.");
                    return StatusCode(500, "Failed to publish bid to RabbitMQ.");
                }

                return Ok(newBid); // Returner det nye bud
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "An error occurred while placing bid for item {ItemId}.", newBid.ItemId);
                return StatusCode(500, "An error occurred while placing the bid.");
            }
        }

/*
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
*/


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
            var item = await _BidProcessingService.ValidateAuctionableItem(itemId!);
            if (item == null)
            {
                return Ok(null); // Returnerer null, hvis ingen items findes
            }

            return Ok(item); // Returnerer 200 OK med items
        }


        // Her skal der implementeres en metode til at poste et bud til rabbitMQ
        // I metoden skal ovenstående metode kaldes for at tjekke om item er auctionable
        // Dertil skal der også valideres om det nye bud er højere end det nuværende højeste bud


        [HttpPost("start-listener")]
        public async Task<IActionResult> StartListener()
        {
            // Trigger ScheduleAuctions method manually
            _rabbitMQListener.StartAuction();
            return Ok(new { message = "BidService listener started." }); // Return JSON        
        }

    /*
        [HttpPost("StartAuction")]
        public async Task<IActionResult> StartAuction()
        {
            // Trigger StartAuction method manually
            _rabbitMQListener.StartAuction();
            return Ok("Auction started");
        }
    */
    }
}