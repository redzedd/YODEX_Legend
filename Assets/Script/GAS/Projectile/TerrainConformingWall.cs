using UnityEngine;

namespace GAS
{
    /// <summary>
    /// 地形貼合光壁 — 程式生成 unit cylinder mesh,執行時對底環做向下 raycast 採樣地形 Y,
    /// 動態變形 mesh 讓圓柱底邊跟隨任何地表(Unity Terrain / mesh terrain / 平台均可)
    ///
    /// 設計約束(與 AoEIndicatorAnimator 解耦):
    ///  • Wall transform 走標準 hierarchy scaling — localScale.x/z 由 AoEBehaviour 同步到 Radius,
    ///    localScale.y 由 AoEIndicatorAnimator 控制升起/亮閃,本元件不動 transform
    ///  • Mesh 採 unit 尺寸(radius=1, height=1),頂點偏移用 local space:
    ///    deltaYLocal = (worldGroundY - wallCenterY) / lossyScale.y
    ///  • 動畫期間 scale.y 從 0 → baseScale.y 時,底環 world Y 從中心點插值到地表,
    ///    自然產生「光壁從地面長出」視覺,動畫完成時完美貼地
    ///
    /// 性能:
    ///  • Activate 與 ChargeMultiplier 改變時各呼叫 ConformToGround 一次,平常 0 cost
    ///  • 頂點/法線/UV/三角形緩衝重用,僅頂點 Y 寫回 mesh(non-alloc)
    /// </summary>
    [RequireComponent(typeof(MeshFilter))]
    public class TerrainConformingWall : MonoBehaviour
    {
        [Header("Mesh Geometry")]
        [Tooltip("圓周分段數 — 越高越平滑,32~64 是典型甜蜜點;影響 raycast 次數(每次 conform = segments 次 raycast)")]
        [Range(8, 128)]
        [SerializeField] private int _segments = 48;

        [Tooltip("生成雙面 mesh(內外都可見)— BOTW 風格光壁玩家可能繞到背面,建議開")]
        [SerializeField] private bool _doubleSided = true;

        [Header("Terrain Sampling")]
        [Tooltip("地形圖層 — 只命中設定的圖層做 ground 偏移計算(建議排除 Player/Enemy/Trigger)")]
        [SerializeField] private LayerMask _groundMask = ~0;

        [Tooltip("Raycast 起點相對 wall transform 中心向上偏移(world units)— 避免從地表內側發射造成 miss")]
        [SerializeField] private float _raycastStartHeight = 20f;

        [Tooltip("Raycast 最大距離(world units)— 從 startHeight 向下能搜的最深距離")]
        [SerializeField] private float _maxRaycastDistance = 60f;

        [Tooltip("沒打到地時用相鄰已命中頂點插值補齊;整圈都沒命中時所有頂點 Y=0")]
        [SerializeField] private bool _interpolateMisses = true;

        [Tooltip("最小可接受地表斜率 — hit.normal · up 小於此值的命中視為 miss(預設 0.5 = 60° 內坡度)。\n" +
                 "用於拒絕牆壁/懸崖立面命中,避免單點被拉到牆頂高度形成尖刺")]
        [Range(0f, 1f)]
        [SerializeField] private float _minSurfaceDot = 0.5f;

        [Tooltip("單點頂點最大偏移距離(world units)— 超過視為異常 clamp 掉。\n" +
                 "預設 5m 對大部分地形足夠;懸崖/陡坡如果視覺斷層太大可調小到 2~3m,讓 mesh 自然攤平")]
        [SerializeField] private float _maxConformDistance = 5f;

        [Tooltip("採樣完做幾次相鄰平滑 — 弭平殘留尖角。0=關閉,1~2 對大部分地形夠用,過多會把細節平滑掉")]
        [Range(0, 5)]
        [SerializeField] private int _smoothPasses = 1;

        [Tooltip("OnEnable 時自動採樣一次(免外部呼叫)— 用於不接 AoEBehaviour 的測試場景")]
        [SerializeField] private bool _conformOnEnable;

        [Header("Debug")]
        [Tooltip("Scene View 中畫出每根 raycast 的軌跡與命中點")]
        [SerializeField] private bool _debugDrawRays;

