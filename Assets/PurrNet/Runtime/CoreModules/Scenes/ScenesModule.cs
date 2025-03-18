using System.Collections.Generic;
using PurrNet.Collections;
using PurrNet.Logging;
using PurrNet.Utils;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace PurrNet.Modules
{
    public interface IScenesModuleFactory
    {
        IScenesModule Create(NetworkManager manager, PlayersManager players);
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

    public struct PurrSceneSettings
    {
        public LoadSceneMode mode;
        public LocalPhysicsMode physicsMode;
        public bool isPublic;
        internal bool wasPresentFromStart;

        public LoadSceneParameters GetLoadSceneParameters()
        {
            return new LoadSceneParameters
            {
                loadSceneMode = mode,
                localPhysicsMode = physicsMode
            };
        }
    }

    internal struct ClientFinishedLoadingScene
    {
        public SceneID sceneID;
    }

    public delegate void OnSceneActionEvent(SceneID scene, bool asServer);

    // public delegate void OnSceneVisibilityEvent(SceneID scene, bool isVisible, bool asServer);

    public delegate void OnPlayerSceneEvent(PlayerID player, SceneID scene, bool asServer);

    public interface IScenesModule : INetworkModule
    {
        /// <summary>
        /// First callback for when a scene is loaded
        /// </summary>
        event OnSceneActionEvent onPreSceneLoaded;

        /// <summary>
        /// Callback for when a scene is loaded
        /// </summary>
        event OnSceneActionEvent onSceneLoaded;

        /// <summary>
        /// Callback for after onSceneLoaded has been called
        /// </summary>
        event OnSceneActionEvent onPostSceneLoaded;

        /// <summary>
        /// Callback for when a scene is unloaded
        /// </summary>
        event OnSceneActionEvent onSceneUnloaded;

        /// <summary>
        /// Callback for when a scene's visibility changes
        /// </summary>
        // event OnSceneVisibilityEvent onSceneVisibilityChanged;

        /// <summary>
        /// Called once the player has started joining the scene (before loading)
        /// </summary>
        public event OnPlayerSceneEvent onPlayerJoinedScene;

        /// <summary>
        /// Called once the player has finished loading the scene
        /// </summary>
        public event OnPlayerSceneEvent onPrePlayerloadedScene;

        /// <summary>
        /// Called once the player has finished loading the scene
        /// </summary>
        public event OnPlayerSceneEvent onPlayerLoadedScene;

        /// <summary>
        /// Called once the player has finished loading the scene
        /// </summary>
        public event OnPlayerSceneEvent onPostPlayerLoadedScene;

        public event OnPlayerSceneEvent onPlayerLeftScene;
        public event OnPlayerSceneEvent onPlayerUnloadedScene;

        IReadOnlyDictionary<SceneID, SceneState> sceneStates { get; }

        bool TryGetSceneID(Scene scene, out SceneID sceneId);
    }

    public abstract class ScenesModule : IScenesModule, IFixedUpdate, ICleanup
    {
        private readonly NetworkManager _networkManager;
        private readonly PlayersManager _players;

        private readonly SceneHistory _history;
        private bool _asServer;

        private readonly Queue<SceneAction> _actionsQueue = new Queue<SceneAction>();

        private readonly Dictionary<SceneID, SceneState> _scenes = new Dictionary<SceneID, SceneState>();
        private readonly Dictionary<Scene, SceneID> _idToScene = new Dictionary<Scene, SceneID>();
        private readonly List<SceneID> _rawScenes = new List<SceneID>();
        private readonly Dictionary<SceneID, PurrHashSet<PlayerID>> _scenePlayers =
            new Dictionary<SceneID, PurrHashSet<PlayerID>>();
        private readonly Dictionary<SceneID, PurrHashSet<PlayerID>> _sceneLoadedPlayers =
            new Dictionary<SceneID, PurrHashSet<PlayerID>>();

        /// <summary>
        /// First callback for when a scene is loaded
        /// </summary>
        public event OnSceneActionEvent onPreSceneLoaded;

        /// <summary>
        /// Callback for when a scene is loaded
        /// </summary>
        public event OnSceneActionEvent onSceneLoaded;

        /// <summary>
        /// Callback for after onSceneLoaded has been called
        /// </summary>
        public event OnSceneActionEvent onPostSceneLoaded;

        /// <summary>
        /// Callback for when a scene is unloaded
        /// </summary>
        public event OnSceneActionEvent onSceneUnloaded;

        /// <summary>
        /// Callback for when a scene's visibility changes
        /// </summary>
        // public event OnSceneVisibilityEvent onSceneVisibilityChanged;

        /// <summary>
        /// Called once the player has started joining the scene (before loading)
        /// </summary>
        public event OnPlayerSceneEvent onPlayerJoinedScene;

        /// <summary>
        /// Called once the player has finished loading the scene
        /// </summary>
        public event OnPlayerSceneEvent onPrePlayerloadedScene;

        /// <summary>
        /// Called once the player has finished loading the scene
        /// </summary>
        public event OnPlayerSceneEvent onPlayerLoadedScene;

        /// <summary>
        /// Called once the player has finished loading the scene
        /// </summary>
        public event OnPlayerSceneEvent onPostPlayerLoadedScene;

        public event OnPlayerSceneEvent onPlayerLeftScene;
        public event OnPlayerSceneEvent onPlayerUnloadedScene;

        public IReadOnlyList<SceneID> scenes => _rawScenes;
        public IReadOnlyDictionary<SceneID, SceneState> sceneStates => _scenes;

        protected bool asServer => _asServer;
        protected NetworkManager networkManager => _networkManager;
        protected PlayersManager players => _players;

        protected ScenesModule(NetworkManager manager, PlayersManager players)
        {
            _networkManager = manager;
            _players = players;
            _history = new SceneHistory();
        }

        public bool TryGetSceneState(SceneID sceneID, out SceneState state)
        {
            return _scenes.TryGetValue(sceneID, out state);
        }

        protected void AddScene(Scene scene, PurrSceneSettings settings, SceneID sceneID)
        {
            if (_scenes.TryGetValue(sceneID, out var state))
            {
                PurrLogger.LogError($"Scene with ID {sceneID} already exists under {state.scene.name}");
                return;
            }

            _history.AddLoadAction(new LoadSceneAction
            {
                sceneID = sceneID,
                settings = settings
            });
            _scenes.Add(sceneID, new SceneState(scene, settings));
            _idToScene.Add(scene, sceneID);
            _rawScenes.Add(sceneID);

            var playersInScene = new PurrHashSet<PlayerID>();
            _scenePlayers.Add(sceneID, playersInScene);
            _sceneLoadedPlayers.Add(sceneID, new PurrHashSet<PlayerID>());

            onPreSceneLoaded?.Invoke(sceneID, _asServer);
            onSceneLoaded?.Invoke(sceneID, _asServer);
            if (!asServer)
            {
                this.OnClientSceneLoaded(sceneID, asServer);
            }

            if (state.settings.isPublic)
                {
                    // if the scene is public, add all connected players to the scene
                    var connectedPlayersCount = _players.players.Count;

                    foreach (var playerID in _players.players)
                    {
                        playersInScene.Add(playerID);
                        onPlayerJoinedScene?.Invoke(playerID, sceneID, asServer);
                    }
                }

            onPostSceneLoaded?.Invoke(sceneID, _asServer);
        }

        private void OnClientSceneLoaded(SceneID sceneID, bool asServer)
        {
            if (!_players.localPlayerId.HasValue)
                return;

            onPrePlayerloadedScene?.Invoke(_players.localPlayerId.Value, sceneID, asServer);
            onPlayerLoadedScene?.Invoke(_players.localPlayerId.Value, sceneID, asServer);
            onPostPlayerLoadedScene?.Invoke(_players.localPlayerId.Value, sceneID, asServer);

            _players.SendToServer(new ClientFinishedLoadingScene { sceneID = sceneID });
        }

        /// <summary>
        /// Used to modify whether the given scene is public or not
        /// </summary>
        /// <param name="scene">The SceneID of the scene to modify</param>
        /// <param name="isPublic">Whether the given scene should be public</param>
        // public void UpdateSceneVisibility(SceneID scene, bool isPublic)
        // {
        //     if (_asServer)
        //     {
        //         PurrLogger.LogError("Only clients can change scene visibility; for now at least ;)");
        //         return;
        //     }

        //     if (!_scenes.TryGetValue(scene, out var state))
        //     {
        //         PurrLogger.LogError($"Scene with ID {scene} not found");
        //         return;
        //     }

        //     state.settings.isPublic = isPublic;
        //     _scenes[scene] = state;

        //     onSceneVisibilityChanged?.Invoke(scene, isPublic, _asServer);
        // }

        private readonly List<SceneID> _scenesToTriggerUnloadEvent = new List<SceneID>();

        protected void RemoveScene(Scene scene, UnloadSceneOptions options = UnloadSceneOptions.None)
        {
            if (!_idToScene.TryGetValue(scene, out var sceneID))
                return;

            _history.AddUnloadAction(new UnloadSceneAction { sceneID = sceneID, options = options });

            _scenes.Remove(sceneID);
            _idToScene.Remove(scene);
            _rawScenes.Remove(sceneID);
            _scenesToTriggerUnloadEvent.Add(sceneID);
        }

        public virtual void Enable(bool asServer)
        {
            _asServer = asServer;

            var currentScene = _networkManager.gameObject.scene;
            var originalScene = _networkManager.originalScene;

            AddScene(currentScene, new PurrSceneSettings
            {
                mode = LoadSceneMode.Single,
                isPublic = true,
                physicsMode = LocalPhysicsMode.None,
                wasPresentFromStart = true
            }, GenerateSceneID());

            if (currentScene != originalScene && originalScene.IsValid())
            {
                AddScene(originalScene, new PurrSceneSettings
                {
                    mode = LoadSceneMode.Additive,
                    isPublic = true,
                    physicsMode = LocalPhysicsMode.None,
                    wasPresentFromStart = true
                }, GenerateSceneID());
            }

            if (asServer)
            {
                _players.onPrePlayerJoined += OnPrePlayerJoined;
                _players.onPlayerJoined += OnPlayerJoined;
                _players.onPlayerLeft += OnPlayerLeft;
                _players.Subscribe<ClientFinishedLoadingScene>(RemoteClientLoadedScene);
            }
            else
            {
                _players.Subscribe<SceneActionsBatch>(OnSceneActionsBatch);
                if (_players.localPlayerId.HasValue)
                    OnLocalPlayerReady(_players.localPlayerId.Value);
                else _players.onLocalPlayerReceivedID += OnLocalPlayerReady;
            }
        }

        public virtual void Disable(bool asServer)
        {
            if (asServer)
            {
                _players.onPrePlayerJoined -= OnPrePlayerJoined;
                _players.onPlayerJoined -= OnPlayerJoined;
                _players.onPlayerLeft -= OnPlayerLeft;
                _players.Unsubscribe<ClientFinishedLoadingScene>(RemoteClientLoadedScene);
            }
            else
            {
                _players.Unsubscribe<SceneActionsBatch>(OnSceneActionsBatch);
                _players.onLocalPlayerReceivedID -= OnLocalPlayerReady;
            }
        }

        private void OnLocalPlayerReady(PlayerID playerID)
        {
            foreach (var (sceneID, state) in _scenes)
            {
                if (state.scene.isLoaded)
                    OnClientSceneLoaded(sceneID, _asServer);
            }

            _players.onLocalPlayerReceivedID -= OnLocalPlayerReady;
        }

        protected virtual void OnPrePlayerJoined(PlayerID playerID, bool isReconnect, bool asServer)
        {
            if (!asServer)
                return;

            var history = _history.GetFullHistory();

            _playerFilteredActions.Clear();

            for (var i = 0; i < history.actions.Count; i++)
            {
                var action = history.actions[i];

                var target = action.type switch
                {
                    SceneActionType.Load => action.loadSceneAction.sceneID,
                    SceneActionType.Unload => action.unloadSceneAction.sceneID,
                    SceneActionType.SetActive => action.setActiveSceneAction.sceneID,
                    _ => default
                };

                if (IsPlayerInScene(playerID, target))
                    _playerFilteredActions.Add(action);
            }

            if (_playerFilteredActions.Count > 0)
                _players.Send(playerID, new SceneActionsBatch { actions = _playerFilteredActions });
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

            foreach (var (scene, state) in _scenes)
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

        private void OnPlayerLeftScene(PlayerID playerID, SceneID sceneID)
        {
            onPlayerLeftScene?.Invoke(playerID, sceneID, _asServer);

            if (!asServer)
                return;

            var isSceneStillValid = _scenes.TryGetValue(sceneID, out var state) && state.scene.IsValid();

            if (!isSceneStillValid)
                return;

            _playerFilteredActions.Clear();
            _playerFilteredActions.Add(new SceneAction
            {
                type = SceneActionType.Unload,
                unloadSceneAction = new UnloadSceneAction
                {
                    sceneID = sceneID,
                    options = UnloadSceneOptions.None
                }
            });

            _players.Send(playerID, new SceneActionsBatch { actions = _playerFilteredActions });
        }

        public void AddPlayerToScene(PlayerID playerID, SceneID sceneID)
        {
            if (!_asServer)
            {
                PurrLogger.LogError("AddPlayerToScene can only be called on the server; for now ;)");
                return;
            }

            if (!_scenePlayers.TryGetValue(sceneID, out var playersInScene))
            {
                PurrLogger.LogError($"SceneID '{sceneID}' not found in scenes module; aborting AddPlayerToScene");
                return;
            }

            if (playersInScene.Add(playerID))
            {
                onPlayerJoinedScene?.Invoke(playerID, sceneID, _asServer);
                var history = _history.GetFullHistory();

                _playerFilteredActions.Clear();

                // send all actions for the scene
                FilterActionsForPlayerBySceneID(playerID, sceneID, history.actions, _playerFilteredActions);

                if (_playerFilteredActions.Count > 0)
                    _players.Send(playerID, new SceneActionsBatch { actions = _playerFilteredActions });
            }
        }

        public void RemovePlayerFromScene(PlayerID playerID, SceneID sceneID)
        {
            if (!_asServer)
            {
                PurrLogger.LogError("RemovePlayerFromScene can only be called on the server; for now ;)");
                return;
            }

            if (_sceneLoadedPlayers.TryGetValue(sceneID, out var loadedPlayersInScene))
            {
                if (loadedPlayersInScene.Remove(playerID))
                    onPlayerUnloadedScene?.Invoke(playerID, sceneID, _asServer);
            }
            else
            {
                PurrLogger.LogError($"SceneID '{sceneID}' not found in {nameof(_sceneLoadedPlayers)}");
            }

            if (_scenePlayers.TryGetValue(sceneID, out var playersInScene))
            {
                if (playersInScene.Remove(playerID))
                    OnPlayerLeftScene(playerID, sceneID);
            }
            else
            {
                PurrLogger.LogError($"SceneID '{sceneID}' not found in {nameof(_scenePlayers)}");
            }
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
        /// Remove the player from all scenes and add them to the new scene
        /// </summary>
        public void MovePlayerToSingleScene(PlayerID player, SceneID scene)
        {
            if (_scenePlayers.TryGetValue(scene, out var playersInScene) && !playersInScene.Contains(player))
                AddPlayerToScene(player, scene);

            foreach (var (existingScene, players) in _scenePlayers)
            {
                if (scene == existingScene)
                    continue;

                if (!players.Contains(player))
                    continue;

                RemovePlayerFromScene(player, existingScene);
            }
        }

        protected abstract SceneID GenerateSceneID();

        protected abstract bool TryLoadScene(SceneID sceneID, PurrSceneSettings settings);

        protected abstract bool TryUnloadScene(SceneID sceneID, UnloadSceneOptions options);

        private void HandleNextSceneAction()
        {
            if (_actionsQueue.Count == 0) return;

            var action = _actionsQueue.Peek();
            switch (action.type)
            {
                case SceneActionType.Load:
                    {
                        if (_networkManager.isHost && !_asServer)
                        {
                            _actionsQueue.Dequeue();
                            break;
                        }

                        var loadAction = action.loadSceneAction;

                        if (!this.TryLoadScene(loadAction.sceneID, loadAction.settings))
                        {
                            break;
                        }

                        if (loadAction.settings.mode == LoadSceneMode.Single)
                        {
                            for (int i = 0; i < _rawScenes.Count; i++)
                            {
                                bool isDontDestroyOnLoad = _scenes[_rawScenes[i]].scene.name == "DontDestroyOnLoad";
                                if (!isDontDestroyOnLoad)
                                    RemoveScene(_scenes[_rawScenes[i]].scene);
                            }
                        }

                        _actionsQueue.Dequeue();
                        break;
                    }
                case SceneActionType.Unload:
                    {
                        var currentlyLoadedCount = _scenes.Count;
                        if (currentlyLoadedCount == 1)
                        {
                            // wait for the next load action
                            break;
                        }

                        var sceneID = action.unloadSceneAction.sceneID;

                        if (_networkManager.isHost && !_asServer)
                        {
                            _scenesToTriggerUnloadEvent.Add(sceneID);
                            _actionsQueue.Dequeue();
                            break;
                        }

                        if (!TryUnloadScene(sceneID, action.unloadSceneAction.options))
                        {
                            break;
                        }

                        _actionsQueue.Dequeue();
                        break;
                    }
            }
        }

        private void OnSceneActionsBatch(PlayerID player, SceneActionsBatch data, bool asServer)
        {
            if (_networkManager.isServer || _asServer)
            {
                var serverModule = _networkManager.GetModule<ScenesModule>(true);
                for (var i = 0; i < data.actions.Count; i++)
                {
                    var action = data.actions[i];

                    switch (action.type)
                    {
                        case SceneActionType.Load:
                            {
                                if (_scenes.ContainsKey(action.loadSceneAction.sceneID))
                                    continue;

                                if (serverModule.TryGetSceneState(action.loadSceneAction.sceneID, out var state))
                                    AddScene(state.scene, state.settings, action.loadSceneAction.sceneID);
                                break;
                            }
                        case SceneActionType.Unload:
                            {
                                if (!_scenes.ContainsKey(action.unloadSceneAction.sceneID))
                                    continue;

                                if (serverModule.TryGetSceneState(action.unloadSceneAction.sceneID, out var state))
                                    RemoveScene(state.scene);
                                break;
                            }

                        case SceneActionType.SetActive:
                        default:
                            break;
                    }
                }

                return;
            }

            for (var i = 0; i < data.actions.Count; i++)
                _actionsQueue.Enqueue(data.actions[i]);

            HandleNextSceneAction();
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

            onPrePlayerloadedScene?.Invoke(playerID, data.sceneID, asServer);
            onPlayerLoadedScene?.Invoke(playerID, data.sceneID, asServer);
            onPostPlayerLoadedScene?.Invoke(playerID, data.sceneID, asServer);
        }

        /// <summary>
        /// Get all players that are both part of the scene and have finished loading the scene
        /// </summary>
        public bool TryGetPlayersInScene(SceneID scene, out IReadonlyHashSet<PlayerID> players)
        {
            if (_sceneLoadedPlayers.TryGetValue(scene, out var data))
            {
                players = data;
                return true;
            }

            players = null;
            return false;
        }

        static readonly List<SceneAction> _playerFilteredActions = new List<SceneAction>();

        private void FilterActionsForPlayer(PlayerID player, IReadOnlyList<SceneAction> actions,
            ICollection<SceneAction> destination)
        {
            for (var i = 0; i < actions.Count; i++)
            {
                var action = actions[i];

                var target = action.type switch
                {
                    SceneActionType.Load => action.loadSceneAction.sceneID,
                    SceneActionType.Unload => action.unloadSceneAction.sceneID,
                    SceneActionType.SetActive => action.setActiveSceneAction.sceneID,
                    _ => default
                };

                if (_scenePlayers.IsPlayerInScene(player, target))
                    destination.Add(action);
            }
        }

        private void FilterActionsForPlayerBySceneID(PlayerID player, SceneID id, IReadOnlyList<SceneAction> actions,
            ICollection<SceneAction> destination)
        {
            for (var i = 0; i < actions.Count; i++)
            {
                var action = actions[i];

                var target = action.type switch
                {
                    SceneActionType.Load => action.loadSceneAction.sceneID,
                    SceneActionType.Unload => action.unloadSceneAction.sceneID,
                    SceneActionType.SetActive => action.setActiveSceneAction.sceneID,
                    _ => default
                };

                if (target != id)
                    continue;

                if (_scenePlayers.IsPlayerInScene(player, target))
                    destination.Add(action);
            }
        }

        public void FixedUpdate()
        {
            HandleNextSceneAction();

            if (_history.hasUnflushedActions)
                FlushActions();

            if (_scenesToTriggerUnloadEvent.Count > 0)
            {
                for (var i = 0; i < _scenesToTriggerUnloadEvent.Count; i++)
                {
                    var sceneID = _scenesToTriggerUnloadEvent[i];
                    if (_scenePlayers.TryGetValue(sceneID, out var playersInScene))
                    {
                        // remove all players from the scene
                        foreach (var player in playersInScene)
                        {
                            OnPlayerLeftScene(player, sceneID);
                            onPlayerUnloadedScene?.Invoke(player, sceneID, asServer);
                        }

                        _scenePlayers.Remove(sceneID);
                        _sceneLoadedPlayers.Remove(sceneID);
                    }
                    onSceneUnloaded?.Invoke(sceneID, _asServer);
                }
                _scenesToTriggerUnloadEvent.Clear();
            }
        }

        private void FlushActions()
        {
            var delta = _history.GetDelta();

            for (var i = 0; i < _players.players.Count; i++)
            {
                var player = _players.players[i];

                _playerFilteredActions.Clear();

                FilterActionsForPlayer(player, delta.actions, _playerFilteredActions);

                if (_playerFilteredActions.Count > 0)
                {
                    _players.Send(player, new SceneActionsBatch { actions = _playerFilteredActions });
                }
            }

            _history.Flush();
        }

        protected SceneID? PrepareSceneServer(PurrSceneSettings settings)
        {
            if (!asServer)
            {
                PurrLogger.LogError("Only server can load scenes; for now at least ;)");
                return null;
            }

            if (settings.mode == LoadSceneMode.Single)
            {
                if (TryGetSceneID(networkManager.gameObject.scene, out var nmId) &&
                    TryGetSceneState(nmId, out var nmScene))
                {
                    if (nmScene.scene.name != "DontDestroyOnLoad")
                    {
                        PurrLogger.LogError("Network manager scene is not DontDestroyOnLoad and you are trying to" +
                                            " load a new scene with LoadSceneMode.Single");
                        return null;
                    }
                }

                for (int i = 0; i < scenes.Count; i++)
                {
                    bool isDontDestroyOnLoad = sceneStates[scenes[i]].scene.name == "DontDestroyOnLoad";
                    if (!isDontDestroyOnLoad)
                        RemoveScene(sceneStates[scenes[i]].scene);
                }
            }

            return GenerateSceneID();
        }

        private readonly List<AsyncOperation> _pendingUnloads = new List<AsyncOperation>();
        private CleanupStage _cleanupStage;

        enum CleanupStage
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

        public virtual bool Cleanup()
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

                        if (_networkManager.TryGetModule(!_asServer, out ScenesModule module))
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
                            if (_networkManager.TryGetModule(!_asServer, out ScenesModule module))
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

                        if (_networkManager.TryGetModule(!_asServer, out ScenesModule module))
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
            if (_scenes.Count > 0)
            {
                _pendingUnloads.Clear();

                foreach (var (_, scene) in _scenes)
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

                _scenes.Clear();
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

        /// <summary>
        /// Attempts to get the Networked SceneId of a scene
        /// </summary>
        /// <param name="scene">Scene to try and get</param>
        /// <param name="sceneId">Networked SceneID of the scene</param>
        /// <returns>Whether it successfully retrieved a scene or not</returns>
        public bool TryGetSceneID(Scene scene, out SceneID sceneId)
        {
            return _idToScene.TryGetValue(scene, out sceneId);
        }

        /// <summary>
        /// Attempts to get the Networked SceneId of a scene
        /// </summary>
        /// <param name="buildIndex">BuildIndex of Scene to try and get</param>
        /// <param name="sceneId">Networked SceneID of the scene</param>
        /// <returns>Whether it successfully retrieved a scene or not</returns>
        public bool TryGetScene(int buildIndex, out SceneID sceneId)
        {
            for (int i = 0; i < _rawScenes.Count; i++)
            {
                if (_scenes.TryGetValue(_rawScenes[i], out var state))
                {
                    if (state.scene.buildIndex == buildIndex)
                    {
                        sceneId = _rawScenes[i];
                        return true;
                    }
                }
            }

            sceneId = default;
            return false;
        }

        /// <summary>
        /// Checks whether a scene is loaded on the network
        /// </summary>
        /// <param name="buildIndex">Build index of scene to check</param>
        /// <returns>Whether the scene is loaded on the network or not</returns>
        // public bool IsSceneLoaded(int buildIndex)
        // {
        //     for (int i = 0; i < _rawScenes.Count; i++)
        //     {
        //         if (_scenes.TryGetValue(_rawScenes[i], out var state))
        //         {
        //             if (state.scene.buildIndex == buildIndex)
        //                 return true;
        //         }
        //     }

        //     return false;
        // }
    }
}
