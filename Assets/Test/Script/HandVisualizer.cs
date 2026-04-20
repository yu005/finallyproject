using UnityEngine;
using UnityEngine.UI;
using Klak.TestTools;
using MediaPipe.HandPose;

public sealed class HandVisualizer : MonoBehaviour
{
    #region Editable attributes

    [SerializeField] ImageSource _source = null;
    [Space]
    [SerializeField] ResourceSet _resources = null;
    [SerializeField] Shader _keyPointShader = null;
    [SerializeField] Shader _handRegionShader = null;
    [Space]
    [SerializeField] RawImage _mainUI = null;
    [SerializeField] RawImage _cropUI = null;

    [Space]
    [Header("美術素材")]
    [SerializeField] Texture2D _seedTexture = null;
    [SerializeField] Color _seedTint = Color.white;
    [SerializeField] Vector2 _seedSize = new Vector2(0.16f, 0.16f);
    [SerializeField] bool _autoLoadSeedFromResources = true;
    [SerializeField] string _seedResourceName = "Seeds_Cereals";

    [SerializeField] Texture2D _soilTexture = null;
    [SerializeField] bool _autoLoadSoilFromResources = true;
    [SerializeField, Range(1, 12)] float _soilTiling = 4;
    [SerializeField] Texture2D _grassTexture = null;
    [SerializeField] bool _autoLoadGrassFromResources = true;
    [SerializeField] string _grassResourceName = "GrassPatch";
    [SerializeField] Color _grassTint = new Color(0.58f, 0.83f, 0.42f, 1);

    [Space]
    [Header("終章儀式（雙手）")]
    [SerializeField, Range(0.3f, 2.5f)] float _placeHoldSeconds = 1.0f;
    [SerializeField, Range(0.3f, 3.0f)] float _placeDropAnimationSeconds = 1.4f;
    [SerializeField, Range(2, 6)] int _placeRequiredDetections = 3;
    [SerializeField, Range(0.05f, 2.0f)] float _maxHandSpeedForPlace = 0.45f;
    [SerializeField, Range(0.0f, 0.4f)] float _placementHeightLimit = 0.10f;
    [SerializeField, Range(1.0f, 1.8f)] float _openPalmRatioThreshold = 1.22f;
    [SerializeField, Range(0.0f, 0.3f)] float _edgeOnThresholdRelax = 0.14f;
    [SerializeField, Range(0.0f, 0.2f)] float _openPalmHysteresis = 0.08f;
    [SerializeField, Range(0.02f, 0.4f)] float _liftStartDistance = 0.10f;
    [SerializeField, Range(0.08f, 0.6f)] float _liftFullDistance = 0.30f;
    [SerializeField, Range(0.003f, 0.08f)] float _placeDetectDownDistance = 0.018f;
    [SerializeField, Range(0.002f, 0.06f)] float _placeRearmLiftDistance = 0.010f;
    [SerializeField, Range(0.02f, 0.8f)] float _gestureLostGraceSeconds = 0.28f;
    [SerializeField, Range(0.02f, 1.0f)] float _placementDecayPerSecond = 0.35f;
    [SerializeField, Range(0.05f, 0.6f)] float _centerSmoothing = 0.20f;

    [SerializeField] bool _showCropPreview = false;
    [SerializeField] bool _drawLandmarks = true;
    [SerializeField] bool _invertY = false;

    #endregion

    #region Private members

    const int DualHandCount = 2;
    static readonly int[] TipIndices = { 4, 8, 12, 16, 20 };
    static readonly int[] PipIndices = { 3, 6, 10, 14, 18 };

    HandPipeline _pipeline;
    (Material keys, Material region) _material;

    Material _seedMaterial;
    Material _sproutMaterial;
    Material _bloomMaterial;
    Material _groundMaterial;
    Material _grassMaterial;

    GameObject _seedObject;
    GameObject _sproutObject;
    GameObject _bloomObject;
    GameObject _groundObject;
    GameObject _grassObject;

    Camera _mainCamera;

    enum RitualState
    {
        WaitingPlace,
        Placing,
        PlaceAnimating,
        WaitingGrow,
        Growing,
        Bloomed
    }

