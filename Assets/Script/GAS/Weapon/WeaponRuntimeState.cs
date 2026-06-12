using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Animancer;
using DG.Tweening;

namespace GAS
{
    /// <summary>
    /// 武器運行時狀態 - 處理殘影效果和模型切換
    /// 在攻擊途中切換武器時，創建殘影讓原角色完成攻擊動作
    /// </summary>
    public class WeaponRuntimeState : MonoBehaviour
    {
        [Header("Afterimage Settings")]
        [Tooltip("殘影材質")]
        [SerializeField] private Material _afterImageMaterial;

        [Tooltip("殘影淡出時間(全域預設,秒)。\n" +
                 "若 WeaponData 上的 AfterImageFadeDuration > 0,該武器會以 WeaponData 的值為準,本欄位被覆寫。\n" +
                 "想讓本欄位生效:把對應 WeaponData 的 AfterImageFadeDuration 設為 0。")]
        [SerializeField] private float _fadeOutDuration = 0.5f;

        [Tooltip("殘影顏色（能量體顏色）")]
        [SerializeField] private Color _afterImageColor = new Color(0.5f, 0.8f, 1f, 0.7f);

        [Tooltip("是否讓殘影完成原動畫")]
        [SerializeField] private bool _completeAnimation = true;

        [Tooltip("同時存在的殘影數量上限 — 玩家連續快速切武器時,超過此數量會銷毀最舊的殘影(節省效能)。\n建議 3~6;太低看不到層次感,太高 GPU/物理計算量會累積")]
        [SerializeField, Range(1, 20)] private int _maxConcurrentAfterImages = 5;

        // 當前活躍的殘影列表
        private readonly List<AfterImageInstance> _activeAfterImages = new();

        // 材質屬性 ID 快取
        private static readonly int ColorPropertyId = Shader.PropertyToID("_Color");
        private static readonly int AlphaPropertyId = Shader.PropertyToID("_Alpha");
        private static readonly int EmissionColorId = Shader.PropertyToID("_EmissionColor");

        #region Public Methods

        /// <summary>
        /// 創建殘影
        /// </summary>
        /// <param name="sourceModel">原始模型</param>
        /// <param name="currentAnimationState">當前播放的動畫狀態</param>
        /// <param name="weaponData">武器資料（獲取殘影材質）</param>
        /// <returns>殘影實例</returns>
        public AfterImageInstance CreateAfterImage(GameObject sourceModel, AnimancerState currentAnimationState, WeaponData weaponData = null, bool freezePose = false)
        {
            if (sourceModel == null) return null;

            // 超過並行上限 — 銷毀最舊的殘影(列表開頭 = 最早 Add 的);
            // 確保連點切武器不會無限累積物理判定 + Animator 計算。
            // Destroy 會觸發 Executor.OnDestroy → Cleanup → VFX detach
            while (_activeAfterImages.Count >= _maxConcurrentAfterImages)
            {
                AfterImageInstance oldest = _activeAfterImages[0];
                _activeAfterImages.RemoveAt(0);
                if (oldest.GameObject != null)
                {
                    Destroy(oldest.GameObject);
                }
            }

            // 複製模型
            GameObject afterImageObj = Instantiate(sourceModel, sourceModel.transform.position, sourceModel.transform.rotation);
            afterImageObj.name = $"{sourceModel.name}_AfterImage";
            // sourceModel 通常是 _modelRoot 的子物件,角色縮放掛在父層 → sourceModel.localScale=1 但 lossyScale=N。
            // Instantiate(prefab, pos, rot) 不帶 parent,新物件繼承的是 localScale(=1)會看起來縮小。
            // 直接把 localScale 設為 source 的 lossyScale,確保殘影與當下角色等大。
            afterImageObj.transform.localScale = sourceModel.transform.lossyScale;

            // 禁用不需要的組件
            DisableUnnecessaryComponents(afterImageObj);

            // [FIX] 設置殘影材質和淡出時間
            Material materialToUse = weaponData?.AfterImageMaterial ?? _afterImageMaterial;
            float fadeDuration = _fadeOutDuration; // 預設值
            
            // 優先使用 WeaponData 的設置（如果大於 0）
            if (weaponData != null && weaponData.AfterImageFadeDuration > 0)
            {
                fadeDuration = weaponData.AfterImageFadeDuration;
            }

            // [DEBUG] 輸出淡出時間資訊
            Debug.Log($"[WeaponRuntimeState] Creating afterimage - WeaponData: {weaponData?.WeaponName ?? "null"}, " +
                      $"FadeDuration from WeaponData: {weaponData?.AfterImageFadeDuration ?? 0f}, " +
                      $"FadeDuration from Component: {_fadeOutDuration}, " +
                      $"Final FadeDuration: {fadeDuration}");

            if (materialToUse != null)
            {
                ApplyAfterImageMaterial(afterImageObj, materialToUse);
            }
            else
            {
                // 如果沒有殘影材質，使用透明度淡出
                SetupDefaultFadeOut(afterImageObj);
            }

            // 創建殘影實例
            AfterImageInstance instance = new AfterImageInstance
            {
                GameObject = afterImageObj,
                FadeDuration = fadeDuration,
                CreationTime = Time.time
            };

            // 凍結姿態模式:Sample 當下姿態到殘影骨骼,停掉 Animator/Animancer
            // 一旦凍結就跳過 SetupAnimationCompletion(那會 Play 動畫,蓋掉凍結效果)
            if (freezePose)
            {
                FreezePose(instance, currentAnimationState);
            }
            else if (_completeAnimation && currentAnimationState != null)
            {
                SetupAnimationCompletion(instance, sourceModel, currentAnimationState);
            }

            // 添加到活躍列表
            _activeAfterImages.Add(instance);

            // 啟動淡出協程
            StartCoroutine(FadeOutAfterImage(instance, fadeDuration));

            return instance;
        }

