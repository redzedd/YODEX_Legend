using System.Collections;
using Animancer;
using Boss;
using CameraSystem;
using DG.Tweening;
using Player.Input;
using Unity.Cinemachine;
using UnityEngine;
using UnityEngine.UI;

namespace EnemyAI.Dragon
{
    /// <summary>
    /// 飛龍 Boss 開場過場 — 掛在「開場觸發區」GameObject 上 (需有 isTrigger 的 Collider)。
    /// 玩家踏進 Trigger (或提前攻擊沉睡的飛龍) → 跑完整段演出:
    ///   鎖玩家操作 → 轉黑 → (黑幕下) 關 HUD + 切過場相機 + 飛龍變 Idle 並轉向玩家 → 等相機就位 → 亮起
    ///   → 飛龍嘶吼 (純動畫,不召隕石) → 回 Idle → 鏡頭飛回玩家 + 開 HUD + 開操作 → 飛龍進入戰鬥。
    /// 重用既有系統:CameraDirector (切相機)、SystemInputReader (鎖輸入)、DOTween (淡入淡出)。
    /// 黑幕未指定時程式自動生成一張全螢幕黑幕,設計師零設定。
    /// 還原邏輯統一走冪等的 RestoreControl,涵蓋正常完成 / 中途死亡 / 物件銷毀 / 例外四種路徑。
    /// </summary>
    public class DragonBossIntroSequence : MonoBehaviour
    {
        #region Serialized Fields

        [Header("引用")]
        [SerializeField] [Tooltip("飛龍 Boss 控制器 — 留空 Awake 會在場景自動找")]
        private DragonBossController _boss;

        [SerializeField] [Tooltip("玩家 GameObject 的 Tag — 踏進 Trigger 觸發開場。預設 \"Player\"")]
        private string _playerTag = "Player";

        [SerializeField] [Tooltip("玩家提前攻擊沉睡的飛龍 (還沒進 Trigger) 也觸發開場")]
        private bool _triggerOnDamaged = true;

        [Header("玩家傳送")]
        [SerializeField] [Tooltip("開場時 (黑幕下) 把玩家傳送到此點 — 拖一個空 GameObject,套用其位置與旋轉。留空則不傳送")]
        private Transform _teleportDestination;

        [Header("空氣牆")]
        [SerializeField] [Tooltip("競技場空氣牆 (隱形碰撞牆) 物件 — 開場時啟用封鎖場地,Boss 死亡時自動關閉。可留空。建議初始 SetActive 關閉")]
        private GameObject _arenaWall;

        [Header("Scream 鏡頭抖動")]
        [SerializeField] [Tooltip("嘶吼時觸發的 CinemachineImpulseSource — 拖場景中一個 Impulse Source (相機需有 CinemachineImpulseListener 才看得到抖動)。留空則自動找場景第一個")]
        private CinemachineImpulseSource _screamShakeSource;

        [SerializeField] [Tooltip("嘶吼抖動強度。建議 0.5~2")]
        private float _screamShakeForce = 1.2f;

        [SerializeField] [Tooltip("嘶吼期間每次抖動的間隔 (秒) — 持續整段嘶吼做出隆隆震動。建議 0.15~0.35")]
        private float _screamShakeInterval = 0.25f;

        [Header("Boss 血條")]
        [SerializeField] [Tooltip("Boss 血條 (類魂) — 開場演出結束、UI 回來時顯示。可留空")]
        private BossHealthBarUI _bossHealthBar;

        [Header("過場相機")]
        [SerializeField] [Tooltip("開場演出用的 CameraEntry — 在過場 CinemachineCamera 上掛 CameraEntry (Layer 設 Cinematic),拖進來。留空則不切相機")]
        private CameraEntry _cinematicCamera;

        [Header("HUD")]
        [SerializeField] [Tooltip("玩家戰鬥 HUD 的根 CanvasGroup — 過場期間隱藏,結束淡回。留空則不處理 HUD")]
        private CanvasGroup _gameplayHud;

        [SerializeField] [Tooltip("HUD 淡回秒數。建議 0.2~0.4")]
        private float _hudFadeInDuration = 0.3f;

        [Header("黑幕")]
        [SerializeField] [Tooltip("轉黑用的全螢幕黑色 CanvasGroup — 留空程式自動生成一張全螢幕黑幕")]
        private CanvasGroup _blackScreen;

        [SerializeField] [Tooltip("轉黑秒數。建議 0.4~0.8")]
        private float _fadeToBlackDuration = 0.6f;

        [SerializeField] [Tooltip("黑幕下等相機就位 + 飛龍切 Idle 的停留秒數。建議 0.5~1.2")]
        private float _holdInBlackDuration = 0.7f;

