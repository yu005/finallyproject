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
    [Header("Final Chapter Ritual (Dual Hand)")]
    [SerializeField, Range(0.3f, 2.5f)] float _placeHoldSeconds = 1.0f;
    [SerializeField, Range(0.05f, 2.0f)] float _maxHandSpeedForPlace = 0.45f;
    [SerializeField, Range(0.0f, 0.4f)] float _placementHeightLimit = 0.10f;
    [SerializeField, Range(1.0f, 1.8f)] float _openPalmRatioThreshold = 1.22f;
    [SerializeField, Range(0.02f, 0.4f)] float _liftStartDistance = 0.10f;
    [SerializeField, Range(0.08f, 0.6f)] float _liftFullDistance = 0.30f;
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
    GameObject _seedObject;
    GameObject _sproutObject;
    GameObject _bloomObject;
    GameObject _groundObject;

    enum RitualState
    {
        WaitingPlace,
        Placing,
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
    float _seedX;
    float _seedGroundY = -0.33f;
    float _referenceYLeft;
    float _referenceYRight;

    HandSignal[] _hands;
    bool[] _hasPrevCenter;
    Vector2[] _prevCenter;
    float[] _smoothedSpeed;
    float[] _opennessRatio;
    int[] _extendedCount;

    string _status = "";

    #endregion

    #region MonoBehaviour implementation

    void Awake()
      => EnsureRuntimeArrays();

    void Start()
    {
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

        _state = RitualState.WaitingPlace;
        _status = "Show both hands to begin the ritual.";

        CreateRitualObjects();
    }

    void OnDestroy()
    {
        _pipeline?.Dispose();
        if (_material.keys != null) Destroy(_material.keys);
        if (_material.region != null) Destroy(_material.region);

        Destroy(_seedMaterial);
        Destroy(_sproutMaterial);
        Destroy(_bloomMaterial);
        Destroy(_groundMaterial);

        if (_seedObject) Destroy(_seedObject);
        if (_sproutObject) Destroy(_sproutObject);
        if (_bloomObject) Destroy(_bloomObject);
        if (_groundObject) Destroy(_groundObject);
    }

    void LateUpdate()
    {
        if (_pipeline == null || _source == null || _source.Texture == null) return;
        EnsureRuntimeArrays();

        _pipeline.ProcessImage(_source.Texture);

        if (_mainUI != null) _mainUI.texture = _source.Texture;
        if (_cropUI != null) _cropUI.texture = _source.Texture;

        for (var i = 0; i < Mathf.Min(DualHandCount, _hands.Length); i++)
            _hands[i] = EvaluateHand(i);

        var left = _hands.Length > 0 ? _hands[0] : default;
        var right = _hands.Length > 1 ? _hands[1] : default;
        UpdateStateMachine(left, right);
        UpdateRitualVisuals();
    }

    void OnRenderObject()
    {
        if (!_drawLandmarks || _material.keys == null || _pipeline == null) return;

        for (var hand = 0; hand < HandPipeline.MaxHandCount; hand++)
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
        EnsureRuntimeArrays();

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
            $"Final Chapter: Place & Bloom (Dual Hands)  |  {_status}",
            titleStyle
        );

        var trackedCount = _pipeline != null ? _pipeline.TrackedHandCount : 0;
        var leftOpen = _opennessRatio.Length > 0 ? _opennessRatio[0] : 0;
        var leftExt = _extendedCount.Length > 0 ? _extendedCount[0] : 0;
        var leftSpeed = _smoothedSpeed.Length > 0 ? _smoothedSpeed[0] : 0;
        var rightOpen = _opennessRatio.Length > 1 ? _opennessRatio[1] : 0;
        var rightExt = _extendedCount.Length > 1 ? _extendedCount[1] : 0;
        var rightSpeed = _smoothedSpeed.Length > 1 ? _smoothedSpeed[1] : 0;

        GUI.Label
        (
            new Rect(35, 72, 990, 28),
            $"Tracked: {trackedCount}/2 | Left OpenRatio: {leftOpen:F2} Fingers: {leftExt}/5 Speed: {leftSpeed:F2}",
            hintStyle
        );

        GUI.Label
        (
            new Rect(35, 98, 990, 28),
            $"Tracked: {trackedCount}/2 | Right OpenRatio: {rightOpen:F2} Fingers: {rightExt}/5 Speed: {rightSpeed:F2} (Press R to reset)",
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
            _smoothedSpeed[handIndex] = 0;
            _opennessRatio[handIndex] = 0;
            _extendedCount[handIndex] = 0;
            return default;
        }

        Vector2 Get2D(int index)
        {
            var p = _pipeline.GetKeyPoint(handIndex, index);
            if (_invertY) p.y = -p.y;
            return new Vector2(p.x, p.y);
        }

        var wrist = Get2D(0);
        var indexMcp = Get2D(5);
        var middleMcp = Get2D(9);
        var ringMcp = Get2D(13);
        var pinkyMcp = Get2D(17);

        var center = (wrist + indexMcp + middleMcp + ringMcp + pinkyMcp) / 5.0f;

        var palmSpan = Vector2.Distance(indexMcp, pinkyMcp);
        var palmLength = Vector2.Distance(wrist, middleMcp);
        var tracked = palmSpan > 0.035f && palmLength > 0.035f;

        if (!tracked)
        {
            _hasPrevCenter[handIndex] = false;
            _smoothedSpeed[handIndex] = 0;
            _opennessRatio[handIndex] = 0;
            _extendedCount[handIndex] = 0;
            return default;
        }

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
            var tip = Get2D(TipIndices[i]);
            var pip = Get2D(PipIndices[i]);

            var tipDist = Vector2.Distance(tip, wrist);
            var pipDist = Vector2.Distance(pip, wrist);

            tipAvg += tipDist;
            pipAvg += pipDist;

            if (tipDist > pipDist * 1.07f) extended++;
        }

        tipAvg /= TipIndices.Length;
        pipAvg /= TipIndices.Length;

        _extendedCount[handIndex] = extended;
        _opennessRatio[handIndex] = tipAvg / Mathf.Max(pipAvg, 0.001f);

        var openPalm = _opennessRatio[handIndex] >= _openPalmRatioThreshold && extended >= 4;

        return new HandSignal
        {
            tracked = true,
            center = center,
            speed = _smoothedSpeed[handIndex],
            openPalm = openPalm
        };
    }

    void EnsureRuntimeArrays()
    {
        if (_hands == null || _hands.Length != DualHandCount)
            _hands = new HandSignal[DualHandCount];

        if (_hasPrevCenter == null || _hasPrevCenter.Length != DualHandCount)
            _hasPrevCenter = new bool[DualHandCount];

        if (_prevCenter == null || _prevCenter.Length != DualHandCount)
            _prevCenter = new Vector2[DualHandCount];

        if (_smoothedSpeed == null || _smoothedSpeed.Length != DualHandCount)
            _smoothedSpeed = new float[DualHandCount];

        if (_opennessRatio == null || _opennessRatio.Length != DualHandCount)
            _opennessRatio = new float[DualHandCount];

        if (_extendedCount == null || _extendedCount.Length != DualHandCount)
            _extendedCount = new int[DualHandCount];
    }

    bool IsPlacementPose(HandSignal hand)
      => hand.tracked &&
         hand.openPalm &&
         hand.speed <= _maxHandSpeedForPlace &&
         hand.center.y <= _placementHeightLimit;

    void UpdateStateMachine(HandSignal left, HandSignal right)
    {
        if (Input.GetKeyDown(KeyCode.R))
        {
            ResetRitual();
            return;
        }

        var bothPlacement = IsPlacementPose(left) && IsPlacementPose(right);

        switch (_state)
        {
            case RitualState.WaitingPlace:
                _status = "Hold both palms steady (lower area) to place the seed.";
                _placeTimer = Mathf.Max(0, _placeTimer - Time.deltaTime * 2);
                if (bothPlacement)
                {
                    _state = RitualState.Placing;
                    _seedX = (left.center.x + right.center.x) * 0.5f;
                }
                break;

            case RitualState.Placing:
                _status = "Placing... keep both palms steady.";
                if (bothPlacement)
                {
                    _placeTimer += Time.deltaTime;
                    _seedX = Mathf.Lerp(_seedX, (left.center.x + right.center.x) * 0.5f, 0.15f);

                    if (_placeTimer >= _placeHoldSeconds)
                    {
                        _placeTimer = _placeHoldSeconds;
                        _state = RitualState.WaitingGrow;
                        _referenceYLeft = left.center.y;
                        _referenceYRight = right.center.y;
                        _status = "Seed placed. Lift both hands upward to nourish growth.";
                    }
                }
                else
                {
                    _state = RitualState.WaitingPlace;
                }
                break;

            case RitualState.WaitingGrow:
                _status = "Scoop/lift both hands upward to start sprouting.";
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
                _status = "Growing... keep lifting both hands upward.";
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
                    _status = "Bloom complete!";
                }
                break;

            case RitualState.Bloomed:
                _status = "Bloomed! Press R to restart.";
                break;
        }
    }

    void ResetRitual()
    {
        _state = RitualState.WaitingPlace;
        _placeTimer = 0;
        _growth = 0;
        _seedX = 0;
        _referenceYLeft = 0;
        _referenceYRight = 0;
        _status = "Reset complete. Show both hands to start again.";
    }

    void CreateRitualObjects()
    {
        _groundMaterial = CreateColorMaterial(new Color(0.18f, 0.28f, 0.22f, 1));
        _seedMaterial = CreateColorMaterial(new Color(0.90f, 0.86f, 0.55f, 1));
        _sproutMaterial = CreateColorMaterial(new Color(0.52f, 0.90f, 0.50f, 1));
        _bloomMaterial = CreateColorMaterial(new Color(0.98f, 0.68f, 0.88f, 1));

        _groundObject = GameObject.CreatePrimitive(PrimitiveType.Quad);
        _groundObject.name = "RitualGround";
        _groundObject.transform.position = new Vector3(0, _seedGroundY - 0.12f, 2.5f);
        _groundObject.transform.localScale = new Vector3(1.25f, 0.30f, 1);
        _groundObject.GetComponent<MeshRenderer>().material = _groundMaterial;

        _seedObject = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        _seedObject.name = "Seed";
        _seedObject.GetComponent<MeshRenderer>().material = _seedMaterial;
        _seedObject.transform.localScale = Vector3.one * 0.06f;

        _sproutObject = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        _sproutObject.name = "Sprout";
        _sproutObject.GetComponent<MeshRenderer>().material = _sproutMaterial;

        _bloomObject = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        _bloomObject.name = "Bloom";
        _bloomObject.GetComponent<MeshRenderer>().material = _bloomMaterial;
        _bloomObject.transform.localScale = Vector3.one * 0.04f;
    }

    Material CreateColorMaterial(Color color)
    {
        var shader = Shader.Find("Universal Render Pipeline/Lit");
        if (!shader) shader = Shader.Find("Standard");

        var mat = new Material(shader);
        mat.color = color;
        return mat;
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

        var seedPos = new Vector3(Mathf.Clamp(_seedX, -0.42f, 0.42f), seedY, 2.4f);
        _seedObject.transform.position = seedPos;

        var stemHeight = Mathf.Lerp(0.001f, 0.28f, _growth);
        var stemScale = new Vector3(0.02f, stemHeight / 2, 0.02f);
        var stemPos = new Vector3(seedPos.x, _seedGroundY + stemHeight / 2 + 0.02f, 2.4f);

        _sproutObject.transform.position = stemPos;
        _sproutObject.transform.localScale = stemScale;

        var bloomScale = Mathf.Lerp(0.02f, 0.08f, _growth);
        _bloomObject.transform.localScale = Vector3.one * bloomScale;
        _bloomObject.transform.position = new Vector3(seedPos.x, _seedGroundY + stemHeight + 0.06f, 2.4f);
    }

    #endregion
}