    struct HandSignal
    {
        public bool tracked;
        public Vector2 center;
        public float speed;
        public bool openPalm;
    }

    RitualState _state;
    float _placeTimer;
    float _growth;
    const float SeedFixedX = 0f;
    readonly float _seedGroundY = -0.33f;
    float _referenceYLeft;
    float _referenceYRight;
    float _placingMaxDrop;
    float _placeLostTimer;
    float _placeAnimTimer;
    int _placeDetectedCount;
    bool _placeNeedRearm;

    HandSignal[] _hands;
    bool[] _hasPrevCenter;
    Vector2[] _prevCenter;
    bool[] _hasSmoothedCenter;
    Vector2[] _smoothedCenter;
    float[] _smoothedSpeed;
    float[] _opennessRatio;
    int[] _extendedCount;
    bool[] _openPalmLatch;

    string _status = "";

    #endregion

    #region MonoBehaviour implementation

    void Awake()
      => EnsureRuntimeArrays();

    void Start()
    {
        _mainCamera = Camera.main;
        EnsureRuntimeArrays();

        _pipeline = new HandPipeline(_resources);
        _material = (new Material(_keyPointShader), new Material(_handRegionShader));

        _material.keys.SetBuffer("_KeyPoints", _pipeline.KeyPointBuffer);
        _material.region.SetBuffer("_Image", _pipeline.HandRegionCropBuffer);

        if (_cropUI != null)
        {
            _cropUI.material = _material.region;
            _cropUI.gameObject.SetActive(_showCropPreview);
        }

        if (_mainUI != null)
            _mainUI.color = new Color(1, 1, 1, 0.78f);

        if (_seedTexture == null && _autoLoadSeedFromResources)
            _seedTexture = Resources.Load<Texture2D>(_seedResourceName);

        if (_soilTexture == null && _autoLoadSoilFromResources)
            _soilTexture = TryLoadResourceTexture("Ground091_2K_Color", "Ground091_1K_Color", "Ground091_Color", "Ground091");
        if (_grassTexture == null && _autoLoadGrassFromResources)
            _grassTexture = TryLoadResourceTexture(_grassResourceName, "GrassPatch");

        _state = RitualState.WaitingPlace;
        _status = "請伸出雙手，準備開始儀式。";

        CreateRitualObjects();
    }

    void OnDestroy()
    {
        _pipeline?.Dispose();

        if (_material.keys != null) Destroy(_material.keys);
        if (_material.region != null) Destroy(_material.region);

        if (_seedMaterial != null) Destroy(_seedMaterial);
        if (_sproutMaterial != null) Destroy(_sproutMaterial);
        if (_bloomMaterial != null) Destroy(_bloomMaterial);
        if (_groundMaterial != null) Destroy(_groundMaterial);
        if (_grassMaterial != null) Destroy(_grassMaterial);

        if (_seedObject != null) Destroy(_seedObject);
        if (_sproutObject != null) Destroy(_sproutObject);
        if (_bloomObject != null) Destroy(_bloomObject);
        if (_groundObject != null) Destroy(_groundObject);
        if (_grassObject != null) Destroy(_grassObject);
    }

    void LateUpdate()
    {
        if (_pipeline == null || _source == null || _source.Texture == null) return;

        _pipeline.ProcessImage(_source.Texture);

        if (_mainUI != null) _mainUI.texture = _source.Texture;
        if (_cropUI != null) _cropUI.texture = _source.Texture;

        for (var i = 0; i < DualHandCount; i++)
            _hands[i] = EvaluateHand(i);

        UpdateStateMachine(_hands[0], _hands[1]);
        UpdateRitualVisuals();
    }

    void OnRenderObject()
    {
        if (!_drawLandmarks || _material.keys == null || _pipeline == null) return;

        for (var hand = 0; hand < DualHandCount; hand++)
        {
            if (!_pipeline.IsHandTracked(hand)) continue;

            _material.keys.SetInt("_KeyPointOffset", hand * HandPipeline.KeyPointCount);

            _material.keys.SetPass(0);
            Graphics.DrawProceduralNow(MeshTopology.Triangles, 96, 21);

            _material.keys.SetPass(1);
            Graphics.DrawProceduralNow(MeshTopology.Lines, 2, 4 * 5 + 1);
        }
    }

