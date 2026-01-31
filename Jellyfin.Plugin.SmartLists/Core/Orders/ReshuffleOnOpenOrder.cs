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
    /// Implements a shuffle that reshuffles each time a playlist is opened/accessed.
    /// Uses a session-based approach with different randomization than standard RandomOrder.
    /// The shuffled order is maintained during the session/playback.
    /// </summary>
    public class ReshuffleOnOpenOrder : Order
    {
        public override string Name => "Reshuffle On Open";

        // Session-based shuffle tracking
        // Key: combination of playlist ID and session identifier, Value: shuffled item order
        private static readonly Dictionary<string, List<Guid>> SessionShuffles = new();
        private static readonly object SessionLock = new();

        // Thread-local random for better concurrency performance
        [ThreadStatic]
        private static Random? _threadRandom;

        private static Random GetThreadSafeRandom()
        {
            // Suppress CA5394: Random is acceptable here - we're not using it for security purposes
#pragma warning disable CA5394
            return _threadRandom ??= new Random((int)(DateTime.Now.Ticks & 0x7FFFFFFF) + Environment.CurrentManagedThreadId);
#pragma warning restore CA5394
        }

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

            // Generate a session-based seed that changes frequently
            // This creates a different shuffle each time the playlist is "opened"
            var sessionSeed = GenerateSessionSeed();

#pragma warning disable CA5394
            var random = new Random(sessionSeed);
#pragma warning restore CA5394

            // Use Fisher-Yates shuffle for unbiased randomization
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
            // refreshCache not used for reshuffle on open ordering
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
            // For reshuffle on open, use a combination of session seed and item ID
            // This provides different ordering per session while being consistent within a session
            var sessionSeed = GenerateSessionSeed();
            var combined = sessionSeed ^ item.Id.GetHashCode();
            return combined;
        }

        /// <summary>
        /// Generates a session seed based on current time.
        /// The seed changes every 5 minutes to balance between fresh shuffles
        /// and maintaining order during a typical viewing session.
        /// </summary>
        private static int GenerateSessionSeed()
        {
            var now = DateTime.UtcNow;
            // Create a seed that changes every 5 minutes
            var sessionWindow = now.Ticks / TimeSpan.TicksPerMinute / 5;
            return (int)(sessionWindow & 0x7FFFFFFF);
        }

        /// <summary>
        /// Gets or creates a shuffled order for a specific session.
        /// This maintains the same shuffle order throughout a session.
        /// </summary>
        /// <param name="playlistId">The playlist ID.</param>
        /// <param name="sessionId">The session identifier.</param>
        /// <param name="itemIds">The list of item IDs to shuffle.</param>
        /// <returns>The shuffled order of item IDs.</returns>
        public static List<Guid> GetOrCreateSessionShuffle(string playlistId, string sessionId, List<Guid> itemIds)
        {
            var key = $"{playlistId}:{sessionId}";

            lock (SessionLock)
            {
                if (SessionShuffles.TryGetValue(key, out var existingShuffle))
                {
                    return existingShuffle;
                }

                // Create new shuffle for this session
                var shuffled = new List<Guid>(itemIds);
                var random = GetThreadSafeRandom();

                // Fisher-Yates shuffle
                int n = shuffled.Count;
                while (n > 1)
                {
                    n--;
#pragma warning disable CA5394
                    int k = random.Next(n + 1);
#pragma warning restore CA5394
                    (shuffled[n], shuffled[k]) = (shuffled[k], shuffled[n]);
                }

                SessionShuffles[key] = shuffled;
                return shuffled;
            }
        }

        /// <summary>
        /// Clears the shuffle for a specific session.
        /// Call this when a session ends to free memory.
        /// </summary>
        /// <param name="playlistId">The playlist ID.</param>
        /// <param name="sessionId">The session identifier.</param>
        public static void ClearSessionShuffle(string playlistId, string sessionId)
        {
            var key = $"{playlistId}:{sessionId}";

            lock (SessionLock)
            {
                SessionShuffles.Remove(key);
            }
        }

        /// <summary>
        /// Gets the current session identifier based on time window.
        /// </summary>
        public static string GetCurrentSessionId()
        {
            var now = DateTime.UtcNow;
            // Session changes every 5 minutes
            var window = now.Ticks / TimeSpan.TicksPerMinute / 5;
            return window.ToString();
        }

        /// <summary>
        /// Clears all session shuffles to free memory.
        /// Should be called periodically (e.g., during cleanup tasks).
        /// </summary>
        public static void CleanupAllSessions()
        {
            lock (SessionLock)
            {
                SessionShuffles.Clear();
            }
        }
    }
}
