using JetBrains.Annotations;
using UnityEngine;

namespace PurrNet
{
    public sealed class NetworkOwnershipToggle : NetworkIdentity
    {
        [Tooltip("Components to toggle from the owner's perspective")]
        [SerializeField] private OwnershipComponentToggle[] _components;
        [Tooltip("GameObjects to toggle from the owner's perspective")]
        [SerializeField] private OwnershipGameObjectToggle[] _gameObjects;

        private bool _lastOwner;

        private void Awake()
        {
            Setup(false);
        }

        [UsedImplicitly]
        public void Setup(bool asOwner)
        {
            _lastOwner = asOwner;

            for (var i = 0; i < _components.Length; i++)
            {
                var target = _components[i].target;
                if (!target) continue;

                bool targetState = _components[i].activeAsOwner == asOwner;
                SetComponentState(target, targetState);
            }

            for (var i = 0; i < _gameObjects.Length; i++)
            {
                var target = _gameObjects[i].target;
                if (!target) continue;

                bool targetState = _gameObjects[i].activeAsOwner == asOwner;
                target.SetActive(targetState);
            }
        }

        private static void SetComponentState(Component target, bool targetState)
        {
            switch (target)
            {
                case Transform go:
                    go.gameObject.SetActive(targetState);
                    break;
                case Behaviour behaviour:
                    behaviour.enabled = targetState;
                    break;
                case Collider col:
                    col.enabled = targetState;
                    break;
                case Renderer r:
                    r.enabled = targetState;
                    break;
            }
        }

        protected override void OnOwnerChanged(PlayerID? oldOwner, PlayerID? newOwner, bool asServer)
        {
            if (isOwner != _lastOwner)
                Setup(isOwner);
        }
    }
}