    void OnGUI()
    {
        GUI.Box(new Rect(20, 20, 1040, 132), "");

        var titleStyle = new GUIStyle(GUI.skin.label)
        {
            fontSize = 24,
            fontStyle = FontStyle.Bold,
            normal = { textColor = Color.white }
        };

        var hintStyle = new GUIStyle(GUI.skin.label)
        {
            fontSize = 16,
            normal = { textColor = new Color(0.85f, 0.95f, 0.95f, 1) }
        };

        GUI.Label
        (
            new Rect(35, 34, 990, 34),
            $"終章：安放與綻放（雙手） | {_status}",
            titleStyle
        );

        var trackedCount = _pipeline != null ? _pipeline.TrackedHandCount : 0;

        GUI.Label
        (
            new Rect(35, 72, 990, 28),
            $"追蹤: {trackedCount}/2 | 左手 開掌比: {_opennessRatio[0]:F2} 伸指: {_extendedCount[0]}/5 速度: {_smoothedSpeed[0]:F2} 開掌: {(_openPalmLatch[0] ? "是" : "否")}",
            hintStyle
        );

        GUI.Label
        (
            new Rect(35, 98, 990, 28),
            $"追蹤: {trackedCount}/2 | 右手 開掌比: {_opennessRatio[1]:F2} 伸指: {_extendedCount[1]}/5 速度: {_smoothedSpeed[1]:F2} 開掌: {(_openPalmLatch[1] ? "是" : "否")} | 下移次數: {_placeDetectedCount}/{Mathf.Max(1, _placeRequiredDetections)}（按 R 重置）",
            hintStyle
        );
    }

    #endregion

    #region Ritual logic

    HandSignal EvaluateHand(int handIndex)
    {
        if (handIndex < 0 || handIndex >= DualHandCount) return default;

        var pipelineTracked = _pipeline.IsHandTracked(handIndex);

        if (!pipelineTracked)
        {
            _hasPrevCenter[handIndex] = false;
            _hasSmoothedCenter[handIndex] = false;
            _smoothedSpeed[handIndex] = 0;
            _opennessRatio[handIndex] = 0;
            _extendedCount[handIndex] = 0;
            _openPalmLatch[handIndex] = false;
            return default;
        }

        Vector3 Get3D(int index)
        {
            var p = _pipeline.GetKeyPoint(handIndex, index);
            if (_invertY) p.y = -p.y;
            return p;
        }

        var wrist = Get3D(0);
        var indexMcp = Get3D(5);
        var middleMcp = Get3D(9);
        var ringMcp = Get3D(13);
        var pinkyMcp = Get3D(17);

        var center3 = (wrist + indexMcp + middleMcp + ringMcp + pinkyMcp) / 5.0f;
        var center = new Vector2(center3.x, center3.y);

        var palmSpan = Vector3.Distance(indexMcp, pinkyMcp);
        var palmLength = Vector3.Distance(wrist, middleMcp);
        var tracked = palmSpan > 0.035f && palmLength > 0.035f;

        if (!tracked)
        {
            _hasPrevCenter[handIndex] = false;
            _hasSmoothedCenter[handIndex] = false;
            _smoothedSpeed[handIndex] = 0;
            _opennessRatio[handIndex] = 0;
            _extendedCount[handIndex] = 0;
            _openPalmLatch[handIndex] = false;
            return default;
        }

        if (_hasSmoothedCenter[handIndex])
            center = Vector2.Lerp(_smoothedCenter[handIndex], center, Mathf.Clamp01(_centerSmoothing));

        _smoothedCenter[handIndex] = center;
        _hasSmoothedCenter[handIndex] = true;

        if (_hasPrevCenter[handIndex])
        {
            var speed = Vector2.Distance(center, _prevCenter[handIndex]) / Mathf.Max(Time.deltaTime, 0.0001f);
            _smoothedSpeed[handIndex] = Mathf.Lerp(_smoothedSpeed[handIndex], speed, 0.25f);
        }
        else
        {
            _smoothedSpeed[handIndex] = 0;
        }

        _prevCenter[handIndex] = center;
        _hasPrevCenter[handIndex] = true;

        var tipAvg = 0.0f;
        var pipAvg = 0.0f;
        var extended = 0;

        for (var i = 0; i < TipIndices.Length; i++)
        {
            var tip = Get3D(TipIndices[i]);
            var pip = Get3D(PipIndices[i]);

            var tipDist = Vector3.Distance(tip, wrist);
            var pipDist = Vector3.Distance(pip, wrist);

            tipAvg += tipDist;
            pipAvg += pipDist;

            if (tipDist > pipDist * 1.07f) extended++;
        }

        tipAvg /= TipIndices.Length;
        pipAvg /= TipIndices.Length;

        _extendedCount[handIndex] = extended;
        _opennessRatio[handIndex] = tipAvg / Mathf.Max(pipAvg, 0.001f);

        // 水平手勢抗性：手掌越側向，門檻越放寬
        var palmNormal = Vector3.Cross(indexMcp - wrist, pinkyMcp - wrist);
        var palmNormalN = palmNormal.sqrMagnitude > 1e-6f ? palmNormal.normalized : Vector3.forward;
        var edgeOn = Mathf.Clamp01(1.0f - Mathf.Abs(palmNormalN.z));
        var adaptiveThreshold = _openPalmRatioThreshold - _edgeOnThresholdRelax * edgeOn;

        // 動作過程抗抖：手速變快時，稍微放寬開掌門檻，避免一下就掉追蹤
        var speedRelax = Mathf.Clamp01(_smoothedSpeed[handIndex] / Mathf.Max(_maxHandSpeedForPlace, 0.0001f));
        adaptiveThreshold -= 0.10f * speedRelax;

        // 防抖：進入/退出不同門檻
        var openEnter = _opennessRatio[handIndex] >= adaptiveThreshold && extended >= 3;
        var openExit = _opennessRatio[handIndex] >= (adaptiveThreshold - _openPalmHysteresis) && extended >= 2;

        _openPalmLatch[handIndex] = _openPalmLatch[handIndex] ? openExit : openEnter;

        return new HandSignal
        {
            tracked = true,
            center = center,
            speed = _smoothedSpeed[handIndex],
            openPalm = _openPalmLatch[handIndex]
        };
    }

