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
    /// Implements a simple random shuffle without state tracking.
    /// Each call produces a new random ordering, suitable for "random next" behavior.
    /// </summary>
    public class SimpleRandomNextOrder : Order
    {
        public override string Name => "Simple Random Next";

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

            var random = GetThreadSafeRandom();

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
            // refreshCache not used for simple random ordering
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
            // For simple random next, generate a new random key each time
            // This ensures different ordering on each request
            var random = GetThreadSafeRandom();
#pragma warning disable CA5394
            return random.Next();
#pragma warning restore CA5394
        }

        /// <summary>
        /// Selects a random item from the collection, optionally excluding the last played item.
        /// </summary>
        /// <param name="items">The collection of items.</param>
        /// <param name="excludeItemId">Optional item ID to exclude from selection.</param>
        /// <returns>A randomly selected item, or null if no items available.</returns>
        public static BaseItem? SelectRandomNext(IEnumerable<BaseItem> items, Guid? excludeItemId = null)
        {
            if (items == null)
            {
                return null;
            }

            var itemsList = items.ToList();
            if (itemsList.Count == 0)
            {
                return null;
            }

            // Filter out excluded item if specified
            var eligibleItems = excludeItemId.HasValue
                ? itemsList.Where(i => i.Id != excludeItemId.Value).ToList()
                : itemsList;

            if (eligibleItems.Count == 0)
            {
                // If all items were excluded, return from original list
                eligibleItems = itemsList;
            }

            if (eligibleItems.Count == 1)
            {
                return eligibleItems[0];
            }

            var random = GetThreadSafeRandom();
#pragma warning disable CA5394
            var index = random.Next(eligibleItems.Count);
#pragma warning restore CA5394
            return eligibleItems[index];
        }

        /// <summary>
        /// Generates a random ordering for the given item IDs.
        /// </summary>
        /// <param name="itemIds">The list of item IDs to shuffle.</param>
        /// <returns>A new randomly shuffled list of item IDs.</returns>
        public static List<Guid> GenerateRandomOrder(List<Guid> itemIds)
        {
            if (itemIds == null || itemIds.Count == 0)
            {
                return new List<Guid>();
            }

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

            return shuffled;
        }
    }
}