        /// <summary>
        /// 立即清除所有殘影
        /// </summary>
        public void ClearAllAfterImages()
        {
            foreach (AfterImageInstance instance in _activeAfterImages)
            {
                if (instance.GameObject != null)
                {
                    Destroy(instance.GameObject);
                }
            }
            _activeAfterImages.Clear();
        }

        /// <summary>
        /// 獲取當前活躍的殘影數量
        /// </summary>
        public int ActiveAfterImageCount => _activeAfterImages.Count;

        #endregion

        #region Private Methods

        /// <summary>
        /// 禁用殘影物件上不需要的組件
        /// </summary>
        private void DisableUnnecessaryComponents(GameObject obj)
        {
            // 禁用碰撞器
            foreach (Collider col in obj.GetComponentsInChildren<Collider>())
            {
                col.enabled = false;
            }

            // 禁用 Rigidbody
            foreach (Rigidbody rb in obj.GetComponentsInChildren<Rigidbody>())
            {
                rb.isKinematic = true;
            }

            // 禁用 CharacterController
            CharacterController cc = obj.GetComponent<CharacterController>();
            if (cc != null)
            {
                cc.enabled = false;
            }

            // [FIX] 禁用/移除所有粒子特效和 VFX（避免殘影發光）
            foreach (ParticleSystem ps in obj.GetComponentsInChildren<ParticleSystem>())
            {
                ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
                Destroy(ps);
            }

            // [FIX] 移除 VFX Graph 組件
            #if UNITY_VFX_GRAPH
            foreach (UnityEngine.VFX.VisualEffect vfx in obj.GetComponentsInChildren<UnityEngine.VFX.VisualEffect>())
            {
                vfx.Stop();
                Destroy(vfx);
            }
            #endif

            // [FIX] 移除 Light 組件（避免殘影發光）
            foreach (Light light in obj.GetComponentsInChildren<Light>())
            {
                Destroy(light);
            }

            // 禁用腳本組件（保留 Animator/Animancer）
            foreach (MonoBehaviour mb in obj.GetComponentsInChildren<MonoBehaviour>())
            {
                // 保留 AnimancerComponent
                if (mb is AnimancerComponent) continue;
                
                mb.enabled = false;
            }

            // 標記為殘影狀態
            obj.tag = "Untagged";
            obj.layer = LayerMask.NameToLayer("Ignore Raycast");
        }