        [SerializeField] [Tooltip("畫面亮起秒數。建議 0.6~1")]
        private float _fadeInDuration = 0.8f;

        [Header("演出節奏 (秒)")]
        [SerializeField] [Tooltip("亮起到開始嘶吼前的停頓。建議 0.2~0.6")]
        private float _preRoarDelay = 0.4f;

        [SerializeField] [Tooltip("嘶吼/Idle 動畫淡入時間。建議 0.15~0.3")]
        private float _animFadeDuration = 0.25f;

        [SerializeField] [Tooltip("嘶吼完回 Idle 後、把鏡頭切回玩家前的停留秒數。建議 0.5~1")]
        private float _postRoarHold = 0.8f;

        [SerializeField] [Tooltip("鏡頭飛回玩家的等待秒數 (等 Cinemachine blend 完)。建議 0.8~1.5")]
        private float _cameraReturnDuration = 1f;

        [SerializeField] [Tooltip("黑幕下飛龍轉向玩家的轉身速度 (度/秒) — 設快一點讓它在黑幕停留期間轉到位,進戰鬥首幀才不會急轉。建議 360~720")]
        private float _introFaceRotationSpeed = 540f;

        [Header("Debug")]
        [SerializeField] [Tooltip("勾選後在 Console 印出每一步")]
        private bool _logSteps = false;

        #endregion

        #region Private Fields

        private bool _started;
        private bool _restored;
        private BossController _bossCore;
        private CameraTicket _cameraTicket;
        private CanvasGroup _runtimeBlack;
        private Tween _blackTween;
        private Tween _hudTween;

        #endregion

        #region Unity Lifecycle

        private void Awake()
        {
            if (_boss == null)
                _boss = FindFirstObjectByType<DragonBossController>();
        }

        private void Start()
        {
            if (_boss == null)
            {
                Debug.LogWarning($"[{name}] DragonBossIntroSequence 找不到 DragonBossController — 開場過場不會運作。請在 Inspector 拖入飛龍 Boss", this);
                return;
            }
            _bossCore = _boss.Boss;
            if (_bossCore != null)
            {
                if (_triggerOnDamaged)
                    _bossCore.OnDamaged += HandleBossDamagedBeforeIntro;
                _bossCore.OnDied += HandleBossDied;
            }
        }

        private void OnDestroy()
        {
            if (_bossCore != null)
            {
                _bossCore.OnDamaged -= HandleBossDamagedBeforeIntro;
                _bossCore.OnDied -= HandleBossDied;
            }
            // 場景卸載 / StopCoroutine 路徑下協程 finally 不會跑 — 在此確保輸入鎖與接管旗標還原
            RestoreControl();
            KillTween(ref _blackTween);
            KillTween(ref _hudTween);
            if (_blackScreen != null) _blackScreen.alpha = 0f;
            if (_runtimeBlack != null) Destroy(_runtimeBlack.gameObject);
        }

        private void OnTriggerEnter(Collider other)
        {
            if (_started) return;
            if (!IsPlayer(other)) return;
            StartIntro();
        }

        #endregion

        #region Intro Flow

        private void HandleBossDamagedBeforeIntro(float damage)
        {
            if (_started) return;
            StartIntro();
        }

        public void StartIntro()
        {
            if (_started || _boss == null) return;
            _started = true;
            // 先鎖接管,再退訂 — 消除「同幀傷害走到完整受擊邏輯」的競態窗口
            _boss.SetCinematicControl(true);
            if (_bossCore != null)
                _bossCore.OnDamaged -= HandleBossDamagedBeforeIntro;
            StartCoroutine(IntroRoutine());
        }

