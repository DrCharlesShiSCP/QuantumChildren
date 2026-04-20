using UnityEngine;

[DisallowMultipleComponent]
public sealed class PortalSurface : MonoBehaviour
{
    private const string DisplayShaderName = "QuantumChildren/Portal/Portal Display";
    private const string MaskShaderName = "QuantumChildren/Portal/Stencil Mask";

    private static readonly int PortalTexId = Shader.PropertyToID("_PortalTex");
    private static readonly int MainTexId = Shader.PropertyToID("_MainTex");
    private static readonly int BaseMapId = Shader.PropertyToID("_BaseMap");
    private static readonly int StencilRefId = Shader.PropertyToID("_StencilRef");
    private static readonly int StencilReadMaskId = Shader.PropertyToID("_StencilReadMask");
    private static readonly int StencilWriteMaskId = Shader.PropertyToID("_StencilWriteMask");

    [Header("Render Source")]
    [SerializeField, Tooltip("Assign the existing alternate-view camera. Its culling mask/layers are not changed.")]
    private Camera sourceCamera;

    [SerializeField, Tooltip("Optional existing RenderTexture. If empty, the camera's current targetTexture is reused.")]
    private RenderTexture targetRenderTexture;

    [SerializeField, Tooltip("Creates a runtime RenderTexture only when neither this field nor the camera has one.")]
    private bool createRenderTextureWhenMissing = true;

    [SerializeField]
    private Vector2Int fallbackRenderTextureSize = new Vector2Int(1024, 1024);

    [Header("Portal Renderers")]
    [SerializeField, Tooltip("Renderer/material slot that shows the alternate camera texture.")]
    private Renderer portalScreenRenderer;

    [SerializeField]
    private int screenMaterialIndex;

    [SerializeField, Tooltip("Renderer/material slot shaped like the portal opening. It should use the stencil mask material.")]
    private Renderer maskRenderer;

    [SerializeField]
    private int maskMaterialIndex;

    [Header("Materials")]
    [SerializeField, Tooltip("Usually Assets/Materials/Portal/PortalDisplay.mat.")]
    private Material displayMaterialTemplate;

    [SerializeField, Tooltip("Usually Assets/Materials/Portal/PortalStencilMask.mat.")]
    private Material maskMaterialTemplate;

    [Header("Stencil")]
    [SerializeField, Range(1, 255), Tooltip("Use a different value for overlapping portals.")]
    private int stencilReference = 1;

    [SerializeField, Range(0, 255)]
    private int stencilMask = 255;

    private RenderTexture ownedRenderTexture;
    private RenderTexture previousCameraTargetTexture;
    private Material runtimeDisplayMaterial;
    private Material runtimeMaskMaterial;
    private Material[] originalScreenMaterials;
    private Material[] originalMaskMaterials;
    private bool cameraTargetWasChanged;
    private bool runtimeConfigured;

    public Camera SourceCamera
    {
        get => sourceCamera;
        set => sourceCamera = value;
    }

    public RenderTexture TargetRenderTexture
    {
        get => targetRenderTexture;
        set => targetRenderTexture = value;
    }

    public Renderer PortalScreenRenderer
    {
        get => portalScreenRenderer;
        set => portalScreenRenderer = value;
    }

    public Renderer MaskRenderer
    {
        get => maskRenderer;
        set => maskRenderer = value;
    }

    private void Reset()
    {
        portalScreenRenderer = GetComponent<Renderer>();
    }

    private void OnEnable()
    {
        if (!Application.isPlaying)
        {
            return;
        }

        ConfigurePortal();
    }

    private void OnDisable()
    {
        if (!runtimeConfigured)
        {
            return;
        }

        RestoreCameraTarget();
        RestoreRendererMaterials();
        DestroyRuntimeMaterial(runtimeDisplayMaterial);
        DestroyRuntimeMaterial(runtimeMaskMaterial);
        runtimeDisplayMaterial = null;
        runtimeMaskMaterial = null;
        ReleaseOwnedRenderTexture();
        runtimeConfigured = false;
    }

