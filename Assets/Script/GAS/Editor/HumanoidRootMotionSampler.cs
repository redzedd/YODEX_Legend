#if UNITY_EDITOR
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;
using UnityEngine.Playables;

namespace GAS.Editor
{
    /// <summary>
    /// Humanoid / Generic Root Motion 編輯器取樣封裝。
    /// Initialize 時以 PlayableGraph + AnimationMode.SamplePlayableGraph
    /// 預先 bake 整段 clip 的累積 RM 軌跡到快取,
    /// Sample 時直接插值查表,避免原生 API 對 Humanoid 不累加 RM 的問題。
    /// </summary>
    public sealed class HumanoidRootMotionSampler
    {
        private const float BAKE_FPS = 60f;
        private Animator _animator;
        private AnimationClip _clip;
        private PlayableGraph _graph;
        private bool _graphCreated;
        private bool _originalApplyRootMotion;
        private readonly List<Vector3> _positions = new();
        private readonly List<Quaternion> _rotations = new();
        private float _clipLength;
        private float _stepTime;

        public bool IsValid => _graphCreated && _animator != null && _clip != null && _positions.Count > 1;

        /// <summary>
        /// 初始化 Sampler 並 bake 出整段 clip 的累積 Root Motion 軌跡。
        /// 呼叫前需處於 AnimationMode.StartAnimationMode() 狀態。
        /// </summary>
        public void Initialize(Animator animator, AnimationClip clip)
        {
            Dispose();
            if (animator == null || clip == null) return;
            _animator = animator;
            _clip = clip;
            _clipLength = Mathf.Max(clip.length, 0.0001f);
            _stepTime = 1f / BAKE_FPS;
            _originalApplyRootMotion = animator.applyRootMotion;
            animator.applyRootMotion = true;
            AnimationPlayableUtilities.PlayClip(animator, clip, out _graph);
            _graphCreated = _graph.IsValid();
            if (!_graphCreated) return;
            BakeTrajectory();
        }

        /// <summary>
        /// 回傳指定時間點累積的 Root Motion 位移/旋轉(相對於 clip 起始姿勢)。
        /// </summary>
        public (Vector3 position, Quaternion rotation) Sample(float time)
        {
            if (!IsValid) return (Vector3.zero, Quaternion.identity);
            float clampedTime = Mathf.Clamp(time, 0f, _clipLength);
            float indexF = clampedTime / _stepTime;
            int i0 = Mathf.FloorToInt(indexF);
            int i1 = Mathf.Min(i0 + 1, _positions.Count - 1);
            i0 = Mathf.Clamp(i0, 0, _positions.Count - 1);
            float t = indexF - i0;
            Vector3 pos = Vector3.Lerp(_positions[i0], _positions[i1], t);
            Quaternion rot = Quaternion.Slerp(_rotations[i0], _rotations[i1], t);
            // 取樣 pose(讓 Animator 骨骼擺在 time 的姿勢;位移由外部套用到父物件)
            AnimationMode.SamplePlayableGraph(_graph, 0, clampedTime);
            // 歸零子物件 local transform,防止模型漂移(與 RootMotionRelay 一致)
            Transform tf = _animator.transform;
            tf.localPosition = Vector3.zero;
            tf.localRotation = Quaternion.identity;
            return (pos, rot);
        }

        public void Dispose()
        {
            if (_graphCreated && _graph.IsValid())
            {
                _graph.Destroy();
            }
            _graphCreated = false;
            if (_animator != null)
            {
                _animator.applyRootMotion = _originalApplyRootMotion;
            }
            _animator = null;
            _clip = null;
            _positions.Clear();
            _rotations.Clear();
        }

        /// <summary>
        /// 依序以遞增時間呼叫 SamplePlayableGraph,透過 Animator 的 OnAnimatorMove
        /// 累積 Root Motion 到 _animator.transform,逐幀紀錄 localPosition/localRotation。
        /// </summary>
        private void BakeTrajectory()
        {
            _positions.Clear();
            _rotations.Clear();
            Transform tf = _animator.transform;
            Vector3 originalPos = tf.localPosition;
            Quaternion originalRot = tf.localRotation;
            // 從原點開始累積
            tf.localPosition = Vector3.zero;
            tf.localRotation = Quaternion.identity;
            int steps = Mathf.CeilToInt(_clipLength * BAKE_FPS) + 1;
            for (int i = 0; i < steps; i++)
            {
                float t = Mathf.Min(i * _stepTime, _clipLength);
                AnimationMode.SamplePlayableGraph(_graph, 0, t);
                _positions.Add(tf.localPosition);
                _rotations.Add(tf.localRotation);
            }
            // 還原 transform,讓後續 Sample 歸零時不突兀
            tf.localPosition = originalPos;
            tf.localRotation = originalRot;
        }
    }
}
#endif
