using UnityEngine;

namespace FiresRPGmaker.Modules.Territories
{
    /// <summary>
    /// Attaches to spawned custom spawner prefabs and notifies runtime when they die to start respawn timers.
    /// </summary>
    internal class TerritorySpawnWatcher : MonoBehaviour
    {
        private TerritoryRuntime.CustomSpawnerRuntime _runtime;
        private float _interval;

        public void SetRuntime(TerritoryRuntime.CustomSpawnerRuntime runtime, float intervalSeconds)
        {
            _runtime = runtime;
            _interval = intervalSeconds;
        }

        private void OnDestroy()
        {
            TerritoryRuntime.OnSpawnedDestroyed(_runtime, _interval, gameObject);
        }
    }
}
