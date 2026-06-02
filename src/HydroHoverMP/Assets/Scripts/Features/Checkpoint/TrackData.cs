using System.Linq;
using Features.Networking;
using Features.Trigger;
using Infrastructure.Services.RaceManager;
using UnityEngine;
using Zenject;

namespace Features.Checkpoint
{
    public class TrackData : MonoBehaviour
    {
        private IRaceManagerService _raceManagerService;

        private void Start()
        {
            ResolveRaceManagerIfNeeded();
            if (_raceManagerService == null)
            {
                Debug.LogWarning("[TrackData] IRaceManagerService недоступен — регистрация трассы пропущена.");
                return;
            }

            var checkpoints = GetComponentsInChildren<CheckpointTrigger>().ToList();

            _raceManagerService.RegisterTrack(checkpoints);
            NetworkRaceManager.Instance?.RegisterTrack(checkpoints.Count);

            if (NetworkSessionController.Instance == null)
                _raceManagerService.StartRace();
        }

        // TrackData живёт в сцене Level. Её контекст наследуется от контракта GameplayContext,
        // который на выделенном сервере может дублироваться, ломая инъекцию через контекст сцены.
        // Берём сервис напрямую из единственного ProjectContext, минуя цепочку контекстов сцен.
        private void ResolveRaceManagerIfNeeded()
        {
            if (_raceManagerService != null) return;
            if (ProjectContext.HasInstance)
                _raceManagerService = ProjectContext.Instance.Container.TryResolve<IRaceManagerService>();
        }
    }
}
