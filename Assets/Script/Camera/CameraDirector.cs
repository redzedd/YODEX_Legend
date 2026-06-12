using System;
using System.Collections.Generic;
using System.Text;
using Unity.Cinemachine;
using UnityEngine;

namespace CameraSystem
{
    /// <summary>
    /// 鏡頭中控 — 維護「請求棧」，依 Layer + Push 順序決定哪台鏡頭勝出。
    /// 場景上唯一一個 Singleton，通常掛在 Main Camera 旁邊（與 CinemachineBrain 共處）。
    ///
    /// API:
    ///   var ticket = CameraDirector.Instance.Request(CameraId.Aim);   // 一一對應的鏡頭
    ///   var ticket = CameraDirector.Instance.Request(myEntry);        // 指定特定 Entry（互動演出）
    ///   ticket.Release();                                              // 退場
    ///
    /// 事件:
    ///   OnStackChanged — 任何 Push/Release 後觸發，訂閱者可檢查 CurrentWinner 決定行為
    ///                    (例 LockOnBridge 用於「LockOn 被覆蓋時自動解除」)
    ///
    /// 內部 Priority 公式：actualPriority = (LayerPriority × 100) + pushIndex
    /// 例：Aim 層 = 50 → 啟用時 Cinemachine Priority = 5000
    ///     同層後 push 的 ticket pushIndex 較大 → 同層內勝出
    /// </summary>
    [DisallowMultipleComponent]
    [DefaultExecutionOrder(-100)]
    public class CameraDirector : MonoBehaviour
    {
        public static CameraDirector Instance { get; private set; }

        [Header("設定")]

        [SerializeField]
        [Tooltip("Layer → Priority 對應表（ScriptableObject）— 留空會用內建預設值（Background 10 / LockOn 40 / Aim 50 / Action 100 / Cinematic 200）")]
        private CameraPriorityProfile _profile;

        [SerializeField]
        [Tooltip("CinemachineBrain（通常在 Main Camera 上）— 留空會自動從 Camera.main 抓取。\nAwake 時會依下方旗標設定它的 IgnoreTimeScale")]
        private CinemachineBrain _brain;

        [SerializeField]
        [Tooltip("勾選後讓 CinemachineBrain 與 ImpulseManager 都用實時間（不受 Time.timeScale 影響）— 頓幀演出（格擋特寫等）需要")]
        private bool _ignoreTimeScale = true;

        [Header("Debug")]

        [SerializeField]
        [Tooltip("輸出 Request / Release / Priority 計算的詳細 log")]
        private bool _verboseLog = false;

        private readonly List<CameraEntry> _registered = new();
        private readonly List<CameraTicket> _activeStack = new();
        private int _pushCounter;
        private CameraEntry _currentWinner;

        private const int LAYER_SCALE = 100;

        /// <summary>當前贏家 — Cinemachine 實際顯示的鏡頭（依 Layer + push 順序計算的最高 priority）</summary>
        public CameraEntry CurrentWinner => _currentWinner;

        /// <summary>
        /// Stack 變動時觸發（Request/Release 後）— 訂閱者可檢查 CurrentWinner 決定是否做行為。
        /// 例 LockOnBridge 訂閱：當 LockOn ticket 仍存在但 CurrentWinner 不是它,就觸發 Unlock 清掉 anchor 副作用。
        /// </summary>
        public event Action OnStackChanged;

        // ────── Unity 生命週期 ──────
        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Debug.LogWarning("[CameraDirector] 場上已有其他 Instance，銷毀重複的", this);
                Destroy(this);
                return;
            }
            Instance = this;