        private MeshFilter _meshFilter;
        private Mesh _runtimeMesh;
        private Vector3[] _vertexBuffer;
        private bool[] _hitMask;
        private float[] _smoothBuffer;
        private int _ringCount;

        private void Awake()
        {
            _meshFilter = GetComponent<MeshFilter>();
            BuildMesh();
        }

        private void OnEnable()
        {
            if (_conformOnEnable) ConformToGround();
        }

        /// <summary>
        /// 對底環每個方位做向下 raycast 採樣地表,把 local Y 偏移寫入底環與上環 vertex
        /// 對外公開供 AoEBehaviour 在 Activate/SyncDecalSize 後呼叫
        /// </summary>
        public void ConformToGround()
        {
            if (_runtimeMesh == null) BuildMesh();
            if (_vertexBuffer == null) return;

            Transform tf = transform;
            float scaleY = Mathf.Abs(tf.lossyScale.y);
            // 防止除 0(scale.y 動畫從 0 升起時會碰到)— 用 baseScale 一個替代值即可,
            // 因為 scale.y=0 時整個 mesh 也被壓平,Y 偏移無視覺影響
            if (scaleY < 0.0001f) scaleY = 1f;

            int seg = _segments;
            int rc = _ringCount;
            float maxLocalDelta = _maxConformDistance / scaleY;

            // 只對 unique vertex(0..seg-1)做 raycast — i=seg 是 i=0 的縫合 duplicate,
            // 不重做避免浮點誤差(cos(2π) 不等於 1.0)造成 vertex[0] 與 vertex[seg] 命中不同物件
            for (int i = 0; i < seg; i++)
            {
                float a = (i / (float)seg) * Mathf.PI * 2f;
                float cs = Mathf.Cos(a);
                float sn = Mathf.Sin(a);

                // 用 TransformPoint 取「實際渲染後的世界位置」— 自動套用父物件 T*R*S。
                // 修正:父 AoE root 透過 LookRotation 套了 Y 旋轉,直接用 center+(cs,sn)*radius 會錯位甚至 180° 鏡像
                Vector3 worldBottomBase = tf.TransformPoint(new Vector3(cs, 0f, sn));
                Vector3 origin = new Vector3(
                    worldBottomBase.x,
                    worldBottomBase.y + _raycastStartHeight,
                    worldBottomBase.z);

                float deltaYLocal = 0f;
                bool valid = false;
                if (Physics.Raycast(
                    origin, Vector3.down,
                    out RaycastHit raycastHit,
                    _maxRaycastDistance + _raycastStartHeight,
                    _groundMask,
                    QueryTriggerInteraction.Ignore))
                {
                    // 拒絕陡峭表面(牆壁/懸崖立面)— 避免單點命中牆頂造成尖刺
                    float upDot = Vector3.Dot(raycastHit.normal, Vector3.up);
                    if (upDot >= _minSurfaceDot)
                    {
                        float deltaWorldY = raycastHit.point.y - worldBottomBase.y;
                        deltaYLocal = Mathf.Clamp(deltaWorldY / scaleY, -maxLocalDelta, maxLocalDelta);
                        valid = true;
                    }
                }
                _hitMask[i] = valid;

                Vector3 bottom = _vertexBuffer[i];
                bottom.y = deltaYLocal;
                _vertexBuffer[i] = bottom;
                Vector3 top = _vertexBuffer[i + rc];
                top.y = 1f + deltaYLocal;
                _vertexBuffer[i + rc] = top;
            }

            if (_interpolateMisses) FillMissesByNeighbor();
            if (_smoothPasses > 0) SmoothRing(_smoothPasses);
            // 縫合同步:vertex[seg] 強制 = vertex[0],無視前面所有處理是否一致
            // 即使 raycast/FillMisses/Smooth 全關,這步也保證接縫處不會撕開
            SyncSeam();

            _runtimeMesh.SetVertices(_vertexBuffer);
            _runtimeMesh.RecalculateBounds();
            // 法線是 XZ outward 方向,不受 Y 偏移影響 → 不需要 RecalculateNormals
        }

