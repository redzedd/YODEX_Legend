using System;
using System.Collections.Generic;
using UnityEngine;

namespace Interaction
{
    /// <summary>
    /// 互動管理器 — 事件驅動的焦點追蹤（零 GC 分配）
    /// 維護當前範圍內的互動對象，自動聚焦優先級最高者
    /// 以 dirty flag + 手動遍歷取代每幀 LINQ OrderBy
    /// DefaultExecutionOrder(-100) 確保 Instance 在其他腳本的 OnEnable/Start 前完成初始化
    /// </summary>
    [DefaultExecutionOrder(-100)]
    public class InteractionManager : MonoBehaviour
    {
        public static InteractionManager Instance { get; private set; }

        /// <summary>焦點變更事件（新焦點, 舊焦點）— InteractionPromptUI 訂閱此事件</summary>
        public event Action<IInteractable, IInteractable> OnFocusChanged;

        [Header("障礙物過濾")]
        [Tooltip("視線遮擋判斷的碰撞層（選 Wall / Environment，勿選 Player 和 Interactable）")]
        [SerializeField] private LayerMask _obstacleMask;
        [Tooltip("玩家視線起點高度偏移（公尺）")]
        [SerializeField] private float _eyeHeight = 1.0f;

        private readonly List<IInteractable> _interactablesInRange = new List<IInteractable>(8);
        private IInteractable _currentFocused;
        private bool _isDirty;
        private Transform _playerTransform;

        /// <summary>當前聚焦的互動對象</summary>
        public IInteractable CurrentFocused => _currentFocused;

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }
            Instance = this;
        }

        private void Start()
        {
            GameObject player = GameObject.FindGameObjectWithTag("Player");
            if (player != null)
                _playerTransform = player.transform;
            else
                Debug.LogWarning("[InteractionManager] 找不到 Tag 為 'Player' 的物件，穿牆過濾將停用。");
        }

        private void LateUpdate()
        {
            CleanNullEntries();
            if (!_isDirty) return;
            _isDirty = false;
            EvaluateBestFocus();
        }

        /// <summary>註冊可互動物件（進入範圍時呼叫）</summary>
        public void RegisterInteractable(IInteractable interactable)
        {
            if (_interactablesInRange.Contains(interactable)) return;
            _interactablesInRange.Add(interactable);
            _isDirty = true;
        }

        /// <summary>取消註冊可互動物件（離開範圍時呼叫）</summary>
        public void UnregisterInteractable(IInteractable interactable)
        {
            if (!_interactablesInRange.Remove(interactable)) return;
            _isDirty = true;
        }

        /// <summary>嘗試對當前焦點物件執行互動</summary>
        public void TryInteract()
        {
            if (_currentFocused == null) return;
            if (!_currentFocused.CanInteract) return;
            _currentFocused.Interact();
        }

        /// <summary>
        /// 零分配最佳互動對象查找 — O(n) 遍歷
        /// 僅在 dirty flag 為 true 時執行
        /// </summary>
        private void EvaluateBestFocus()
        {
            IInteractable best = null;
            int bestPriority = int.MaxValue;
            for (int i = 0; i < _interactablesInRange.Count; i++)
            {
                IInteractable candidate = _interactablesInRange[i];
                if (candidate == null) continue;
                if (!candidate.CanInteract) continue;
                if (IsBlockedByObstacle(candidate)) continue;
                if (candidate.Priority < bestPriority)
                {
                    bestPriority = candidate.Priority;
                    best = candidate;
                }
            }
            if (best == _currentFocused) return;
            IInteractable old = _currentFocused;
            old?.OnUnfocus();
            _currentFocused = best;
            _currentFocused?.OnFocus();
            OnFocusChanged?.Invoke(_currentFocused, old);
        }

        /// <summary>判斷玩家與互動物件之間是否有障礙物遮擋</summary>
        private bool IsBlockedByObstacle(IInteractable interactable)
        {
            if (_playerTransform == null) return false;
            if (interactable is not MonoBehaviour mono) return false;
            Vector3 eyePos = _playerTransform.position + Vector3.up * _eyeHeight;
            // 取互動物件的碰撞器中心；無碰撞器時退回「與眼睛同高」的位置
            // 避免射向地面 pivot 時穿過地板 Collider 而誤判為遮擋
            Collider col = mono.GetComponent<Collider>();
            Vector3 targetPos = col != null
                ? col.bounds.center
                : new Vector3(mono.transform.position.x, eyePos.y, mono.transform.position.z);
            return Physics.Linecast(eyePos, targetPos, _obstacleMask);
        }

        /// <summary>反向遍歷清除已銷毀的物件（避免 RemoveAll lambda 分配）</summary>
        private void CleanNullEntries()
        {
            for (int i = _interactablesInRange.Count - 1; i >= 0; i--)
            {
                if (_interactablesInRange[i] is UnityEngine.Object obj && obj == null)
                {
                    _interactablesInRange.RemoveAt(i);
                    _isDirty = true;
                }
                else if (_interactablesInRange[i] == null)
                {
                    _interactablesInRange.RemoveAt(i);
                    _isDirty = true;
                }
            }
        }
    }
}
