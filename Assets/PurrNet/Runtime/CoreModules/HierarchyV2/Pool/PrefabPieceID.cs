using System;

namespace PurrNet.Modules
{
    public enum PrefabPoolType
    {
        Scene,
        Prefab,
    }

    public readonly struct PrefabPieceID : IEquatable<PrefabPieceID>
    {
        public readonly Guid prefabId;
        public readonly int componentIndex;
        public readonly PrefabPoolType poolType;

        public PrefabPieceID(Guid prefabId, int componentIndex, PrefabPoolType poolType)
        {
            this.prefabId = prefabId;
            this.componentIndex = componentIndex;
            this.poolType = poolType;
        }

        public override string ToString()
        {
            return $"PrefabPieceID: {{ prefabId: {prefabId}, componentIndex: {componentIndex}, poolType: {poolType} }}";
        }

        public bool Equals(PrefabPieceID other)
        {
            return prefabId == other.prefabId && componentIndex == other.componentIndex && poolType == other.poolType;
        }

        public override bool Equals(object obj)
        {
            return obj is PrefabPieceID other && Equals(other);
        }

        public override int GetHashCode()
        {
            return HashCode.Combine(prefabId, componentIndex, poolType);
        }
    }
}