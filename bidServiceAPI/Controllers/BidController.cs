using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using System;
using System.Threading.Tasks;
using BidServiceAPI.Models;
using System.Text.Json;
using RabbitMQ.Client;

namespace BidService.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class BidController : ControllerBase
    {
        private readonly ILogger<BidController> _logger;
        private readonly IHttpClientFactory _httpClientFactory;
        private readonly IConfiguration _config;
        public BidController(ILogger<BidController> logger, IConfiguration config, IHttpClientFactory httpClientFactory)
        {
            _logger = logger;
            _config = config;
            _httpClientFactory = httpClientFactory;
        }

        // POST place a bid on an item
        [HttpPost]
        public async Task<IActionResult> PlaceBid([FromBody] Bid newBid)
        { return null!;
            /*
            // Hvis buddet er null, returner en fejl
            if (newBid == null)
            {
                return BadRequest("Bid cannot be null.");
            }

            try
            {
                // Opret et unikt ID og timestamp for buddet
                newBid.Id = Guid.NewGuid().ToString();
                newBid.BidTime = DateTime.UtcNow;

                // Hent det nyeste bud for den vare, som der bydes på
                var latestBid = await _bidRepository.GetLatestBidForItem(newBid.ItemId);

                // Sæt minimumsbid til det sidste bud + 10, eller 100 hvis der ikke er noget bud
                decimal minimumAmount = latestBid?.Amount + 10.0m ?? 100.0m;

                // Hvis buddet er mindre end minimumsbeløbet, returner fejl
                if (newBid.Amount < minimumAmount)
                {
                    return BadRequest($"Bid amount must be at least {minimumAmount:C}.");
                }

                // Opret det nye bud
                var createdBid = await _bidRepository.CreateBid(newBid);
                return CreatedAtAction(nameof(GetBidById), new { id = createdBid.Id }, createdBid);
            }
            catch (Exception ex)
            {
                // Hvis der er en fejl, log den og returner en serverfejl
                _logger.LogError(ex, "An error occurred while placing a bid.");
                return StatusCode(500, "Internal server error.");
            }*/
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


/*
        [HttpGet("items")]
        public async Task<IActionResult> GetItems()
        {
            var items = await IsItemAuctionable("items");
            if (items == null)
            {
                return Ok(null); // Returnerer null, hvis ingen items findes
            }

            return Ok(items); // Returnerer 200 OK med items
        }*/


    }
}