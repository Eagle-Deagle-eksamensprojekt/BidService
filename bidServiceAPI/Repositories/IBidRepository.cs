using System.Collections.Generic;
using System.Threading.Tasks;
using BidModel;

namespace BidService.Repositories
{
    public interface IBidRepository
    {
        // Henter alle bud
        Task<IEnumerable<Bid>> GetAllBids();

        // Henter et bud baseret på ID
        Task<Bid> GetBidById(string id);

        // Opretter et nyt bud
        Task<Bid> CreateBid(Bid newBid);

        // Opdaterer et eksisterende bud
        Task<bool> UpdateBid(string id, Bid updatedBid);

        // Sletter et bud baseret på ID
        Task<bool> DeleteBid(string id);
    }
}