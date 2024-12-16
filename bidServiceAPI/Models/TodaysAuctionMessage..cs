namespace BidServiceAPI.Models
{
    public class TodaysAuctionMessage
    {
        public string ItemId { get; set; }
        public DateTimeOffset StartAuctionDateTime { get; set; }
        public DateTimeOffset EndAuctionDateTime { get; set; }

    }
}
