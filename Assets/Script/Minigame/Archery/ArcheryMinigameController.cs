using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using CameraSystem;

namespace Minigame.Archery
{
    /// <summary>
    /// 射箭小遊戲 — 主控制器
    /// 狀態流程：
    ///   Idle → Preparing(3 秒倒數) → Phase1Arrow → Phase2AOE → Win (終局，凍結時間 + VFX + 寶箱)
    ///   ↓                                       ↓
    ///   Lose ← (時間到 / 中途離開 Trigger) ← AwaitingRestart (停留 10 秒或重新進入觸發)
    /// 由 MinigameTrigger 透過 OnPlayerEnteredZone / OnPlayerExitedZone 驅動
    /// </summary>
    public class ArcheryMinigameController : MonoBehaviour
    {
        private enum Phase { Idle, Preparing, Phase1Arrow, Phase2AOE, Win, Lose, AwaitingRestart }

        [Header("UI")]
        [Tooltip("倒數計時 UI（拖場景中 MinigameCountdownUI 實例）")]
        [SerializeField] private MinigameCountdownUI _countdownUI;

        [Tooltip("Phase 1（弓箭階段）顯示文字")]
        [SerializeField] private string _phase1Message = "用弓箭射落所有靶心！";

        [Tooltip("Phase 2（AOE 階段）顯示文字")]
        [SerializeField] private string _phase2Message = "使用 AOE 攻擊摧毀巨大靶心！";

        [Tooltip("勝利顯示文字")]
        [SerializeField] private string _winMessage = "挑戰成功！";

        [Tooltip("超時失敗文字")]
        [SerializeField] private string _loseMessage = "時間到，挑戰失敗";

        [Tooltip("玩家中途離開觸發區時的中斷文字")]
        [SerializeField] private string _abortMessage = "離開挑戰區，挑戰中斷";

        [Header("準備倒數（遊戲開始前）")]
        [Tooltip("Phase1 開始前的準備倒數秒數（建議 3）")]
        [SerializeField] private int _prepareSeconds = 3;

        [Tooltip("準備倒數階段的提示文字")]
        [SerializeField] private string _readyMessage = "準備...";

        [Tooltip("倒數完成時的提示文字")]
        [SerializeField] private string _goMessage = "開始！";

        [Tooltip("「3、2、1」每一聲倒數音效")]
        [SerializeField] private AudioClip _countdownTickSFX;

        [Tooltip("「開始！」音效")]
        [SerializeField] private AudioClip _goSFX;

        [Tooltip("準備階段顯示的場景提示物件（例如指向靶心的箭頭、光柱、粒子）。建議在場景中預先擺好並 SetActive(false)，Controller 會在準備倒數時自動開啟、進入 Phase1 時關閉")]
        [SerializeField] private GameObject _preparationHintObject;

        [Header("總計時")]
        [Tooltip("整場小遊戲總秒數（含 Phase1 + Phase2，歸零未完成 → 失敗）")]
        [SerializeField] private float _totalTimeLimit = 45f;

        [Header("Phase 1 — 移動靶心")]
        [Tooltip("移動靶心 Prefab（掛 MinigameMovingTarget）")]
        [SerializeField] private MinigameMovingTarget _movingTargetPrefab;

        [Tooltip("移動靶心生成點清單（建議多個分散位置）")]
        [SerializeField] private List<Transform> _movingTargetSpawnPoints = new();

        [Tooltip("一次同時存在的移動靶心數量上限（建議 3~5）")]
        [SerializeField] private int _simultaneousTargets = 3;

        [Tooltip("總共要擊落幾隻移動靶心才進入 Phase 2（建議 5~10）")]
        [SerializeField] private int _totalTargetsToKill = 6;

        [Tooltip("一隻被擊落後，補生下一隻的延遲秒數")]
        [SerializeField] private float _respawnDelay = 0.6f;

