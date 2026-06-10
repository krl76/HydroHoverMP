using System;
using System.Collections.Generic;
using System.Linq;
using Data.Leaderbords;
using Features.Networking;
using Infrastructure.Services.Leaderboard;
using Infrastructure.Services.Network;
using NUnit.Framework;

namespace HydroHoverMP.Tests.Editor
{
    public sealed class LeaderboardQueryTests
    {
        [Test]
        public void TakeTop_OrdersByTimeThenDate_AndCapsCount()
        {
            var source = new[]
            {
                new NetworkLeaderboardRecord(30f, 200, "C"),
                new NetworkLeaderboardRecord(10f, 100, "A"),
                new NetworkLeaderboardRecord(10f, 50, "A0"),
                new NetworkLeaderboardRecord(20f, 150, "B"),
            };

            NetworkLeaderboardRecord[] top = LeaderboardRecordMapper.TakeTop(source, 2);

            Assert.That(top.Length, Is.EqualTo(2));
            Assert.That(top[0].Nickname, Is.EqualTo("A0")); // 10f, earlier Date wins the tie
            Assert.That(top[1].Nickname, Is.EqualTo("A"));  // 10f, later Date
        }

        [Test]
        public void ToRecords_MapsFieldsAndHandlesNullArray()
        {
            var source = new[] { new NetworkLeaderboardRecord(12.5f, 999, "Pilot") };

            var records = LeaderboardRecordMapper.ToRecords(source);
            Assert.That(records.Count, Is.EqualTo(1));
            Assert.That(records[0].Time, Is.EqualTo(12.5f));
            Assert.That(records[0].Date, Is.EqualTo(999));
            Assert.That(records[0].PlayerName, Is.EqualTo("Pilot"));

            var empty = LeaderboardRecordMapper.ToRecords(null);
            Assert.That(empty, Is.Empty);
        }
    }

    public sealed class LeaderboardServiceQueryTests
    {
        // Minimal fake so LeaderboardService can be exercised without FishNet.
        private sealed class FakeConnectionService : INetworkConnectionService
        {
            public int BeginCalls;
            public int LastCount;
            public event Action<IReadOnlyList<Record>> OnLeaderboardRecordsReceived;

            public bool BeginLeaderboardQuery(int count)
            {
                BeginCalls++;
                LastCount = count;
                return true;
            }

            public void CancelLeaderboardQuery() { }
            public void Emit(IReadOnlyList<Record> records) => OnLeaderboardRecordsReceived?.Invoke(records);

            // Remaining INetworkConnectionService members are unused by these tests — stubbed to
            // satisfy the interface.
            public NetworkConnectionStatus Status => NetworkConnectionStatus.Offline;
            public int ConnectedClientCount => 0;
            public bool IsHost => false;
            public bool IsClient => false;
            public bool IsServer => false;

#pragma warning disable 67 // events intentionally unused in the fake
            public event Action<NetworkConnectionStatus> OnStatusChanged;
            public event Action<int> OnClientCountChanged;
            public event Action<string> OnConnectionFailed;
#pragma warning restore 67

            public bool StartHost(ushort port = 7770) => false;
            public bool StartClient(string address, ushort port = 7770) => false;
            public bool StartServer(ushort port = 7770) => false;
            public ushort ResolveServerPort() => 0;
            public void StopConnection() { }
            public void RefreshStatus() { }
        }

        [Test]
        public void DedicatedMode_Offline_RequestThenResult_PopulatesGetTopRecordsAndFiresEvent()
        {
            var config = new LeaderboardConfiguration { SourceMode = LeaderboardSourceMode.DedicatedServer };
            var fake = new FakeConnectionService();
            var service = new LeaderboardService(config, fake);

            bool fired = false;
            service.OnDedicatedRecordsUpdated += () => fired = true;

            service.RequestDedicatedRecords();
            Assert.That(fake.BeginCalls, Is.EqualTo(1));

            fake.Emit(new List<Record> { new Record { Time = 5f, PlayerName = "A" } });

            Assert.That(fired, Is.True);
            var top = service.GetTopRecords(5);
            Assert.That(top.Count, Is.EqualTo(1));
            Assert.That(top[0].PlayerName, Is.EqualTo("A"));
        }
    }
}
