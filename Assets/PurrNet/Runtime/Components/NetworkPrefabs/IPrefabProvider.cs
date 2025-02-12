using System;
using System.Collections.Generic;
using UnityEngine;

namespace PurrNet
{
    public struct PrefabData
    {
        public Guid prefabId;
        public GameObject prefab;
        public bool pooled;
        public int warmupCount;
    }

    public interface IPrefabProvider
    {
        IEnumerable<PrefabData> Prefabs { get; }

        bool TryGetPrefab(Guid prefabId, out GameObject prefab);

        bool TryGetPrefabData(Guid prefabId, out PrefabData prefabData);

        bool TryGetPrefabData(GameObject prefab, out PrefabData prefabData);

        bool TryGetPrefab(Guid prefabId, int offset, out GameObject prefab);
    }

    public abstract class PrefabProviderScriptable : ScriptableObject, IPrefabProvider
    {
        public abstract IEnumerable<PrefabData> Prefabs { get; }

        public abstract bool TryGetPrefab(Guid prefabId, out GameObject prefab);

        public abstract bool TryGetPrefabData(Guid prefabId, out PrefabData prefabData);

        public abstract bool TryGetPrefabData(GameObject prefab, out PrefabData prefabData);

        public abstract bool TryGetPrefab(Guid prefabId, int offset, out GameObject prefab);
    }
}
