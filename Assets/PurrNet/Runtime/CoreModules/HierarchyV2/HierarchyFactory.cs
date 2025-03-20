using System.Collections.Generic;
using PurrNet.Logging;
using UnityEngine.SceneManagement;

namespace PurrNet.Modules
{
    public class HierarchyFactory : INetworkModule, IFixedUpdate, IPreFixedUpdate, ICleanup
    {
        readonly IScenesManager _scenes;

        readonly NetworkManager _manager;

        readonly Dictionary<SceneID, HierarchyV2> _hierarchies = new();

        readonly List<HierarchyV2> _rawHierarchies = new();

        readonly PlayersManager _playersManager;

        public HierarchyFactory(NetworkManager manager, IScenesManager scenes, PlayersManager playersManager)
        {
            _manager = manager;
            _scenes = scenes;
            _playersManager = playersManager;
        }

        public event IdentityAction onEarlyIdentityAdded;

        public event IdentityAction onIdentityAdded;

        public event IdentityAction onIdentityRemoved;

        public event ObserverAction onEarlyObserverAdded;

        // public event ObserverAction onObserverAdded;

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
            for (var i = 0; i < _rawHierarchies.Count; i++)
                _rawHierarchies[i].Disable();

            _scenes.onSceneLoaded -= OnPreSceneLoaded;
            _scenes.onSceneUnloaded -= OnSceneUnloaded;
        }

        private void OnPreSceneLoaded(SceneID sceneID, bool asServer)
        {
            if (_hierarchies.ContainsKey(sceneID))
            {
                PurrLogger.LogError(
                    $"Hierarchy module for scene {sceneID} already exists; trying to create another one?");
                return;
            }

            if (!_scenes.TryGetScene(sceneID, out var scene))
            {
                PurrLogger.LogError($"Scene {sceneID} doesn't exist; trying to create hierarchy module for it?");
                return;
            }

            var hierarchy = new HierarchyV2(_manager, sceneID, scene, _scenes, _playersManager, asServer);

            hierarchy.onEarlyIdentityAdded += OnEarlyIdentityAdded;
            hierarchy.onObserverAdded += OnObserverAdded;
            hierarchy.onIdentityAdded += OnIdentityAdded;
            hierarchy.onIdentityRemoved += OnIdentityRemoved;

            hierarchy.Enable();

            _rawHierarchies.Add(hierarchy);
            _hierarchies.Add(sceneID, hierarchy);
        }

        private void OnEarlyIdentityAdded(NetworkIdentity identity) => onEarlyIdentityAdded?.Invoke(identity);

        private void OnObserverAdded(PlayerID player, NetworkIdentity identity) =>
            onEarlyObserverAdded?.Invoke(player, identity);

        private void OnIdentityAdded(NetworkIdentity identity) => onIdentityAdded?.Invoke(identity);

        private void OnIdentityRemoved(NetworkIdentity identity) => onIdentityRemoved?.Invoke(identity);

        private void OnSceneUnloaded(SceneID scene, bool asserver)
        {
            if (!_hierarchies.TryGetValue(scene, out var hierarchy))
            {
                PurrLogger.LogError($"Hierarchy module for scene {scene} doesn't exist; trying to unload it?");
                return;
            }

            hierarchy.Disable();

            hierarchy.onEarlyIdentityAdded -= OnEarlyIdentityAdded;
            hierarchy.onObserverAdded -= OnObserverAdded;
            hierarchy.onIdentityAdded -= OnIdentityAdded;
            hierarchy.onIdentityRemoved -= OnIdentityRemoved;

            _rawHierarchies.Remove(hierarchy);
            _hierarchies.Remove(scene);
        }

        public void FixedUpdate()
        {
            for (var i = 0; i < _rawHierarchies.Count; i++)
                _rawHierarchies[i].PostNetworkMessages();
        }

        public void PreFixedUpdate()
        {
            for (var i = 0; i < _rawHierarchies.Count; i++)
                _rawHierarchies[i].PreNetworkMessages();
        }

        public bool TryGetHierarchy(SceneID sceneId, out HierarchyV2 o)
        {
            return _hierarchies.TryGetValue(sceneId, out o);
        }

        public bool TryGetHierarchy(Scene scene, out HierarchyV2 o)
        {
            if (_scenes.TryGetSceneID(scene, out var sceneId))
                return _hierarchies.TryGetValue(sceneId, out o);
            o = null;
            return false;
        }

        public bool TryGetIdentity(SceneID scene, NetworkID id, out NetworkIdentity result)
        {
            if (_hierarchies.TryGetValue(scene, out var hierarchy))
                return hierarchy.TryGetIdentity(id, out result);
            result = null;
            return false;
        }

        public bool Cleanup()
        {
            for (var i = 0; i < _rawHierarchies.Count; i++)
            {
                if (!_rawHierarchies[i].Cleanup())
                    return false;
            }

            return true;
        }
    }
}