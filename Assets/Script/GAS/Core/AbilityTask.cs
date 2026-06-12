using System;
using System.Collections;
using UnityEngine;

namespace GAS
{
    /// <summary>
    /// 能力任務基類 - 用於在能力中執行異步操作
    /// 類似 Unreal GAS 的 AbilityTask
    /// </summary>
    public abstract class AbilityTask
    {
        /// <summary>
        /// 關聯的能力 Spec
        /// </summary>
        protected GameplayAbilitySpec AbilitySpec { get; private set; }

        /// <summary>
        /// 擁有者 ASC
        /// </summary>
        protected AbilitySystemComponent Owner => AbilitySpec?.Owner;

        /// <summary>
        /// 任務是否正在執行
        /// </summary>
        public bool IsActive { get; protected set; }

        /// <summary>
        /// 當任務完成時觸發
        /// </summary>
        public event Action OnTaskCompleted;

        /// <summary>
        /// 當任務被取消時觸發
        /// </summary>
        public event Action OnTaskCancelled;

        // 執行中的 Coroutine
        private Coroutine _activeCoroutine;

        /// <summary>
        /// 初始化任務
        /// </summary>
        public virtual void InitTask(GameplayAbilitySpec abilitySpec)
        {
            AbilitySpec = abilitySpec;
            IsActive = false;
        }

        /// <summary>
        /// 開始執行任務
        /// </summary>
        public virtual void Activate()
        {
            if (Owner == null)
            {
                Debug.LogError("[AbilityTask] Cannot activate: Owner is null");
                return;
            }

            IsActive = true;
            _activeCoroutine = Owner.StartCoroutine(ExecuteTask());
        }

        /// <summary>
        /// 結束任務
        /// </summary>
        public virtual void EndTask()
        {
            if (!IsActive) return;

            IsActive = false;

            if (_activeCoroutine != null && Owner != null)
            {
                Owner.StopCoroutine(_activeCoroutine);
                _activeCoroutine = null;
            }

            OnTaskCompleted?.Invoke();
        }

        /// <summary>
        /// 取消任務
        /// </summary>
        public virtual void CancelTask()
        {
            if (!IsActive) return;

            IsActive = false;

            if (_activeCoroutine != null && Owner != null)
            {
                Owner.StopCoroutine(_activeCoroutine);
                _activeCoroutine = null;
            }

            OnTaskCancelled?.Invoke();
        }

        /// <summary>
        /// 執行任務的主要邏輯 - 子類實現
        /// </summary>
        protected abstract IEnumerator ExecuteTask();

        /// <summary>
        /// 清理任務
        /// </summary>
        public virtual void Cleanup()
        {
            OnTaskCompleted = null;
            OnTaskCancelled = null;
        }
    }

    /// <summary>
    /// 等待時間任務
    /// </summary>
    public class WaitDelayTask : AbilityTask
    {
        private readonly float _duration;

        public WaitDelayTask(float duration)
        {
            _duration = duration;
        }

        protected override IEnumerator ExecuteTask()
        {
            yield return new WaitForSeconds(_duration);
            EndTask();
        }

        /// <summary>
        /// 創建等待時間任務
        /// </summary>
        public static WaitDelayTask Create(GameplayAbilitySpec spec, float duration)
        {
            var task = new WaitDelayTask(duration);
            task.InitTask(spec);
            return task;
        }
    }

    /// <summary>
    /// 播放動畫任務
    /// </summary>
    public class PlayAnimationTask : AbilityTask
    {
        private readonly Animator _animator;
        private readonly string _stateName;
        private readonly int _layer;
        private readonly float _normalizedTime;
        private readonly bool _waitForCompletion;

        public PlayAnimationTask(Animator animator, string stateName, int layer = 0, 
            float normalizedTime = 0f, bool waitForCompletion = true)
        {
            _animator = animator;
            _stateName = stateName;
            _layer = layer;
            _normalizedTime = normalizedTime;
            _waitForCompletion = waitForCompletion;
        }