        /// <summary>
        /// 應用殘影材質
        /// </summary>
        private void ApplyAfterImageMaterial(GameObject obj, Material afterImageMaterial)
        {
            foreach (Renderer renderer in obj.GetComponentsInChildren<Renderer>())
            {
                // [FIX] 跳過粒子系統的渲染器（避免特效材質被替換）
                if (renderer is ParticleSystemRenderer)
                {
                    continue;
                }

                // 為每個渲染器創建材質實例
                Material[] newMaterials = new Material[renderer.materials.Length];
                for (int i = 0; i < newMaterials.Length; i++)
                {
                    newMaterials[i] = new Material(afterImageMaterial);
                    
                    // [FIX] 確保材質支援透明度
                    SetupTransparentMaterial(newMaterials[i]);
                    
                    // 設置初始顏色
                    if (newMaterials[i].HasProperty(ColorPropertyId))
                    {
                        newMaterials[i].SetColor(ColorPropertyId, _afterImageColor);
                    }
                    if (newMaterials[i].HasProperty(EmissionColorId))
                    {
                        newMaterials[i].SetColor(EmissionColorId, _afterImageColor * 0.5f);
                    }
                }
                renderer.materials = newMaterials;
            }
        }

        /// <summary>
        /// 設置材質為透明模式
        /// </summary>
        private void SetupTransparentMaterial(Material mat)
        {
            // 設置渲染模式為 Transparent
            if (mat.HasProperty("_Mode"))
            {
                mat.SetFloat("_Mode", 3); // Transparent
            }
            
            // 設置混合模式
            mat.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
            mat.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            mat.SetInt("_ZWrite", 0);
            
            // 設置關鍵字
            mat.DisableKeyword("_ALPHATEST_ON");
            mat.EnableKeyword("_ALPHABLEND_ON");
            mat.DisableKeyword("_ALPHAPREMULTIPLY_ON");
            
            // 設置渲染隊列
            mat.renderQueue = 3000;
        }

        /// <summary>
        /// 設置預設的淡出效果（當沒有殘影材質時）
        /// </summary>
        private void SetupDefaultFadeOut(GameObject obj)
        {
            foreach (Renderer renderer in obj.GetComponentsInChildren<Renderer>())
            {
                // [FIX] 跳過粒子系統的渲染器
                if (renderer is ParticleSystemRenderer)
                {
                    continue;
                }

                // [FIX] 為每個材質創建實例（避免影響原始材質）
                Material[] newMaterials = new Material[renderer.materials.Length];
                for (int i = 0; i < renderer.materials.Length; i++)
                {
                    newMaterials[i] = new Material(renderer.materials[i]);
                    
                    // 設置為透明模式
                    SetupTransparentMaterial(newMaterials[i]);

                    // 設置初始透明度
                    if (newMaterials[i].HasProperty(ColorPropertyId))
                    {
                        Color color = newMaterials[i].GetColor(ColorPropertyId);
                        color.a = _afterImageColor.a;
                        newMaterials[i].SetColor(ColorPropertyId, color);
                    }
                }
                renderer.materials = newMaterials;
            }
        }