        [Header("Phase 2 — 巨大靶心")]
        [Tooltip("巨大靶心 Prefab（掛 MinigameAOETarget）")]
        [SerializeField] private MinigameAOETarget _giantTargetPrefab;

        [Tooltip("巨大靶心生成位置")]
        [SerializeField] private Transform _giantTargetSpawnPoint;

        [Header("勝利演出")]
        [Tooltip("勝利時播放的特效 Prefab（會自動將粒子設為 useUnscaledTime，凍結時間時仍會播完）")]
        [SerializeField] private GameObject _victoryVFXPrefab;

        [Tooltip("勝利特效生成位置（留空則用本物件位置）")]
        [SerializeField] private Transform _victoryVFXSpawnPoint;

        [Tooltip("勝利時生成的獎勵寶箱 Prefab（建議掛 LockedChestHandler 或 ChestHandler）")]
        [SerializeField] private GameObject _rewardChestPrefab;

        [Tooltip("寶箱生成位置（留空則用 _victoryVFXSpawnPoint 或本物件位置）")]
        [SerializeField] private Transform _rewardChestSpawnPoint;

        [Tooltip("勝利音效")]
        [SerializeField] private AudioClip _winSFX;

        [Tooltip("勝利時是否凍結時間（Time.timeScale = 0）")]
        [SerializeField] private bool _pauseTimeOnWin = true;

        [Tooltip("勝利凍結時間持續秒數（使用 unscaled time）")]
        [SerializeField] private float _winPauseDuration = 3f;

        [Header("勝利 Cinemachine — 寶箱聚焦相機")]
        [Tooltip("聚焦寶箱的 CameraEntry（拖一台 Cinemachine 相機 GameObject，上面要先掛 CameraEntry 元件）。留空 = 不切換相機")]
        [SerializeField] private CameraEntry _chestCameraEntry;

        [Tooltip("Request 相機後等待 blend 完成的秒數，建議 1~2 秒")]
        [SerializeField] private float _chestCameraFocusDuration = 1.5f;

        [Tooltip("Release 相機後等待 blend 回主視角的秒數，建議 0.5~1 秒")]
        [SerializeField] private float _chestCameraReturnDuration = 0.5f;

        [Header("失敗後重試")]
        [Tooltip("失敗後在 Trigger 內停留此秒數會自動重新開始")]
        [SerializeField] private float _autoRestartInsideDelay = 10f;

        [Tooltip("AwaitingRestart 階段、玩家在區內時的提示文字（{0} 會被替換為剩餘秒數）")]
        [SerializeField] private string _restartHintMessage = "{0} 秒後重新挑戰";

        [Tooltip("AwaitingRestart 階段、玩家在區外時的提示文字")]
        [SerializeField] private string _outsideRestartHintMessage = "回到挑戰區可重新開始";

        [Header("結束行為")]
        [Tooltip("勝利/失敗結果文字停留秒數，才進 AwaitingRestart 或關閉 UI")]
        [SerializeField] private float _endMessageDuration = 2.5f;

        [Header("音源")]
        [SerializeField] private AudioSource _audioSource;

        private Phase _phase = Phase.Idle;
        private float _timeRemaining;
        private int _killedCount;
        private bool _playerInTrigger;
        private readonly List<MinigameMovingTarget> _activeMovingTargets = new();
        private MinigameAOETarget _activeGiantTarget;
        private Coroutine _activeRoutine;
        private CameraTicket _chestCameraTicket;

        public bool IsRunning => _phase == Phase.Preparing
            || _phase == Phase.Phase1Arrow
            || _phase == Phase.Phase2AOE;

        public bool IsTerminalWin => _phase == Phase.Win;

        private void Awake()
        {
            if (_audioSource == null)
                _audioSource = GetComponent<AudioSource>();
        }

        private void OnDestroy()
        {
            // 防止物件銷毀時 timeScale 卡在 0
            if (_pauseTimeOnWin && Time.timeScale == 0f)
                Time.timeScale = 1f;
            ReleaseChestCamera();
            SetPreparationHintActive(false);
        }