        /// <summary>
        /// 把 vertex[0] 的 Y 強制複製到 vertex[seg](bottom + top 都同步)
        /// vertex[0] 與 vertex[seg] 共用同一 XZ 位置但 UV 不同,Y 不一致就會在接縫處撕開三角形
        /// </summary>
        private void SyncSeam()
        {
            int seg = _segments;
            int rc = _ringCount;
            Vector3 b0 = _vertexBuffer[0];
            Vector3 dupBottom = _vertexBuffer[seg];
            dupBottom.y = b0.y;
            _vertexBuffer[seg] = dupBottom;

            Vector3 t0 = _vertexBuffer[rc];
            Vector3 dupTop = _vertexBuffer[seg + rc];
            dupTop.y = t0.y;
            _vertexBuffer[seg + rc] = dupTop;
        }

        /// <summary>
        /// 對底環 Y 做相鄰加權平滑(3-tap weighted average, weights 1-2-1)— 弭平單點殘留尖角
        /// 同步處理縫合點(i=0 與 i=segments 是同一位置,需保持值相同)
        /// </summary>
        private void SmoothRing(int passes)
        {
            int seg = _segments;
            int rc = _ringCount;
            for (int p = 0; p < passes; p++)
            {
                // 1. 用環狀鄰居算每個 unique vertex 的平滑 Y
                for (int i = 0; i < seg; i++)
                {
                    int left = (i - 1 + seg) % seg;
                    int right = (i + 1) % seg;
                    _smoothBuffer[i] = (_vertexBuffer[left].y + _vertexBuffer[i].y * 2f + _vertexBuffer[right].y) * 0.25f;
                }
                // 2. 寫回 unique vertices
                for (int i = 0; i < seg; i++)
                {
                    Vector3 b = _vertexBuffer[i];
                    b.y = _smoothBuffer[i];
                    _vertexBuffer[i] = b;
                    Vector3 t = _vertexBuffer[i + rc];
                    t.y = 1f + _smoothBuffer[i];
                    _vertexBuffer[i + rc] = t;
                }
                // 3. 同步縫合 duplicate(vertex[seg] = vertex[0])
                Vector3 db = _vertexBuffer[seg];
                db.y = _smoothBuffer[0];
                _vertexBuffer[seg] = db;
                Vector3 dt = _vertexBuffer[seg + rc];
                dt.y = 1f + _smoothBuffer[0];
                _vertexBuffer[seg + rc] = dt;
            }
        }

        /// <summary>
        /// 對 miss 的方位用左右最近 hit 線性插值補齊 — 避免突兀的「斷層」
        /// 整圈都 miss 時不動(全 Y=0)
        /// </summary>
        private void FillMissesByNeighbor()
        {
            int seg = _segments;
            int rc = _ringCount;
            // 只看 unique 0..seg-1,vertex[seg] 等 SyncSeam 處理
            bool anyHit = false;
            for (int i = 0; i < seg; i++)
            {
                if (_hitMask[i]) { anyHit = true; break; }
            }
            if (!anyHit) return;

            for (int i = 0; i < seg; i++)
            {
                if (_hitMask[i]) continue;

                float leftY = 0f; int leftDist = 0; bool leftFound = false;
                for (int d = 1; d < seg; d++)
                {
                    int idx = ((i - d) % seg + seg) % seg;
                    if (_hitMask[idx])
                    {
                        leftY = _vertexBuffer[idx].y;
                        leftDist = d;
                        leftFound = true;
                        break;
                    }
                }

                float rightY = 0f; int rightDist = 0; bool rightFound = false;
                for (int d = 1; d < seg; d++)
                {
                    int idx = (i + d) % seg;
                    if (_hitMask[idx])
                    {
                        rightY = _vertexBuffer[idx].y;
                        rightDist = d;
                        rightFound = true;
                        break;
                    }
                }

                float y;
                if (leftFound && rightFound)
                {
                    float t = leftDist / (float)(leftDist + rightDist);
                    y = Mathf.Lerp(leftY, rightY, t);
                }
                else if (leftFound) y = leftY;
                else if (rightFound) y = rightY;
                else y = 0f;

                Vector3 bottom = _vertexBuffer[i];
                bottom.y = y;
                _vertexBuffer[i] = bottom;
                Vector3 top = _vertexBuffer[i + rc];
                top.y = 1f + y;
                _vertexBuffer[i + rc] = top;
            }
        }

