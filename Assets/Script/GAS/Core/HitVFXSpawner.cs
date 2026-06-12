using UnityEngine;

namespace GAS
{
    /// <summary>
    /// 命中特效生成共用工具 — 近戰與遠程的 Hit VFX 都走這條,行為與 EnemyAttackExecutor 的 Timeline VFX 一致。
    /// 縮放邏輯:final scale = vfx 原本 localScale × ScaleMultiplier × AttackerScale。
    ///   • ScaleMultiplier — 設計師在攻擊資料上配置的「這招特效要多大」(Vector3,可做扁壓 / 拉長)
    ///   • AttackerScale — 角色當下的整體縮放(SpatialScaleUtility.GetScaleFactor),讓巨大化 / 縮小狀態下特效自動跟著放大
    /// ScaleAllChildren: 切換子物件粒子系統的 ScalingMode — Hierarchy 讓粒子大小/發射形狀一起放大,Local 則只縮 transform。
    /// </summary>
    public static class HitVFXSpawner
    {
        /// <summary>
        /// 生成命中特效,套用縮放與粒子模式,附加銷毀時間與表面吸附。
        /// </summary>
        public static GameObject Spawn(
            GameObject prefab,
            Vector3 position,
            Quaternion rotation,
            Vector3 scaleMultiplier,
            float attackerScale,
            bool scaleAllChildren,
            float lifetime,
            Transform attachTo)
        {
            if (prefab == null) return null;
            GameObject vfx = Object.Instantiate(prefab, position, rotation);
            Vector3 effective = scaleMultiplier * attackerScale;
            vfx.transform.localScale = Vector3.Scale(vfx.transform.localScale, effective);
            ApplyParticleScalingMode(vfx, scaleAllChildren);
            if (attachTo != null)
            {
                vfx.transform.SetParent(attachTo, true);
            }
            if (lifetime > 0f)
            {
                Object.Destroy(vfx, lifetime);
            }
            return vfx;
        }

        /// <summary>
        /// 切換 prefab 下所有 ParticleSystem 的 ScalingMode。
        /// Hierarchy: 粒子大小/速度/發射形狀都隨 transform 縮放(整顆特效等比放大);
        /// Local: 只縮 transform,粒子本身維持原始尺寸(多粒子組成的複雜特效視覺易斷層,不推薦)。
        /// </summary>
        private static void ApplyParticleScalingMode(GameObject root, bool useHierarchy)
        {
            ParticleSystem[] systems = root.GetComponentsInChildren<ParticleSystem>(true);
            ParticleSystemScalingMode mode = useHierarchy ? ParticleSystemScalingMode.Hierarchy : ParticleSystemScalingMode.Local;
            for (int i = 0; i < systems.Length; i++)
            {
                ParticleSystem ps = systems[i];
                if (ps == null) continue;
                ParticleSystem.MainModule main = ps.main;
                main.scalingMode = mode;
            }
        }
    }
}
