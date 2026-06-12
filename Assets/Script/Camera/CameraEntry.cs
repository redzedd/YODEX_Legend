using Unity.Cinemachine;
using UnityEngine;

namespace CameraSystem
{
    /// <summary>
    /// 鏡頭名牌 — 掛在每個 CinemachineCamera 上，自動向 CameraDirector 註冊。
    /// 設計師工作：選擇 ID 與 Layer，常駐底層鏡頭勾選「Activate On Enable」。
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(CinemachineCamera))]
    public class CameraEntry : MonoBehaviour
    {
        [Header("身分")]

        [SerializeField]
        [Tooltip("鏡頭 ID — 程式呼叫 Director.Request(id) 時用來找到這台鏡頭。\n同 ID 可以有多個 Entry（互動演出鏡頭用 Cinematic），程式則透過 Director.Request(entry) 直接指定")]
        private CameraId _id = CameraId.None;

        [SerializeField]
        [Tooltip("優先層 — 決定這台鏡頭壓得過誰、被誰壓。\nBackground=底層, Aim=瞄準, LockOn=鎖定, Action=戰鬥特寫, Cinematic=演出")]
        private CameraLayer _layer = CameraLayer.Background;

        [Header("行為")]

        [SerializeField]
        [Tooltip("勾選後在 OnEnable 自動向 Director 請求啟用 — 適合常駐底層的主視角鏡頭（ThirdPerson 必勾）")]
        private bool _activateOnEnable = false;

        [Header("引用")]

        [SerializeField]
        [Tooltip("CinemachineCamera 引用 — 留空時 Awake 自動 GetComponent")]
        private CinemachineCamera _camera;

        public CameraId Id => _id;
        public CameraLayer Layer => _layer;
        public CinemachineCamera Camera => _camera;

        private CameraTicket _autoTicket;

        private void Awake()
        {
            if (_camera == null)
            {
                _camera = GetComponent<CinemachineCamera>();
            }
        }

        private void OnEnable()
        {
            CameraDirector director = CameraDirector.Instance;
            if (director == null)
            {
                Debug.LogWarning($"[CameraEntry] {name} OnEnable 時找不到 CameraDirector — 確認場上有 Director 且執行順序設為 -100", this);
                return;
            }
            director.Register(this);
            if (_activateOnEnable)
            {
                _autoTicket = director.Request(this);
            }
        }

        private void OnDisable()
        {
            CameraDirector director = CameraDirector.Instance;
            if (director == null) return;
            if (_autoTicket != null)
            {
                _autoTicket.Release();
                _autoTicket = null;
            }
            director.Unregister(this);
        }
    }
}
