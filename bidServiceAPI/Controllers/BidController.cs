using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using System;
using System.Threading.Tasks;
using BidModel;
using BidService.Repositories;

namespace BidService.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class BidController : ControllerBase
    {
        private readonly IBidRepository _bidRepository;
        private readonly ILogger<BidController> _logger;

        public BidController(ILogger<BidController> logger, IBidRepository bidRepository)
        {
            _logger = logger;
            _bidRepository = bidRepository;
        }

        [HttpGet]
        public async Task<IActionResult> GetAllBids()
        {
            try
            {
                // Async call to get all bids
                var bids = await _bidRepository.GetAllBids();
                return Ok(bids);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "An error occurred while getting all bids.");
                return StatusCode(500, "Internal server error.");
            }
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
    }
}
