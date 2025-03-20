using System;

namespace PurrNet
{
    public readonly struct SceneID : IEquatable<SceneID>
    {
        private Guid _id { get; }

        public Guid id => _id;

        public SceneID(Guid id)
        {
            _id = id;
        }

        public override string ToString()
        {
            return _id.ToString("N");
        }

        public override int GetHashCode()
        {
            return _id.GetHashCode();
        }

        public bool Equals(SceneID other)
        {
            return _id == other._id;
        }

        public override bool Equals(object obj)
        {
            return obj is SceneID other && Equals(other);
        }

        public static bool operator ==(SceneID a, SceneID b)
        {
            return a._id == b._id;
        }

        public static bool operator !=(SceneID a, SceneID b)
        {
            return a._id != b._id;
        }

        public static SceneID New()
        {
            return new SceneID(Guid.NewGuid());
        }
    }
}