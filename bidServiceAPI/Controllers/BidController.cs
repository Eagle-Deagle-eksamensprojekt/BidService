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
        if (newBid == null)
        {
            return BadRequest("Bid cannot be null.");
        }

        try
        {
            // Sæt en unik ID og tidspunkt for buddet
            newBid.Id = Guid.NewGuid().ToString();
            newBid.BidTime = DateTime.UtcNow;

            // Brug CreateBidAsync i stedet for AddBidAsync
            var createdBid = await _bidRepository.CreateBid(newBid);
            return CreatedAtAction(nameof(GetBidById), new { id = createdBid.Id }, createdBid);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "An error occurred while placing a bid.");
            return StatusCode(500, "Internal server error.");
        }
    }


        [HttpGet("{id}")]
        public async Task<IActionResult> GetBidById(string id)
        {
            try
            {
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
