using UnityEngine;

namespace GAS
{
    /// <summary>
    /// 殘影專用 Root Motion 驅動 — 接管 OnAnimatorMove 並自行累加位移/旋轉。
    /// 設計目的:
    /// 1. 殘影按動畫 root motion 自然位移(取代純靜止的 AfterImagePositionLock)
    /// 2. 避開 Animancer 首次評估把 root bone 絕對位置寫到 transform 的問題
    ///    — LateUpdate 強制 transform = baseline + accumulatedDelta,蓋掉任何外部寫入
    /// 3. 第一次 OnAnimatorMove 跳過,避免 binding pose → clip pose 的初始大跳
    /// </summary>
    public class AfterImageRootMotionDriver : MonoBehaviour
    {
        private Animator _animator;
        private Vector3 _basePosition;
        private Quaternion _baseRotation;
        private Vector3 _accumulatedDelta;
        private Quaternion _accumulatedRotDelta;
        private bool _initialized;
        private bool _firstFireSkipped;

        /// <summary>由 WeaponManager 在創完殘影後呼叫,設定起始位置(= 玩家切換當下)</summary>
        public void Begin(Vector3 startPos, Quaternion startRot)
        {
            _basePosition = startPos;
            _baseRotation = startRot;
            _accumulatedDelta = Vector3.zero;
            _accumulatedRotDelta = Quaternion.identity;
            _animator = GetComponent<Animator>();
            if (_animator != null)
            {
                _animator.applyRootMotion = true;
            }
            _initialized = true;
            _firstFireSkipped = false;
            transform.SetPositionAndRotation(_basePosition, _baseRotation);
        }

        private void OnAnimatorMove()
        {
            if (!_initialized || _animator == null) return;
            // 第一次評估的 delta 可能含「binding pose → clip 起始 pose」的大跳,直接丟棄
            if (!_firstFireSkipped)
            {
                _firstFireSkipped = true;
                return;
            }
            _accumulatedDelta += _animator.deltaPosition;
            _accumulatedRotDelta = _animator.deltaRotation * _accumulatedRotDelta;
        }

        private void LateUpdate()
        {
            if (!_initialized) return;
            // 強制覆寫 transform — 蓋掉 Humanoid Avatar / Animancer / 其他寫入,保留純粹的 baseline + 累加 RM
            transform.SetPositionAndRotation(
                _basePosition + _accumulatedDelta,
                _accumulatedRotDelta * _baseRotation);
        }
    }
}