    bool IsPlacementReady(HandSignal hand)
      => hand.tracked && hand.openPalm;

    void UpdateStateMachine(HandSignal left, HandSignal right)
    {
        if (Input.GetKeyDown(KeyCode.R))
        {
            ResetRitual();
            return;
        }

        var bothPlacement = IsPlacementReady(left) && IsPlacementReady(right);

        switch (_state)
        {
            case RitualState.WaitingPlace:
                _status = $"雙手維持開掌，向下偵測 {Mathf.Max(1, _placeRequiredDetections)} 次後安放種子。";
                _placeTimer = Mathf.Max(0, _placeTimer - Time.deltaTime * 2);
                if (bothPlacement)
                {
                    _state = RitualState.Placing;
                    _referenceYLeft = left.center.y;
                    _referenceYRight = right.center.y;
                    _placingMaxDrop = 0;
                    _placeLostTimer = 0;
                    _placeAnimTimer = 0;
                    _placeNeedRearm = false;
                    _placeTimer = 0;
                }
                break;

            case RitualState.Placing:
                _status = $"安放手勢中…請向下 {Mathf.Max(0, _placeRequiredDetections - _placeDetectedCount)} 次。";
                if (bothPlacement)
                {
                    if (_placeLostTimer > 0)
                    {
                        _referenceYLeft = left.center.y;
                        _referenceYRight = right.center.y;
                        _placingMaxDrop = 0;
                        _placeNeedRearm = false;
                    }

                    _placeLostTimer = 0;

                    if (!_placeNeedRearm)
                    {
                        var dropLeft = _referenceYLeft - left.center.y;
                        var dropRight = _referenceYRight - right.center.y;
                        var drop = Mathf.Max(dropLeft, dropRight);

                        _placingMaxDrop = Mathf.Max(_placingMaxDrop, drop);

                        if (_placingMaxDrop >= _placeDetectDownDistance)
                        {
                            _placeDetectedCount++;
                            _placeNeedRearm = true;
                            _placingMaxDrop = 0;
                            _referenceYLeft = left.center.y;
                            _referenceYRight = right.center.y;

                            if (_placeDetectedCount >= Mathf.Max(1, _placeRequiredDetections))
                            {
                                _state = RitualState.PlaceAnimating;
                                _placeAnimTimer = 0;
                                _status = "偵測完成，種子安放中…";
                            }
                        }
                    }
                    else
                    {
                        var liftLeft = left.center.y - _referenceYLeft;
                        var liftRight = right.center.y - _referenceYRight;
                        var lift = Mathf.Max(liftLeft, liftRight);

                        if (lift >= _placeRearmLiftDistance)
                        {
                            _placeNeedRearm = false;
                            _referenceYLeft = left.center.y;
                            _referenceYRight = right.center.y;
                        }
                    }
                }
                else
                {
                    _placeLostTimer += Time.deltaTime;
                    _placeTimer = Mathf.Max(0, _placeTimer - Time.deltaTime * _placementDecayPerSecond);

                    if (_placeLostTimer >= _gestureLostGraceSeconds)
                        _status = "偵測中斷（進度保留），雙手回到畫面可繼續。";
                    else
                        _status = "偵測短暫中斷（進度保留），雙手回到畫面可繼續。";
                }
                break;

            case RitualState.PlaceAnimating:
                _status = "安放動畫中…種子正在慢慢落地。";
                _placeAnimTimer += Time.deltaTime;
                var placeAnimDuration = Mathf.Max(_placeDropAnimationSeconds, 0.01f);
                var placeAnimT = Mathf.Clamp01(_placeAnimTimer / placeAnimDuration);
                _placeTimer = placeAnimT * _placeHoldSeconds;

                if (placeAnimT >= 0.995f)
                {
                    _placeTimer = _placeHoldSeconds;
                    _state = RitualState.WaitingGrow;

                    if (left.tracked) _referenceYLeft = left.center.y;
                    if (right.tracked) _referenceYRight = right.center.y;

                    _status = "安放完成，請雙手向上托舉滋養種子。";
                }
                break;

            case RitualState.WaitingGrow:
                _status = "雙手向上托舉，開始發芽。";
                if (left.tracked && right.tracked)
                {
                    var liftLeft = left.center.y - _referenceYLeft;
                    var liftRight = right.center.y - _referenceYRight;
                    var lift = Mathf.Min(liftLeft, liftRight);
                    var target = Mathf.InverseLerp(_liftStartDistance, _liftFullDistance, lift);
                    _growth = Mathf.Max(_growth, target);

                    if (_growth > 0.01f) _state = RitualState.Growing;
                }
                break;

            case RitualState.Growing:
                _status = "生長中…請持續雙手向上托舉。";
                if (left.tracked && right.tracked)
                {
                    var liftLeft = left.center.y - _referenceYLeft;
                    var liftRight = right.center.y - _referenceYRight;
                    var lift = Mathf.Min(liftLeft, liftRight);
                    var target = Mathf.InverseLerp(_liftStartDistance, _liftFullDistance, lift);
                    _growth = Mathf.Clamp01(Mathf.Max(_growth, target));
                }

                if (_growth >= 0.995f)
                {
                    _growth = 1;
                    _state = RitualState.Bloomed;
                    _status = "綻放完成！";
                }
                break;

            case RitualState.Bloomed:
                _status = "已綻放！按 R 可重新開始。";
                break;
        }
    }

