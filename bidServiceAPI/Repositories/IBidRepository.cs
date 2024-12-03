using System.Collections.Generic;
using System.Threading.Tasks;
using BidModel;

namespace BidService.Repositories
{
    public interface IBidRepository
    {
        // Retrieves all bids
        Task<IEnumerable<Bid>> GetAllBids();

        // Retrieves a bid based on ID
        Task<Bid> GetBidById(string id);

        // Creates a new bid
        Task<Bid> CreateBid(Bid newBid);

        // Updates an existing bid
        Task<bool> UpdateBid(string id, Bid updatedBid);

        // Deletes a bid based on ID
        Task<bool> DeleteBid(string id);

        // Retrieves the latest bid for a specific item
        Task<Bid> GetLatestBidForItem(string itemId);
    }
}