        private IEnumerator IntroRoutine()
        {
            Log("開場開始 — 鎖玩家操作");
            try
            {
                // 1. 鎖玩家所有操作 (移動/攻擊/鏡頭/鎖定/互動)
                SystemInputReader.Instance?.DisablePlayerInput();

                // 2. 轉黑
                Log("轉黑");
                yield return FadeBlack(1f, _fadeToBlackDuration);
                if (AbortIfDead()) yield break;

                // 3. 黑幕下:傳送玩家、封空氣牆、關 HUD、切過場相機、飛龍變 Idle 並轉向玩家
                Log("黑幕下:傳送玩家 + 封空氣牆 + 關 HUD + 切相機 + 飛龍 Idle + 轉向玩家");
                TeleportPlayer();
                EnableArenaWall();
                SetHud(0f, 0f);
                RequestCinematicCamera();
                PlayDragon(_boss.Animations != null ? _boss.Animations.Idle : null);
                FaceDragonToPlayer();
                yield return WaitRealtime(_holdInBlackDuration);
                if (AbortIfDead()) yield break;

                // 4. 亮起
                Log("亮起");
                yield return FadeBlack(0f, _fadeInDuration);
                yield return WaitRealtime(_preRoarDelay);
                if (AbortIfDead()) yield break;

                // 5. 嘶吼 (純動畫,不召隕石) → 等動畫播完 (用實時,避免外部凍時卡死)
                Log("嘶吼 + 鏡頭抖動");
                ClipTransition roarClip = _boss.Animations != null ? _boss.Animations.Scream : null;
                AnimancerState roar = _boss.PlayAnimation(roarClip, _animFadeDuration);
                float roarLength = roar != null ? roar.Length : 2f;
                StartCoroutine(ScreamShakeLoop(roarLength));
                yield return WaitRealtime(roarLength);
                if (AbortIfDead()) yield break;

                // 6. 回 Idle
                Log("回 Idle");
                PlayDragon(_boss.Animations != null ? _boss.Animations.Idle : null);
                yield return WaitRealtime(_postRoarHold);
                if (AbortIfDead()) yield break;

                // 7. 鏡頭飛回玩家 + 開 HUD + 顯示 Boss 血條
                Log("鏡頭飛回玩家 + 開 HUD + 顯示 Boss 血條");
                ReleaseCinematicCamera();
                SetHud(1f, _hudFadeInDuration);
                if (_bossHealthBar != null) _bossHealthBar.Show();
                yield return WaitRealtime(_cameraReturnDuration);
                if (AbortIfDead()) yield break;

                // 8. 開操作 (延遲到放開按鍵) + 飛龍進入戰鬥
                Log("開操作 + 飛龍進入戰鬥");
                RestoreControl();
                if (_boss != null) _boss.ChangeState(_boss.IdleState);
            }
            finally
            {
                // 例外路徑補救 — 正常完成 / AbortIfDead 已 RestoreControl,_restored 旗標使此處冪等跳過
                RestoreControl();
            }
        }

        #endregion

        #region Restore / Abort

        private bool IsBossDead => _boss != null && _boss.Boss != null && _boss.Boss.IsDead;

        /// <summary>開場中飛龍死亡 → 中止演出,還原控制 (不把死龍塞進戰鬥)</summary>
        private bool AbortIfDead()
        {
            if (!IsBossDead) return false;
            Log("飛龍在開場中死亡 — 中止演出,還原控制");
            RestoreControl();
            SnapToGameplay();
            return true;
        }

        /// <summary>
        /// 冪等還原:釋放過場相機、延遲開玩家輸入 (等放開按鍵防殘留)、解除 Boss 接管。
        /// 正常完成 / 死亡中止 / 物件銷毀 / 例外 四路徑共用,_restored 確保只實際執行一次。
        /// </summary>
        private void RestoreControl()
        {
            if (_restored) return;
            _restored = true;
            ReleaseCinematicCamera();
            SystemInputReader input = SystemInputReader.Instance;
            if (input != null && !input.IsPlayerInputEnabled)
                input.EnablePlayerInputDeferred();
            if (_boss != null) _boss.SetCinematicControl(false);
        }

        /// <summary>中止時把畫面立即拉回正常 (黑幕清掉、HUD 顯示),不顧平滑</summary>
        private void SnapToGameplay()
        {
            KillTween(ref _blackTween);
            if (_blackScreen != null) { _blackScreen.alpha = 0f; _blackScreen.blocksRaycasts = false; }
            if (_runtimeBlack != null) { _runtimeBlack.alpha = 0f; _runtimeBlack.blocksRaycasts = false; }
            KillTween(ref _hudTween);
            if (_gameplayHud != null)
            {
                _gameplayHud.alpha = 1f;
                _gameplayHud.interactable = true;
                _gameplayHud.blocksRaycasts = true;
            }
        }

        #endregion

        #region Helpers

        private bool IsPlayer(Collider other)
        {
            if (other == null) return false;
            if (other.CompareTag(_playerTag)) return true;
            Transform root = other.transform.root;
            return root != null && root.CompareTag(_playerTag);
        }

        private void PlayDragon(ClipTransition clip)
        {
            if (clip == null) return;
            _boss.PlayAnimation(clip, _animFadeDuration);
        }

        private void FaceDragonToPlayer()
        {
            if (_boss == null || _boss.Locomotion == null || _boss.Player == null) return;
            Vector3 toPlayer = _boss.Player.position - _boss.transform.position;
            _boss.Locomotion.SetFacing(toPlayer, _introFaceRotationSpeed);
        }

