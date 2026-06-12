using DG.Tweening;
using UnityEngine;

namespace Enemy.AttackSystem
{
    /// <summary>
    /// 招架預警視覺效果。
    /// 訂閱同 GameObject 上 EnemyAttackExecutor 的攻擊事件：
    /// • 可招架攻擊 → 在「可招架窗」期間顯示「黃光」
    /// • 不可招架攻擊 → 從攻擊開始到攻擊判定生效顯示「紅光」（提示玩家必須閃避）
    /// 兩種光的 GameObject 都可獨立設定縮放動畫。
    /// </summary>
    [RequireComponent(typeof(EnemyAttackExecutor))]
    public class ParryTelegraphVFX : MonoBehaviour
    {
        [Header("元件引用")]

        [SerializeField]
        [Tooltip("要訂閱的攻擊執行器。留空會自動抓同 GameObject 上的元件")]
        private EnemyAttackExecutor _executor;

        [Header("黃光特效本體（可招架）")]

        [SerializeField]
        [Tooltip("黃光 GameObject — 攻擊招式 IsParryable 勾選時亮起。\n可招架窗（ParryFlashDuration）期間顯示，過後熄滅。留空則不顯示黃光")]
        private GameObject _yellowGlowObject;

        [Header("紅光特效本體（不可招架）")]

        [SerializeField]
        [Tooltip("紅光 GameObject — 攻擊招式 IsParryable 取消勾選時亮起。\n從攻擊開始持續到 HitWindow 開啟（武器揮出）前熄滅，提示玩家「擋不住，必須閃避」。留空則不顯示紅光")]
        private GameObject _redGlowObject;

        [Header("縮放進場 / 退場動畫")]

        [SerializeField]
        [Tooltip("勾選後使用 DOTween 縮放動畫；取消勾選則純 SetActive 開關。兩種光共用此設定")]
        private bool _useScaleAnimation = true;

        [SerializeField]
        [Tooltip("光亮起時的縮放時間（秒）— OutBack easing 給彈出感。建議 0.08~0.2")]
        private float _fadeInDuration = 0.12f;

        [SerializeField]
        [Tooltip("光熄滅時的縮放時間（秒）— InQuad easing 給乾淨收尾。建議 0.1~0.3")]
        private float _fadeOutDuration = 0.15f;

        [SerializeField]
        [Tooltip("光完全展開時的縮放值")]
        private Vector3 _maxScale = Vector3.one;

        private Tween _yellowTween;
        private Tween _redTween;

        private void Awake()
        {
            if (_executor == null)
            {
                _executor = GetComponent<EnemyAttackExecutor>();
            }
            if (_yellowGlowObject != null) _yellowGlowObject.SetActive(false);
            if (_redGlowObject != null) _redGlowObject.SetActive(false);
        }

        private void OnEnable()
        {
            if (_executor == null) return;
            _executor.OnAttackStart += HandleAttackStart;
            _executor.OnParryWindowClose += HandleParryClose;
            _executor.OnHitWindowOpen += HandleHitOpen;
            _executor.OnAttackCanceled += HandleAttackCanceled;
            _executor.OnAttackEnd += HandleAttackEnd;
        }

        private void OnDisable()
        {
            if (_executor == null) return;
            _executor.OnAttackStart -= HandleAttackStart;
            _executor.OnParryWindowClose -= HandleParryClose;
            _executor.OnHitWindowOpen -= HandleHitOpen;
            _executor.OnAttackCanceled -= HandleAttackCanceled;
            _executor.OnAttackEnd -= HandleAttackEnd;
        }

        private void HandleAttackStart(EnemyAttackExecutor sender, EnemyAttackProfile profile)
        {
            if (profile == null) return;
            if (profile.IsParryable)
            {
                ShowGlow(_yellowGlowObject, ref _yellowTween);
            }
            else
            {
                ShowGlow(_redGlowObject, ref _redTween);
            }
        }

        // 招架窗關閉 → 黃光熄滅（即使攻擊還沒結束，黃光也只在 ParryFlashDuration 期間亮）
        private void HandleParryClose(EnemyAttackExecutor sender, EnemyAttackProfile profile)
        {
            HideGlow(_yellowGlowObject, ref _yellowTween);
        }

        // 攻擊判定生效 → 紅光熄滅（紅光只在「揮出之前」提示，揮出後就沒意義了）
        private void HandleHitOpen(EnemyAttackExecutor sender, EnemyAttackProfile profile)
        {
            HideGlow(_redGlowObject, ref _redTween);
        }

        // 玩家招架成功被取消 → 兩光都收掉
        private void HandleAttackCanceled(EnemyAttackExecutor sender, EnemyAttackProfile profile)
        {
            HideGlow(_yellowGlowObject, ref _yellowTween);
            HideGlow(_redGlowObject, ref _redTween);
        }

        // 攻擊正常結束 → 保險再收一次
        private void HandleAttackEnd(EnemyAttackExecutor sender, EnemyAttackProfile profile)
        {
            HideGlow(_yellowGlowObject, ref _yellowTween);
            HideGlow(_redGlowObject, ref _redTween);
        }

        private void ShowGlow(GameObject glow, ref Tween tween)
        {
            if (glow == null) return;
            KillTween(ref tween);
            glow.SetActive(true);
            if (!_useScaleAnimation)
            {
                glow.transform.localScale = _maxScale;
                return;
            }
            glow.transform.localScale = Vector3.zero;
            tween = glow.transform
                .DOScale(_maxScale, _fadeInDuration)
                .SetEase(Ease.OutBack)
                .SetLink(gameObject);
        }

        private void HideGlow(GameObject glow, ref Tween tween)
        {
            if (glow == null || !glow.activeSelf) return;
            KillTween(ref tween);
            if (!_useScaleAnimation)
            {
                glow.SetActive(false);
                return;
            }
            GameObject captured = glow;
            tween = glow.transform
                .DOScale(Vector3.zero, _fadeOutDuration)
                .SetEase(Ease.InQuad)
                .SetLink(gameObject)
                .OnComplete(() => captured.SetActive(false));
        }

        private static void KillTween(ref Tween tween)
        {
            if (tween != null && tween.IsActive())
            {
                tween.Kill();
            }
            tween = null;
        }
    }
}