            if (_brain == null && Camera.main != null)
            {
                _brain = Camera.main.GetComponent<CinemachineBrain>();
            }
            ApplyIgnoreTimeScale();
        }

        private void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }

        private void ApplyIgnoreTimeScale()
        {
            if (_brain != null)
            {
                _brain.IgnoreTimeScale = _ignoreTimeScale;
            }
            if (CinemachineImpulseManager.Instance != null)
            {
                CinemachineImpulseManager.Instance.IgnoreTimeScale = _ignoreTimeScale;
            }
        }

        // ────── 註冊（由 CameraEntry 在 OnEnable 自動呼叫）──────
        public void Register(CameraEntry entry)
        {
            if (entry == null) return;
            if (_registered.Contains(entry)) return;
            _registered.Add(entry);
            // 新註冊的 entry 預設關閉（Priority 設成 inactive）
            SetEntryPriority(entry, GetInactivePriority());
            if (_verboseLog) Debug.Log($"[CameraDirector] Register {entry.Id} (Layer={entry.Layer})", this);
        }

        public void Unregister(CameraEntry entry)
        {
            if (entry == null) return;
            // 移除此 entry 對應的所有 active ticket
            for (int i = _activeStack.Count - 1; i >= 0; i--)
            {
                if (_activeStack[i].Entry == entry)
                {
                    _activeStack[i].IsActive = false;
                    _activeStack.RemoveAt(i);
                }
            }
            _registered.Remove(entry);
            RecomputePriorities();
            if (_verboseLog) Debug.Log($"[CameraDirector] Unregister {entry.Id}", this);
        }

        // ────── Request API ──────
        /// <summary>
        /// 用 CameraId 請求鏡頭 — 適合一一對應的鏡頭（ThirdPerson / Aim / LockOn / Parry）。
        /// 同 ID 有多個 Entry 時會回傳第一個找到的；多個 Cinematic 鏡頭請改用 Request(entry)。
        /// </summary>
        public CameraTicket Request(CameraId id)
        {
            CameraEntry entry = FindEntryById(id);
            if (entry == null)
            {
                Debug.LogWarning($"[CameraDirector] 找不到 ID={id} 的 CameraEntry — 確認場上有對應鏡頭掛了 CameraEntry 元件並選好 ID", this);
                return null;
            }
            return Request(entry);
        }

        /// <summary>
        /// 用 Entry 直接請求鏡頭 — 適合互動演出鏡頭（多台同 ID 時用此方式指定）。
        /// </summary>
        public CameraTicket Request(CameraEntry entry)
        {
            if (entry == null) return null;
            if (!_registered.Contains(entry))
            {
                Debug.LogWarning("[CameraDirector] Entry 尚未 Register — 確認 Entry GameObject 是否啟用", entry);
                return null;
            }
            _pushCounter++;
            CameraTicket ticket = new(this, entry, _pushCounter);
            _activeStack.Add(ticket);
            if (_verboseLog) Debug.Log($"[CameraDirector] Request → {entry.Id} (push={_pushCounter})", this);
            RecomputePriorities();
            return ticket;
        }

        // ────── Release API ──────
        public void Release(CameraTicket ticket)
        {
            if (ticket == null) return;
            if (!ticket.IsActive) return;
            ticket.IsActive = false;
            _activeStack.Remove(ticket);
            if (_verboseLog) Debug.Log($"[CameraDirector] Release → {ticket.Entry?.Id} (push={ticket.PushOrder})", this);
            RecomputePriorities();
        }

        // ────── 內部 ──────
        private CameraEntry FindEntryById(CameraId id)
        {
            for (int i = 0; i < _registered.Count; i++)
            {
                if (_registered[i].Id == id) return _registered[i];
            }
            return null;
        }

        private void RecomputePriorities()
        {
            // 1) 先把所有註冊鏡頭設成關閉值
            int inactive = GetInactivePriority();
            for (int i = 0; i < _registered.Count; i++)
            {
                SetEntryPriority(_registered[i], inactive);
            }
            // 2) Stack 內的 entry：actualPriority = LayerPriority × 100 + stackIndex
            //    stackIndex 越大代表越晚 push → 同 layer 時優先級高
            //    同時計算 winner（最高 actualPriority 的 entry）
            CameraEntry winner = null;
            int winnerPriority = int.MinValue;
            for (int i = 0; i < _activeStack.Count; i++)
            {
                CameraTicket ticket = _activeStack[i];
                if (ticket.Entry == null) continue;
                int basePriority = GetLayerPriority(ticket.Entry.Layer);
                int actual = (basePriority * LAYER_SCALE) + i;
                SetEntryPriority(ticket.Entry, actual);
                if (actual > winnerPriority)
                {
                    winnerPriority = actual;
                    winner = ticket.Entry;
                }
            }
            _currentWinner = winner;
            if (_verboseLog) LogCurrentStack();
            // 觸發事件 — 訂閱者(例 LockOnBridge)可檢查 CurrentWinner 決定是否做覆蓋退場
            OnStackChanged?.Invoke();
        }

        private void SetEntryPriority(CameraEntry entry, int priority)
        {
            if (entry == null || entry.Camera == null) return;
            entry.Camera.Priority = priority;
        }

        private int GetLayerPriority(CameraLayer layer)
        {
            return _profile != null ? _profile.GetPriority(layer) : DefaultPriority(layer);
        }

        private int GetInactivePriority()
        {
            return _profile != null ? _profile.InactivePriority : -1;
        }

        // _profile 未指派時的預設值（與 SO 的預設一致）
        private static int DefaultPriority(CameraLayer layer)
        {
            return layer switch
            {
                CameraLayer.Background => 10,
                CameraLayer.LockOn => 40,
                CameraLayer.Aim => 50,
                CameraLayer.Action => 100,
                CameraLayer.Cinematic => 200,
                _ => 0
            };
        }

        private void LogCurrentStack()
        {
            if (_activeStack.Count == 0)
            {
                Debug.Log("[CameraDirector] Stack 空（無啟用鏡頭）", this);
                return;
            }
            StringBuilder sb = new("[CameraDirector] Stack: ");
            for (int i = 0; i < _activeStack.Count; i++)
            {
                CameraEntry e = _activeStack[i].Entry;
                if (e == null) continue;
                sb.Append($"{e.Id}(Layer={e.Layer},Prio={e.Camera.Priority.Value})");
                if (i < _activeStack.Count - 1) sb.Append(" → ");
            }
            Debug.Log(sb.ToString(), this);
        }
    }
}