    void ResetRitual()
    {
        _state = RitualState.WaitingPlace;
        _placeTimer = 0;
        _growth = 0;
        _referenceYLeft = 0;
        _referenceYRight = 0;
        _placingMaxDrop = 0;
        _placeLostTimer = 0;
        _placeAnimTimer = 0;
        _placeDetectedCount = 0;
        _placeNeedRearm = false;

        for (var i = 0; i < _openPalmLatch.Length; i++)
        {
            _openPalmLatch[i] = false;
            _hasPrevCenter[i] = false;
            _hasSmoothedCenter[i] = false;
            _smoothedSpeed[i] = 0;
        }

        _status = "已重置，請再次伸出雙手。";
    }

    #endregion

    #region Visual objects

    void EnsureRuntimeArrays()
    {
        if (_hands == null || _hands.Length != DualHandCount)
            _hands = new HandSignal[DualHandCount];

        if (_hasPrevCenter == null || _hasPrevCenter.Length != DualHandCount)
            _hasPrevCenter = new bool[DualHandCount];

        if (_prevCenter == null || _prevCenter.Length != DualHandCount)
            _prevCenter = new Vector2[DualHandCount];

        if (_hasSmoothedCenter == null || _hasSmoothedCenter.Length != DualHandCount)
            _hasSmoothedCenter = new bool[DualHandCount];

        if (_smoothedCenter == null || _smoothedCenter.Length != DualHandCount)
            _smoothedCenter = new Vector2[DualHandCount];

        if (_smoothedSpeed == null || _smoothedSpeed.Length != DualHandCount)
            _smoothedSpeed = new float[DualHandCount];

        if (_opennessRatio == null || _opennessRatio.Length != DualHandCount)
            _opennessRatio = new float[DualHandCount];

        if (_extendedCount == null || _extendedCount.Length != DualHandCount)
            _extendedCount = new int[DualHandCount];

        if (_openPalmLatch == null || _openPalmLatch.Length != DualHandCount)
            _openPalmLatch = new bool[DualHandCount];
    }

