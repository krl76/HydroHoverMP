using System.Collections.Generic;
using System.IO;
using System.Linq;
using Data.Leaderbords;
using Newtonsoft.Json;
using UnityEngine;

namespace Infrastructure.Services.Leaderboard
{
    public class LeaderboardService : ILeaderboardService
    {
        private const string FileName = "leaderboard.json";
        private readonly string _path;
        private LeaderboardData _data;

        public LeaderboardService()
        {
            _path = Path.Combine(Application.persistentDataPath, FileName);
            Load();
        }

        public void AddRecord(float time)
        {
            _data.Records.Add(new Record
            {
                Time = time,
                Date = System.DateTime.Now.Ticks
            });
            
            _data.Records = _data.Records.OrderBy(r => r.Time).ToList();
            Save();
        }

        public List<Record> GetTopRecords(int count)
        {
            return _data.Records.Take(count).ToList();
        }

        public float GetBestTime()
        {
            return _data.Records.Count > 0 ? _data.Records[0].Time : 0f;
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