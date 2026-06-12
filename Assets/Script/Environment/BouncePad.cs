using DG.Tweening;
using GAS;
using UnityEngine;

namespace Environment
{
    /// <summary>
    /// 起跳平台 — 玩家從上方落下接觸 Trigger 時被向上彈射,落地不計下落傷害。
    /// 視覺上以 DOTween 對自身 Scale 做「壓縮 → 回彈」的 Bound 動畫,呈現彈簧反作用感。
    /// 設計用途:跳跳菇、彈簧板、風力柱等「跳台類關卡道具」。
    /// 使用方式:
    ///   1. GameObject 加上一個 Collider 並勾選 Is Trigger(Box/Capsule 皆可)
    ///   2. 掛上此腳本
    ///   3. 不需 Rigidbody;碰撞偵測由 Unity Physics 系統處理
    ///   4. 玩家根物件(NewGASPlayerController 所在)有任何 Collider 即可被偵測到
    /// </summary>
    [RequireComponent(typeof(Collider))]
    public sealed class BouncePad : MonoBehaviour
    {
        [Header("彈跳設定")]
        [SerializeField, Tooltip("彈射高度(公尺) — 玩家被彈起時可達到的最高點(相對於觸發瞬間)。\n" +
                                   "依重力換算為向上速度:v = √(2gh)。設大 = 飛更高。建議 5~15。")]
        private float _bounceHeight = 8f;
        [SerializeField, Tooltip("觸發冷卻時間(秒) — 觸發後此期間再次進入 Trigger 不會被彈,避免單幀內多次觸發。\n" +
                                   "建議 0.3~0.5。設 0 表示無冷卻(每次 OnTriggerEnter 都彈)。")]
        private float _cooldown = 0.3f;

        [Header("Bound 動畫")]
        [SerializeField, Tooltip("壓縮階段的 Scale 倍率(相對於原始 Scale)。\n" +
                                   "(1.2, 0.6, 1.2) 表示 X/Z 拉長到 120%、Y 壓扁到 60%,呈現典型壓扁姿勢。\n" +
                                   "若不想要橫向拉伸,設 (1, 0.6, 1) 即可只壓扁不變寬。")]
        private Vector3 _squashScale = new Vector3(1.2f, 0.6f, 1.2f);
        [SerializeField, Tooltip("壓縮階段時間(秒)— 平台壓扁的速度。建議 0.05~0.12。")]
        private float _squashDuration = 0.08f;
        [SerializeField, Tooltip("回彈階段時間(秒)— 平台從壓扁回到原 Scale 的速度。建議 0.3~0.6。")]
        private float _reboundDuration = 0.4f;
        [SerializeField, Tooltip("回彈使用的 DOTween Ease。\n" +
                                   "推薦 OutBounce(彈跳收尾)或 OutBack(回彈帶超過再回正,Q 彈感)。")]
        private Ease _reboundEase = Ease.OutBounce;

        private Vector3 _originalScale;
        private float _cooldownTimer;
        private Sequence _activeAnim;

        private void Awake()
        {
            _originalScale = transform.localScale;
            // 確保掛在 Collider 上的勾選與此邏輯一致 — 玩家須直接通過 Trigger
            Collider col = GetComponent<Collider>();
            if (!col.isTrigger)
            {
                Debug.LogWarning($"[BouncePad] {name} 的 Collider 未勾選 Is Trigger,彈跳偵測會失敗。已在執行時自動修正。", this);
                col.isTrigger = true;
            }
        }

        private void Update()
        {
            if (_cooldownTimer > 0f)
            {
                _cooldownTimer -= Time.deltaTime;
            }
        }

        private void OnTriggerEnter(Collider other)
        {
            if (_cooldownTimer > 0f)
            {
                return;
            }
            // 只認玩家的「身體」 — CharacterController 即玩家移動用 Collider。
            // 玩家根物件上可能還有 CombatTargetFinder、HitTargetMemory 等偵測用 Collider,
            // 那些範圍通常很大,若不過濾會被誤觸發「在遠方就被彈起」。
            if (!(other is CharacterController))
            {
                return;
            }
            NewGASPlayerController player = other.GetComponent<NewGASPlayerController>();
            if (player == null)
            {
                return;
            }
            // 換算所需的上拋速度 — Controller 內建公式:v = √(2gh),沿用同一個 Gravity 設定。
            // LaunchUpward 內部已自動清掉跳躍路徑旗標 + 累積落差 + 滯空計時器,呼叫端不需重複處理。
            float upwardVelocity = player.CalculateJumpVelocityForHeight(_bounceHeight);
            player.LaunchUpward(upwardVelocity);
            _cooldownTimer = _cooldown;
            PlayBounceAnim();
        }

        private void PlayBounceAnim()
        {
            // 殺掉前一次的動畫,確保連續觸發時的視覺從原 Scale 重新開始
            _activeAnim?.Kill();
            transform.localScale = _originalScale;
            Vector3 squashed = Vector3.Scale(_originalScale, _squashScale);
            Sequence seq = DOTween.Sequence();
            seq.Append(transform.DOScale(squashed, _squashDuration).SetEase(Ease.OutQuad));
            seq.Append(transform.DOScale(_originalScale, _reboundDuration).SetEase(_reboundEase));
            seq.SetLink(gameObject);
            _activeAnim = seq;
        }

        private void OnDestroy()
        {
            _activeAnim?.Kill();
        }
    }
}
