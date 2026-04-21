#if UNITY_EDITOR
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

public static class PlantGrowAnimationBuilder
{
    private const string FramesFolder = "Assets/Test/BloomAssets/PlantGrowing/Frames";
    private const string OutputFolder = "Assets/Test/BloomAssets/PlantGrowing/Generated";
    private const string ClipPath = OutputFolder + "/PlantGrow.anim";
    private const string ControllerPath = OutputFolder + "/PlantGrow.controller";
    private const string PrefabPath = OutputFolder + "/PlantGrow.prefab";

    private const float FramesPerSecond = 10f;
    private const float PixelsPerUnit = 24f;
    private const float PreviewScale = 6f;

    [MenuItem("Tools/Bloom/建立 PlantGrowing 動畫資產")]
    public static void BuildPlantGrowAssets()
    {
        if (TryBuildAssets(out var prefab, out var message))
        {
            EditorUtility.DisplayDialog("PlantGrowing", message, "OK");
            Selection.activeObject = prefab;
            EditorGUIUtility.PingObject(prefab);
            return;
        }

        EditorUtility.DisplayDialog("PlantGrowing", message, "OK");
    }

    [MenuItem("Tools/Bloom/建立並放入目前場景")]
    public static void BuildAndSpawnInScene()
    {
        if (!TryBuildAssets(out var prefab, out var message))
        {
            EditorUtility.DisplayDialog("PlantGrowing", message, "OK");
            return;
        }

        var instance = PrefabUtility.InstantiatePrefab(prefab) as GameObject;
        if (instance == null)
        {
            instance = Object.Instantiate(prefab);
        }

        Undo.RegisterCreatedObjectUndo(instance, "放入 PlantGrowing");
        instance.name = "PlantGrow";
        instance.transform.position = Vector3.zero;

        Selection.activeObject = instance;
        EditorGUIUtility.PingObject(instance);
        EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());

