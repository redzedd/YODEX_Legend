using UnityEngine;
using UnityEngine.UI;

public class PlayerHUD : MonoBehaviour
{
    [Header("Health Images")]
    public Image healthFillImage;
    public Image healthChipImage; // 可留空，則不顯示 chip 特效

    [Header("Stamina Images")]
    public Image staminaFillImage;
    public Image staminaChipImage; // 可留空，則不顯示 chip 特效

    [Header("Shared Bar Smoothing Params")]
    [Tooltip("小變化的最短平滑時間（秒）")]
    public float smoothTimeMin = 0.08f;
    [Tooltip("大變化的最長平滑時間（秒）")]
    public float smoothTimeMax = 0.28f;
    [Tooltip("當變化幅度超過此比例(0~1)時，視為大變化，使用較長平滑時間")]
    public float bigChangeThreshold = 0.25f;

    [Header("Chip (Only on Decrease)")]
    [Tooltip("下降（受傷/耗體）時，chip 開始追蹤前的延遲（秒）")]
    public float chipDelay = 0.12f;
    [Tooltip("下降（受傷/耗體）時，chip 追上前景的追蹤時間（越大越慢）")]
    public float chipCatchupTime = 0.4f;

    [Header("Health Regen Goal (optional)")]
    [Tooltip("持續回復時顯示的目標位置條（例如 current+pending 的位置）。")]
    public Image healthRegenGoalImage;
    [SerializeField] private float regenGoalSmoothTime = 0.15f; // 目標條的平滑時間(秒)

    // ===== 內部狀態 =====
    private BarRuntime _health;
    private BarRuntime _stamina;

    private float _regenGoalTarget01 = 0f;
    private float _regenGoalShown01 = 0f;
    private float _regenGoalVel = 0f;
    private bool _regenGoalVisible = false;

    [System.Serializable]
    private struct BarRuntime
    {
        public float target01;       // 目標(0~1)
        public float shown01;        // 畫面顯示(0~1)
        public float vel;            // SmoothDamp 速度
        public float chipDelayTimer; // 下降時的 chip 延遲計時
        public float lastTarget01;   // 上次設定的目標（用來判斷升/降）
        public int lastMax;          // 上次的 max
        public bool initialized;     // 是否已初始化
    }

    // ====== 對外 API（Direct/Regen 名稱保留以相容，行為一致：回升同步、下降延遲追） ======
    public void SetHealthTarget_Direct(int current, int max) => SetBarTargetCommon(current, max, ref _health, healthFillImage, healthChipImage);
    public void SetStaminaTarget_Direct(int current, int max) => SetBarTargetCommon(current, max, ref _stamina, staminaFillImage, staminaChipImage);

    // 統一：上升→chip 與前景同步；下降→啟動 chip 延遲追
    private void SetBarTargetCommon(int value, int max, ref BarRuntime bar, Image fill, Image chip)
    {
        float newTarget = max > 0 ? (float)value / max : 0f;

        if (!bar.initialized)
        {
            bar.shown01 = bar.target01 = bar.lastTarget01 = newTarget;
            bar.lastMax = max;
            bar.initialized = true;
            if (fill) fill.fillAmount = newTarget;
            if (chip) chip.fillAmount = newTarget;
            return;
        }

        if (newTarget < bar.target01)
        {
            // 下降：讓 chip 延遲追
            bar.chipDelayTimer = chipDelay;
        }

        bar.target01 = newTarget;
        bar.lastTarget01 = newTarget;
        bar.lastMax = max;
    }

    // ====== 再生目標條：只顯示參考線，不推動前景 ======
    public void ShowHealthRegenGoal(int currentPlusPending, int max)
    {
        if (healthRegenGoalImage == null) return;
        float v = max > 0 ? (float)currentPlusPending / max : 0f;

        if (!_regenGoalVisible)
        {
            // ⭐ 首次顯示：以「前景當前顯示值」作為起點，不要直接貼目標，避免瞬移
            float start = healthFillImage ? healthFillImage.fillAmount : v;
            _regenGoalShown01 = start;
            _regenGoalVisible = true;
            healthRegenGoalImage.gameObject.SetActive(true);
            healthRegenGoalImage.fillAmount = _regenGoalShown01;
        }

        _regenGoalTarget01 = v; // 之後每次只更新目標，Update 裡平滑追
    }

    public void HideHealthRegenGoal()
    {
        if (healthRegenGoalImage == null) return;
        _regenGoalVisible = false;
        healthRegenGoalImage.gameObject.SetActive(false);
    }

    // 直接用 0~1 的比例設定「再生目標條」的目標（用 HUD 內建 SmoothDamp 做平滑）
    public void ShowHealthRegenGoal01(float ratio01)
    {
        if (healthRegenGoalImage == null) return;
        float v = Mathf.Clamp01(ratio01);

        if (!_regenGoalVisible)
        {
            // ⭐ 首次顯示：以「前景當前顯示值」作為起點，不要直接貼目標
            float start = healthFillImage ? healthFillImage.fillAmount : v;
            _regenGoalShown01 = start;
            _regenGoalVisible = true;
            healthRegenGoalImage.gameObject.SetActive(true);
            healthRegenGoalImage.fillAmount = _regenGoalShown01;
        }

        _regenGoalTarget01 = v; // 交給 Update() 用 SmoothDamp 慢慢追
    }

    // ====== 每幀更新 ======
    private void Update()
    {
        UpdateBar(ref _health, healthFillImage, healthChipImage);
        UpdateBar(ref _stamina, staminaFillImage, staminaChipImage);

        // Regen 目標條平滑
        if (_regenGoalVisible && healthRegenGoalImage != null)
        {
            _regenGoalShown01 = Mathf.SmoothDamp(_regenGoalShown01, _regenGoalTarget01, ref _regenGoalVel, regenGoalSmoothTime);
            healthRegenGoalImage.fillAmount = _regenGoalShown01;
        }
    }

    private void UpdateBar(ref BarRuntime bar, Image fill, Image chip)
    {
        if (!bar.initialized) return;

        // 依變化幅度動態調整前景平滑時間
        float delta = Mathf.Abs(bar.target01 - bar.shown01);
        float smoothTime = Mathf.Lerp(smoothTimeMin, smoothTimeMax, Mathf.Clamp01(delta / bigChangeThreshold));

        // 前景條：SmoothDamp 追目標
        bar.shown01 = Mathf.SmoothDamp(bar.shown01, bar.target01, ref bar.vel, smoothTime);
        if (fill != null) fill.fillAmount = bar.shown01;

        // CHIP 條：
        if (chip != null)
        {
            if (chip.fillAmount > bar.shown01)
            {
                // 只有在「前景下降」時才啟動延遲追（經由 SetBarTargetCommon 設定）
                if (bar.chipDelayTimer > 0f) bar.chipDelayTimer -= Time.deltaTime;

                float c = chip.fillAmount;
                if (bar.chipDelayTimer <= 0f)
                {
                    // 用「追蹤時間」平滑靠近前景（越大越慢）
                    c = Mathf.MoveTowards(c, bar.shown01, Time.deltaTime / Mathf.Max(0.0001f, chipCatchupTime));
                    chip.fillAmount = c;
                }
            }
            else
            {
                // 上升或持平：chip 與前景同步（不做領跑）
                chip.fillAmount = bar.shown01;
            }
        }
    }
}
