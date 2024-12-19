namespace BidServiceAPI.Models
{
    public class TodaysAuctionMessage
    {
        /// <summary>
        /// The ID of the item being auctioned
        /// </summary>
        public string ItemId { get; set; }
        /// <summary>
        /// The start date and time for the auction of this item.
        /// </summary>
        public DateTimeOffset StartAuctionDateTime { get; set; }
        /// <summary>
        /// The date and time when the auction ends
        /// </summary>
        public DateTimeOffset EndAuctionDateTime { get; set; }

    }
}
