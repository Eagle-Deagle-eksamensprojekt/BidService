using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using System;
using System.Threading.Tasks;
using BidServiceAPI.Models;
using System.Text.Json;
using RabbitMQ.Client;
using System.Text;
using System.Diagnostics;

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

        // POST place a bid on an item
        [HttpPost]
        public async Task<IActionResult> PlaceBid([FromBody] Bid newBid)
        {
            _logger.LogInformation("Placing bid on item {ItemId} for {Amount:C}.", newBid.ItemId, newBid.Amount);

            // Tjek om item er auctionable
            var auctionable = await IsItemAuctionable(newBid.ItemId);
            if (!auctionable)
            {
                _logger.LogWarning("Item {ItemId} is not auctionable.", newBid.ItemId);
                return BadRequest("Item is not auctionable.");
            }

            // Publish bid to RabbitMQ
            var published = await _rabbitMQPublisher.PublishToRabbitMQ(newBid);
            if (!published)
            {
                _logger.LogError("Failed to publish bid for {ItemId} to RabbitMQ.", newBid.ItemId);
                return StatusCode(500, "Failed to publish bid.");
            }

            _logger.LogInformation("Bid for {ItemId} published successfully to RabbitMQ.", newBid.ItemId);
            return Ok(newBid); // Returner det nye bud
        }

        // Til tjek om item er auctionable returnerer null hvis item ikke er auctionable
        // Get auctionable items from the item service
        private async Task<bool> IsItemAuctionable(string itemId)
        {
            var existsUrl = $"{_config["AuctionServiceEndpoint"]}/auctionable/{itemId}?dateTime={DateTimeOffset.UtcNow.UtcDateTime}"; // Der er så¨meget bøvl med dato formatet, nu virker det
            _logger.LogInformation("Checking if item is auctionable at: {ExistsUrl}", existsUrl);

            var client = _httpClientFactory.CreateClient();
            HttpResponseMessage response;

            //Indsæt cash ind her, sæt evt. en udløbsdato på

            try
            {
                client.DefaultRequestHeaders.Add("Accept", "application/json");
                response = await client.GetAsync(existsUrl);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error while checking if item is auctionable.");
                return false; // Antag som default, at item ikke er auctionable ved fejl
            }

            if (response.IsSuccessStatusCode)
            {
                // Deserialiser respons som bool
                string responseContent = await response.Content.ReadAsStringAsync();
                bool itemAuctionable = JsonSerializer.Deserialize<bool>(responseContent);

                if (itemAuctionable)
                {
                    _logger.LogInformation("Item {ItemId} is auctionable.", itemId);
                    return true;
                }

                _logger.LogInformation("Item {ItemId} is not auctionable.", itemId);
                return false;
            }

            _logger.LogWarning("Failed to determine if item {ItemId} is auctionable.", itemId);
            return false; // Antag som default, at item ikke er auctionable ved ukendt status
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