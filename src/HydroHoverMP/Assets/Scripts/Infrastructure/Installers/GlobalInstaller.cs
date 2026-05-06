using Core.States.Base;
using Infrastructure.Factories;
using Infrastructure.Providers.Assets;
using Infrastructure.Services.Audio;
using Infrastructure.Services.Input;
using Infrastructure.Services.Leaderboard;
using Infrastructure.Services.Network;
using Infrastructure.Services.Player;
using Infrastructure.Services.RaceManager;
using Infrastructure.Services.SceneManagement;
using Infrastructure.Services.Settings;
using Infrastructure.Services.Window;
using Zenject;

namespace Infrastructure.Installers
{
    public class GlobalInstaller : MonoInstaller
    {
        public override void InstallBindings()
        {
            BindCoreSystems();
            BindFactories();
            BindProviders();
        }

        private void BindProviders()
        {
            Container.BindInterfacesTo<AssetsAddressablesProvider>().AsSingle();
        }

        private void BindFactories()
        {
            Container.Bind<IStateFactory>().To<StateFactory>().AsSingle();
            Container.Bind<IUIFactory>().To<UIFactory>().AsSingle();
            Container.Bind<IGameObjectFactory>().To<GameObjectFactory>().AsSingle();
        }
    
        private void BindCoreSystems()
        {
            Container.Bind<GameStateMachine>().AsSingle();
            Container.BindInterfacesTo<InputService>().AsSingle();
            Container.Bind<ISceneLoaderService>().To<SceneLoaderService>().AsSingle();
            Container.Bind<IWindowService>().To<WindowService>().AsSingle();
            Container.Bind<IPlayerService>().To<PlayerService>().AsSingle();
            Container.Bind<IRaceManagerService>().To<RaceManagerService>().AsSingle();
            Container.Bind<ILeaderboardService>().To<LeaderboardService>().AsSingle();
            Container.Bind<IAudioService>().To<AudioService>().AsSingle();
            Container.Bind<ISettingsService>().To<SettingsService>().AsSingle();
            Container.BindInterfacesTo<NetworkConnectionService>().AsSingle();
        }
    }
}