        /// <summary>
        /// 凍結姿態 — 把當下動畫姿態烤到殘影骨骼上,然後關掉 Animator/Animancer,
        /// 讓殘影像「定格快照」般保留切換瞬間的動作。供攻擊以外狀態(站立/移動/跳躍等)切武器時使用。
        /// </summary>
        private void FreezePose(AfterImageInstance instance, AnimancerState sourceAnimState)
        {
            if (instance.GameObject == null) return;

            // SampleAnimation 會把 root 寫到 clip 空間位置(通常 0,0,0)— 先存後還原
            Vector3 savedPos = instance.GameObject.transform.position;
            Quaternion savedRot = instance.GameObject.transform.rotation;

            if (sourceAnimState?.Clip != null)
            {
                sourceAnimState.Clip.SampleAnimation(instance.GameObject, sourceAnimState.Time);
            }

            instance.GameObject.transform.SetPositionAndRotation(savedPos, savedRot);

            // 停掉 Animator/Animancer,讓 sample 出來的姿態不被後續評估蓋掉
            foreach (Animator a in instance.GameObject.GetComponentsInChildren<Animator>())
            {
                a.enabled = false;
            }
            foreach (AnimancerComponent ac in instance.GameObject.GetComponentsInChildren<AnimancerComponent>())
            {
                ac.enabled = false;
            }

            // AnimationRemainingTime 保持 0 → FadeOutAfterImage 不會等待,殘影創建後就會走淡出流程
            // (淡出時間還是吃 _fadeOutDuration / WeaponData.AfterImageFadeDuration)
        }

        /// <summary>
        /// 設置動畫完成
        /// </summary>
        private void SetupAnimationCompletion(AfterImageInstance instance, GameObject sourceModel, AnimancerState sourceAnimState)
        {
            // 獲取殘影的 Animancer
            AnimancerComponent afterImageAnimancer = instance.GameObject.GetComponent<AnimancerComponent>();
            if (afterImageAnimancer == null)
            {
                afterImageAnimancer = instance.GameObject.AddComponent<AnimancerComponent>();
            }

            // 獲取原始 Animancer 的 Animator
            Animator sourceAnimator = sourceModel.GetComponent<Animator>();
            if (sourceAnimator != null)
            {
                Animator afterImageAnimator = instance.GameObject.GetComponent<Animator>();
                if (afterImageAnimator == null)
                {
                    afterImageAnimator = instance.GameObject.AddComponent<Animator>();
                }
                afterImageAnimator.runtimeAnimatorController = sourceAnimator.runtimeAnimatorController;
                afterImageAnimator.avatar = sourceAnimator.avatar;
            }

            // 播放相同的動畫
            if (sourceAnimState?.Clip != null)
            {
                AnimancerState newState = afterImageAnimancer.Play(sourceAnimState.Clip);
                newState.Time = sourceAnimState.Time;
                instance.AnimancerState = newState;
                
                // 計算剩餘時間
                float remainingTime = sourceAnimState.Clip.length - sourceAnimState.Time;
                instance.AnimationRemainingTime = Mathf.Max(0, remainingTime);
            }
        }

        /// <summary>
        /// 殘影上是否仍有執行中的 Ghost Executor(近戰或遠程)
        /// </summary>
        private static bool HasActiveGhostExecutor(GameObject ghost)
        {
            if (ghost == null) return false;
            MeleeAttackGhostExecutor melee = ghost.GetComponent<MeleeAttackGhostExecutor>();
            if (melee != null && melee.IsRunning) return true;
            RangedAttackGhostExecutor ranged = ghost.GetComponent<RangedAttackGhostExecutor>();
            if (ranged != null && ranged.IsRunning) return true;
            return false;
        }

