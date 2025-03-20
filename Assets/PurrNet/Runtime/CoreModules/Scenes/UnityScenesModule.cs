using System.Collections.Generic;
using System.Linq;
using PurrNet.Logging;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace PurrNet.Modules
{
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

    public class UnityScenesModule : INetworkModule
    {
        private readonly NetworkManager _networkManager;
        private readonly PlayersManager _playersManager;
        private readonly ScenesManager _scenesManager;
        private bool _asServer;
        private readonly List<PendingOperation> _pendingOperations = new List<PendingOperation>();
        private readonly Dictionary<SceneID, int> _sceneToBuildIndex = new Dictionary<SceneID, int>();

        public UnityScenesModule(NetworkManager networkManager, PlayersManager playersManager, ScenesManager scenesManager)
        {
            _networkManager = networkManager;
            _playersManager = playersManager;
            _scenesManager = scenesManager;
        }

        public void Enable(bool asServer)
        {
            _asServer = asServer;
            if (!asServer)
            {
                _playersManager.Subscribe<UnitySceneDataBatch>(OnSceneDataBatch);
            }
            SceneManager.sceneLoaded += SceneManagerOnSceneLoaded;
        }

        public void Disable(bool asServer)
        {
            SceneManager.sceneLoaded -= SceneManagerOnSceneLoaded;
        }

        private void OnSceneDataBatch(PlayerID player, UnitySceneDataBatch data, bool asServer)
        {
            if (_networkManager.isServer || asServer)
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

        private void OnPrePlayerJoined(PlayerID player, bool isReconnect, bool asServer)
        {
            if (asServer)
            {
                _playersManager.Send(player, new UnitySceneDataBatch
                {
                    scenes = _sceneToBuildIndex.Select(x => new UnitySceneData
                    {
                        buildIndex = x.Value,
                        sceneID = x.Key
                    }).ToList()
                });
            }
        }

        private void SceneManagerOnSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            for (int i = 0; i < _pendingOperations.Count; i++)
            {
                var operation = _pendingOperations[i];

                if (operation.buildIndex == scene.buildIndex/* && operation.settings.mode == mode*/)
                {
                    _scenesManager.AddScene(operation.idToAssign, operation.settings, scene);
                    _pendingOperations.RemoveAt(i);
                    break;
                }
            }
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
        /// Loads a scene asynchronously by its name - Must be in build settings
        /// </summary>
        /// <param name="sceneName">The name of the scene to load</param>
        public AsyncOperation LoadSceneAsync(string sceneName)
        {
            return LoadSceneAsync(sceneName, new PurrSceneSettings
            {
                isPublic = true,
            });
        }

        /// <summary>
        /// Loads a scene asynchronously by its name - Must be in build settings
        /// </summary>
        /// <param name="sceneName">The name of the scene to load</param>
        /// <param name="settings">The PurrSceneSettings to use when loading the scene</param>
        public AsyncOperation LoadSceneAsync(string sceneName, PurrSceneSettings settings)
        {
            var buildIndex = SceneNameToBuildIndex(sceneName);

            if (buildIndex == -1)
            {
                PurrLogger.LogError($"Scene {sceneName} not found in build settings");
                return null;
            }

            return LoadSceneAsync(buildIndex, settings);
        }

        /// <summary>
        /// Loads a scene asynchronously by its build index - Must be in build settings
        /// </summary>
        /// <param name="buildIndex">Build index of the scene</param>
        /// <param name="parameters">The UnityEngine LoadSceneParameters to use</param>
        /// <returns></returns>
        public AsyncOperation LoadSceneAsync(int buildIndex)
        {
            return LoadSceneAsync(buildIndex, new PurrSceneSettings
            {
                isPublic = true,
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
            if (!_asServer)
            {
                PurrLogger.LogError("Only server can load scenes; for now at least ;)");
                return null;
            }

            var asyncOperation = SceneManager.LoadSceneAsync(buildIndex);
            var pendingOperation = new PendingOperation
            {
                buildIndex = buildIndex,
                settings = settings,
                idToAssign = SceneID.New(),
            };

            _pendingOperations.Add(pendingOperation);

            if (_asServer && _networkManager.isHost)
            {
                var clientModule = _networkManager.GetModule<UnityScenesModule>(false);
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
            if (!_asServer)
            {
                PurrLogger.LogError("Only server can unload scenes; for now at least ;)");
                return null;
            }

            if (_networkManager.gameObject.scene == scene)
            {
                PurrLogger.LogError("Can't unload the network manager scene");
                return null;
            }

            _scenesManager.RemoveScene(scene);

            return SceneManager.UnloadSceneAsync(scene, options);
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

        public bool Cleanup()
        {
            if (_pendingOperations.Count > 0)
                return false;
            return true;
        }
    }
}