        #region 公開介面 — 由 MinigameTrigger 呼叫

        public void OnPlayerEnteredZone()
        {
            _playerInTrigger = true;
            // 閒置或等待重啟階段時，進場即重啟整場流程
            if (_phase == Phase.Idle || _phase == Phase.AwaitingRestart)
                StartGame();
        }

        public void OnPlayerExitedZone()
        {
            _playerInTrigger = false;
            // 中途離開 → 立即中斷遊戲，進入 Lose 流程
            if (IsRunning)
                AbortToLose();
        }

        public void StartGame()
        {
            if (IsTerminalWin) return;
            StopActiveRoutine();
            CleanupAllTargets();
            Time.timeScale = 1f;
            _activeRoutine = StartCoroutine(FullRoutine());
        }

        #endregion

        #region 主流程

        private IEnumerator FullRoutine()
        {
            yield return RunPreparing();
            // RunPreparing 期間若玩家離開，AbortToLose 已切換 routine，此處不會繼續
            yield return RunPhase1Setup();
            yield return RunMainTimerLoop();
            // 出迴圈時 _phase 必為 Win 或 Lose
            if (_phase == Phase.Win)
                yield return RunWinSequence();
            else
                yield return RunLoseSequence(aborted: false);
        }

        private IEnumerator RunPreparing()
        {
            _phase = Phase.Preparing;
            SetPreparationHintActive(true);
            if (_countdownUI != null)
                _countdownUI.Show(_prepareSeconds, _readyMessage);
            for (int i = _prepareSeconds; i > 0; i--)
            {
                if (_countdownUI != null)
                {
                    _countdownUI.UpdateStageMessage(_readyMessage);
                    _countdownUI.UpdateTime(i);
                }
                if (_countdownTickSFX != null && _audioSource != null)
                    _audioSource.PlayOneShot(_countdownTickSFX);
                yield return new WaitForSeconds(1f);
            }
            if (_countdownUI != null) _countdownUI.UpdateStageMessage(_goMessage);
            if (_goSFX != null && _audioSource != null)
                _audioSource.PlayOneShot(_goSFX);
            yield return new WaitForSeconds(0.6f);
        }

        private IEnumerator RunPhase1Setup()
        {
            _phase = Phase.Phase1Arrow;
            SetPreparationHintActive(false);
            _killedCount = 0;
            _timeRemaining = _totalTimeLimit;
            if (_countdownUI != null) _countdownUI.UpdateStageMessage(_phase1Message);
            int initialBatch = Mathf.Min(_simultaneousTargets, _totalTargetsToKill);
            for (int i = 0; i < initialBatch; i++)
                SpawnMovingTarget();
            yield break;
        }

        private IEnumerator RunMainTimerLoop()
        {
            while (_phase == Phase.Phase1Arrow || _phase == Phase.Phase2AOE)
            {
                _timeRemaining -= Time.deltaTime;
                if (_countdownUI != null) _countdownUI.UpdateTime(_timeRemaining);
                if (_timeRemaining <= 0f) break;
                yield return null;
            }
        }

        #endregion

        #region 勝利演出

