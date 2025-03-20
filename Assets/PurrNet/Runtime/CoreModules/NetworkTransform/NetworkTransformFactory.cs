using System.Collections.Generic;
using PurrNet.Logging;

namespace PurrNet.Modules
{
    public class NetworkTransformFactory : INetworkModule, IPostFixedUpdate
    {
        readonly IScenesManager _scenes;
        readonly PlayersBroadcaster _broadcaster;
        readonly NetworkManager _manager;
        private readonly HierarchyFactory _factory;

        readonly List<NetworkTransformModule> _rawModules = new();
        readonly Dictionary<SceneID, NetworkTransformModule> _modules = new();

        public NetworkTransformFactory(IScenesManager scenes, PlayersBroadcaster broadcaster,
            NetworkManager manager, HierarchyFactory factory)
        {
            _scenes = scenes;
            _broadcaster = broadcaster;
            _manager = manager;
            _factory = factory;
        }

        public void Enable(bool asServer)
        {
            foreach (var (sceneID, scene) in _scenes.scenes)
            {
                if (scene.isLoaded)
                    OnPreSceneLoaded(sceneID, asServer);
            }

            _scenes.onSceneLoaded += OnPreSceneLoaded;
            _scenes.onSceneUnloaded += OnSceneUnloaded;
        }

        public void Disable(bool asServer)
        {
            for (var i = 0; i < _rawModules.Count; i++)
                _rawModules[i].Disable(asServer);

            _scenes.onSceneLoaded -= OnPreSceneLoaded;
            _scenes.onSceneUnloaded -= OnSceneUnloaded;
        }

        private void OnPreSceneLoaded(SceneID scene, bool asServer)
        {
            if (_modules.ContainsKey(scene))
            {
                PurrLogger.LogError(
                    $"Hierarchy module for scene {scene} already exists; trying to create another one?");
                return;
            }

            var hierarchy = new NetworkTransformModule(_manager, _broadcaster, _scenes, scene, _factory);

            hierarchy.Enable(asServer);

            _rawModules.Add(hierarchy);
            _modules.Add(scene, hierarchy);
        }

        private void OnSceneUnloaded(SceneID scene, bool asServer)
        {
            if (!_modules.TryGetValue(scene, out var hierarchy))
            {
                PurrLogger.LogError($"Hierarchy module for scene {scene} doesn't exist; trying to remove it?");
                return;
            }

            hierarchy.Disable(asServer);

            _rawModules.Remove(hierarchy);
            _modules.Remove(scene);
        }

        public void PostFixedUpdate()
        {
            for (var i = 0; i < _rawModules.Count; i++)
                _rawModules[i].FixedUpdate();
        }

        public bool TryGetModule(SceneID sceneId, out NetworkTransformModule module)
        {
            return _modules.TryGetValue(sceneId, out module);
        }
    }
}
