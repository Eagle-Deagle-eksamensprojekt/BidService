using Microsoft.AspNetCore.Mvc;
using System;
using System.Collections.Generic;
using System.Linq;
using BidModel;

namespace BidService.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class BidController : ControllerBase
    {
        // Simuleret database
        private static readonly List<Bid> Bids = new List<Bid>();

        [HttpGet]
        public IActionResult GetAllBids()
        {
            // Henter alle bud
            return Ok(Bids);
        }

        [HttpPost]
        public IActionResult PlaceBid([FromBody] Bid newBid)
        {
            if (newBid == null)
            {
                return BadRequest("Bid cannot be null.");
            }

            // Sæt en unik ID til buddet
            newBid.Id = Guid.NewGuid().ToString();
            newBid.BidTime = DateTime.UtcNow;

            Bids.Add(newBid);

            return CreatedAtAction(nameof(GetAllBids), new { id = newBid.Id }, newBid);
        }

        [HttpGet("{id}")]
        public IActionResult GetBidById(string id)
        {
            var bid = Bids.FirstOrDefault(b => b.Id == id);
            if (bid == null)
            {
                return NotFound("Bid not found.");
            }

            return Ok(bid);
        }
    }
}