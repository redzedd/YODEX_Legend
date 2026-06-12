using UnityEngine;

namespace GAS
{
    /// <summary>
    /// 殘影位置/旋轉鎖 — 每幀 LateUpdate 把根 transform 強制設回鎖定值。
    /// 必要性:Animancer 首次評估時會把動畫的 root bone 絕對位置寫到 transform 根節點,
    /// 導致殘影跳到動畫原點(通常是 0,0,0)。applyRootMotion=false 擋不住這種寫入。
    /// LateUpdate 在 Animator/Animancer 套用完之後跑,鎖在這裡就能蓋掉動畫的寫入,
    /// 同時保留骨骼動畫(子 transform 的 local 動作不受影響)。
    /// </summary>
    public class AfterImagePositionLock : MonoBehaviour
    {
        private Vector3 _lockedPosition;
        private Quaternion _lockedRotation;
        private bool _locked;

        public void LockHere(Vector3 position, Quaternion rotation)
        {
            _lockedPosition = position;
            _lockedRotation = rotation;
            transform.SetPositionAndRotation(position, rotation);
            _locked = true;
        }

        private void LateUpdate()
        {
            if (_locked)
            {
                transform.SetPositionAndRotation(_lockedPosition, _lockedRotation);
            }
        }
    }
}
