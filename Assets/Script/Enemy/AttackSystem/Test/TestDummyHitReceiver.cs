using UnityEngine;

namespace Enemy.AttackSystem.Test
{
    /// <summary>
    /// 測試假人的受擊接收器。
    /// 實作 IHitReceiver，被打到時在 Console 印出完整 HitContext，
    /// 方便驗證 DefensiveAssistResponder 的傷害計算是否正確傳遞。
    /// </summary>
    public class TestDummyHitReceiver : MonoBehaviour, IHitReceiver
    {
        [SerializeField]
        [Tooltip("勾選後印出每次受擊的 HitContext")]
        private bool _logHits = true;

        [SerializeField]
        [Tooltip("勾選後在受擊瞬間讓假人短暫變色（紅閃 0.15 秒），純視覺回饋")]
        private bool _flashOnHit = true;

        [SerializeField]
        [Tooltip("受擊時要變色的 Renderer。留空會自動抓取 MeshRenderer / SkinnedMeshRenderer")]
        private Renderer _hitFlashRenderer;

        private Color _originalColor;
        private MaterialPropertyBlock _propertyBlock;
        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
        private float _flashTimer;

        private void Awake()
        {
            if (_hitFlashRenderer == null)
            {
                _hitFlashRenderer = GetComponentInChildren<Renderer>();
            }
            if (_hitFlashRenderer != null)
            {
                _propertyBlock = new MaterialPropertyBlock();
                _originalColor = _hitFlashRenderer.sharedMaterial != null
                    ? _hitFlashRenderer.sharedMaterial.color
                    : Color.white;
            }
        }

        private void Update()
        {
            if (_flashTimer <= 0f || _hitFlashRenderer == null)
            {
                return;
            }
            _flashTimer -= Time.deltaTime;
            if (_flashTimer <= 0f)
            {
                _propertyBlock.SetColor(BaseColorId, _originalColor);
                _hitFlashRenderer.SetPropertyBlock(_propertyBlock);
            }
        }

        public void OnHit(ref HitContext ctx)
        {
            if (_logHits)
            {
                Debug.Log(
                    $"[測試假人受擊] 傷害={ctx.damage} | 失衡={ctx.poiseDamage} | 擊退={ctx.knockbackForce} | " +
                    $"重攻擊={ctx.isHeavyAttack} | 方向={ctx.attackDirection}",
                    this);
            }
            if (_flashOnHit && _hitFlashRenderer != null)
            {
                _propertyBlock.SetColor(BaseColorId, Color.red);
                _hitFlashRenderer.SetPropertyBlock(_propertyBlock);
                _flashTimer = 0.15f;
            }
        }
    }
}