    private void OnValidate()
    {
        screenMaterialIndex = Mathf.Max(0, screenMaterialIndex);
        maskMaterialIndex = Mathf.Max(0, maskMaterialIndex);
        fallbackRenderTextureSize = new Vector2Int(
            Mathf.Max(16, fallbackRenderTextureSize.x),
            Mathf.Max(16, fallbackRenderTextureSize.y));
    }

    public void ConfigurePortal()
    {
        RenderTexture renderTexture = ResolveRenderTexture();
        AssignSourceCameraTarget(renderTexture);

        runtimeDisplayMaterial = BuildRuntimeMaterial(
            displayMaterialTemplate,
            portalScreenRenderer,
            screenMaterialIndex,
            DisplayShaderName,
            "Portal Display Instance");

        runtimeMaskMaterial = BuildRuntimeMaterial(
            maskMaterialTemplate,
            maskRenderer,
            maskMaterialIndex,
            MaskShaderName,
            "Portal Stencil Mask Instance");

        ConfigureDisplayMaterial(runtimeDisplayMaterial, renderTexture);
        ConfigureMaskMaterial(runtimeMaskMaterial);

        CacheOriginalMaterials(portalScreenRenderer, ref originalScreenMaterials);
        AssignMaterial(portalScreenRenderer, screenMaterialIndex, runtimeDisplayMaterial);

        if (maskRenderer != null)
        {
            if (maskRenderer == portalScreenRenderer && maskMaterialIndex == screenMaterialIndex)
            {
                Debug.LogWarning($"{name}: mask and display use the same renderer material slot. Use separate slots or renderers.", this);
            }
            else
            {
                if (maskRenderer != portalScreenRenderer)
                {
                    CacheOriginalMaterials(maskRenderer, ref originalMaskMaterials);
                }

                AssignMaterial(maskRenderer, maskMaterialIndex, runtimeMaskMaterial);
            }
        }
        else
        {
            Debug.LogWarning($"{name}: no mask renderer assigned. The portal texture will only be clipped by the display mesh, not by stencil.", this);
        }

        if (Application.isPlaying)
        {
            runtimeConfigured = true;
        }
    }

    private RenderTexture ResolveRenderTexture()
    {
        if (targetRenderTexture != null)
        {
            return targetRenderTexture;
        }

        if (sourceCamera != null && sourceCamera.targetTexture != null)
        {
            return sourceCamera.targetTexture;
        }

        if (!createRenderTextureWhenMissing)
        {
            return null;
        }

        if (ownedRenderTexture == null ||
            ownedRenderTexture.width != fallbackRenderTextureSize.x ||
            ownedRenderTexture.height != fallbackRenderTextureSize.y)
        {
            ReleaseOwnedRenderTexture();
            var descriptor = new RenderTextureDescriptor(
                fallbackRenderTextureSize.x,
                fallbackRenderTextureSize.y,
                RenderTextureFormat.ARGB32,
                24)
            {
                msaaSamples = 1,
                useMipMap = false,
                autoGenerateMips = false,
                sRGB = QualitySettings.activeColorSpace == ColorSpace.Linear
            };

            ownedRenderTexture = new RenderTexture(descriptor)
            {
                name = $"{name}_PortalRenderTexture"
            };
            ownedRenderTexture.Create();
        }

        return ownedRenderTexture;
    }

    private void AssignSourceCameraTarget(RenderTexture renderTexture)
    {
        if (sourceCamera == null || renderTexture == null || sourceCamera.targetTexture == renderTexture)
        {
            return;
        }

        previousCameraTargetTexture = sourceCamera.targetTexture;
        sourceCamera.targetTexture = renderTexture;
        cameraTargetWasChanged = true;
    }

