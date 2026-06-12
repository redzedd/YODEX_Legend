using System;
using UnityEngine;

namespace Enemy.AttackSystem
{
    /// <summary>
    /// 攻擊招式內的單一 VFX 事件。
    /// 一個 EnemyAttackProfile 可包含多個 VFX 事件（刀光、武器拖尾、命中閃光...），
    /// 由 EnemyAttackExecutor 在動畫時間軸跨過 Time 時觸發一次 Instantiate。
    /// </summary>
    [Serializable]
    public class EnemyAttackVfxEvent
    {
        [SerializeField]
        [Tooltip("事件顯示名稱（Timeline 編輯器標籤用，例：「揮刀殘影」「武器拖尾」）")]
        private string _label = "新特效";

        [SerializeField]
        [Tooltip("觸發時刻（動畫時間軸的秒數，從 0 到動畫片段長度）。HitStart 之前 = 揮刀預兆、HitStart 之後 = 命中特效")]
        private float _time = 0.2f;

        [SerializeField]
        [Tooltip("要生成的特效 Prefab — 可放 ParticleSystem、VFX Graph、Mesh 任何 GameObject。建議在 Prefab 上勾 Play On Awake")]
        private GameObject _vfxPrefab;

        [SerializeField]
        [Tooltip("綁定的骨骼名稱（例：「RightHandWeapon」、「LeftHand」、「Spine」）。\n留空 = 綁在敵人根節點（角色腳下中心）")]
        private string _boneName = "";

        [SerializeField]
        [Tooltip("相對骨骼的位置偏移（local space）。例：(0, 0, 0.3) 把特效往骨骼前方推 0.3 公尺")]
        private Vector3 _positionOffset = Vector3.zero;

        [SerializeField]
        [Tooltip("相對骨骼的旋轉偏移（local Euler 角度）。例：(0, 90, 0) 讓特效繞 Y 軸轉 90 度")]
        private Vector3 _rotationOffset = Vector3.zero;

        [SerializeField]
        [Tooltip("特效縮放倍率（在 Prefab 自帶 scale 上乘上此倍率）。\n(1, 1, 1) = 維持 Prefab 原始大小\n(2, 2, 2) = 雙倍\n(0.5, 0.5, 0.5) = 半倍\n設計師不用改 Prefab 就能調整這招的特效大小")]
        private Vector3 _scaleMultiplier = Vector3.one;

        [SerializeField]
        [Tooltip("勾選 = 縮放會套用到所有子物件的 ParticleSystem（粒子大小、發射形狀都跟著放大）\n取消 = 只縮放 GameObject 的 Transform，子物件的粒子系統維持原始尺寸（多 PS 組成的特效會視覺不一致）\n建議：保持勾選")]
        private bool _scaleAllChildren = true;

        [SerializeField]
        [Tooltip("勾選 = 特效跟著骨骼動（例：武器拖尾要跟著揮刀）。\n取消 = 生成那一刻位置就脫離父子關係，留在世界座標（例：地面打擊痕跡）")]
        private bool _attachToBone = true;

        [SerializeField]
        [Tooltip("自動銷毀秒數。\n> 0：經過此秒數後 Destroy GameObject\n= 0：不主動銷毀，讓 ParticleSystem 自然消逝（建議在 Prefab 的 ParticleSystem Main 設 Stop Action = Destroy）")]
        private float _lifetime = 2f;

        public string Label => _label;
        public float Time => _time;
        public GameObject VfxPrefab => _vfxPrefab;
        public string BoneName => _boneName;
        public Vector3 PositionOffset => _positionOffset;
        public Vector3 RotationOffset => _rotationOffset;
        public Vector3 ScaleMultiplier => _scaleMultiplier;
        public bool ScaleAllChildren => _scaleAllChildren;
        public bool AttachToBone => _attachToBone;
        public float Lifetime => _lifetime;

        // Editor 視窗拖曳 marker 時呼叫 — 直接改私有欄位，避免設計師自己寫 reflection
        public void SetTime(float time)
        {
            _time = Mathf.Max(0f, time);
        }

        public void SetLabel(string label)
        {
            _label = label;
        }
    }
}
