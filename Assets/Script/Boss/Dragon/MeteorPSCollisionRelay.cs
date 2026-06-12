using UnityEngine;

namespace Boss.Dragon
{
    /// <summary>
    /// ParticleSystem 碰撞橋接元件
    /// Unity 規則:OnParticleCollision 只送到「跟 ParticleSystem 同 GameObject」的腳本
    /// MeteorPSCollisionHandler 在 prefab 根,所以子 PS 需要這個 relay 轉發 (含 PS 自身引用,讓 handler 能呼叫 GetCollisionEvents 取碰撞點)
    /// 由 MeteorPSCollisionHandler.Initialize 自動 AddComponent,設計師不需要手動掛
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(ParticleSystem))]
    public class MeteorPSCollisionRelay : MonoBehaviour
    {
        private MeteorPSCollisionHandler _handler;
        private ParticleSystem _particleSystem;

        private void Awake()
        {
            _particleSystem = GetComponent<ParticleSystem>();
        }

        public void Initialize(MeteorPSCollisionHandler handler)
        {
            _handler = handler;
            if (_particleSystem == null) _particleSystem = GetComponent<ParticleSystem>();
        }

        private void OnParticleCollision(GameObject other)
        {
            if (_handler != null && _particleSystem != null)
            {
                _handler.HandleParticleHit(_particleSystem, other);
            }
        }
    }
}
