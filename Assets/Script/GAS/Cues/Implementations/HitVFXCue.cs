using UnityEngine;

namespace GAS
{
    /// <summary>
    /// 受擊特效 Cue - 專門用於命中反饋
    /// </summary>
    [CreateAssetMenu(fileName = "New Hit VFX Cue", menuName = "GAS/Cues/Hit VFX Cue")]
    public class HitVFXCue : VFXCue
    {
        [Header("Hit Specific")]
        [Tooltip("使用受擊點作為生成位置")]
        public bool UseHitPoint = true;

        [Tooltip("面向攻擊者")]
        public bool FaceInstigator = true;

        [Tooltip("對齊命中表面法線（使用 Parameters.Rotation 作為表面法線方向）")]
        public bool AlignToSurfaceNormal;

        [Tooltip("附著在被命中的物體表面（特效會跟隨物體移動，例如箭矢插入效果）")]
        public bool AttachToHitSurface;

        public override void OnExecute(GameplayCueParameters parameters)
        {
            if (VFXPrefab == null) return;
            Vector3 position = UseHitPoint ? parameters.Location : GetSpawnPosition(parameters);
            position += AdditionalPositionOffset;
            Quaternion rotation;
            if (AlignToSurfaceNormal && parameters.Rotation != default && parameters.Rotation != Quaternion.identity)
            {
                // 使用表面法線方向（由投射物碰撞偵測傳入）
                rotation = parameters.Rotation * Quaternion.Euler(AdditionalRotationOffset);
            }
            else if (FaceInstigator && parameters.Instigator != null)
            {
                Vector3 direction = parameters.Instigator.transform.position - position;
                direction.y = 0f;
                if (direction.sqrMagnitude > 0.001f)
                {
                    rotation = Quaternion.LookRotation(direction) * Quaternion.Euler(AdditionalRotationOffset);
                }
                else
                {
                    rotation = GetSpawnRotation(parameters) * Quaternion.Euler(AdditionalRotationOffset);
                }
            }
            else
            {
                rotation = GetSpawnRotation(parameters) * Quaternion.Euler(AdditionalRotationOffset);
            }
            GameObject instance = Instantiate(VFXPrefab, position, rotation);
            // 附著到被命中物體表面
            if (AttachToHitSurface && parameters.TargetObject != null)
            {
                instance.transform.SetParent(parameters.TargetObject.transform, true);
            }
            // 使用 parameters.Scale（如果有的話），並反算父物件縮放以保持世界空間大小
            Vector3 desiredScale = (parameters.Scale != Vector3.zero)
                ? Vector3.Scale(parameters.Scale, AdditionalScale)
                : AdditionalScale;
            if (instance.transform.parent != null)
            {
                Vector3 parentLossy = instance.transform.parent.lossyScale;
                instance.transform.localScale = new Vector3(
                    desiredScale.x / parentLossy.x,
                    desiredScale.y / parentLossy.y,
                    desiredScale.z / parentLossy.z);
            }
            else
            {
                instance.transform.localScale = desiredScale;
            }
            if (AutoDestroyTime > 0f)
            {
                Destroy(instance, AutoDestroyTime);
            }
        }
    }
}