    Texture2D TryLoadResourceTexture(params string[] names)
    {
        for (var i = 0; i < names.Length; i++)
        {
            var tex = Resources.Load<Texture2D>(names[i]);
            if (tex != null) return tex;
        }

        return null;
    }

    void ApplyMainTexture(Material mat, Texture tex)
    {
        if (mat == null || tex == null) return;

        if (mat.HasProperty("_BaseMap")) mat.SetTexture("_BaseMap", tex);
        if (mat.HasProperty("_MainTex")) mat.SetTexture("_MainTex", tex);
    }

    void ApplyMainColor(Material mat, Color color)
    {
        if (mat == null) return;

        if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", color);
        if (mat.HasProperty("_Color")) mat.SetColor("_Color", color);
    }

    void SetMainTextureTiling(Material mat, Vector2 tiling)
    {
        if (mat == null) return;

        if (mat.HasProperty("_BaseMap")) mat.SetTextureScale("_BaseMap", tiling);
        if (mat.HasProperty("_MainTex")) mat.SetTextureScale("_MainTex", tiling);
    }

    Material CreateLitMaterial(Color color)
    {
        var shader = Shader.Find("Universal Render Pipeline/Lit");
        if (shader == null) shader = Shader.Find("Standard");

        var mat = new Material(shader);
        ApplyMainColor(mat, color);
        return mat;
    }

    Material CreateGroundMaterial()
    {
        var baseColor = new Color(0.30f, 0.20f, 0.12f, 1);
        var shader = Shader.Find("Universal Render Pipeline/Unlit");
        if (shader == null) shader = Shader.Find("Unlit/Texture");
        if (shader == null) shader = Shader.Find("Unlit/Color");
        if (shader == null) shader = Shader.Find("Standard");

        if (_soilTexture != null)
        {
            var mat = new Material(shader);
            ApplyMainColor(mat, baseColor);
            ApplyMainTexture(mat, _soilTexture);
            SetMainTextureTiling(mat, new Vector2(_soilTiling, _soilTiling));
            return mat;
        }
        else
        {
            var mat = new Material(shader);
            ApplyMainColor(mat, baseColor);
            return mat;
        }
    }

    Material CreateGrassMaterial()
    {
        var shader = Shader.Find("Unlit/Transparent");
        if (shader == null) shader = Shader.Find("Universal Render Pipeline/Unlit");
        if (shader == null) shader = Shader.Find("Standard");

        var mat = new Material(shader);
        ApplyMainColor(mat, _grassTint);

        if (_grassTexture != null)
            ApplyMainTexture(mat, _grassTexture);
        else
            Debug.LogWarning("HandVisualizer: 沒有載入到草地貼圖，將使用純色草地。");

        return mat;
    }