        /// <summary>
        /// 程式生成 unit cylinder mesh:radius=1, Y∈[0,1],雙面可選
        /// 邊界縫合 duplicate(_segments + 1 個方位)讓 UV 在 U=0/U=1 處不錯位
        /// </summary>
        private void BuildMesh()
        {
            if (_meshFilter == null) _meshFilter = GetComponent<MeshFilter>();

            _runtimeMesh = new Mesh { name = "TerrainConformingWall_Runtime" };
            _runtimeMesh.MarkDynamic();

            int seg = _segments;
            int rc = seg + 1;
            _ringCount = rc;
            int vertexCount = rc * 2;

            _vertexBuffer = new Vector3[vertexCount];
            _hitMask = new bool[rc];
            _smoothBuffer = new float[rc];
            Vector3[] normals = new Vector3[vertexCount];
            Vector2[] uvs = new Vector2[vertexCount];

            for (int i = 0; i < rc; i++)
            {
                float a = (i / (float)seg) * Mathf.PI * 2f;
                float cs = Mathf.Cos(a);
                float sn = Mathf.Sin(a);
                Vector3 outDir = new Vector3(cs, 0f, sn);
                float u = i / (float)seg;

                _vertexBuffer[i] = new Vector3(cs, 0f, sn);
                _vertexBuffer[i + rc] = new Vector3(cs, 1f, sn);
                normals[i] = outDir;
                normals[i + rc] = outDir;
                uvs[i] = new Vector2(u, 0f);
                uvs[i + rc] = new Vector2(u, 1f);
            }

            // 每個 segment 一個 quad,雙面時每 quad 4 三角形
            int facesPerSegment = _doubleSided ? 2 : 1;
            int[] tris = new int[seg * facesPerSegment * 2 * 3];
            int t = 0;
            for (int i = 0; i < seg; i++)
            {
                int b0 = i;
                int b1 = i + 1;
                int t0 = i + rc;
                int t1 = i + 1 + rc;
                // 外側(從外面看 CCW)
                tris[t++] = b0; tris[t++] = t0; tris[t++] = t1;
                tris[t++] = b0; tris[t++] = t1; tris[t++] = b1;
                if (_doubleSided)
                {
                    // 內側反向
                    tris[t++] = b0; tris[t++] = t1; tris[t++] = t0;
                    tris[t++] = b0; tris[t++] = b1; tris[t++] = t1;
                }
            }

            _runtimeMesh.vertices = _vertexBuffer;
            _runtimeMesh.normals = normals;
            _runtimeMesh.uv = uvs;
            _runtimeMesh.triangles = tris;
            _runtimeMesh.RecalculateBounds();

            _meshFilter.sharedMesh = _runtimeMesh;
        }

        private void OnDestroy()
        {
            if (_runtimeMesh != null)
            {
                if (Application.isPlaying) Destroy(_runtimeMesh);
                else DestroyImmediate(_runtimeMesh);
            }
        }

#if UNITY_EDITOR
        [ContextMenu("Conform To Ground (Test)")]
        private void EditorTestConform()
        {
            BuildMesh();
            ConformToGround();
        }

        private void OnDrawGizmosSelected()
        {
            if (!_debugDrawRays) return;
            if (_vertexBuffer == null || _hitMask == null) return;
            Transform tf = transform;
            for (int i = 0; i < _ringCount; i++)
            {
                float a = (i / (float)_segments) * Mathf.PI * 2f;
                float cs = Mathf.Cos(a);
                float sn = Mathf.Sin(a);
                Vector3 worldBottomBase = tf.TransformPoint(new Vector3(cs, 0f, sn));
                Vector3 origin = new Vector3(
                    worldBottomBase.x,
                    worldBottomBase.y + _raycastStartHeight,
                    worldBottomBase.z);
                Gizmos.color = _hitMask[i] ? Color.green : Color.red;
                Gizmos.DrawLine(origin, origin + Vector3.down * (_maxRaycastDistance + _raycastStartHeight));
            }
        }
#endif
    }
}
