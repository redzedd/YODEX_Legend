using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Pool;

namespace GAS
{
    /// <summary>
    /// 投射物物件池管理器 - 使用 Unity ObjectPool 管理投射物的生成和回收
    /// 每個投射物 Prefab 對應一個獨立的池
    /// </summary>
    public class ProjectilePoolManager : MonoBehaviour
    {
        public static ProjectilePoolManager Instance { get; private set; }

        [Header("Pool Settings")]
        [Tooltip("每個 Prefab 池的預設容量")]
        [SerializeField] private int _defaultCapacity = 10;

        [Tooltip("每個 Prefab 池的最大容量")]
        [SerializeField] private int _maxSize = 50;

        /// <summary>以 Prefab InstanceID 為 Key 的池字典</summary>
        private readonly Dictionary<int, ObjectPool<ProjectileBehaviour>> _pools = new();

        /// <summary>Prefab 快取（InstanceID → Prefab）</summary>
        private readonly Dictionary<int, GameObject> _prefabCache = new();

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }
            Instance = this;
        }

        /// <summary>
        /// 從池中取得投射物
        /// </summary>
        public ProjectileBehaviour Get(GameObject prefab, Vector3 position, Quaternion rotation)
        {
            if (prefab == null)
            {
                Debug.LogError("[ProjectilePoolManager] Prefab is null!");
                return null;
            }

            int prefabId = prefab.GetInstanceID();
            ObjectPool<ProjectileBehaviour> pool = GetOrCreatePool(prefabId, prefab);
            ProjectileBehaviour projectile = pool.Get();
            Transform tf = projectile.transform;
            tf.position = position;
            tf.rotation = rotation;
            return projectile;
        }

        /// <summary>
        /// 回收投射物到池中
        /// </summary>
        public void Return(ProjectileBehaviour projectile)
        {
            if (projectile == null) return;
            projectile.ReturnToPool();
        }

        /// <summary>
        /// 取得或建立指定 Prefab 的池
        /// </summary>
        private ObjectPool<ProjectileBehaviour> GetOrCreatePool(int prefabId, GameObject prefab)
        {
            if (_pools.TryGetValue(prefabId, out ObjectPool<ProjectileBehaviour> existingPool))
            {
                return existingPool;
            }

            _prefabCache[prefabId] = prefab;

            var newPool = new ObjectPool<ProjectileBehaviour>(
                createFunc: () => CreateProjectile(prefabId),
                actionOnGet: proj => proj.OnGetFromPool(),
                actionOnRelease: proj => proj.OnReturnToPool(),
                actionOnDestroy: proj => Destroy(proj.gameObject),
                collectionCheck: false,
                defaultCapacity: _defaultCapacity,
                maxSize: _maxSize
            );

            _pools[prefabId] = newPool;
            return newPool;
        }

        /// <summary>
        /// 建立新投射物實例
        /// </summary>
        private ProjectileBehaviour CreateProjectile(int prefabId)
        {
            if (!_prefabCache.TryGetValue(prefabId, out GameObject prefab))
            {
                Debug.LogError("[ProjectilePoolManager] Prefab not found in cache!");
                return null;
            }

            GameObject instance = Instantiate(prefab, transform);
            ProjectileBehaviour behaviour = instance.GetComponent<ProjectileBehaviour>();

            if (behaviour == null)
            {
                behaviour = instance.AddComponent<ProjectileBehaviour>();
            }

            // 設定池引用，讓投射物知道要回收到哪裡
            behaviour.Pool = _pools[prefabId];
            instance.SetActive(false);
            return behaviour;
        }

        /// <summary>
        /// 清除所有池
        /// </summary>
        public void ClearAllPools()
        {
            foreach (var pool in _pools.Values)
            {
                pool.Clear();
            }
            _pools.Clear();
            _prefabCache.Clear();
        }

        private void OnDestroy()
        {
            ClearAllPools();
        }
    }
}
