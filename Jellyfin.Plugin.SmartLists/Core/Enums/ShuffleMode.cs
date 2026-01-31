using System.Text.Json.Serialization;

namespace Jellyfin.Plugin.SmartLists.Core.Enums
{
    /// <summary>
    /// Defines the shuffle behavior mode for SmartLists.
    /// </summary>
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public enum ShuffleMode
    {
        /// <summary>
        /// No shuffle behavior - items are played in their defined order.
        /// </summary>
        None = 0,

        /// <summary>
        /// Ensures each item plays once per round before any repeats.
        /// Tracks played items and resets when all items have been played.
        /// </summary>
        RoundBased = 1,

        /// <summary>
        /// Reshuffles the playlist each time it is opened/accessed by a user,
        /// maintaining that shuffled order during sequential playback.
        /// </summary>
        ReshuffleOnOpen = 2,

        /// <summary>
        /// Simple randomization without state tracking.
        /// Each next item is chosen randomly from all items.
        /// </summary>
        SimpleRandomNext = 3
    }
}
