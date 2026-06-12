using UnityEngine;

public interface IHitReceiver
{
    void OnHit(ref HitContext ctx);
}

/// <summary>
/// 攻擊類型 — 影響受擊方的反應級別
/// Normal:一般攻擊,打斷 Idle/Walk/Alert 等基本動作,但被攻擊霸體 / 蓄力霸體擋下
/// Light:輕攻擊,只觸發抖動 VFX,不打斷任何動作(對應 ZZZ 短劍輕擊 / DOT 傷害)
/// Heavy:重攻擊,能打斷攻擊霸體(但被蓄力霸體擋下);Poise 擊破時走 Knockback 而非 Stagger
/// </summary>
public enum AttackTier
{
    Normal = 0,
    Light = 1,
    Heavy = 2,
}

[System.Serializable]
public struct HitContext
{
    public float damage;
    public float poiseDamage;
    /// <summary>
    /// 擊退漸近距離(公尺)。
    /// 不再用來選擇動畫分支(Stagger / Knockback)— 分支改由 <see cref="attackTier"/> 決定。
    /// 設為 0 代表該攻擊 Poise 破後只播動畫、不產生位移(純動作類)。
    /// 衰減:v₀ = distance / τ,指數衰減下漸近總位移約等於此值,95% 於 3τ 秒內完成。
    /// </summary>
    public float knockbackForce;
    /// <summary>
    /// 攻擊類型 — Normal / Light / Heavy,參見 <see cref="AttackTier"/>
    /// </summary>
    public AttackTier attackTier;
    /// <summary>
    /// 是否為重攻擊（兼容欄位）— 由 attackTier == Heavy 判定。
    /// 既有讀取者 (NewGASPlayerController KnockbackState 分支) 仍透過此欄位讀取。
    /// 寫入端請填 attackTier，此欄位由寫入者同步填 (attackTier == Heavy)。
    /// </summary>
    public bool isHeavyAttack;
    public Vector3 hitPoint;
    public Vector3 hitNormal;
    public Vector3 attackDirection;
    public AttackProfile sourceProfile;

    // 是否跳過命中特效與音效
    public bool skipHitEffects;

    // GAS 系統已透過 GameplayEffect 扣血，HandleDamage 不需再次扣血
    public bool gasDamageApplied;

    // 頓幀設定 — 由攻擊方明確指定，0 = 不觸發頓幀
    public float hitStopDuration;
    public float hitStopTimeScale;

    // 相機抖動設定 — 由攻擊方明確指定，0 = 不觸發抖動
    public float cameraShakeIntensity;

    // 由接收方在 OnHit 中設定，true = 此次攻擊被擋住（無敵、死亡等），呼叫方應跳過後續效果
    public bool wasBlocked;

    // 由接收方在 OnHit 中設定，true = 此次攻擊在完美閃避窗口中被閃避
    public bool wasPerfectDodged;
}