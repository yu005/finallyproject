using UnityEngine;
using UnityEngine.UI;
using Unity.Mathematics;
using Klak.TestTools;
using MediaPipe.FaceMesh;

public sealed class Visualizer : MonoBehaviour
{
    #region Editable attributes

    [SerializeField] ImageSource _source = null;
    [Space]
    [SerializeField] ResourceSet _resources = null;
    [SerializeField] Shader _shader = null;
    [Space]
    [SerializeField] RawImage _mainUI = null;
    [SerializeField] RawImage _faceUI = null;
    [SerializeField] RawImage _leftEyeUI = null;
    [SerializeField] RawImage _rightEyeUI = null;

    #endregion

    #region Private members

    FacePipeline _pipeline;
    Material _material;

    #endregion

    #region MonoBehaviour implementation

    void Start()
    {
        _pipeline = new FacePipeline(_resources);
        _material = new Material(_shader);
    }

    void OnDestroy()
    {
        _pipeline.Dispose();
        Destroy(_material);
    }

    void LateUpdate()
    {
        // Processing on the face pipeline
        _pipeline.ProcessImage(_source.Texture);

        // UI update
        _mainUI.texture = _source.Texture;
        _faceUI.texture = _pipeline.CroppedFaceTexture;
        _leftEyeUI.texture = _pipeline.CroppedLeftEyeTexture;
        _rightEyeUI.texture = _pipeline.CroppedRightEyeTexture;

        // ==========================================
        // --- 專業版：加入「臉部比例尺」防鏡頭遠近干擾 ---
        // ==========================================

        Vector4[] faceVertices = new Vector4[468];
        _pipeline.RefinedFaceVertexBuffer.GetData(faceVertices);

        // 1. 新增：抓取左右臉頰最外側，作為「臉部比例尺」
        Vector3 leftCheek = faceVertices[234];
        Vector3 rightCheek = faceVertices[454];
        float faceWidth = Vector3.Distance(leftCheek, rightCheek);

        // 2. 抓取關鍵特徵點
        Vector3 leftMouth = faceVertices[61];
        Vector3 rightMouth = faceVertices[291];
        Vector3 leftInnerEyebrow = faceVertices[107];
        Vector3 rightInnerEyebrow = faceVertices[336];
        Vector3 chin = faceVertices[152];

        // 3. 核心升級：計算距離後，全部「除以臉寬 (faceWidth)」！這稱為正規化 (Normalization)
        float currentMouthWidth = Vector3.Distance(leftMouth, rightMouth) / faceWidth;
        float currentBrowDist = Vector3.Distance(leftInnerEyebrow, rightInnerEyebrow) / faceWidth;
        float currentMouthDrop = ((Vector3.Distance(leftMouth, chin) + Vector3.Distance(rightMouth, chin)) / 2f) / faceWidth;

        // --- (A) 動作 1：按下【空白鍵】，記錄平靜基準值 ---
        if (UnityEngine.Input.GetKeyDown(UnityEngine.KeyCode.Space))
        {
            UnityEngine.PlayerPrefs.SetFloat("NeutralMouthWidth", currentMouthWidth);
            UnityEngine.PlayerPrefs.SetFloat("NeutralBrowDist", currentBrowDist);
            UnityEngine.PlayerPrefs.SetFloat("NeutralMouthDrop", currentMouthDrop);
            UnityEngine.Debug.Log($"✅ [校正完成] 已記錄防干擾的平靜臉孔！");
        }

        // --- (B) 動作 2：按下【Enter 鍵】，執行情緒判斷 ---
        if (UnityEngine.Input.GetKeyDown(UnityEngine.KeyCode.Return))
        {
            float neutralMouthWidth = UnityEngine.PlayerPrefs.GetFloat("NeutralMouthWidth", 0f);
            float neutralBrowDist = UnityEngine.PlayerPrefs.GetFloat("NeutralBrowDist", 0f);
            float neutralMouthDrop = UnityEngine.PlayerPrefs.GetFloat("NeutralMouthDrop", 0f);

            UnityEngine.Debug.Log("------------------------------------");

            if (neutralMouthWidth > 0f)
            {
                // 計算變化比例
                float mouthWidthRatio = currentMouthWidth / neutralMouthWidth;
                float browDistRatio = currentBrowDist / neutralBrowDist;
                float mouthDropRatio = currentMouthDrop / neutralMouthDrop;

                UnityEngine.Debug.Log($"📸 肌肉變化 -> 嘴角寬: {mouthWidthRatio:F2}x | 眉間: {browDistRatio:F2}x | 嘴角下垂: {mouthDropRatio:F2}x");

                // 1. 快樂 (Happiness)
                if (mouthWidthRatio > 1.15f)
                {
                    UnityEngine.Debug.Log("🌱 偵測結果：「快樂」！");
                }
                // 2. 憤怒 (Anger)：眉心往中間擠 (小於 0.95倍)
                // Ekman 指出憤怒時眉間會出現垂直皺紋
                else if (browDistRatio < 0.95f)
                {
                    UnityEngine.Debug.Log("🔥 偵測結果：「憤怒」！ (眉間緊皺)");
                }
                // 3. 不開心 / 悲傷 (Sadness)：嘴角往下掉 (大於 1.05倍 代表拉長了到下巴的距離，或依據您的測試調整)
                else if (mouthDropRatio < 0.95f)
                {
                    UnityEngine.Debug.Log("💧 偵測結果：「不開心 / 悲傷」！");
                }
                else
                {
                    UnityEngine.Debug.Log("😐 偵測結果：平靜無波紋");
                }
            }
        }
        // ==========================================
    }

    #endregion
}
