namespace PurrNet.Modules
{
    public class ColliderRollbackFactory : SceneScopedFactory<RollbackModule>, IPostFixedUpdate
    {
        readonly TickManager _tick;

        public ColliderRollbackFactory(TickManager tick, IScenesManager scenes)
            : base(scenes)
        {
            _tick = tick;
        }

        protected override RollbackModule CreateModule(SceneID sceneID, bool asServer)
        {
            if (scenes.TryGetScene(sceneID, out var scene))
                return new RollbackModule(_tick, scene);
            return new RollbackModule(_tick, default);
        }

        public void PostFixedUpdate()
        {
            foreach (var module in modules)
                module.OnPostTick();
        }
    }
}