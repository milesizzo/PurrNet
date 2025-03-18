using System;
using System.Collections.Generic;
using System.Linq;
using PurrNet.Logging;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace PurrNet.Modules
{
    public class UnityScenesModuleFactory : IScenesModuleFactory
    {
        public IScenesModule Create(NetworkManager manager, PlayersManager players)
        {
            return new UnityScenesModule(manager, players);
        }
    }

    public struct UnitySceneData
    {
        public int buildIndex;
        public SceneID sceneID;
    }

    public struct UnitySceneDataBatch
    {
        public List<UnitySceneData> scenes;
    }

    internal struct PendingOperation
    {
        public int buildIndex;
        public PurrSceneSettings settings;
        public SceneID idToAssign;
    }

    public class UnityScenesModule : ScenesModule
    {
        private ushort _nextSceneID = 1;
        private readonly List<PendingOperation> _pendingOperations = new List<PendingOperation>();
        private readonly Dictionary<SceneID, int> _sceneToBuildIndex = new Dictionary<SceneID, int>();

        public UnityScenesModule(NetworkManager manager, PlayersManager players)
            : base(manager, players)
        {
        }

        public override void Enable(bool asServer)
        {
            base.Enable(asServer);
            if (!asServer)
            {
                players.Subscribe<UnitySceneDataBatch>(OnSceneDataBatch);
            }
            SceneManager.sceneLoaded += SceneManagerOnSceneLoaded;
        }

        public override void Disable(bool asServer)
        {
            base.Disable(asServer);
            SceneManager.sceneLoaded -= SceneManagerOnSceneLoaded;
        }

        private void OnSceneDataBatch(PlayerID player, UnitySceneDataBatch data, bool asServer)
        {
            if (networkManager.isServer || asServer)
            {
                PurrLogger.LogError("Only clients can receive scene data");
                return;
            }
            _sceneToBuildIndex.Clear();
            for (var i = 0; i < data.scenes.Count; i++)
            {
                var scene = data.scenes[i];
                _sceneToBuildIndex.Add(scene.sceneID, scene.buildIndex);
            }
        }

        protected override void OnPrePlayerJoined(PlayerID player, bool isReconnect, bool asServer)
        {
            if (asServer)
            {
                players.Send(player, new UnitySceneDataBatch
                {
                    scenes = _sceneToBuildIndex.Select(x => new UnitySceneData
                    {
                        buildIndex = x.Value,
                        sceneID = x.Key
                    }).ToList()
                });
            }
            base.OnPrePlayerJoined(player, isReconnect, asServer);
        }

        private void SceneManagerOnSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            for (int i = 0; i < _pendingOperations.Count; i++)
            {
                var operation = _pendingOperations[i];

                if (operation.buildIndex == scene.buildIndex && operation.settings.mode == mode)
                {
                    AddScene(scene, operation.settings, operation.idToAssign);
                    _pendingOperations.RemoveAt(i);
                    break;
                }
            }
        }

        protected override SceneID GenerateSceneID()
        {
            return new(_nextSceneID++);
        }

        private bool IsScenePending(SceneID sceneId)
        {
            for (int i = 0; i < _pendingOperations.Count; i++)
            {
                if (_pendingOperations[i].idToAssign == sceneId)
                    return true;
            }

            return false;
        }

        /// <summary>
        /// Loads a scene asynchronously by its build index - Must be in build settings
        /// </summary>
        /// <param name="sceneIndex">Build index of the scene</param>
        /// <param name="mode">What UnityEngine scene load mode to use</param>
        public AsyncOperation LoadSceneAsync(int sceneIndex, LoadSceneMode mode = LoadSceneMode.Single)
        {
            var parameters = new LoadSceneParameters(mode);
            return LoadSceneAsync(sceneIndex, parameters);
        }

        /// <summary>
        /// Loads a scene asynchronously by its name - Must be in build settings
        /// </summary>
        /// <param name="sceneName">The name of the scene to load</param>
        /// <param name="mode">What UnityEngine scene load mode to use</param>
        public AsyncOperation LoadSceneAsync(string sceneName, LoadSceneMode mode = LoadSceneMode.Single)
        {
            var idx = SceneNameToBuildIndex(sceneName);

            if (idx == -1)
            {
                PurrLogger.LogError($"Scene {sceneName} not found in build settings");
                return null;
            }

            var parameters = new LoadSceneParameters(mode);
            return LoadSceneAsync(idx, parameters);
        }

        /// <summary>
        /// Loads a scene asynchronously by its name - Must be in build settings
        /// </summary>
        /// <param name="sceneName">The name of the scene to load</param>
        /// <param name="parameters">The UnityEngine LoadSceneParameters to use</param>
        public AsyncOperation LoadSceneAsync(string sceneName, LoadSceneParameters parameters)
        {
            var idx = SceneNameToBuildIndex(sceneName);

            if (idx == -1)
            {
                PurrLogger.LogError($"Scene {sceneName} not found in build settings");
                return null;
            }

            return LoadSceneAsync(idx, parameters);
        }

        /// <summary>
        /// Loads a scene asynchronously by its name - Must be in build settings
        /// </summary>
        /// <param name="sceneName">The name of the scene to load</param>
        /// <param name="settings">The PurrSceneSettings to use when loading the scene</param>
        public AsyncOperation LoadSceneAsync(string sceneName, PurrSceneSettings settings)
        {
            var idx = SceneNameToBuildIndex(sceneName);

            if (idx == -1)
            {
                PurrLogger.LogError($"Scene {sceneName} not found in build settings");
                return null;
            }

            return LoadSceneAsync(idx, settings);
        }

        /// <summary>
        /// Loads a scene asynchronously by its build index - Must be in build settings
        /// </summary>
        /// <param name="sceneIndex">Build index of the scene</param>
        /// <param name="parameters">The UnityEngine LoadSceneParameters to use</param>
        /// <returns></returns>
        public AsyncOperation LoadSceneAsync(int sceneIndex, LoadSceneParameters parameters)
        {
            if (!asServer)
            {
                PurrLogger.LogError("Only server can load scenes; for now at least ;)");
                return null;
            }

            return LoadSceneAsync(sceneIndex, new PurrSceneSettings
            {
                mode = parameters.loadSceneMode,
                physicsMode = parameters.localPhysicsMode,
                isPublic = true
            });
        }

        /// <summary>
        /// Loads a scene asynchronously by its build index - Must be in build settings
        /// </summary>
        /// <param name="buildIndex">Build index of the scene</param>
        /// <param name="settings">The PurrSceneSettings to use when loading the scene</param>
        /// <returns></returns>
        public AsyncOperation LoadSceneAsync(int buildIndex, PurrSceneSettings settings)
        {
            var sceneID = PrepareSceneServer(settings);
            if (!sceneID.HasValue)
                return null;

            var asyncOperation = SceneManager.LoadSceneAsync(buildIndex, new LoadSceneParameters(settings.mode, settings.physicsMode));
            var pendingOperation = new PendingOperation
            {
                buildIndex = buildIndex,
                settings = settings,
                idToAssign = GenerateSceneID(),
            };

            _pendingOperations.Add(pendingOperation);

            if (asServer && networkManager.isHost)
            {
                var clientModule = networkManager.GetModule<ScenesModule>(false) as UnityScenesModule;
                clientModule?._pendingOperations.Add(pendingOperation);
            }

            return asyncOperation;
        }

        /// <summary>
        /// Unloads a scene asynchronously by its name - Must be in build settings
        /// </summary>
        /// <param name="sceneName">Name of the scene to unload</param>
        /// <param name="options">The UnityEngine UnloadSceneOptions to use for the unloading</param>
        public AsyncOperation UnloadSceneAsync(string sceneName, UnloadSceneOptions options = UnloadSceneOptions.None)
        {
            var scene = SceneManager.GetSceneByName(sceneName);

            if (!scene.IsValid())
            {
                PurrLogger.LogError($"Scene with name '{sceneName}' not found");
                return null;
            }

            return UnloadSceneAsync(scene, options);
        }

        /// <summary>
        /// Unloads a scene asynchronously by its build index - Must be in build settings
        /// </summary>
        /// <param name="buildIndex">Build index of the scene to unload</param>
        /// <param name="options">The UnityEngine UnloadSceneOptions to use for the unloading</param>
        public AsyncOperation UnloadSceneAsync(int buildIndex, UnloadSceneOptions options = UnloadSceneOptions.None)
        {
            var scene = SceneManager.GetSceneByBuildIndex(buildIndex);

            if (!scene.IsValid())
            {
                PurrLogger.LogError($"Scene with build index {buildIndex} not found");
                return null;
            }

            return UnloadSceneAsync(scene, options);
        }

        /// <summary>
        /// Unloads a scene asynchronously by its Scene object - Must be in build settings
        /// </summary>
        /// <param name="scene">The Scene to unload</param>
        /// <param name="options">The UnityEngine UnloadSceneOptions to use for the unloading</param>
        public AsyncOperation UnloadSceneAsync(Scene scene, UnloadSceneOptions options = UnloadSceneOptions.None)
        {
            if (!asServer)
            {
                PurrLogger.LogError("Only server can unload scenes; for now at least ;)");
                return null;
            }

            if (networkManager.gameObject.scene == scene)
            {
                PurrLogger.LogError("Can't unload the network manager scene");
                return null;
            }

            if (!TryGetSceneID(scene, out var sceneID))
            {
                PurrLogger.LogError($"Scene {scene.name} not found in scenes list");
                return null;
            }

            RemoveScene(scene, options);

            return SceneManager.UnloadSceneAsync(scene, options);
        }

        protected override bool TryLoadScene(SceneID sceneID, PurrSceneSettings settings)
        {
            if (!_sceneToBuildIndex.TryGetValue(sceneID, out var buildIndex))
            {
                PurrLogger.LogError($"Scene with ID {sceneID} not found in build settings");
                return false;
            }
            try
            {
                SceneManager.LoadSceneAsync(buildIndex, settings.GetLoadSceneParameters());
            }
            catch (Exception e)
            {
                PurrLogger.LogError($"Error loading scene: {e}");
                return false;
            }
            _pendingOperations.Add(new PendingOperation
            {
                buildIndex = buildIndex,
                settings = settings,
                idToAssign = sceneID,
            });
            return true;
        }

        protected override bool TryUnloadScene(SceneID sceneID, UnloadSceneOptions options)
        {
            // if the scene is pending, don't do anything for now
            if (IsScenePending(sceneID)) return false;

            if (!TryGetSceneState(sceneID, out var sceneState))
            {
                PurrLogger.LogError($"Couldn't find scene with index {sceneID} to unload");
                return false;
            }

            SceneManager.UnloadSceneAsync(sceneState.scene, options);
            RemoveScene(sceneState.scene);
            return true;
        }

        private static int SceneNameToBuildIndex(string name)
        {
            var bIdxCount = SceneManager.sceneCountInBuildSettings;

            for (int i = 0; i < bIdxCount; i++)
            {
                var path = SceneUtility.GetScenePathByBuildIndex(i);
                var sceneName = System.IO.Path.GetFileNameWithoutExtension(path);

                if (sceneName == name)
                {
                    return i;
                }
            }

            return -1;
        }

        public override bool Cleanup()
        {
            if (_pendingOperations.Count > 0)
                return false;
            return base.Cleanup();
        }
    }
}
