using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using System;
using System.Threading.Tasks;
using BidServiceAPI.Models;
using System.Text.Json;

namespace BidService.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class BidController : ControllerBase
    {
        private readonly ILogger<BidController> _logger;

        public BidController(ILogger<BidController> logger)
        {
            _logger = logger;
        }

        // GET all bids on item {itemId}
        [HttpGet("{itemId}")]
        public async Task<IActionResult> GetAllBidsOnItem(string ItemId)
        { return null;
        }

        [HttpPost]
        public async Task<IActionResult> PlaceBid([FromBody] Bid newBid)
        {
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
            }
        }



        [HttpGet("{id}")]
        public async Task<IActionResult> GetBidById(string id)
        {
            try
            {
                // Async call to get the bid by ID
                var bid = await _bidRepository.GetBidById(id);
                if (bid == null)
                {
                    return NotFound("Bid not found.");
                }

                return Ok(bid);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "An error occurred while getting a bid by ID.");
                return StatusCode(500, "Internal server error.");
            }
        }


        // Til tjek om items er auctionable
        // Get auctionable items from the item service, should be a list
         private async Task<Item?> GetAuctionableItems()
        {
            // Tjek om brugeren eksisterer
            var existsUrl = $"{_config["ItemServiceEndpoint"]}/auctionable";
            
            
            var client = _httpClientFactory.CreateClient();
            HttpResponseMessage response;

            try
            {
                client.DefaultRequestHeaders.Add("Accept", "application/json");
                response = await client.GetAsync(existsUrl);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, ex.Message);
                return null;
            }

            if (response.IsSuccessStatusCode)
            {
                string responseContent = await response.Content.ReadAsStringAsync();
                _logger.LogInformation("Response content: {ResponseContent}", responseContent);

                try
                {
                    // Forsøg at deserialisere til User
                    var item = JsonSerializer.Deserialize<Item>(responseContent);
                    if (item == null)
                    {
                        _logger.LogInformation("No items Found.");
                        return null;
                    }

                    _logger.LogInformation("Item data successfully deserialized.");
                    return item;
                }
                catch (JsonException ex)
                {
                    _logger.LogError(ex, "Failed to deserialize Item data.");
                    return null;
                }
            }
            _logger.LogWarning("Failed to check if user exists.");
            return null;
        }


        [HttpGet("items")]
        public async Task<IActionResult> GetItems()
        {
            var items = await GetAuctionableItems();
            if (items == null)
            {
                return Ok(null); // Returnerer null, hvis ingen items findes
            }

            return Ok(items); // Returnerer 200 OK med items
        }


    }
}
    }
}
