using Microsoft.AspNetCore.Mvc;
using BidServiceAPI.Models;
using System.Text.Json;
using System.Diagnostics;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.AspNetCore.Authorization;



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
        [Authorize]
        [HttpGet("version")]
        public async Task<IActionResult> GetVersion()
        {
            var properties = new Dictionary<string, string>();

            var ver = FileVersionInfo.GetVersionInfo(
                typeof(Program).Assembly.Location).ProductVersion ?? "N/A";
            properties.Add("version", ver);

            return Ok(new {properties});
        }


        // POST: bidServiceAPI/Bid/PlaceBid
        [Authorize]
        [HttpPost]
        public async Task<IActionResult> PlaceBid([FromBody] Bid newBid)
        {

            var itemId = newBid.ItemId!;


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
                
                // indsæt cache for at tjekke om item er auctionable så slipper vi for mange kald til auctionService
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
        [Authorize]
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

        // Start listener for at lytte på beskeder fra RabbitMQ og returnerer ItemId
        [Authorize]
        [HttpPost("start-listener")]
        public async Task<IActionResult> StartListener()
        {
            // Trigger StartAuction method manually
            _rabbitMQListener.StartAuction();

            // Fetch the active ItemId
            var activeItemId = _rabbitMQListener.GetActiveItemId();

            // Return JSON with ItemId
            return Ok(new
            {
                message = "BidService listener started. You can now bid on ItemId.",
                ItemId = activeItemId
            });
        }

    }
}