using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Data.Leaderbords;
using Features.Networking;
using Infrastructure.Services.Network;
using Newtonsoft.Json;
using UnityEngine;

namespace Infrastructure.Services.Leaderboard
{
    public class LeaderboardService : ILeaderboardService
    {
        private const string FileName = "leaderboard.json";
        private readonly string _path;
        private readonly LeaderboardConfiguration _configuration;
        private readonly INetworkConnectionService _connectionService;
        private LeaderboardData _data;
        private List<Record> _dedicatedRecords = new();
        private bool _dedicatedRequestInFlight;

        public event Action OnDedicatedRecordsUpdated;

        public LeaderboardService(LeaderboardConfiguration configuration = null, INetworkConnectionService connectionService = null)
        {
            _configuration = configuration ?? new LeaderboardConfiguration();
            _connectionService = connectionService;
            _path = Path.Combine(Application.persistentDataPath, FileName);
            Load();

            if (_connectionService != null)
                _connectionService.OnLeaderboardRecordsReceived += OnDedicatedRecordsReceived;
        }

        public LeaderboardSourceMode SourceMode => _configuration.SourceMode;
        public string DedicatedServerAddress => _configuration.NormalizedDedicatedServerAddress;
        public ushort DedicatedServerPort => _configuration.NormalizedDedicatedServerPort;
        public bool IsUsingDedicatedServer => _configuration.UseDedicatedServer;

        public void AddRecord(float time, string nickname)
        {
            if (IsUsingDedicatedServer)
            {
                // On a dedicated server the authoritative record (time + nickname) is written
                // server-side in NetworkSessionController.ServerAddDedicatedLeaderboardRecord when
                // the player finishes; the client never persists locally in this mode.
                RequestDedicatedRecords();
                return;
            }

            AddLocalRecord(time, nickname);
        }

        public List<Record> GetTopRecords(int count)
        {
            if (IsUsingDedicatedServer)
            {
                NetworkSessionController session = NetworkSessionController.Instance;
                if (session != null)
                {
                    session.RequestDedicatedLeaderboardServerRpc();
                    return session.GetDedicatedLeaderboardRecords(count);
                }

                // Disconnected (main menu): serve the most recent live-query result, fetched via
                // the short-lived leaderboard-only connection (see RequestDedicatedRecords).
                return _dedicatedRecords.Take(count).ToList();
            }

            return _data.Records.Take(count).ToList();
        }

        public float GetBestTime()
        {
            List<Record> records = GetTopRecords(1);
            return records.Count > 0 ? records[0].Time : 0f;
        }

        public bool RequestDedicatedRecords()
        {
            if (!IsUsingDedicatedServer) return false;

            // Already in a live session: the connected SyncList path serves data; nothing to fetch.
            if (NetworkSessionController.Instance != null) return false;
            if (_connectionService == null) return false;
            if (_dedicatedRequestInFlight) return true; // a query is already pending; results will arrive.

            _dedicatedRequestInFlight = _connectionService.BeginLeaderboardQuery(5);
            return _dedicatedRequestInFlight;
        }

        public void CancelDedicatedRecordsRequest()
        {
            // Always forward: the query connection stays open after results are delivered, so the
            // window closing must tear it down even when no request is "in flight". Safe no-op if
            // there is no active query.
            _dedicatedRequestInFlight = false;
            _connectionService?.CancelLeaderboardQuery();
        }

        private void OnDedicatedRecordsReceived(IReadOnlyList<Record> records)
        {
            _dedicatedRequestInFlight = false;
            _dedicatedRecords = records != null ? new List<Record>(records) : new List<Record>();
            OnDedicatedRecordsUpdated?.Invoke();
        }

        private void AddLocalRecord(float time, string nickname)
        {
            _data.Records.Add(new Record
            {
                Time = time,
                Date = System.DateTime.Now.Ticks,
                PlayerName = string.IsNullOrWhiteSpace(nickname) ? "Pilot" : nickname.Trim()
            });

            _data.Records = _data.Records.OrderBy(r => r.Time).ToList();
            Save();
        }

        private void Save()
        {
            string json = JsonConvert.SerializeObject(_data, Formatting.Indented);
            File.WriteAllText(_path, json);
        }

        private void Load()
        {
            if (File.Exists(_path))
            {
                string json = File.ReadAllText(_path);
                _data = JsonConvert.DeserializeObject<LeaderboardData>(json) ?? new LeaderboardData();
            }
            else
            {
                _data = new LeaderboardData();
            }
        }
    }
}

