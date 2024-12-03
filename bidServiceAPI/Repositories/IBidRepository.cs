using System.Collections.Generic;
using System.Threading.Tasks;
using BidModel;

namespace BidService.Repositories
{
    public interface IBidRepository
    {
        // Henter alle bud
        Task<IEnumerable<Bid>> GetAllBidsAsync();

        // Henter et bud baseret på ID
        Task<Bid> GetBidByIdAsync(string id);

        // Opretter et nyt bud
        Task<Bid> CreateBidAsync(Bid newBid);

        // Opdaterer et eksisterende bud
        Task<bool> UpdateBidAsync(string id, Bid updatedBid);

        // Sletter et bud baseret på ID
        Task<bool> DeleteBidAsync(string id);
    }
}