        protected override IEnumerator ExecuteTask()
        {
            if (_animator == null)
            {
                EndTask();
                yield break;
            }

            // 播放動畫
            _animator.Play(_stateName, _layer, _normalizedTime);

            if (_waitForCompletion)
            {
                // 等待動畫開始
                yield return null;

                // 等待動畫結束
                var stateInfo = _animator.GetCurrentAnimatorStateInfo(_layer);
                while (_animator.GetCurrentAnimatorStateInfo(_layer).normalizedTime < 1f)
                {
                    if (!IsActive) yield break;
                    yield return null;
                }
            }

            EndTask();
        }

        /// <summary>
        /// 創建播放動畫任務
        /// </summary>
        public static PlayAnimationTask Create(GameplayAbilitySpec spec, Animator animator, 
            string stateName, int layer = 0, float normalizedTime = 0f, bool waitForCompletion = true)
        {
            var task = new PlayAnimationTask(animator, stateName, layer, normalizedTime, waitForCompletion);
            task.InitTask(spec);
            return task;
        }
    }

    /// <summary>
    /// 等待輸入任務
    /// </summary>
    public class WaitInputTask : AbilityTask
    {
        private readonly float _timeout;
        private readonly bool _waitForPress;

        /// <summary>
        /// 輸入是否在超時前被觸發
        /// </summary>
        public bool WasInputTriggered { get; private set; }

        public WaitInputTask(float timeout = float.MaxValue, bool waitForPress = true)
        {
            _timeout = timeout;
            _waitForPress = waitForPress;
        }

        protected override IEnumerator ExecuteTask()
        {
            float elapsed = 0f;
            WasInputTriggered = false;

            while (elapsed < _timeout)
            {
                if (!IsActive) yield break;

                // 檢查輸入狀態
                bool inputState = AbilitySpec?.InputPressed ?? false;
                if (_waitForPress && inputState)
                {
                    WasInputTriggered = true;
                    break;
                }
                else if (!_waitForPress && !inputState)
                {
                    WasInputTriggered = true;
                    break;
                }

                elapsed += Time.deltaTime;
                yield return null;
            }

            EndTask();
        }

        /// <summary>
        /// 創建等待輸入任務
        /// </summary>
        public static WaitInputTask Create(GameplayAbilitySpec spec, float timeout = float.MaxValue, 
            bool waitForPress = true)
        {
            var task = new WaitInputTask(timeout, waitForPress);
            task.InitTask(spec);
            return task;
        }
    }

    /// <summary>
    /// 追蹤目標任務
    /// </summary>
    public class TrackTargetTask : AbilityTask
    {
        private readonly Transform _owner;
        private readonly Transform _target;
        private readonly float _duration;
        private readonly float _rotationSpeed;

        public TrackTargetTask(Transform owner, Transform target, float duration, float rotationSpeed = 720f)
        {
            _owner = owner;
            _target = target;
            _duration = duration;
            _rotationSpeed = rotationSpeed;
        }

        protected override IEnumerator ExecuteTask()
        {
            if (_owner == null || _target == null)
            {
                EndTask();
                yield break;
            }

            float elapsed = 0f;

            while (elapsed < _duration && IsActive)
            {
                Vector3 direction = (_target.position - _owner.position).normalized;
                direction.y = 0f;

                if (direction.sqrMagnitude > 0.001f)
                {
                    Quaternion targetRotation = Quaternion.LookRotation(direction);
                    _owner.rotation = Quaternion.RotateTowards(
                        _owner.rotation, 
                        targetRotation, 
                        _rotationSpeed * Time.deltaTime
                    );
                }

                elapsed += Time.deltaTime;
                yield return null;
            }

            EndTask();
        }

        /// <summary>
        /// 創建追蹤目標任務
        /// </summary>
        public static TrackTargetTask Create(GameplayAbilitySpec spec, Transform owner, 
            Transform target, float duration, float rotationSpeed = 720f)
        {
            var task = new TrackTargetTask(owner, target, duration, rotationSpeed);
            task.InitTask(spec);
            return task;
        }
    }
}
