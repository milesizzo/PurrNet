using System;
using System.Collections.Generic;
using System.Linq;
using PurrNet.Collections;
using PurrNet.Logging;
using PurrNet.Utils;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace PurrNet.Modules
{
    public struct PurrSceneSettings
    {
        public bool isPublic;
        internal bool wasPresentFromStart;
    }

    public struct SceneState
    {
        /// <summary>
        /// The unity scene object this ID is associated with
        /// </summary>
        public Scene scene;

        /// <summary>
        /// The network settings for this scene
        /// </summary>
        public PurrSceneSettings settings;

        public SceneState(Scene scene, PurrSceneSettings settings)
        {
            this.scene = scene;
            this.settings = settings;
        }
    }

    public struct LeaveSceneAction
    {
        public SceneID sceneID;
    }

    public struct JoinSceneAction
    {
        public SceneID sceneID;
    }

    internal struct ClientFinishedLoadingScene
    {
        public SceneID sceneID;
    }

    public delegate void OnSceneActionEvent(SceneID scene, bool asServer);

    public delegate void OnPlayerSceneEvent(PlayerID player, SceneID scene, bool asServer);

    public interface IScenesManager : INetworkModule
    {
        bool TryGetScene(SceneID sceneID, out Scene scene);

        bool TryGetSceneID(Scene scene, out SceneID sceneID);

        bool TryGetPlayersInScene(SceneID scene, out IReadOnlyHashSet<PlayerID> players);

        bool TryGetPlayerScenes(PlayerID playerId, out SceneID[] scenes);

        bool IsPlayerLoadedInScene(PlayerID player, SceneID scene);

        void AddPlayerToScene(PlayerID playerID, SceneID sceneID);

        void RemovePlayerFromScene(PlayerID playerID, SceneID sceneID);

        IEnumerable<(SceneID, Scene)> scenes { get; }

        /// <summary>
        /// First callback for when a scene is loaded
        /// </summary>
        public event OnSceneActionEvent onSceneLoaded;

        /// <summary>
        /// Callback for when a scene is unloaded
        /// </summary>
        public event OnSceneActionEvent onSceneUnloaded;

        /// <summary>
        /// Called once the player has started joining the scene (before loading)
        /// </summary>
        public event OnPlayerSceneEvent onPlayerJoinedScene;

        /// <summary>
        /// Called once the player has finished loading the scene
        /// </summary>
        public event OnPlayerSceneEvent onPrePlayerLoadedScene;

        /// <summary>
        /// Called once the player has finished loading the scene
        /// </summary>
        public event OnPlayerSceneEvent onPlayerLoadedScene;

        public event OnPlayerSceneEvent onPlayerLeftScene;
        public event OnPlayerSceneEvent onPlayerUnloadedScene;
    }

    public class ScenesManager : IScenesManager, ICleanup
    {
        private readonly NetworkManager _networkManager;
        private readonly PlayersManager _players;

        private bool _asServer;

        private readonly Dictionary<SceneID, SceneState> _sceneStates = new Dictionary<SceneID, SceneState>();
        private readonly Dictionary<Scene, SceneID> _sceneToSceneID = new Dictionary<Scene, SceneID>();
        private readonly Dictionary<SceneID, PurrHashSet<PlayerID>> _scenePlayers =
            new Dictionary<SceneID, PurrHashSet<PlayerID>>();
        private readonly Dictionary<SceneID, PurrHashSet<PlayerID>> _sceneLoadedPlayers =
            new Dictionary<SceneID, PurrHashSet<PlayerID>>();

        /// <summary>
        /// First callback for when a scene is loaded
        /// </summary>
        public event OnSceneActionEvent onSceneLoaded;

        /// <summary>
        /// Callback for when a scene is unloaded
        /// </summary>
        public event OnSceneActionEvent onSceneUnloaded;

        /// <summary>
        /// Called once the player has started joining the scene (before loading)
        /// </summary>
        public event OnPlayerSceneEvent onPlayerJoinedScene;

        /// <summary>
        /// Called once the player has finished loading the scene
        /// </summary>
        public event OnPlayerSceneEvent onPrePlayerLoadedScene;

        /// <summary>
        /// Called once the player has finished loading the scene
        /// </summary>
        public event OnPlayerSceneEvent onPlayerLoadedScene;

        public event OnPlayerSceneEvent onPlayerLeftScene;
        public event OnPlayerSceneEvent onPlayerUnloadedScene;

        public ScenesManager(NetworkManager manager, PlayersManager players)
        {
            _networkManager = manager;
            _players = players;
        }

        public bool TryGetScene(SceneID sceneID, out Scene state)
        {
            if (!_sceneStates.TryGetValue(sceneID, out var sceneState))
            {
                state = default;
                return false;
            }
            state = sceneState.scene;
            return true;
        }

        public bool TryGetPlayerScenes(PlayerID playerId, out SceneID[] scenes)
        {
            // FIXME(mgi): improve this
            var playerScenes = new List<SceneID>();

            foreach (var (scene, players) in _scenePlayers)
            {
                if (players.Contains(playerId))
                    playerScenes.Add(scene);
            }

            if (playerScenes.Count > 0)
            {
                scenes = playerScenes.ToArray();
                return true;
            }

            scenes = null;
            return false;
        }

        public IEnumerable<(SceneID, Scene)> scenes => _sceneStates.Select(x => (x.Key, x.Value.scene));

        public void AddScene(SceneID sceneID, PurrSceneSettings settings, Scene scene)
        {
            if (_sceneStates.TryGetValue(sceneID, out var sceneState))
            {
                PurrLogger.LogError($"Scene with ID {sceneID} already exists under {sceneState.scene.name}");
                return;
            }

            _sceneStates.Add(sceneID, new SceneState(scene, settings));
            _sceneToSceneID.Add(scene, sceneID);

            var playersInScene = new PurrHashSet<PlayerID>();
            _scenePlayers.Add(sceneID, playersInScene);
            _sceneLoadedPlayers.Add(sceneID, new PurrHashSet<PlayerID>());

            onSceneLoaded?.Invoke(sceneID, _asServer);
            if (!_asServer)
            {
                OnSceneLoadedClient(sceneID);
            }

            if (settings.isPublic)
            {
                // if the scene is public, add all connected players to the scene
                foreach (var playerID in _players.players)
                {
                    playersInScene.Add(playerID);
                    PlayerJoinedScene(playerID, sceneID);
                }
            }
        }

        private void OnSceneLoadedClient(SceneID sceneID)
        {
            if (!_players.localPlayerId.HasValue)
                return;

            onPrePlayerLoadedScene?.Invoke(_players.localPlayerId.Value, sceneID, false);
            onPlayerLoadedScene?.Invoke(_players.localPlayerId.Value, sceneID, false);

            _players.SendToServer(new ClientFinishedLoadingScene { sceneID = sceneID });
        }

        public void RemoveScene(Scene scene)
        {
            if (!_sceneToSceneID.TryGetValue(scene, out var sceneID))
            {
                PurrLogger.LogError($"Scene {scene.name} not found in {nameof(_sceneToSceneID)}");
                return;
            }

            _sceneStates.Remove(sceneID);
            _sceneToSceneID.Remove(scene);

            if (_scenePlayers.TryGetValue(sceneID, out var playersInScene))
            {
                // remove all players from the scene
                foreach (var playerID in playersInScene)
                {
                    onPlayerLeftScene?.Invoke(playerID, sceneID, _asServer);
                    onPlayerUnloadedScene?.Invoke(playerID, sceneID, _asServer);
                }

                _scenePlayers.Remove(sceneID);
                _sceneLoadedPlayers.Remove(sceneID);
            }
            onSceneUnloaded?.Invoke(sceneID, _asServer);
        }

        public virtual void Enable(bool asServer)
        {
            _asServer = asServer;

            var currentScene = _networkManager.gameObject.scene;
            var originalScene = _networkManager.originalScene;

            AddScene(SceneID.New(), new PurrSceneSettings
            {
                isPublic = true,
                wasPresentFromStart = true
            }, currentScene);

            if (currentScene != originalScene && originalScene.IsValid())
            {
                AddScene(SceneID.New(), new PurrSceneSettings
                {
                    isPublic = true,
                    wasPresentFromStart = true
                }, originalScene);
            }

            if (asServer)
            {
                _players.onPlayerJoined += OnPlayerJoined;
                _players.onPlayerLeft += OnPlayerLeft;
                _players.Subscribe<ClientFinishedLoadingScene>(RemoteClientLoadedScene);
            }
            else
            {
                if (_players.localPlayerId.HasValue)
                    OnLocalPlayerReady(_players.localPlayerId.Value);
                else _players.onLocalPlayerReceivedID += OnLocalPlayerReady;
                _players.Subscribe<JoinSceneAction>(OnLocalClientJoinedScene);
                _players.Subscribe<LeaveSceneAction>(OnLocalClientLeftScene);
            }
        }

        public virtual void Disable(bool asServer)
        {
            if (asServer)
            {
                _players.onPlayerJoined -= OnPlayerJoined;
                _players.onPlayerLeft -= OnPlayerLeft;
                _players.Unsubscribe<ClientFinishedLoadingScene>(RemoteClientLoadedScene);
            }
            else
            {
                _players.onLocalPlayerReceivedID -= OnLocalPlayerReady;
            }
        }

        private void OnLocalPlayerReady(PlayerID playerID)
        {
            foreach (var (sceneID, state) in _sceneStates)
            {
                if (state.scene.isLoaded)
                    OnSceneLoadedClient(sceneID);
            }

            _players.onLocalPlayerReceivedID -= OnLocalPlayerReady;
        }

        private void OnPlayerJoined(PlayerID player, bool isReconnect, bool asServer)
        {
            if (isReconnect && !_networkManager.networkRules.ShouldRemovePlayerFromSceneOnLeave())
            {
                /*foreach (var (scene, players) in _scenePlayers)
                {
                    if (players.Contains(player))
                        continue;

                    AddPlayerToScene
                }*/
                return;
            }

            foreach (var (scene, state) in _sceneStates)
            {
                if (!state.settings.isPublic)
                    continue;

                AddPlayerToScene(player, scene);
            }
        }

        private void OnPlayerLeft(PlayerID playerID, bool asServer)
        {
            if (!_networkManager.networkRules.ShouldRemovePlayerFromSceneOnLeave())
            {
                foreach (var (sceneID, players) in _sceneLoadedPlayers)
                {
                    if (players.Remove(playerID))
                        onPlayerUnloadedScene?.Invoke(playerID, sceneID, _asServer);
                }

                return;
            }

            foreach (var (scene, players) in _scenePlayers)
            {
                if (!players.Contains(playerID))
                    continue;

                RemovePlayerFromScene(playerID, scene);
            }
        }

        public void AddPlayerToScene(PlayerID playerID, SceneID sceneID)
        {
            if (!_asServer)
            {
                PurrLogger.LogError($"{nameof(AddPlayerToScene)} can only be called on the server; for now ;)");
                return;
            }

            if (!_scenePlayers.TryGetValue(sceneID, out var playersInScene))
            {
                PurrLogger.LogError($"SceneID '{sceneID}' not found in {nameof(_scenePlayers)}");
                return;
            }

            if (playersInScene.Add(playerID))
            {
                PlayerJoinedScene(playerID, sceneID);
            }
        }

        private void PlayerJoinedScene(PlayerID playerID, SceneID sceneID)
        {
            onPlayerJoinedScene?.Invoke(playerID, sceneID, _asServer);
            if (_asServer)
            {
                _players.Send(playerID, new JoinSceneAction { sceneID = sceneID });
            }
        }

        public void RemovePlayerFromScene(PlayerID playerID, SceneID sceneID)
        {
            if (!_asServer)
            {
                PurrLogger.LogError($"{nameof(RemovePlayerFromScene)} can only be called on the server; for now ;)");
                return;
            }

            RemoveLoadedPlayerFromScene(playerID, sceneID);

            if (!_scenePlayers.TryGetValue(sceneID, out var playersInScene))
            {
                PurrLogger.LogError($"SceneID '{sceneID}' not found in {nameof(_scenePlayers)}");
            }

            if (!playersInScene.Remove(playerID))
            {
                return;
            }

            var isSceneStillValid = _sceneStates.TryGetValue(sceneID, out var sceneState) && sceneState.scene.IsValid();
            if (!isSceneStillValid)
            {
                return;
            }
            _players.Send(playerID, new LeaveSceneAction { sceneID = sceneID });
        }

        private void RemoveLoadedPlayerFromScene(PlayerID playerID, SceneID sceneID)
        {
            if (!_sceneLoadedPlayers.TryGetValue(sceneID, out var loadedPlayersInScene))
            {
                PurrLogger.LogError($"SceneID '{sceneID}' not found in {nameof(_sceneLoadedPlayers)}");
                return;
            }

            if (loadedPlayersInScene.Remove(playerID))
                onPlayerUnloadedScene?.Invoke(playerID, sceneID, _asServer);
        }

        private void OnLocalClientJoinedScene(PlayerID player, JoinSceneAction data, bool asServer)
        {
            throw new NotImplementedException();
        }

        private void OnLocalClientLeftScene(PlayerID player, LeaveSceneAction data, bool asServer)
        {
            throw new NotImplementedException();
        }

        public bool IsPlayerLoadedInScene(PlayerID player, SceneID scene)
        {
            return _sceneLoadedPlayers.TryGetValue(scene, out var playersInScene) && playersInScene.Contains(player);
        }

        public bool IsPlayerInScene(PlayerID player, SceneID scene)
        {
            return _scenePlayers.TryGetValue(scene, out var playersInScene) && playersInScene.Contains(player);
        }

        public bool TryGetScenesForPlayer(PlayerID playerId, out SceneID[] scenes)
        {
            var playerScenes = new List<SceneID>();

            foreach (var (scene, players) in _scenePlayers)
            {
                if (players.Contains(playerId))
                    playerScenes.Add(scene);
            }

            if (playerScenes.Count > 0)
            {
                scenes = playerScenes.ToArray();
                return true;
            }

            scenes = null;
            return false;
        }

        /// <summary>
        /// Attempts to get the Networked SceneId of a scene
        /// </summary>
        /// <param name="scene">Scene to try and get</param>
        /// <param name="sceneId">Networked SceneID of the scene</param>
        /// <returns>Whether it successfully retrieved a scene or not</returns>
        public bool TryGetSceneID(Scene scene, out SceneID sceneId)
        {
            return _sceneToSceneID.TryGetValue(scene, out sceneId);
        }

        private void RemoteClientLoadedScene(PlayerID playerID, ClientFinishedLoadingScene data, bool asServer)
        {
            if (!_scenePlayers.TryGetValue(data.sceneID, out var playersInScene))
                return;

            if (!playersInScene.Contains(playerID))
                return;

            if (_sceneLoadedPlayers.TryGetValue(data.sceneID, out var loadedPlayers))
            {
                loadedPlayers.Add(playerID);
            }
            else
            {
                PurrLogger.LogError($"SceneID '{data.sceneID}' not found in {nameof(_sceneLoadedPlayers)}");
            }

            onPrePlayerLoadedScene?.Invoke(playerID, data.sceneID, asServer);
            onPlayerLoadedScene?.Invoke(playerID, data.sceneID, asServer);
        }

        /// <summary>
        /// Get all players that are both part of the scene and have finished loading the scene
        /// </summary>
        public bool TryGetPlayersInScene(SceneID scene, out IReadOnlyHashSet<PlayerID> players)
        {
            if (_sceneLoadedPlayers.TryGetValue(scene, out var data))
            {
                players = data;
                return true;
            }

            players = null;
            return false;
        }

        private readonly List<AsyncOperation> _pendingUnloads = new List<AsyncOperation>();
        private CleanupStage _cleanupStage;

        private enum CleanupStage
        {
            None,
            Skip,
            LoadEmptyScene,
            WaitOneFrame,
            UnloadScenes,
            UnloadScenesOnly,
            LoadOGScene,
            UnloadEmptyScene,
            ResetScene,
            Done
        }

        private Scene? _emptyScene;
        private AsyncOperation _ogSceneLoad;

        public bool Cleanup()
        {
            if (ApplicationContext.isQuitting)
                return true;

            if (!_networkManager.isOffline)
                return true;

            switch (_cleanupStage)
            {
                case CleanupStage.None:
                    {
                        _cleanupStage = _networkManager.IsDontDestroyOnLoad()
                            ? CleanupStage.LoadEmptyScene
                            : CleanupStage.UnloadScenesOnly;

                        if (_networkManager.TryGetModule(!_asServer, out ScenesManager module))
                            module._cleanupStage = CleanupStage.Skip;

                        return false;
                    }
                case CleanupStage.Skip: return false;
                case CleanupStage.Done: return true;
                case CleanupStage.LoadEmptyScene:
                    {
                        _cleanupStage = CleanupStage.WaitOneFrame;
                        _emptyScene = SceneManager.CreateScene("EmptyScene");
                        return false;
                    }
                case CleanupStage.WaitOneFrame:
                    {
                        _cleanupStage = CleanupStage.UnloadScenes;
                        return false;
                    }
                case CleanupStage.UnloadScenes:
                    {
                        if (UnloadAllScenesCleanup(false))
                            _cleanupStage = CleanupStage.LoadOGScene;
                        return false;
                    }
                case CleanupStage.UnloadScenesOnly:
                    {
                        if (UnloadAllScenesCleanup(true))
                        {
                            if (_networkManager.TryGetModule(!_asServer, out ScenesManager module))
                                module._cleanupStage = CleanupStage.Done;
                            _cleanupStage = CleanupStage.Done;
                        }

                        return false;
                    }
                case CleanupStage.LoadOGScene:
                    {
                        if (_ogSceneLoad == null)
                        {
                            if (_networkManager.originalSceneBuildIndex != -1)
                            {
                                _ogSceneLoad = SceneManager.LoadSceneAsync(_networkManager.originalSceneBuildIndex,
                                    LoadSceneMode.Additive);

                                if (_ogSceneLoad != null)
                                    _ogSceneLoad.allowSceneActivation = true;
                            }
                            else
                            {
                                _cleanupStage = CleanupStage.UnloadEmptyScene;
                            }
                        }

                        if (_ogSceneLoad is { isDone: true })
                        {
                            _cleanupStage = CleanupStage.ResetScene;
                        }

                        return false;
                    }
                case CleanupStage.ResetScene:
                    {
                        var activeScene = SceneManager.GetSceneByBuildIndex(_networkManager.originalSceneBuildIndex);
                        _networkManager.ResetOriginalScene(activeScene);
                        _cleanupStage = CleanupStage.UnloadEmptyScene;
                        return false;
                    }
                case CleanupStage.UnloadEmptyScene:
                    {
                        if (_emptyScene != null)
                        {
                            SceneManager.UnloadSceneAsync(_emptyScene.Value);
                            _emptyScene = null;
                            return false;
                        }

                        if (_networkManager.TryGetModule(!_asServer, out ScenesManager module))
                            module._cleanupStage = CleanupStage.Done;
                        _cleanupStage = CleanupStage.Done;
                        return false;
                    }
                default: return true;
            }
        }

        private bool UnloadAllScenesCleanup(bool keepNetworkManager)
        {
            // unload all scenes that aren't the network manager scene
            if (_sceneStates.Count > 0)
            {
                _pendingUnloads.Clear();

                foreach (var (_, scene) in _sceneStates)
                {
                    var unityScene = scene.scene;

                    if (keepNetworkManager && _networkManager.gameObject.scene.handle == unityScene.handle)
                        continue;

                    if (!unityScene.IsValid())
                        continue;

                    if (!unityScene.isLoaded)
                        continue;

                    bool isDontDestroyOnLoad = unityScene.name == "DontDestroyOnLoad";

                    if (isDontDestroyOnLoad)
                        continue;

                    _pendingUnloads.Add(SceneManager.UnloadSceneAsync(unityScene));
                }

                _sceneStates.Clear();
            }

            if (_pendingUnloads.Count > 0)
            {
                for (int i = 0; i < _pendingUnloads.Count; i++)
                {
                    if (_pendingUnloads[i] != null && !_pendingUnloads[i].isDone)
                        return false;
                }
            }

            return true;
        }
    }
}