        private IEnumerator RunWinSequence()
        {
            _phase = Phase.Win;
            CleanupAllTargets();
            if (_countdownUI != null) _countdownUI.UpdateStageMessage(_winMessage);
            // 1. 播勝利特效（粒子轉 unscaled time 才能在凍結時間下播放）
            if (_victoryVFXPrefab != null)
            {
                Vector3 vfxPos = _victoryVFXSpawnPoint != null
                    ? _victoryVFXSpawnPoint.position : transform.position;
                Quaternion vfxRot = _victoryVFXSpawnPoint != null
                    ? _victoryVFXSpawnPoint.rotation : Quaternion.identity;
                GameObject vfx = Instantiate(_victoryVFXPrefab, vfxPos, vfxRot);
                SetParticlesUnscaled(vfx);
            }
            // 2. 生成獎勵寶箱
            if (_rewardChestPrefab != null)
            {
                Transform chestSpawn = _rewardChestSpawnPoint != null
                    ? _rewardChestSpawnPoint
                    : (_victoryVFXSpawnPoint != null ? _victoryVFXSpawnPoint : transform);
                Instantiate(_rewardChestPrefab, chestSpawn.position, chestSpawn.rotation);
            }
            // 3. 播勝利音效
            if (_winSFX != null && _audioSource != null)
                _audioSource.PlayOneShot(_winSFX);
            // 4. 請求 Cinemachine 寶箱相機，等待 blend 完成（用 scaled time，這時 timeScale=1）
            RequestChestCamera();
            yield return new WaitForSecondsRealtime(_chestCameraFocusDuration);
            // 5. 凍結時間 + 戲劇性停留（用 unscaled time）
            if (_pauseTimeOnWin) Time.timeScale = 0f;
            yield return new WaitForSecondsRealtime(_winPauseDuration);
            if (_pauseTimeOnWin) Time.timeScale = 1f;
            // 6. 釋放相機，等待 blend 回主視角
            ReleaseChestCamera();
            yield return new WaitForSecondsRealtime(_chestCameraReturnDuration);
            // 7. 結算文字停留後關 UI
            yield return new WaitForSecondsRealtime(_endMessageDuration);
            if (_countdownUI != null) _countdownUI.Hide();
            _activeRoutine = null;
        }

        private void RequestChestCamera()
        {
            if (_chestCameraEntry == null) return;
            CameraDirector director = CameraDirector.Instance;
            if (director == null) return;
            _chestCameraTicket = director.Request(_chestCameraEntry);
        }

        private void ReleaseChestCamera()
        {
            if (_chestCameraTicket == null) return;
            CameraTicket t = _chestCameraTicket;
            _chestCameraTicket = null;
            t.Release();
        }

        private void SetPreparationHintActive(bool active)
        {
            if (_preparationHintObject != null)
                _preparationHintObject.SetActive(active);
        }

        private void SetParticlesUnscaled(GameObject root)
        {
            ParticleSystem[] particles = root.GetComponentsInChildren<ParticleSystem>(true);
            for (int i = 0; i < particles.Length; i++)
            {
                ParticleSystem.MainModule main = particles[i].main;
                main.useUnscaledTime = true;
            }
        }

        #endregion

        #region 失敗 / 中斷 / 重試

        private void AbortToLose()
        {
            StopActiveRoutine();
            CleanupAllTargets();
            SetPreparationHintActive(false);
            ReleaseChestCamera();
            Time.timeScale = 1f;
            _activeRoutine = StartCoroutine(RunLoseSequence(aborted: true));
        }

        private IEnumerator RunLoseSequence(bool aborted)
        {
            _phase = Phase.Lose;
            CleanupAllTargets();
            string msg = aborted ? _abortMessage : _loseMessage;
            if (_countdownUI != null)
            {
                if (!_countdownUI.gameObject.activeSelf)
                    _countdownUI.Show(0f, msg);
                else
                    _countdownUI.UpdateStageMessage(msg);
                _countdownUI.UpdateTime(0f);
            }
            yield return new WaitForSeconds(_endMessageDuration);
            yield return RunAwaitingRestartLoop();
        }

