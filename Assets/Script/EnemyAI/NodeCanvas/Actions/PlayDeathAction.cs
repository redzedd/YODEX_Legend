using Animancer;
using NodeCanvas.Framework;
using ParadoxNotion.Design;
using UnityEngine;

namespace EnemyAI.NodeCanvasTasks
{
    /// <summary>
    /// NodeCanvas Action — 進入 Dead 狀態時呼叫
    /// 流程：播 Death 動畫 → 動畫播完 → 在敵人中心生成屍體特效 → 等延遲秒數 → 銷毀 GameObject
    /// EnemyController.TriggerDeath 已負責停 A*、取消攻擊、禁碰撞
    /// </summary>
    [Category("Enemy AI/Reaction")]
    [Name("Play Death")]
    [Description("播放 Death 動畫與音效，動畫播完後在敵人中心生成屍體特效，等指定秒數後銷毀屍體")]
    public class PlayDeathAction : ActionTask<EnemyController>
    {
        [ParadoxNotion.Design.Header("動畫")]
        [Tooltip("Death 動畫淡入時間（秒）— 建議 0.1~0.3")]
        public float fadeDuration = 0.2f;

        [ParadoxNotion.Design.Header("屍體特效")]
        [Tooltip("動畫播完後在敵人中心生成的特效 Prefab — 留空則不生成特效（仍會依下方延遲銷毀屍體）")]
        public GameObject corpseVfxPrefab;

        [Tooltip("特效位置偏移（相對敵人 transform 的 local 座標）— 例如 Y 設 1 讓特效從腰部冒出，預設 0 是在腳底")]
        public Vector3 corpseVfxOffset = Vector3.zero;

        [Tooltip("特效縮放倍率（疊加在 Prefab 自帶 scale 上）— Prefab scale × 此倍率 × 敵人 lossyScale")]
        public Vector3 corpseVfxScaleMultiplier = Vector3.one;

        [Tooltip("特效自動銷毀時長（秒）— 0 = 由特效自己管生命週期（例如 ParticleSystem 設 StopAction=Destroy）")]
        public float corpseVfxLifetime = 3f;

        [ParadoxNotion.Design.Header("屍體消失")]
        [Tooltip("特效生成後等多久銷毀敵人 GameObject（秒）— 0 = 不銷毀，屍體留在場上。建議 0.5~2 讓特效有時間蓋住屍體再消失")]
        public float corpseDisappearDelay = 1f;

        [Tooltip("找不到 Death 動畫時的 fallback 時長（秒）— 動畫資產忘了設時，等多久後直接進入「生 VFX → 銷毀」流程")]
        public float noAnimationFallbackDuration = 1.5f;

        private AnimancerState _deathState;
        private float _enterTime;
        private float _vfxSpawnTime;
        private bool _vfxSpawned;
        private bool _destroyed;

        protected override string info
        {
            get
            {
                if (corpseDisappearDelay <= 0f) return "Play Death (corpse stays)";
                return $"Play Death → VFX → destroy after {corpseDisappearDelay}s";
            }
        }

        protected override void OnExecute()
        {
            _enterTime = Time.time;
            _vfxSpawned = false;
            _destroyed = false;
            _deathState = agent.PlayAnimation(EnemyAnimationType.Death, fadeDuration);
            agent.PlaySfx(agent.DeathSfx);
        }

        protected override void OnUpdate()
        {
            if (_destroyed) return;

            if (!_vfxSpawned && IsDeathAnimationFinished())
            {
                SpawnCorpseVfx();
                _vfxSpawnTime = Time.time;
                _vfxSpawned = true;
            }

            if (_vfxSpawned && corpseDisappearDelay > 0f && Time.time - _vfxSpawnTime >= corpseDisappearDelay)
            {
                _destroyed = true;
                Object.Destroy(agent.gameObject);
            }
        }

        private bool IsDeathAnimationFinished()
        {
            if (_deathState == null)
            {
                return Time.time - _enterTime >= noAnimationFallbackDuration;
            }
            return _deathState.NormalizedTime >= 1f;
        }

        private void SpawnCorpseVfx()
        {
            if (corpseVfxPrefab == null) return;
            Transform tf = agent.transform;
            Vector3 worldPos = tf.TransformPoint(corpseVfxOffset);
            GameObject vfx = Object.Instantiate(corpseVfxPrefab, worldPos, tf.rotation);
            Vector3 baseScale = Vector3.Scale(vfx.transform.localScale, corpseVfxScaleMultiplier);
            vfx.transform.localScale = Vector3.Scale(baseScale, tf.lossyScale);
            if (corpseVfxLifetime > 0f)
            {
                Object.Destroy(vfx, corpseVfxLifetime);
            }
        }
    }
}