    private Material BuildRuntimeMaterial(Material template, Renderer renderer, int materialIndex, string shaderName, string fallbackName)
    {
        Material source = template;

        if (source == null && renderer != null && materialIndex < renderer.sharedMaterials.Length)
        {
            Material candidate = renderer.sharedMaterials[materialIndex];
            if (candidate != null && candidate.shader != null && candidate.shader.name == shaderName)
            {
                source = candidate;
            }
        }

        if (source == null)
        {
            Shader shader = Shader.Find(shaderName);
            if (shader == null)
            {
                Debug.LogWarning($"{name}: shader '{shaderName}' was not found. Assign the portal material asset in the Inspector.", this);
                return null;
            }

            source = new Material(shader) { name = fallbackName };
        }

        Material runtimeMaterial = new Material(source)
        {
            name = $"{source.name} (Runtime)",
            hideFlags = HideFlags.DontSave
        };

        return runtimeMaterial;
    }

    private void ConfigureDisplayMaterial(Material material, RenderTexture renderTexture)
    {
        if (material == null)
        {
            return;
        }

        SetStencilProperties(material, true, false);

        if (renderTexture == null)
        {
            Debug.LogWarning($"{name}: no RenderTexture is available for the portal display.", this);
            return;
        }

        SetTextureIfPresent(material, PortalTexId, renderTexture);
        SetTextureIfPresent(material, MainTexId, renderTexture);
        SetTextureIfPresent(material, BaseMapId, renderTexture);
    }

    private void ConfigureMaskMaterial(Material material)
    {
        if (material == null)
        {
            return;
        }

        SetStencilProperties(material, false, true);
    }

    private void SetStencilProperties(Material material, bool read, bool write)
    {
        if (material.HasProperty(StencilRefId))
        {
            material.SetInt(StencilRefId, stencilReference);
        }

        if (read && material.HasProperty(StencilReadMaskId))
        {
            material.SetInt(StencilReadMaskId, stencilMask);
        }

        if (write && material.HasProperty(StencilWriteMaskId))
        {
            material.SetInt(StencilWriteMaskId, stencilMask);
        }
    }

    private static void SetTextureIfPresent(Material material, int propertyId, Texture texture)
    {
        if (material.HasProperty(propertyId))
        {
            material.SetTexture(propertyId, texture);
        }
    }

    private void AssignMaterial(Renderer targetRenderer, int materialIndex, Material material)
    {
        if (targetRenderer == null || material == null)
        {
            return;
        }

        Material[] materials = targetRenderer.sharedMaterials;
        if (materialIndex >= materials.Length)
        {
            Debug.LogWarning($"{name}: material index {materialIndex} is outside '{targetRenderer.name}' material slots.", this);
            return;
        }

        materials[materialIndex] = material;
        targetRenderer.sharedMaterials = materials;
    }

    private static void CacheOriginalMaterials(Renderer targetRenderer, ref Material[] materialCache)
    {
        if (targetRenderer != null && materialCache == null)
        {
            materialCache = targetRenderer.sharedMaterials;
        }
    }

    private void RestoreRendererMaterials()
    {
        RestoreRendererMaterials(portalScreenRenderer, originalScreenMaterials);

        if (maskRenderer != portalScreenRenderer)
        {
            RestoreRendererMaterials(maskRenderer, originalMaskMaterials);
        }

        originalScreenMaterials = null;
        originalMaskMaterials = null;
    }

    private static void RestoreRendererMaterials(Renderer targetRenderer, Material[] materials)
    {
        if (targetRenderer != null && materials != null)
        {
            targetRenderer.sharedMaterials = materials;
        }
    }

    private void RestoreCameraTarget()
    {
        if (cameraTargetWasChanged && sourceCamera != null)
        {
            sourceCamera.targetTexture = previousCameraTargetTexture;
        }

        cameraTargetWasChanged = false;
        previousCameraTargetTexture = null;
    }

    private void ReleaseOwnedRenderTexture()
    {
        if (ownedRenderTexture == null)
        {
            return;
        }

        if (ownedRenderTexture.IsCreated())
        {
            ownedRenderTexture.Release();
        }

        Destroy(ownedRenderTexture);
        ownedRenderTexture = null;
    }

    private static void DestroyRuntimeMaterial(Material material)
    {
        if (material != null)
        {
            Destroy(material);
        }
    }
}
