using System;
using System.Collections;
using UnityEngine;

namespace Boss.Dragon
{
    /// <summary>
    /// 隕石攻擊執行器 — 掛在 Boss prefab 上,由 DragonScreamState 呼叫 Execute()
    /// 流程簡化版:延遲 → 定時 spawn N 顆隕石 Prefab → 完成
    /// 隕石下降/爆炸/傷害判定都由 Prefab 內的 ParticleSystem + MeteorPSCollisionHandler 處理
    /// </summary>
    public class MeteorAttackController : MonoBehaviour
    {
        #region Serialized Fields

        [Header("資料")]
        [SerializeField] [Tooltip("隕石攻擊數值設定 SO")]
        private MeteorAttackData _data;

        [Header("瞄準目標")]
        [SerializeField] [Tooltip("玩家 GameObject 的 Tag — 用來找隕石目標")]
        private string _playerTag = "Player";

        #endregion

        #region Private Fields

        private Transform _player;
        private Coroutine _sequenceRoutine;

        #endregion

        #region Properties

        /// <summary>當前是否正在執行隕石序列 (定時 spawn 階段未結束)</summary>
        public bool IsExecuting => _sequenceRoutine != null;

        /// <summary>數值設定 — 給外部 (FSM) 讀取時間參數用</summary>
        public MeteorAttackData Data => _data;

        #endregion

        #region Events

        /// <summary>所有隕石 spawn 完成時觸發 — 注意此時最後一顆可能還在落下/爆炸 (Prefab 內 PS 仍在跑)</summary>
        public event Action OnSequenceComplete;

        #endregion

        #region Unity Lifecycle

        private void Start()
        {
            if (!string.IsNullOrEmpty(_playerTag))
            {
                GameObject playerGO = GameObject.FindWithTag(_playerTag);
                if (playerGO != null) _player = playerGO.transform;
            }
        }

        #endregion

        #region Public API

        /// <summary>觸發隕石序列。若已在執行中則忽略</summary>
        public void Execute()
        {
            if (IsExecuting) return;
            if (_data == null)
            {
                Debug.LogError($"[{name}] MeteorAttackController 缺少 MeteorAttackData", this);
                return;
            }
            if (_data.MeteorPrefab == null)
            {
                Debug.LogError($"[{name}] MeteorAttackData.MeteorPrefab 未設定", this);
                return;
            }
            _sequenceRoutine = StartCoroutine(RunMeteorSequence());
        }

        /// <summary>強制停止 — 中斷 spawn 序列,但已 spawn 的 Prefab 仍會自己播完 (Coroutine 各自獨立)</summary>
        public void Cancel()
        {
            if (_sequenceRoutine != null)
            {
                StopCoroutine(_sequenceRoutine);
                _sequenceRoutine = null;
            }
        }

        #endregion

        #region Private Methods

        private IEnumerator RunMeteorSequence()
        {
            if (_data.InitialDelay > 0f)
                yield return new WaitForSeconds(_data.InitialDelay);

            for (int i = 0; i < _data.MeteorCount; i++)
            {
                Vector3 targetPos = ComputeMeteorTarget();
                SpawnMeteor(targetPos);
                if (i < _data.MeteorCount - 1 && _data.SpawnInterval > 0f)
                    yield return new WaitForSeconds(_data.SpawnInterval);
            }

            _sequenceRoutine = null;
            OnSequenceComplete?.Invoke();
        }

        private Vector3 ComputeMeteorTarget()
        {
            // 用玩家當下位置 + 隨機散佈 (假設競技場是平地,玩家 Y = 地面 Y)
            Vector3 basePos = _player != null ? _player.position : transform.position;
            Vector2 spread = UnityEngine.Random.insideUnitCircle * _data.SpawnSpreadRadius;
            basePos.x += spread.x;
            basePos.z += spread.y;
            return basePos;
        }

        private void SpawnMeteor(Vector3 pos)
        {
            GameObject instance = Instantiate(_data.MeteorPrefab, pos, Quaternion.identity);

            // 注入 data 給 prefab 內的 handler (傷害數值來源)
            MeteorPSCollisionHandler handler = instance.GetComponent<MeteorPSCollisionHandler>();
            if (handler != null)
            {
                handler.Initialize(_data);
            }
            else
            {
                Debug.LogWarning($"[{name}] Spawn 的隕石 Prefab 沒掛 MeteorPSCollisionHandler — 隕石不會造成傷害。請在 Prefab Root 加上此元件", instance);
            }

            if (_data.MeteorLifetime > 0f)
            {
                Destroy(instance, _data.MeteorLifetime);
            }
        }

        #endregion
    }
}
