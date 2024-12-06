namespace BidModel
{
    using System;
    using System.Collections.Generic;

    using System.Text.Json;
    using System.Text.Json.Serialization;
    using System.Globalization;

    /// <summary>
    /// A bid placed on an item in an auction
    /// </summary>
    public partial class Bid
    {
        /// <summary>
        /// The amount of the bid
        /// </summary>
        [JsonPropertyName("Amount")]
        public double Amount { get; set; }

        /// <summary>
        /// The time when the bid was placed
        /// </summary>
        [JsonPropertyName("BidTime")]
        public DateTimeOffset BidTime { get; set; }

        /// <summary>
        /// The unique identifier for the bid
        /// </summary>
        [JsonPropertyName("Id")]
        public string Id { get; set; }

        /// <summary>
        /// The unique identifier for the item being bid on
        /// </summary>
        [JsonPropertyName("ItemId")]
        public string ItemId { get; set; }

        /// <summary>
        /// The unique identifier for the user who placed the bid
        /// </summary>
        [JsonPropertyName("UserId")]
        public string UserId { get; set; }
    }
}
