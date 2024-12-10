using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using System;
using System.Threading.Tasks;
using BidServiceAPI.Models;
using System.Text.Json;
using RabbitMQ.Client;
using System.Text;

namespace BidService.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class BidController : ControllerBase
    {
        private readonly ILogger<BidController> _logger;
        private readonly IHttpClientFactory _httpClientFactory;
        private readonly IConnectionFactory _connectionFactory;
        private readonly IConfiguration _config;
        public BidController(ILogger<BidController> logger, IConfiguration config, IHttpClientFactory httpClientFactory, IConnectionFactory connectionFactory)
        {
            _logger = logger;
            _config = config;
            _httpClientFactory = httpClientFactory;
            _connectionFactory = connectionFactory;
        }/*
        {
            _logger = logger;
            _config = config;
            _httpClientFactory = httpClientFactory;
        }*/

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
            var published = await PublishToRabbitMQ(newBid);
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

        private async Task<bool> PublishToRabbitMQ(Bid bid)
        {
            var rabbitHost = $"{_config["RABBITMQ_HOST"]}" ?? "localhost"; // Default til localhost

            try
            {
                // RabbitMQ connection factory
                var factory = new ConnectionFactory
                {
                    HostName = rabbitHost,
                    DispatchConsumersAsync = true // Hvis du vil understøtte async consumers
                };

                // Create connection
                using var connection = factory.CreateConnection();
                using var channel = connection.CreateModel();

                // Queue name based on AuctionId
                // Denne auctionId skal findes fra cash værdi fra isItemAuctionable
                // Metoden er ikke lavet ordenligt endnu, så derfor er der en dummy værdi
                //var auctionId = await GetAuctionIdForItem(bid.ItemId);
                var auctionId = "AuctionId"; // Dummy value
                if (auctionId == null)
                {
                    _logger.LogError("Failed to get AuctionId for ItemId {ItemId}.", bid.ItemId);
                    return false;
                }
                var queueName = $"{auctionId}Queue";

                // Declare the queue (only necessary the first time)
                channel.QueueDeclare(
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
                channel.BasicPublish(
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
}