        private IEnumerator RunAwaitingRestartLoop()
        {
            _phase = Phase.AwaitingRestart;
            float timer = _autoRestartInsideDelay;
            while (_phase == Phase.AwaitingRestart)
            {
                if (_playerInTrigger)
                {
                    timer -= Time.deltaTime;
                    if (_countdownUI != null)
                    {
                        _countdownUI.UpdateStageMessage(
                            string.Format(_restartHintMessage, Mathf.CeilToInt(Mathf.Max(0f, timer))));
                        _countdownUI.UpdateTime(timer);
                    }
                    if (timer <= 0f)
                    {
                        StartGame();
                        yield break;
                    }
                }
                else
                {
                    // 玩家離開 → 重置倒數（下次再入內或重進觸發都會重啟）
                    timer = _autoRestartInsideDelay;
                    if (_countdownUI != null)
                    {
                        _countdownUI.UpdateStageMessage(_outsideRestartHintMessage);
                        _countdownUI.UpdateTime(_autoRestartInsideDelay);
                    }
                }
                yield return null;
            }
        }

        #endregion

        #region 靶心生成

        private void SpawnMovingTarget()
        {
            if (_movingTargetPrefab == null || _movingTargetSpawnPoints.Count == 0) return;
            int totalSpawned = _killedCount + _activeMovingTargets.Count;
            if (totalSpawned >= _totalTargetsToKill) return;
            Transform spawn = _movingTargetSpawnPoints[Random.Range(0, _movingTargetSpawnPoints.Count)];
            MinigameMovingTarget target = Instantiate(_movingTargetPrefab, spawn.position, spawn.rotation);
            target.OnKilled += OnMovingTargetKilled;
            _activeMovingTargets.Add(target);
        }

        private void OnMovingTargetKilled(MinigameMovingTarget target)
        {
            target.OnKilled -= OnMovingTargetKilled;
            _activeMovingTargets.Remove(target);
            _killedCount++;
            if (_killedCount >= _totalTargetsToKill)
                StartCoroutine(EnterPhase2());
            else
                StartCoroutine(DelayedRespawn());
        }

        private IEnumerator DelayedRespawn()
        {
            yield return new WaitForSeconds(_respawnDelay);
            if (_phase == Phase.Phase1Arrow) SpawnMovingTarget();
        }

        private IEnumerator EnterPhase2()
        {
            _phase = Phase.Phase2AOE;
            CleanupMovingTargets();
            if (_countdownUI != null) _countdownUI.UpdateStageMessage(_phase2Message);
            yield return new WaitForSeconds(0.5f);
            SpawnGiantTarget();
        }

        private void SpawnGiantTarget()
        {
            if (_giantTargetPrefab == null || _giantTargetSpawnPoint == null)
            {
                Debug.LogWarning("[ArcheryMinigameController] 巨大靶心 Prefab 或生成點未設定", this);
                _phase = Phase.Win;
                return;
            }
            _activeGiantTarget = Instantiate(
                _giantTargetPrefab,
                _giantTargetSpawnPoint.position,
                _giantTargetSpawnPoint.rotation);
            _activeGiantTarget.OnKilled += OnGiantTargetKilled;
        }

        private void OnGiantTargetKilled(MinigameAOETarget target)
        {
            target.OnKilled -= OnGiantTargetKilled;
            _activeGiantTarget = null;
            _phase = Phase.Win;
        }

        #endregion

        #region 清理

        private void StopActiveRoutine()
        {
            if (_activeRoutine != null)
            {
                StopCoroutine(_activeRoutine);
                _activeRoutine = null;
            }
            StopAllCoroutines();
        }

        private void CleanupMovingTargets()
        {
            for (int i = _activeMovingTargets.Count - 1; i >= 0; i--)
            {
                MinigameMovingTarget t = _activeMovingTargets[i];
                if (t == null) continue;
                t.OnKilled -= OnMovingTargetKilled;
                Destroy(t.gameObject);
            }
            _activeMovingTargets.Clear();
        }

        private void CleanupAllTargets()
        {
            CleanupMovingTargets();
            if (_activeGiantTarget != null)
            {
                _activeGiantTarget.OnKilled -= OnGiantTargetKilled;
                Destroy(_activeGiantTarget.gameObject);
                _activeGiantTarget = null;
            }
        }

        #endregion
    }
}