    Material CreateSeedMaterial()
    {
        var shader = Shader.Find("Unlit/Transparent");
        if (shader == null) shader = Shader.Find("Universal Render Pipeline/Unlit");
        if (shader == null) shader = Shader.Find("Standard");

        var mat = new Material(shader);
        ApplyMainColor(mat, _seedTint);
        ApplyMainTexture(mat, _seedTexture);

        return mat;
    }

    void CreateRitualObjects()
    {
        _groundMaterial = CreateGroundMaterial();
        _grassMaterial = CreateGrassMaterial();
        _sproutMaterial = CreateLitMaterial(new Color(0.52f, 0.90f, 0.50f, 1));
        _bloomMaterial = CreateLitMaterial(new Color(0.98f, 0.68f, 0.88f, 1));

        _seedMaterial = CreateSeedMaterial();

        _groundObject = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        _groundObject.name = "RitualGround";
        _groundObject.transform.position = new Vector3(0, _seedGroundY - 0.26f, 2.62f);
        _groundObject.transform.localScale = new Vector3(0.92f, 0.17f, 0.52f);
        _groundObject.GetComponent<MeshRenderer>().material = _groundMaterial;

        _grassObject = GameObject.CreatePrimitive(PrimitiveType.Quad);
        _grassObject.name = "RitualGrass";
        _grassObject.transform.position = new Vector3(0, _seedGroundY - 0.17f, 2.50f);
        _grassObject.transform.localScale = new Vector3(0.78f, 0.18f, 1);
        _grassObject.GetComponent<MeshRenderer>().material = _grassMaterial;

        _seedObject = GameObject.CreatePrimitive(PrimitiveType.Quad);
        _seedObject.name = "Seed2D";
        _seedObject.GetComponent<MeshRenderer>().material = _seedMaterial;
        _seedObject.transform.localScale = new Vector3(_seedSize.x, _seedSize.y, 1);

        _sproutObject = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        _sproutObject.name = "Sprout";
        _sproutObject.GetComponent<MeshRenderer>().material = _sproutMaterial;

        _bloomObject = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        _bloomObject.name = "Bloom";
        _bloomObject.GetComponent<MeshRenderer>().material = _bloomMaterial;
        _bloomObject.transform.localScale = Vector3.one * 0.04f;
    }

    void UpdateRitualVisuals()
    {
        var dropT = Mathf.Clamp01(_placeTimer / Mathf.Max(_placeHoldSeconds, 0.0001f));
        var seedY = Mathf.Lerp(0.18f, _seedGroundY, dropT);

        if (_state == RitualState.WaitingGrow ||
            _state == RitualState.Growing ||
            _state == RitualState.Bloomed)
        {
            seedY = _seedGroundY;
        }

        var seedPos = new Vector3(SeedFixedX, seedY, 2.4f);

        if (_seedObject != null)
        {
            _seedObject.transform.position = seedPos;
            _seedObject.transform.localScale = new Vector3(_seedSize.x, _seedSize.y, 1);

            if (_mainCamera == null) _mainCamera = Camera.main;
            if (_mainCamera != null)
                _seedObject.transform.rotation = Quaternion.LookRotation(-_mainCamera.transform.forward, _mainCamera.transform.up);
        }

        if (_grassObject != null)
        {
            if (_mainCamera == null) _mainCamera = Camera.main;
            if (_mainCamera != null)
                _grassObject.transform.rotation = Quaternion.LookRotation(-_mainCamera.transform.forward, _mainCamera.transform.up);
        }

        var stemHeight = Mathf.Lerp(0.001f, 0.28f, _growth);
        var stemScale = new Vector3(0.02f, stemHeight / 2, 0.02f);
        var stemPos = new Vector3(seedPos.x, _seedGroundY + stemHeight / 2 + 0.02f, 2.4f);

        if (_sproutObject != null)
        {
            _sproutObject.transform.position = stemPos;
            _sproutObject.transform.localScale = stemScale;
        }

        var bloomScale = Mathf.Lerp(0.02f, 0.08f, _growth);
        if (_bloomObject != null)
        {
            _bloomObject.transform.localScale = Vector3.one * bloomScale;
            _bloomObject.transform.position = new Vector3(seedPos.x, _seedGroundY + stemHeight + 0.06f, 2.4f);
        }
    }

    #endregion
}
