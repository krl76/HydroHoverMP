using System.Collections.Generic;
using System.IO;
using System.Linq;
using Data.Leaderbords;
using Features.Networking;
using Newtonsoft.Json;
using UnityEngine;

namespace Infrastructure.Services.Leaderboard
{
    public class LeaderboardService : ILeaderboardService
    {
        private const string FileName = "leaderboard.json";
        private readonly string _path;
        private readonly LeaderboardConfiguration _configuration;
        private LeaderboardData _data;

        public LeaderboardService(LeaderboardConfiguration configuration = null)
        {
            _configuration = configuration ?? new LeaderboardConfiguration();
            _path = Path.Combine(Application.persistentDataPath, FileName);
            Load();
        }

        public LeaderboardSourceMode SourceMode => _configuration.SourceMode;
        public string DedicatedServerAddress => _configuration.NormalizedDedicatedServerAddress;
        public ushort DedicatedServerPort => _configuration.NormalizedDedicatedServerPort;
        public bool IsUsingDedicatedServer => _configuration.UseDedicatedServer;

        public void AddRecord(float time)
        {
            if (IsUsingDedicatedServer)
            {
                RequestDedicatedRecords();
                return;
            }

            AddLocalRecord(time);
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

                Debug.LogWarning($"[LeaderboardService] Dedicated leaderboard mode is selected, but no NetworkSessionController is available. Connect to {DedicatedServerAddress}:{DedicatedServerPort} to view server records.");
                return new List<Record>();
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
            if (!IsUsingDedicatedServer) return;

            NetworkSessionController session = NetworkSessionController.Instance;
            if (session != null)
                session.RequestDedicatedLeaderboardServerRpc();
        }

        private void AddLocalRecord(float time)
        {
            _data.Records.Add(new Record
            {
                Time = time,
                Date = System.DateTime.Now.Ticks
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

