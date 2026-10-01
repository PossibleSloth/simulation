using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Renders a 2-D EM interference field onto a full-screen quad by evaluating
/// every WaveSource in the scene at each pixel, CPU-side (no shaders).
///
/// SETUP
/// -----
/// 1. Create a Quad (GameObject → 3D Object → Quad).
///    - Set its Z position slightly in front of everything else you want to
///      occlude (e.g. z = -0.1 when using an orthographic camera at z = -10).
///    - Scale it to fill the camera view (see helper note below).
/// 2. Attach this script to the Quad.
/// 3. Switch your Main Camera to Orthographic.
/// 4. Add WaveSource components to any GameObjects in the scene.
/// 5. Press Play – done.
///
/// SCALING THE QUAD TO FILL THE CAMERA
/// ------------------------------------
/// For an orthographic camera with size S (half-height in world units):
///   Quad scaleY = 2 * S
///   Quad scaleX = 2 * S * Screen.width / Screen.height
/// You can do this once manually in the Editor, or tick
/// "Auto-size quad to camera" in the Inspector and the script will do it.
/// </summary>
[RequireComponent(typeof(MeshRenderer))]
public class WaveFieldRenderer : MonoBehaviour
{
    // ── Inspector ──────────────────────────────────────────────────────────

    [Header("Texture")]
    [Tooltip("Pixel resolution of the simulation texture (width). " +
             "Lower = faster; higher = sharper. 256–512 is a good start.")]
    public int textureWidth = 256;

    [Tooltip("Pixel resolution of the simulation texture (height).")]
    public int textureHeight = 256;

    [Header("Color Mapping")]
    [Tooltip("Maps the normalized field value [-1, 1] to a color. " +
             "Left = most negative, centre = zero, right = most positive. " +
             "A simple default gradient is created at runtime if left empty.")]
    public Gradient fieldGradient;

    [Tooltip("Clamp the raw summed field to ±this value before normalising. " +
             "Increase if sources are very strong; decrease to boost contrast.")]
    public float fieldClamp = 2f;

    [Header("Camera Fit")]
    [Tooltip("Automatically scale this Quad each frame to fill the orthographic camera.")]
    public bool autoSizeQuadToCamera = true;


    [Header("Default Gradient")]
    [Tooltip("Use the default gradient.")]
    public bool useDefaultGradient = true;

    public Camera targetCamera; // leave null to use Camera.main

    // ── Private state ──────────────────────────────────────────────────────

    Texture2D _tex;
    Color32[] _pixels;
    Material _mat;
    Camera _cam;

    // Cached to avoid per-frame allocation
    readonly List<WaveSource> _sources = new List<WaveSource>();

    // ── Unity lifecycle ────────────────────────────────────────────────────

    void Awake()
    {
        _cam = targetCamera != null ? targetCamera : Camera.main;

        // Create texture
        _tex = new Texture2D(textureWidth, textureHeight, TextureFormat.RGBA32, false)
        {
            filterMode = FilterMode.Bilinear,
            wrapMode   = TextureWrapMode.Clamp
        };
        _pixels = new Color32[textureWidth * textureHeight];

        // Assign to this renderer's material
        _mat = GetComponent<MeshRenderer>().material;
        // Use Unlit/Texture so lighting doesn't affect the field colours
        _mat.shader = Shader.Find("Unlit/Texture");
        _mat.mainTexture = _tex;

        // Build a default gradient if none was assigned
        if (fieldGradient == null || !HasKeys(fieldGradient) || useDefaultGradient)
            fieldGradient = BuildDefaultGradient();
    }

    void Update()
    {
        if (autoSizeQuadToCamera)
            FitQuadToCamera();

        CollectSources();
        UpdatePixels();
    }

    void OnDestroy()
    {
        if (_tex != null) Destroy(_tex);
        if (_mat != null) Destroy(_mat);
    }

    // ── Core ───────────────────────────────────────────────────────────────

    void CollectSources()
    {
        _sources.Clear();
        var found = FindObjectsByType<WaveSource>();
        foreach (var s in found) _sources.Add(s);
    }

    void UpdatePixels()
    {
        float t = Time.time;

        // World-space rect covered by the quad (its own transform defines this)
        // We sample pixel centres in local UV space and convert to world space.
        float halfW = transform.lossyScale.x * 0.5f;
        float halfH = transform.lossyScale.y * 0.5f;
        Vector3 origin = transform.position;

        int w = textureWidth;
        int h = textureHeight;
        float clamp = Mathf.Max(fieldClamp, 1e-5f);

        for (int py = 0; py < h; py++)
        {
            // UV: 0 → 1 mapped to -halfH → +halfH in world space
            float wy = origin.y + Mathf.Lerp(-halfH, halfH, (py + 0.5f) / h);

            for (int px = 0; px < w; px++)
            {
                float wx = origin.x + Mathf.Lerp(-halfW, halfW, (px + 0.5f) / w);
                Vector2 worldPos = new Vector2(wx, wy);

                // Sum contributions from all sources
                float field = 0f;
                for (int si = 0; si < _sources.Count; si++)
                    field += _sources[si].Evaluate(worldPos, t);

                // Normalise to [0, 1] for the gradient
                float norm = Mathf.InverseLerp(-clamp, clamp, Mathf.Clamp(field, -clamp, clamp));
                _pixels[py * w + px] = fieldGradient.Evaluate(norm);
            }
        }

        _tex.SetPixels32(_pixels);
        _tex.Apply(false); // false = don't recalculate mipmaps (we have none)
    }

    // ── Helpers ────────────────────────────────────────────────────────────

    void FitQuadToCamera()
    {
        if (_cam == null || !_cam.orthographic) return;

        float camH = _cam.orthographicSize * 2f;
        float camW = camH * _cam.aspect;

        transform.localScale = new Vector3(camW, camH, 1f);
        // Keep the quad centred on the camera
        transform.position = new Vector3(
            _cam.transform.position.x,
            _cam.transform.position.y,
            transform.position.z);
    }

    // Default gradient: deep blue (negative) → black (zero) → bright yellow (positive)
    static Gradient BuildDefaultGradient()
    {
        var g = new Gradient();
        g.SetKeys(
            new[]
            {
                new GradientColorKey(new Color(0.00f, 0.10f, 0.60f), 0.0f),  // negative peak
                new GradientColorKey(new Color(0.02f, 0.02f, 0.05f), 0.5f),  // zero crossing
                new GradientColorKey(new Color(1.00f, 0.85f, 0.10f), 1.0f),  // positive peak
            },
            new[]
            {
                new GradientAlphaKey(1f, 0f),
                new GradientAlphaKey(1f, 0.5f),
                new GradientAlphaKey(1f, 1f),
            }
        );
        return g;
    }

    static bool HasKeys(Gradient g)
    {
        // Gradient is non-null but may be uninitialised
        return g.colorKeys != null && g.colorKeys.Length > 0;
    }
}