        EditorUtility.DisplayDialog("PlantGrowing", "已建立動畫並放入目前場景（位置在 0,0,0）。", "OK");
    }

    private static bool TryBuildAssets(out GameObject prefab, out string message)
    {
        prefab = null;

        if (!AssetDatabase.IsValidFolder(FramesFolder))
        {
            message = $"找不到資料夾：{FramesFolder}";
            return false;
        }

        var absoluteFramesPath = Path.Combine(Directory.GetCurrentDirectory(), FramesFolder.Replace("/", "\\"));
        if (!Directory.Exists(absoluteFramesPath))
        {
            message = $"找不到幀圖資料夾：{absoluteFramesPath}";
            return false;
        }

        var frameAssetPaths = Directory.GetFiles(absoluteFramesPath, "grow_*.png", SearchOption.TopDirectoryOnly)
            .Select(ToAssetPath)
            .OrderBy(p => p)
            .ToList();

        if (frameAssetPaths.Count == 0)
        {
            message = "沒有找到 grow_00.png~grow_17.png。";
            return false;
        }

        EnsureFolder(OutputFolder);

        var sprites = new List<Sprite>(frameAssetPaths.Count);
        foreach (var framePath in frameAssetPaths)
        {
            ConfigureSpriteImport(framePath);

            var sprite = AssetDatabase.LoadAssetAtPath<Sprite>(framePath);
            if (sprite != null)
            {
                sprites.Add(sprite);
            }
        }

        if (sprites.Count == 0)
        {
            message = "幀圖讀取失敗，請先回 Unity 等待素材匯入完成。";
            return false;
        }

        var clip = CreateOrUpdateClip(sprites);
        var controller = CreateOrUpdateController(clip);
        prefab = CreateOrUpdatePrefab(controller, sprites[0]);

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        message =
            "完成！\n" +
            $"- 幀數：{sprites.Count}\n" +
            $"- Animation：{ClipPath}\n" +
            $"- Controller：{ControllerPath}\n" +
            $"- Prefab：{PrefabPath}";
        return true;
    }

    private static AnimationClip CreateOrUpdateClip(IReadOnlyList<Sprite> sprites)
    {
        var clip = AssetDatabase.LoadAssetAtPath<AnimationClip>(ClipPath);
        if (clip == null)
        {
            clip = new AnimationClip();
            AssetDatabase.CreateAsset(clip, ClipPath);
        }

        clip.frameRate = FramesPerSecond;

        var binding = new EditorCurveBinding
        {
            path = string.Empty,
            type = typeof(SpriteRenderer),
            propertyName = "m_Sprite"
        };

        var keyframes = new ObjectReferenceKeyframe[sprites.Count];
        for (var i = 0; i < sprites.Count; i++)
        {
            keyframes[i] = new ObjectReferenceKeyframe
            {
                time = i / FramesPerSecond,
                value = sprites[i]
            };
        }

        AnimationUtility.SetObjectReferenceCurve(clip, binding, keyframes);

        var settings = AnimationUtility.GetAnimationClipSettings(clip);
        settings.loopTime = false;
        AnimationUtility.SetAnimationClipSettings(clip, settings);

        EditorUtility.SetDirty(clip);
        return clip;
    }

    private static AnimatorController CreateOrUpdateController(AnimationClip clip)
    {
        var controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(ControllerPath);
        if (controller == null)
        {
            controller = AnimatorController.CreateAnimatorControllerAtPath(ControllerPath);
        }

        var stateMachine = controller.layers[0].stateMachine;
        var state = stateMachine.states.FirstOrDefault(s => s.state.name == "PlantGrow").state;
        if (state == null)
        {
            state = stateMachine.AddState("PlantGrow");
        }

        state.motion = clip;
        stateMachine.defaultState = state;

        EditorUtility.SetDirty(controller);
        return controller;
    }

    private static GameObject CreateOrUpdatePrefab(AnimatorController controller, Sprite firstSprite)
    {
        var root = new GameObject("PlantGrow");

        var spriteRenderer = root.AddComponent<SpriteRenderer>();
        spriteRenderer.sprite = firstSprite;
        spriteRenderer.sortingOrder = 30;

        var animator = root.AddComponent<Animator>();
        animator.runtimeAnimatorController = controller;

        root.transform.localScale = Vector3.one * PreviewScale;

        PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
        Object.DestroyImmediate(root);

        return AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
    }

    private static void EnsureFolder(string assetFolder)
    {
        if (AssetDatabase.IsValidFolder(assetFolder))
        {
            return;
        }

        var parts = assetFolder.Split('/');
        var current = parts[0];
        for (var i = 1; i < parts.Length; i++)
        {
            var next = $"{current}/{parts[i]}";
            if (!AssetDatabase.IsValidFolder(next))
            {
                AssetDatabase.CreateFolder(current, parts[i]);
            }

            current = next;
        }
    }

    private static void ConfigureSpriteImport(string assetPath)
    {
        var importer = AssetImporter.GetAtPath(assetPath) as TextureImporter;
        if (importer == null)
        {
            return;
        }

        var changed = false;

        if (importer.textureType != TextureImporterType.Sprite)
        {
            importer.textureType = TextureImporterType.Sprite;
            changed = true;
        }

        if (importer.spriteImportMode != SpriteImportMode.Single)
        {
            importer.spriteImportMode = SpriteImportMode.Single;
            changed = true;
        }

        if (Mathf.Abs(importer.spritePixelsPerUnit - PixelsPerUnit) > 0.001f)
        {
            importer.spritePixelsPerUnit = PixelsPerUnit;
            changed = true;
        }

        if (importer.mipmapEnabled)
        {
            importer.mipmapEnabled = false;
            changed = true;
        }

        if (importer.filterMode != FilterMode.Point)
        {
            importer.filterMode = FilterMode.Point;
            changed = true;
        }

        if (importer.wrapMode != TextureWrapMode.Clamp)
        {
            importer.wrapMode = TextureWrapMode.Clamp;
            changed = true;
        }

        if (importer.textureCompression != TextureImporterCompression.Uncompressed)
        {
            importer.textureCompression = TextureImporterCompression.Uncompressed;
            changed = true;
        }

        if (!importer.alphaIsTransparency)
        {
            importer.alphaIsTransparency = true;
            changed = true;
        }

        if (changed)
        {
            importer.SaveAndReimport();
        }
    }

    private static string ToAssetPath(string absolutePath)
    {
        var normalized = absolutePath.Replace('\\', '/');
        var projectRoot = Directory.GetCurrentDirectory().Replace('\\', '/');
        if (normalized.StartsWith(projectRoot))
        {
            return normalized.Substring(projectRoot.Length + 1);
        }

        return normalized;
    }
}
#endif