        /// <summary>
        /// 殘影淡出協程
        /// </summary>
        private IEnumerator FadeOutAfterImage(AfterImageInstance instance, float duration)
        {
            // [DEBUG] 輸出淡出開始資訊
            Debug.Log($"[WeaponRuntimeState] Starting fade out - Duration: {duration}s, " +
                      $"AnimationRemainingTime: {instance.AnimationRemainingTime}s, " +
                      $"CompleteAnimation: {_completeAnimation}");

            // 先等一幀,讓呼叫者(WeaponManager)有機會把 Ghost Executor 掛上殘影。
            // 不等的話,coroutine 同步跑到第一個 yield 時 executor 還沒附上,偵測會回 false。
            yield return null;

            // 等待策略(依優先順序):
            // 1. 有 Ghost Executor → 等執行器跑完(它自己控制動畫時間 + SheatheCancelTime),與 _completeAnimation 無關
            // 2. _completeAnimation=true → 等動畫播完(舊行為,純視覺殘影)
            // 3. 其他 → 立即淡出(_completeAnimation=false 的「閃現」效果)
            if (instance.GameObject != null && HasActiveGhostExecutor(instance.GameObject))
            {
                while (instance.GameObject != null && HasActiveGhostExecutor(instance.GameObject))
                {
                    yield return null;
                }
                Debug.Log($"[WeaponRuntimeState] Ghost executor finished, starting fade out now");
            }
            else if (_completeAnimation && instance.AnimationRemainingTime > 0)
            {
                yield return new WaitForSeconds(instance.AnimationRemainingTime);
                Debug.Log($"[WeaponRuntimeState] Animation completed, starting fade out now");
            }

            // 開始淡出
            float elapsed = 0f;
            List<Renderer> renderers = new List<Renderer>();
            List<Material[]> originalMaterials = new List<Material[]>();
            
            if (instance.GameObject != null)
            {
                foreach (Renderer renderer in instance.GameObject.GetComponentsInChildren<Renderer>())
                {
                    // [FIX] 跳過粒子系統渲染器
                    if (renderer is ParticleSystemRenderer)
                    {
                        continue;
                    }
                    
                    renderers.Add(renderer);
                    originalMaterials.Add(renderer.materials);
                }
            }
            
            Debug.Log($"[WeaponRuntimeState] Found {renderers.Count} renderers to fade");

            while (elapsed < duration && instance.GameObject != null)
            {
                elapsed += Time.deltaTime;
                float alpha = 1f - (elapsed / duration);

                // 更新所有材質的透明度
                for (int i = 0; i < renderers.Count; i++)
                {
                    Renderer renderer = renderers[i];
                    if (renderer == null) continue;

                    Material[] materials = originalMaterials[i];
                    foreach (Material mat in materials)
                    {
                        if (mat == null) continue;

                        // [FIX] 更新顏色的 alpha 通道
                        if (mat.HasProperty(ColorPropertyId))
                        {
                            Color color = mat.GetColor(ColorPropertyId);
                            color.a = _afterImageColor.a * alpha;
                            mat.SetColor(ColorPropertyId, color);
                        }
                        
                        // [FIX] 如果材質有 _Alpha 屬性，也更新它
                        if (mat.HasProperty(AlphaPropertyId))
                        {
                            mat.SetFloat(AlphaPropertyId, alpha);
                        }
                        
                        // [FIX] 更新自發光顏色（如果有）
                        if (mat.HasProperty(EmissionColorId))
                        {
                            Color emission = _afterImageColor * 0.5f * alpha;
                            mat.SetColor(EmissionColorId, emission);
                        }
                        
                        // [FIX] 如果是 URP/HDRP，可能需要更新 _BaseColor
                        if (mat.HasProperty("_BaseColor"))
                        {
                            Color baseColor = mat.GetColor("_BaseColor");
                            baseColor.a = _afterImageColor.a * alpha;
                            mat.SetColor("_BaseColor", baseColor);
                        }
                    }
                }

                yield return null;
            }

            // [DEBUG] 淡出完成
            Debug.Log($"[WeaponRuntimeState] Fade out completed - Total time: {elapsed}s, Target duration: {duration}s");

            // 清理
            if (instance.GameObject != null)
            {
                Destroy(instance.GameObject);
            }

            _activeAfterImages.Remove(instance);
        }

        #endregion

        #region Unity Lifecycle

        private void OnDestroy()
        {
            ClearAllAfterImages();
        }

        #endregion
    }

    /// <summary>
    /// 殘影實例數據
    /// </summary>
    public class AfterImageInstance
    {
        /// <summary>殘影遊戲物件</summary>
        public GameObject GameObject { get; set; }

        /// <summary>淡出持續時間</summary>
        public float FadeDuration { get; set; }

        /// <summary>創建時間</summary>
        public float CreationTime { get; set; }

        /// <summary>Animancer 狀態（用於繼續播放動畫）</summary>
        public AnimancerState AnimancerState { get; set; }

        /// <summary>動畫剩餘時間</summary>
        public float AnimationRemainingTime { get; set; }
    }
}
