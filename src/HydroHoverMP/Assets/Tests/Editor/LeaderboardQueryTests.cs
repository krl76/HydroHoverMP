using System.Collections.Generic;
using System.IO;
using System.Linq;
using Data.Leaderbords;
using Infrastructure.Services.Leaderboard;
using Newtonsoft.Json;
using NUnit.Framework;
using UnityEngine;

namespace HydroHoverMP.Tests.Editor
{
    // Dedicated mode serves the leaderboard from a client-side cache when disconnected (main menu).
    // The cache is written while connected and read back here from disk.
    public sealed class LeaderboardCacheTests
    {
        private static string CachePath => Path.Combine(Application.persistentDataPath, "dedicated_leaderboard_cache.json");

        [SetUp]
        public void SetUp()
        {
            if (File.Exists(CachePath)) File.Delete(CachePath);
        }

        [TearDown]
        public void TearDown()
        {
            if (File.Exists(CachePath)) File.Delete(CachePath);
        }

        [Test]
        public void DedicatedMode_Disconnected_ServesCachedRecordsFromDisk()
        {
            var data = new LeaderboardData
            {
                Records = new List<Record>
                {
                    new Record { Time = 5f, Date = 100, PlayerName = "A" },
                    new Record { Time = 9f, Date = 200, PlayerName = "B" },
                }
            };
            File.WriteAllText(CachePath, JsonConvert.SerializeObject(data));

            var config = new LeaderboardConfiguration { SourceMode = LeaderboardSourceMode.DedicatedServer };
            var service = new LeaderboardService(config); // no connection => NetworkSessionController.Instance is null

            List<Record> top = service.GetTopRecords(5);

            Assert.That(top.Count, Is.EqualTo(2));
            Assert.That(top[0].PlayerName, Is.EqualTo("A"));
            Assert.That(top[1].PlayerName, Is.EqualTo("B"));
        }

        [Test]
        public void DedicatedMode_Disconnected_NoCache_ReturnsEmpty()
        {
            // SetUp already removed any cache file.
            var config = new LeaderboardConfiguration { SourceMode = LeaderboardSourceMode.DedicatedServer };
            var service = new LeaderboardService(config);

            List<Record> top = service.GetTopRecords(5);

            Assert.That(top, Is.Empty);
        }

        [Test]
        public void DedicatedMode_RespectsRequestedCount()
        {
            var data = new LeaderboardData
            {
                Records = Enumerable.Range(0, 10)
                    .Select(i => new Record { Time = i, Date = i, PlayerName = $"P{i}" })
                    .ToList()
            };
            File.WriteAllText(CachePath, JsonConvert.SerializeObject(data));

            var service = new LeaderboardService(new LeaderboardConfiguration { SourceMode = LeaderboardSourceMode.DedicatedServer });

            Assert.That(service.GetTopRecords(3).Count, Is.EqualTo(3));
        }
    }
}
