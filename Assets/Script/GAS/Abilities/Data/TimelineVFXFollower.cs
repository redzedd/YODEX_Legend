using UnityEngine;

namespace GAS
{
    /// <summary>
    /// TimelineEvent VFX 的軸選擇性跟隨元件。
    /// 每幀依 <see cref="AttachAxes"/> 同步指定的軸到 socket;未勾選的軸維持 spawn 當下的世界值。
    /// 不論勾選何種組合,縮放永遠 = baseScale × socket.lossyScale,確保角色放大時特效等比放大。
    /// 攻擊結束或 socket 失效時呼叫 <see cref="StopFollowing"/> 凍結 VFX,讓粒子在原地播完。
    /// [ExecuteAlways] — Edit Mode 下也跑 LateUpdate,讓 EditorWindow 預覽中拖時間軸可以即時跟隨。
    /// </summary>
    [ExecuteAlways]
    public class TimelineVFXFollower : MonoBehaviour
    {
        private Transform _socket;
        private AttachAxes _axes;
        private Vector3 _baseScale;
        private Vector3 _initialWorldPos;
        private Vector3 _initialWorldEuler;
        private Vector3 _localPositionOffset;
        private Quaternion _localRotationOffset;

        public void Setup(Transform socket, AttachAxes axes, Vector3 localPositionOffset, Vector3 localRotationOffsetEuler, Vector3 baseScale)
        {
            _socket = socket;
            _axes = axes;
            _baseScale = baseScale;
            _localPositionOffset = localPositionOffset;
            _localRotationOffset = Quaternion.Euler(localRotationOffsetEuler);
            _initialWorldPos = transform.position;
            _initialWorldEuler = transform.rotation.eulerAngles;
            ApplyTransform();
        }

        /// <summary>停止跟隨,VFX 凍結在當下世界位置 / 旋轉 / 縮放,粒子繼續播完。</summary>
        public void StopFollowing()
        {
            _socket = null;
        }

        /// <summary>外部主動觸發一次 transform 計算 — 供 Editor 預覽在手動 SampleAnimation 後同步使用。</summary>
        public void Sample()
        {
            if (_socket == null || !_socket) return;
            ApplyTransform();
        }

        /// <summary>
        /// 編輯器專用同步 — 把當下 evt 數值塞進 follower 並重算初始世界值 + 立即套用 transform。
        /// 用於 Inspector 改 Position / Rotation / Scale / Axes 後讓 VFX 即時反映新值。
        /// 與 <see cref="Setup"/> 不同處:不需要保留「spawn 那刻的初始值」,因為設計師正在動態調整,
        /// 未跟隨軸的 initial 也要跟著 Inspector 新數值重算才看得到效果。
        /// </summary>
        public void EditorSync(Transform socket, AttachAxes axes, Vector3 localPositionOffset, Vector3 localRotationOffsetEuler, Vector3 baseScale)
        {
            if (socket == null) return;
            _socket = socket;
            _axes = axes;
            _baseScale = baseScale;
            _localPositionOffset = localPositionOffset;
            _localRotationOffset = Quaternion.Euler(localRotationOffsetEuler);
            Quaternion worldRot = socket.rotation * _localRotationOffset;
            _initialWorldPos = socket.TransformPoint(localPositionOffset);
            _initialWorldEuler = worldRot.eulerAngles;
            ApplyTransform();
        }

        private void LateUpdate()
        {
            if (_socket == null) return;
            // socket 被銷毀(fake-null)→ 凍結
            if (!_socket) { _socket = null; return; }
            ApplyTransform();
        }

        private void ApplyTransform()
        {
            Vector3 followedPos = _socket.TransformPoint(_localPositionOffset);
            Quaternion followedRot = _socket.rotation * _localRotationOffset;
            Vector3 followedEuler = followedRot.eulerAngles;

            Vector3 finalPos = new Vector3(
                (_axes & AttachAxes.PositionX) != 0 ? followedPos.x : _initialWorldPos.x,
                (_axes & AttachAxes.PositionY) != 0 ? followedPos.y : _initialWorldPos.y,
                (_axes & AttachAxes.PositionZ) != 0 ? followedPos.z : _initialWorldPos.z);

            Vector3 finalEuler = new Vector3(
                (_axes & AttachAxes.RotationX) != 0 ? followedEuler.x : _initialWorldEuler.x,
                (_axes & AttachAxes.RotationY) != 0 ? followedEuler.y : _initialWorldEuler.y,
                (_axes & AttachAxes.RotationZ) != 0 ? followedEuler.z : _initialWorldEuler.z);

            transform.SetPositionAndRotation(finalPos, Quaternion.Euler(finalEuler));

            Vector3 socketScale = _socket.lossyScale;
            transform.localScale = new Vector3(
                _baseScale.x * socketScale.x,
                _baseScale.y * socketScale.y,
                _baseScale.z * socketScale.z);
        }
    }
}
