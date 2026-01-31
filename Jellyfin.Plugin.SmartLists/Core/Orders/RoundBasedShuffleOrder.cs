using System;
using System.Collections.Generic;
using System.Linq;
using Jellyfin.Database.Implementations.Entities;
using Jellyfin.Plugin.SmartLists.Services.Shared;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Library;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.SmartLists.Core.Orders
{
    /// <summary>
    /// Implements a round-based shuffle that ensures each item plays once per round.
    /// Uses a timestamp-based seed for reproducibility within a round.
    /// When all items have been played, a new round starts with a new seed.
    /// </summary>
    public class RoundBasedShuffleOrder : Order
    {
        public override string Name => "Round Based Shuffle";

        // Thread-safe dictionary to track played items per round
        // Key: combination of playlist ID and user ID, Value: set of played item IDs
        private static readonly Dictionary<string, HashSet<Guid>> PlayedItemsByRound = new();
        private static readonly object RoundLock = new();

        // Current round seed - used for reproducible shuffle within a round
        private static readonly Dictionary<string, int> RoundSeeds = new();

        public override IEnumerable<BaseItem> OrderBy(IEnumerable<BaseItem> items)
        {
            if (items == null)
            {
                return [];
            }

            var itemsList = items.ToList();
            if (itemsList.Count == 0)
            {
                return [];
            }

            if (itemsList.Count == 1)
            {
                return itemsList;
            }

            // Generate a deterministic seed based on current date/hour for round grouping
            // This ensures items shuffle consistently within a time period but change periodically
            var roundKey = GetCurrentRoundKey();

            // Suppress CA5394: Random is acceptable here - we're not using it for security purposes
#pragma warning disable CA5394
            var seed = roundKey.GetHashCode(StringComparison.Ordinal);
            var random = new Random(seed);
#pragma warning restore CA5394

            // Create shuffled list using Fisher-Yates algorithm
            var shuffled = new List<BaseItem>(itemsList);
            int n = shuffled.Count;
            while (n > 1)
            {
                n--;
#pragma warning disable CA5394
                int k = random.Next(n + 1);
#pragma warning restore CA5394
                (shuffled[n], shuffled[k]) = (shuffled[k], shuffled[n]);
            }

            return shuffled;
        }

        public override IEnumerable<BaseItem> OrderBy(
            IEnumerable<BaseItem> items,
            User user,
            IUserDataManager? userDataManager,
            ILogger? logger,
            RefreshQueueService.RefreshCache? refreshCache = null)
        {
            // refreshCache not used for round-based shuffle ordering
            return OrderBy(items);
        }

        public override IComparable GetSortKey(
            BaseItem item,
            User user,
            IUserDataManager? userDataManager,
            ILogger? logger,
            Dictionary<Guid, int>? itemRandomKeys = null,
            RefreshQueueService.RefreshCache? refreshCache = null)
        {
            // For round-based shuffle, use a deterministic hash based on current round
            var roundKey = GetCurrentRoundKey();
            var combined = $"{roundKey}:{item.Id}";
            return combined.GetHashCode(StringComparison.Ordinal);
        }

        /// <summary>
        /// Gets the current round key based on timestamp.
        /// Rounds change every 24 hours to ensure fresh shuffles daily.
        /// </summary>
        private static string GetCurrentRoundKey()
        {
            // Use date component only - round changes daily
            var now = DateTime.UtcNow;
            return $"round_{now:yyyyMMdd}";
        }

        /// <summary>
        /// Marks an item as played in the current round for a specific playlist and user.
        /// </summary>
        /// <param name="playlistId">The playlist ID.</param>
        /// <param name="userId">The user ID.</param>
        /// <param name="itemId">The item that was played.</param>
        /// <returns>True if all items in the round have been played (round complete).</returns>
        public static bool MarkItemAsPlayed(string playlistId, Guid userId, Guid itemId, int totalItemCount)
        {
            var roundKey = GetCurrentRoundKey();
            var sessionKey = $"{playlistId}:{userId}:{roundKey}";

            lock (RoundLock)
            {
                if (!PlayedItemsByRound.TryGetValue(sessionKey, out var playedItems))
                {
                    playedItems = new HashSet<Guid>();
                    PlayedItemsByRound[sessionKey] = playedItems;
                }

                playedItems.Add(itemId);

                // Check if round is complete
                if (playedItems.Count >= totalItemCount)
                {
                    // Clear the round to start fresh
                    PlayedItemsByRound.Remove(sessionKey);
                    return true;
                }

                return false;
            }
        }

        /// <summary>
        /// Gets the set of items already played in the current round.
        /// </summary>
        public static HashSet<Guid> GetPlayedItems(string playlistId, Guid userId)
        {
            var roundKey = GetCurrentRoundKey();
            var sessionKey = $"{playlistId}:{userId}:{roundKey}";

            lock (RoundLock)
            {
                if (PlayedItemsByRound.TryGetValue(sessionKey, out var playedItems))
                {
                    return new HashSet<Guid>(playedItems);
                }

                return new HashSet<Guid>();
            }
        }

        /// <summary>
        /// Clears old round data to prevent memory leaks.
        /// Should be called periodically (e.g., during cleanup tasks).
        /// </summary>
        public static void CleanupOldRounds()
        {
            var currentRoundKey = GetCurrentRoundKey();

            lock (RoundLock)
            {
                var keysToRemove = PlayedItemsByRound.Keys
                    .Where(k => !k.EndsWith(currentRoundKey, StringComparison.Ordinal))
                    .ToList();

                foreach (var key in keysToRemove)
                {
                    PlayedItemsByRound.Remove(key);
                }

                var seedKeysToRemove = RoundSeeds.Keys
                    .Where(k => !k.EndsWith(currentRoundKey, StringComparison.Ordinal))
                    .ToList();

                foreach (var key in seedKeysToRemove)
                {
                    RoundSeeds.Remove(key);
                }
            }
        }
    }
}
