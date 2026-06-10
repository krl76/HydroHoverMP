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
        private const string DedicatedCacheFileName = "dedicated_leaderboard_cache.json";
        private const int CacheSize = 50;

        private readonly string _path;
        private readonly string _dedicatedCachePath;
        private readonly LeaderboardConfiguration _configuration;
        private readonly INetworkConnectionService _connectionService;
        private LeaderboardData _data;
        private List<Record> _dedicatedCache = new();

        public LeaderboardService(LeaderboardConfiguration configuration = null, INetworkConnectionService connectionService = null)
        {
            _configuration = configuration ?? new LeaderboardConfiguration();
            _connectionService = connectionService;
            _path = Path.Combine(Application.persistentDataPath, FileName);
            _dedicatedCachePath = Path.Combine(Application.persistentDataPath, DedicatedCacheFileName);
            Load();
            LoadDedicatedCache();

            // Snapshot the replicated leaderboard the instant we begin leaving a live session, while
            // the SyncList is still populated — that's how the just-finished race ends up visible in
            // the disconnected main-menu view.
            if (_connectionService != null)
                _connectionService.OnStatusChanged += OnConnectionStatusChanged;
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
                // server-side in NetworkSessionController.ServerAddDedicatedLeaderboardRecord when the
                // player finishes; the client only mirrors the replicated list into its local cache.
                CacheFromLiveSession();
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
                    // Connected: the replicated SyncList is authoritative. Snapshot it so the menu can
                    // still show the last-known records after we disconnect. Persist to disk when the
                    // board actually changed, so even an UNEXPECTED drop (which never fires Stopping)
                    // leaves the latest board on disk for the next launch.
                    List<Record> live = session.GetDedicatedLeaderboardRecords(CacheSize);
                    bool changed = !RecordsEqual(_dedicatedCache, live);
                    _dedicatedCache = live;
                    if (changed)
                        SaveDedicatedCache();
                    return live.Take(count).ToList();
                }

                // Disconnected (main menu): serve the last-known cached records.
                return _dedicatedCache.Take(count).ToList();
            }

            return _data.Records.Take(count).ToList();
        }

        public float GetBestTime()
        {
            List<Record> records = GetTopRecords(1);
            return records.Count > 0 ? records[0].Time : 0f;
        }

        public void RequestDedicatedRecords()
        {
            // Cache-based model: nothing to fetch from the menu. If we happen to be connected, refresh
            // the cache from the live replicated list so the menu shows the latest after disconnect.
            if (IsUsingDedicatedServer)
                CacheFromLiveSession();
        }

        private void OnConnectionStatusChanged(NetworkConnectionStatus status)
        {
            // StopConnection sets Stopping BEFORE actually tearing the client down, so the
            // NetworkSessionController + its SyncList are still alive here — the right moment to
            // persist the final board (including the race that was just finished).
            if (status == NetworkConnectionStatus.Stopping)
                CacheFromLiveSession();
        }

        private void CacheFromLiveSession()
        {
            if (!IsUsingDedicatedServer) return;

            NetworkSessionController session = NetworkSessionController.Instance;
            if (session == null) return;

            _dedicatedCache = session.GetDedicatedLeaderboardRecords(CacheSize);
            SaveDedicatedCache();
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
            try
            {
                _data = File.Exists(_path)
                    ? JsonConvert.DeserializeObject<LeaderboardData>(File.ReadAllText(_path)) ?? new LeaderboardData()
                    : new LeaderboardData();
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[Leaderboard] Failed to load local leaderboard from '{_path}': {e.Message}");
                _data = new LeaderboardData();
            }

            // Newtonsoft overwrites the field initializer with null for an explicit "Records": null.
            _data.Records ??= new List<Record>();
        }

        private static bool RecordsEqual(List<Record> a, List<Record> b)
        {
            if (a == null || b == null) return ReferenceEquals(a, b);
            if (a.Count != b.Count) return false;

            for (int i = 0; i < a.Count; i++)
            {
                Record x = a[i];
                Record y = b[i];
                if (x == null || y == null)
                {
                    if (!ReferenceEquals(x, y)) return false;
                    continue;
                }

                if (x.Time != y.Time || x.Date != y.Date || x.PlayerName != y.PlayerName)
                    return false;
            }

            return true;
        }

        private void LoadDedicatedCache()
        {
            try
            {
                if (!File.Exists(_dedicatedCachePath))
                {
                    _dedicatedCache = new List<Record>();
                    return;
                }

                string json = File.ReadAllText(_dedicatedCachePath);
                LeaderboardData data = JsonConvert.DeserializeObject<LeaderboardData>(json) ?? new LeaderboardData();
                _dedicatedCache = data.Records ?? new List<Record>();
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[Leaderboard] Failed to load dedicated cache from '{_dedicatedCachePath}': {e.Message}");
                _dedicatedCache = new List<Record>();
            }
        }

        private void SaveDedicatedCache()
        {
            try
            {
                LeaderboardData data = new() { Records = _dedicatedCache ?? new List<Record>() };
                File.WriteAllText(_dedicatedCachePath, JsonConvert.SerializeObject(data, Formatting.Indented));
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[Leaderboard] Failed to save dedicated cache to '{_dedicatedCachePath}': {e.Message}");
            }
        }
    }
}