        // 把玩家傳送到指定點 — 關 CharacterController 才能直接寫座標 (沿用 InSceneTeleportHandler 手法)
        private void TeleportPlayer()
        {
            if (_teleportDestination == null) return;
            GameObject playerGo = GameObject.FindWithTag(_playerTag);
            if (playerGo == null) return;
            CharacterController cc = playerGo.GetComponent<CharacterController>();
            if (cc != null)
            {
                cc.enabled = false;
                playerGo.transform.SetPositionAndRotation(_teleportDestination.position, _teleportDestination.rotation);
                cc.enabled = true;
            }
            else
            {
                playerGo.transform.SetPositionAndRotation(_teleportDestination.position, _teleportDestination.rotation);
            }
            Physics.SyncTransforms();
        }

        private void EnableArenaWall()
        {
            if (_arenaWall != null) _arenaWall.SetActive(true);
        }

        // Boss 死亡 → 關掉空氣牆,放玩家離開競技場
        private void HandleBossDied()
        {
            if (_arenaWall != null) _arenaWall.SetActive(false);
        }

        // 嘶吼期間持續每隔 interval 生成一次 Cinemachine 衝擊,做出隆隆震動 (沿用 CameraShakeCue 手法)
        private IEnumerator ScreamShakeLoop(float duration)
        {
            CinemachineImpulseSource source = ResolveShakeSource();
            if (source == null) yield break;
            float elapsed = 0f;
            float interval = Mathf.Max(0.05f, _screamShakeInterval);
            while (elapsed < duration)
            {
                Vector3 velocity = Vector3.down * _screamShakeForce;
                velocity += Random.insideUnitSphere * (_screamShakeForce * 0.3f);
                source.GenerateImpulse(velocity);
                yield return new WaitForSecondsRealtime(interval);
                elapsed += interval;
            }
        }

        private CinemachineImpulseSource ResolveShakeSource()
        {
            if (_screamShakeSource == null)
                _screamShakeSource = FindFirstObjectByType<CinemachineImpulseSource>();
            return _screamShakeSource;
        }

        private void RequestCinematicCamera()
        {
            if (_cinematicCamera == null) return;
            CameraDirector director = CameraDirector.Instance;
            if (director == null) return;
            _cameraTicket = director.Request(_cinematicCamera);
        }

        private void ReleaseCinematicCamera()
        {
            if (_cameraTicket == null) return;
            CameraTicket t = _cameraTicket;
            _cameraTicket = null;
            t.Release();
        }

        private IEnumerator FadeBlack(float targetAlpha, float duration)
        {
            CanvasGroup cg = EnsureBlack();
            cg.blocksRaycasts = targetAlpha > 0.01f;
            KillTween(ref _blackTween);
            if (duration <= 0f)
            {
                cg.alpha = targetAlpha;
                yield break;
            }
            _blackTween = cg.DOFade(targetAlpha, duration)
                .SetEase(Ease.InOutSine)
                .SetUpdate(true)
                .SetLink(cg.gameObject);
            yield return _blackTween.WaitForCompletion();
        }

        private void SetHud(float targetAlpha, float duration)
        {
            if (_gameplayHud == null) return;
            KillTween(ref _hudTween);
            _gameplayHud.interactable = targetAlpha > 0.99f;
            _gameplayHud.blocksRaycasts = targetAlpha > 0.99f;
            if (duration <= 0f)
            {
                _gameplayHud.alpha = targetAlpha;
                return;
            }
            _hudTween = _gameplayHud.DOFade(targetAlpha, duration)
                .SetEase(Ease.InOutSine)
                .SetUpdate(true)
                .SetLink(_gameplayHud.gameObject);
        }

        private CanvasGroup EnsureBlack()
        {
            if (_blackScreen != null) return _blackScreen;
            if (_runtimeBlack != null) return _runtimeBlack;

            GameObject canvasGo = new GameObject("[BossIntroBlackScreen]");
            Canvas canvas = canvasGo.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 32000;
            CanvasGroup cg = canvasGo.AddComponent<CanvasGroup>();
            cg.alpha = 0f;
            cg.interactable = false;
            cg.blocksRaycasts = false;

            GameObject imgGo = new GameObject("Black", typeof(RectTransform));
            imgGo.transform.SetParent(canvasGo.transform, false);
            Image img = imgGo.AddComponent<Image>();
            img.color = Color.black;
            RectTransform rt = img.rectTransform;
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;

            _runtimeBlack = cg;
            return cg;
        }

        private static IEnumerator WaitRealtime(float seconds)
        {
            if (seconds > 0f) yield return new WaitForSecondsRealtime(seconds);
        }

        private static void KillTween(ref Tween tween)
        {
            if (tween == null) return;
            if (tween.IsActive()) tween.Kill();
            tween = null;
        }

        private void Log(string step)
        {
            if (_logSteps) Debug.Log($"[飛龍開場] {step}", this);
        }

        #endregion
    }
}
