using UnityEngine;

namespace GAS
{
    /// <summary>
    /// 隕石雨 AoE 的碰撞橋接元件
    /// Unity 規則:OnParticleCollision 只送到「和 ParticleSystem 同 GameObject」的腳本
    /// AoEBehaviour 在 prefab 根,ParticleSystem 在子物件 — 直接收不到。本 relay 解決此問題。
    /// 由 AoEBehaviour.Activate(MeteorRain) 時自動 AddComponent + Initialize,設計師不需手動掛。
    /// </summary>
    [DisallowMultipleComponent]
    public class AoEMeteorCollisionRelay : MonoBehaviour
    {
        private AoEBehaviour _owner;
        private ParticleSystem _particleSystem;

        /// <summary>
        /// 由 AoEBehaviour 在 Activate 時呼叫,綁定 parent 與 source ParticleSystem
        /// </summary>
        public void Initialize(AoEBehaviour owner, ParticleSystem particleSystem)
        {
            _owner = owner;
            _particleSystem = particleSystem;
        }

        /// <summary>
        /// Unity 自動呼叫 — 每當這個 GameObject 上的 ParticleSystem 有粒子撞到 collider 就觸發一次
        /// </summary>
        private void OnParticleCollision(GameObject other)
        {
            if (_owner == null || _particleSystem == null) return;
            _owner.HandleMeteorImpacts(_particleSystem, other);
        }
    }
}
