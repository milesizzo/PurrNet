using System.Collections.Generic;
using PurrNet.Packing;
using PurrNet.Pooling;
using PurrNet.Transports;

namespace PurrNet.Modules
{
    public struct NetworkTransformDelta : IPackedAuto
    {
        public SceneID scene;
        public readonly ByteData packet;

        public NetworkTransformDelta(SceneID context, BitPacker packer)
        {
            scene = context;
            packet = packer.ToByteData();
        }
    }

    public struct NetworkTransformRegister : IPackedAuto
    {
        public SceneID scene;
        public readonly DisposableList<NetworkID> toRegister;

        public NetworkTransformRegister(SceneID context, DisposableList<NetworkID> list)
        {
            scene = context;
            toRegister = list;
        }
    }

    public class NetworkTransformModule : INetworkModule, IFixedUpdate
    {
        private readonly List<NetworkTransform> _networkTransforms = new();
        private readonly IScenesManager _scenes;
        private readonly PlayersBroadcaster _broadcaster;
        private readonly NetworkManager _manager;
        private readonly SceneID _scene;
        private readonly HierarchyFactory _factory;
        private bool _asServer;

        public NetworkTransformModule(NetworkManager manager, PlayersBroadcaster broadcaster,
            IScenesManager scenes, SceneID scene, HierarchyFactory factory)
        {
            _manager = manager;
            _scenes = scenes;
            _broadcaster = broadcaster;
            _scene = scene;
            _factory = factory;
        }

        public void Enable(bool asServer)
        {
            _asServer = asServer;
            _factory.onObserverAdded += OnObserverAdded;

            _broadcaster.Subscribe<NetworkTransformDelta>(OnNetworkTransformDelta);

            if (!asServer)
                _broadcaster.Subscribe<NetworkTransformRegister>(OnNetworkTransformRegister);
        }

        public void Disable(bool asServer)
        {
            _factory.onObserverAdded -= OnObserverAdded;
            _broadcaster.Unsubscribe<NetworkTransformDelta>(OnNetworkTransformDelta);

            if (!asServer)
                _broadcaster.Unsubscribe<NetworkTransformRegister>(OnNetworkTransformRegister);
        }

        readonly Dictionary<PlayerID, DisposableList<NetworkID>> _pendingToRegister = new();

        private void OnObserverAdded(PlayerID player, NetworkIdentity identity)
        {
            if (identity is not NetworkTransform)
                return;

            var id = identity.id;

            if (!id.HasValue)
                return;

            if (!_pendingToRegister.TryGetValue(player, out var list))
            {
                list = new DisposableList<NetworkID>(16);
                _pendingToRegister[player] = list;
            }

            list.Add(id.Value);
        }

        private void OnNetworkTransformRegister(PlayerID player, NetworkTransformRegister data, bool asServer)
        {
            if (asServer)
                return;

            if (data.scene != _scene)
                return;

            int count = data.toRegister.Count;
            for (var i = 0; i < count; i++)
            {
                var id = data.toRegister[i];

                if (_factory.TryGetIdentity(_scene, id, out var identity) &&
                    identity is NetworkTransform nt)
                {
                    _networkTransforms.Add(nt);
                }
            }
        }

        private void OnNetworkTransformDelta(PlayerID player, NetworkTransformDelta data, bool asServer)
        {
            if (data.scene != _scene)
                return;

            using var packet = BitPackerPool.Get(data.packet);

            packet.ResetPositionAndMode(true);

            int ntCount = _networkTransforms.Count;

            if (asServer)
            {
                for (var i = 0; i < ntCount; i++)
                {
                    var nt = _networkTransforms[i];
                    if (nt.IsControlling(player, false))
                        nt.DeltaRead(packet);
                }
            }
            else
            {
                for (var i = 0; i < ntCount; i++)
                {
                    var nt = _networkTransforms[i];
                    if (!nt.IsControlling(nt.localPlayerForced, false))
                        nt.DeltaRead(packet);
                }
            }
        }

        public PlayerID GetLocalPlayer()
        {
            if (_manager.TryGetModule<PlayersManager>(false, out var _players))
                return _players.localPlayerId.GetValueOrDefault();
            return PlayerID.Server;
        }

        private bool PrepareDeltaState(BitPacker packer, PlayerID player)
        {
            var localPlayer = GetLocalPlayer();
            int ntCount = _networkTransforms.Count;
            bool anyWritten = false;

            if (player == PlayerID.Server)
            {
                for (var i = 0; i < ntCount; i++)
                {
                    var nt = _networkTransforms[i];
                    if (nt.IsControlling(localPlayer, false))
                        anyWritten = nt.DeltaWrite(packer) || anyWritten;
                }
            }
            else
            {
                for (var i = 0; i < ntCount; i++)
                {
                    var nt = _networkTransforms[i];
                    if (!nt.IsControlling(player, false) && nt.observers.Contains(player))
                        anyWritten = nt.DeltaWrite(packer) || anyWritten;
                }
            }

            return anyWritten;
        }

        public void Register(NetworkTransform networkTransform)
        {
            if (!_asServer)
                return;

            if (!networkTransform.id.HasValue)
                return;

            _networkTransforms.Add(networkTransform);
        }

        public void Unregister(NetworkTransform networkTransform)
        {
            _networkTransforms.Remove(networkTransform);
        }

        private void FlushPendingRegistrations(PlayerID localPlayer)
        {
            foreach (var (player, toRegister) in _pendingToRegister)
            {
                if (player == localPlayer)
                    continue;

                if (toRegister.Count == 0)
                    continue;

                NetworkTransformRegister packet = new (_scene, toRegister);
                _broadcaster.Send(player, packet);
            }

            _pendingToRegister.Clear();
        }

        public void FixedUpdate()
        {
            var localPlayer = GetLocalPlayer();

            if (_asServer)
                FlushPendingRegistrations(localPlayer);

            int ntCount = _networkTransforms.Count;

            for (var i = 0; i < ntCount; i++)
            {
                var nt = _networkTransforms[i];
                if (nt.IsControlling(localPlayer, _asServer))
                    nt.GatherState();
            }

            if (!_asServer)
            {
                using var packer = BitPackerPool.Get();

                if (PrepareDeltaState(packer, PlayerID.Server) && packer.positionInBits > 0)
                    _broadcaster.SendToServer(new NetworkTransformDelta(_scene, packer));
            }
            else if (_scenes.TryGetPlayersInScene(_scene, out var players))
            {
                foreach (var player in players)
                {
                    if (player == localPlayer)
                        continue;

                    using var packer = BitPackerPool.Get();

                    if (PrepareDeltaState(packer, player) && packer.positionInBits > 0)
                        _broadcaster.Send(player, new NetworkTransformDelta(_scene, packer));
                }
            }

            for (var i = 0; i < ntCount; i++)
            {
                var nt = _networkTransforms[i];
                if (nt.IsControlling(localPlayer, _asServer))
                    nt.DeltaSave();
            }
        }
    }
}
