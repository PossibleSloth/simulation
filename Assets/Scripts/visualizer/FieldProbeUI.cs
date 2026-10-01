using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.InputSystem;

/// <summary>
/// Click anywhere on the wave field to move the probe to that world position.
/// A fixed UI panel in the top-right corner always shows:
///   • World-space coordinates of the probe point
///   • Instantaneous field value  E(t)
///   • An animated arrow whose length and direction encode E(t) in real time
///   • A scrolling oscilloscope trace of E(t)
///   • Per-source frequency and distance summary
///
/// Uses the Unity New Input System (UnityEngine.InputSystem) — requires the
/// "Input System" package to be installed via Package Manager.
///
/// SETUP
/// -----
/// 1. Attach this script to any persistent GameObject (e.g. a "UIManager" empty).
/// 2. Assign the "Wave Field Renderer" reference in the Inspector.
/// 3. The script builds all UI elements procedurally – nothing else to wire up.
///
/// DEPENDENCIES
/// ------------
/// Requires WaveFieldRenderer.cs and WaveSource.cs from the wave simulation.
/// </summary>
public class FieldProbeUI : MonoBehaviour
{
    // ── Inspector ──────────────────────────────────────────────────────────

    [Header("References")]
    [Tooltip("The WaveFieldRenderer in the scene.")]
    public WaveFieldRenderer waveFieldRenderer;

    [Tooltip("Camera used for screen-to-world conversion. Defaults to Camera.main.")]
    public Camera targetCamera;

    [Header("Probe Appearance")]
    [Tooltip("Radius of the crosshair marker drawn at the probe point (world units).")]
    public float markerRadius = 0.15f;

    [Tooltip("Maximum arrow length in UI pixels when |E| == fieldClamp.")]
    public float maxArrowPixels = 80f;

    [Tooltip("How many seconds of history the oscilloscope trace shows.")]
    public float traceWindowSeconds = 3f;

    [Tooltip("Samples per second recorded for the oscilloscope trace.")]
    public int traceSampleRate = 120;

    // ── Private state ──────────────────────────────────────────────────────

    Camera          _cam;
    bool            _probeActive;
    Vector2         _probeWorld;        // world-space position of probe

    // Oscilloscope history
    readonly Queue<float> _traceValues = new Queue<float>();
    float _sampleTimer;

    // World-space crosshair drawn with LineRenderer
    LineRenderer _crosshair;

    // UI references (built procedurally)
    Canvas          _canvas;
    RectTransform   _panel;
    Text            _lblCoords;
    Text            _lblFieldVal;
    Text            _lblSources;
    RectTransform   _arrowShaft;
    RectTransform   _arrowHead;
    RawImage        _scopeImage;
    Texture2D       _scopeTex;

    // Cached sources list (reused to avoid alloc)
    readonly List<WaveSource> _sources = new List<WaveSource>();

    // ── Constants ──────────────────────────────────────────────────────────

    const int   SCOPE_W         = 220;
    const int   SCOPE_H         = 80;
    const float PANEL_W         = 260f;
    const float PANEL_H         = 310f;

    static readonly Color32 SCOPE_BG     = new Color32(  5,  12,  20, 255);
    static readonly Color32 SCOPE_GRID   = new Color32( 20,  50,  40, 180);
    static readonly Color32 SCOPE_TRACE  = new Color32( 50, 220, 120, 255);
    static readonly Color32 SCOPE_ZERO   = new Color32( 40, 100,  70, 200);

    // ── Unity lifecycle ────────────────────────────────────────────────────

    void Start()
    {
        _cam = targetCamera != null ? targetCamera : Camera.main;
        BuildWorldMarker();
        BuildUI();
        SetPanelVisible(false);
    }

    void Update()
    {
        HandleClick();

        if (!_probeActive) return;

        float t = Time.time;
        CollectSources();
        float field = EvaluateField(_probeWorld, t);

        UpdatePanel(field, t);
        UpdateArrow(field);
        RecordSample(field);
        UpdateOscilloscope();
        UpdateCrosshair();
    }

    // ── Input ──────────────────────────────────────────────────────────────

    void HandleClick()
    {
        var mouse = Mouse.current;
        if (mouse == null || !mouse.leftButton.wasPressedThisFrame) return;

        // Ignore clicks that land on the UI panel itself
        Vector2 mouseScreenPos = mouse.position.ReadValue();
        if (_panel != null && RectTransformUtility.RectangleContainsScreenPoint(
                _panel, mouseScreenPos, null))
            return;

        Vector3 worldPos = _cam.ScreenToWorldPoint(
            new Vector3(mouseScreenPos.x, mouseScreenPos.y, -_cam.transform.position.z));

        _probeWorld  = new Vector2(worldPos.x, worldPos.y);
        _probeActive = true;
        _traceValues.Clear();
        _sampleTimer = 0f;

        // Panel stays fixed — just make it visible on first click
        SetPanelVisible(true);
    }

    // ── Field evaluation ───────────────────────────────────────────────────

    void CollectSources()
    {
        _sources.Clear();
        var found = FindObjectsByType<WaveSource>();
        foreach (var s in found) _sources.Add(s);
    }

    float EvaluateField(Vector2 pos, float t)
    {
        float sum = 0f;
        foreach (var src in _sources)
            sum += src.Evaluate(pos, t);
        return sum;
    }

    // ── Panel content ──────────────────────────────────────────────────────

    void UpdatePanel(float field, float t)
    {
        _lblCoords.text = $"<color=#66ffcc>PROBE</color>  ({_probeWorld.x:F2}, {_probeWorld.y:F2})";

        float clamp = waveFieldRenderer != null ? waveFieldRenderer.fieldClamp : 2f;
        float norm  = field / Mathf.Max(clamp, 1e-5f);  // -1 … +1

        string sign  = field >= 0 ? "+" : "−";
        string color = field >= 0 ? "#ffdd44" : "#44aaff";
        _lblFieldVal.text = $"E(t) = <color={color}>{sign}{Mathf.Abs(field):F4}</color>";

        // Source summary
        System.Text.StringBuilder sb = new System.Text.StringBuilder();
        for (int i = 0; i < _sources.Count && i < 4; i++)
        {
            var s = _sources[i];
            float dist = Vector2.Distance(_probeWorld, s.transform.position);
            sb.AppendLine($"  src {i+1}  f={s.frequency:F2}Hz  d={dist:F2}u");
        }
        if (_sources.Count > 4) sb.AppendLine($"  … +{_sources.Count - 4} more");
        _lblSources.text = sb.Length > 0 ? sb.ToString().TrimEnd() : "  (no sources)";
    }

    // ── Animated arrow ─────────────────────────────────────────────────────

    void UpdateArrow(float field)
    {
        float clamp = waveFieldRenderer != null ? waveFieldRenderer.fieldClamp : 2f;
        float norm  = Mathf.Clamp(field / Mathf.Max(clamp, 1e-5f), -1f, 1f);

        // Arrow points up for positive, down for negative
        float pixels = norm * maxArrowPixels;

        // Shaft: a tall-thin rect, pivot at centre-bottom of the arrow area
        //   We reposition its anchored position so the base stays fixed
        _arrowShaft.sizeDelta        = new Vector2(4f, Mathf.Abs(pixels));
        _arrowShaft.anchoredPosition = new Vector2(0f, pixels * 0.5f);

        // Colour: positive = warm yellow, negative = cool blue
        var shaftImg = _arrowShaft.GetComponent<Image>();
        shaftImg.color = pixels >= 0
            ? new Color(1f, 0.85f, 0.1f)
            : new Color(0.15f, 0.55f, 1f);

        // Arrowhead triangle sits at the tip
        _arrowHead.anchoredPosition = new Vector2(0f, pixels);
        _arrowHead.localScale       = new Vector3(1f, pixels >= 0 ? 1f : -1f, 1f);
        _arrowHead.GetComponent<Image>().color = shaftImg.color;
    }

    // ── Oscilloscope ───────────────────────────────────────────────────────

    void RecordSample(float field)
    {
        _sampleTimer += Time.deltaTime;
        float interval = 1f / Mathf.Max(traceSampleRate, 1);
        if (_sampleTimer < interval) return;
        _sampleTimer -= interval;

        _traceValues.Enqueue(field);

        int maxSamples = Mathf.CeilToInt(traceWindowSeconds * traceSampleRate);
        while (_traceValues.Count > maxSamples)
            _traceValues.Dequeue();
    }

    void UpdateOscilloscope()
    {
        // Clear to background
        Color32[] px = _scopeTex.GetPixels32();
        for (int i = 0; i < px.Length; i++) px[i] = SCOPE_BG;

        // Grid lines
        DrawHLine(px, SCOPE_H / 2, SCOPE_ZERO);   // zero line
        DrawHLine(px, SCOPE_H / 4, SCOPE_GRID);
        DrawHLine(px, 3 * SCOPE_H / 4, SCOPE_GRID);
        for (int gx = 0; gx <= 4; gx++)
            DrawVLine(px, gx * SCOPE_W / 4, SCOPE_GRID);

        // Trace
        float clamp = waveFieldRenderer != null ? waveFieldRenderer.fieldClamp : 2f;
        float[] vals = new float[_traceValues.Count];
        _traceValues.CopyTo(vals, 0);

        int maxSamples = Mathf.CeilToInt(traceWindowSeconds * traceSampleRate);

        for (int i = 1; i < vals.Length; i++)
        {
            int x0 = Mathf.RoundToInt((float)(i - 1) / maxSamples * (SCOPE_W - 1));
            int x1 = Mathf.RoundToInt((float)i        / maxSamples * (SCOPE_W - 1));

            float n0 = Mathf.Clamp(vals[i-1] / clamp, -1f, 1f);
            float n1 = Mathf.Clamp(vals[i]   / clamp, -1f, 1f);

            int y0 = Mathf.RoundToInt((n0 * 0.45f + 0.5f) * (SCOPE_H - 1));
            int y1 = Mathf.RoundToInt((n1 * 0.45f + 0.5f) * (SCOPE_H - 1));

            DrawLine(px, x0, y0, x1, y1, SCOPE_TRACE);
        }

        _scopeTex.SetPixels32(px);
        _scopeTex.Apply(false);
    }

    // ── World-space crosshair (LineRenderer) ───────────────────────────────

    void BuildWorldMarker()
    {
        var go = new GameObject("ProbeMarker");
        go.transform.SetParent(transform);
        _crosshair = go.AddComponent<LineRenderer>();
        _crosshair.useWorldSpace   = true;
        _crosshair.loop            = false;
        _crosshair.startWidth      = 0.03f;
        _crosshair.endWidth        = 0.03f;
        _crosshair.material        = new Material(Shader.Find("Sprites/Default"));
        _crosshair.startColor      = new Color(0.2f, 1f, 0.6f, 0.9f);
        _crosshair.endColor        = new Color(0.2f, 1f, 0.6f, 0.9f);
        _crosshair.positionCount   = 0;
        _crosshair.sortingOrder    = 10;
        _crosshair.gameObject.SetActive(false);
    }

    void UpdateCrosshair()
    {
        if (!_crosshair.gameObject.activeSelf)
            _crosshair.gameObject.SetActive(true);

        float r = markerRadius;
        float z = -0.05f; // in front of wave quad
        _crosshair.positionCount = 8;
        _crosshair.SetPositions(new Vector3[]
        {
            // Horizontal bar
            new Vector3(_probeWorld.x - r, _probeWorld.y,     z),
            new Vector3(_probeWorld.x + r, _probeWorld.y,     z),
            // Gap
            new Vector3(_probeWorld.x + r, _probeWorld.y,     z),
            new Vector3(_probeWorld.x,     _probeWorld.y,     z),
            // Vertical bar
            new Vector3(_probeWorld.x,     _probeWorld.y - r, z),
            new Vector3(_probeWorld.x,     _probeWorld.y + r, z),
            // Circle approximated – just redraw the cross wider for simplicity
            new Vector3(_probeWorld.x,     _probeWorld.y + r, z),
            new Vector3(_probeWorld.x,     _probeWorld.y,     z),
        });

        // Pulse the alpha
        float alpha = 0.6f + 0.4f * Mathf.Sin(Time.time * 6f);
        _crosshair.startColor = new Color(0.2f, 1f, 0.6f, alpha);
        _crosshair.endColor   = _crosshair.startColor;
    }

    // ── UI construction ────────────────────────────────────────────────────

    void BuildUI()
    {
        // Canvas
        var canvasGO = new GameObject("FieldProbeCanvas");
        _canvas = canvasGO.AddComponent<Canvas>();
        _canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        _canvas.sortingOrder = 10;
        canvasGO.AddComponent<CanvasScaler>();
        canvasGO.AddComponent<GraphicRaycaster>();

        // Panel background
        var panelGO = new GameObject("ProbePanel");
        panelGO.transform.SetParent(_canvas.transform, false);
        _panel = panelGO.AddComponent<RectTransform>();
        _panel.sizeDelta = new Vector2(PANEL_W, PANEL_H);
        _panel.pivot     = new Vector2(0f, 1f); // anchor top-left of panel

        var panelImg = panelGO.AddComponent<Image>();
        panelImg.color = new Color(0.02f, 0.06f, 0.12f, 0.92f);

        // Thin border via outline image
        AddBorderOutline(panelGO);

        // Layout: stack labels from top
        float y = -10f;

        // ── Coordinates label
        _lblCoords = AddLabel(panelGO, "coords",
            new Vector2(10f, y), new Vector2(PANEL_W - 20f, 22f),
            "", 11, TextAnchor.MiddleLeft, FontStyle.Normal,
            new Color(0.6f, 1f, 0.8f));
        y -= 26f;

        // ── Separator
        AddSeparator(panelGO, y);  y -= 6f;

        // ── Field value label
        _lblFieldVal = AddLabel(panelGO, "fieldVal",
            new Vector2(10f, y), new Vector2(PANEL_W - 20f, 22f),
            "E(t) = —", 13, TextAnchor.MiddleLeft, FontStyle.Bold,
            Color.white);
        y -= 28f;

        // ── Arrow area  (fixed 180px tall region centred horizontally)
        float arrowAreaH  = 180f;
        float arrowCentreX = PANEL_W * 0.5f;
        float arrowCentreY = y - arrowAreaH * 0.5f;

        // Zero-line
        AddHRule(panelGO, arrowCentreY, new Color(0.2f, 0.5f, 0.35f, 0.6f));

        // "E↑" label
        AddLabel(panelGO, "arrowLabel",
            new Vector2(arrowCentreX + 16f, arrowCentreY - 8f),
            new Vector2(40f, 16f),
            "E →", 9, TextAnchor.MiddleLeft, FontStyle.Italic,
            new Color(0.5f, 0.8f, 0.6f, 0.7f));

        // Arrow shaft
        var shaftGO = new GameObject("ArrowShaft");
        shaftGO.transform.SetParent(panelGO.transform, false);
        _arrowShaft = shaftGO.AddComponent<RectTransform>();
        _arrowShaft.pivot            = new Vector2(0.5f, 0.5f);
        _arrowShaft.anchorMin        = new Vector2(0.5f, 0f);
        _arrowShaft.anchorMax        = new Vector2(0.5f, 0f);
        _arrowShaft.anchoredPosition = new Vector2(0f, arrowCentreY);
        _arrowShaft.sizeDelta        = new Vector2(4f, 0f);
        var shaftImg = shaftGO.AddComponent<Image>();
        shaftImg.color = Color.yellow;

        // Arrowhead (triangle via a rotated square for simplicity)
        var headGO = new GameObject("ArrowHead");
        headGO.transform.SetParent(panelGO.transform, false);
        _arrowHead = headGO.AddComponent<RectTransform>();
        _arrowHead.pivot            = new Vector2(0.5f, 0f);
        _arrowHead.anchorMin        = new Vector2(0.5f, 0f);
        _arrowHead.anchorMax        = new Vector2(0.5f, 0f);
        _arrowHead.anchoredPosition = new Vector2(0f, arrowCentreY);
        _arrowHead.sizeDelta        = new Vector2(12f, 10f);
        _arrowHead.localRotation    = Quaternion.Euler(0f, 0f, 0f);
        var headImg = headGO.AddComponent<Image>();
        headImg.color = Color.yellow;
        // Rotate 45° so the square looks like a diamond arrowhead tip
        headGO.transform.localRotation = Quaternion.Euler(0f, 0f, 45f);

        y -= arrowAreaH + 4f;

        // ── Separator
        AddSeparator(panelGO, y);  y -= 6f;

        // ── Oscilloscope label
        AddLabel(panelGO, "scopeLabel",
            new Vector2(10f, y), new Vector2(80f, 16f),
            "TRACE", 9, TextAnchor.MiddleLeft, FontStyle.Bold,
            new Color(0.3f, 0.8f, 0.5f));
        y -= 18f;

        // ── Oscilloscope image
        _scopeTex = new Texture2D(SCOPE_W, SCOPE_H, TextureFormat.RGBA32, false)
        {
            filterMode = FilterMode.Point
        };
        var scopeGO = new GameObject("Oscilloscope");
        scopeGO.transform.SetParent(panelGO.transform, false);
        var scopeRT = scopeGO.AddComponent<RectTransform>();
        scopeRT.anchorMin        = new Vector2(0f, 1f);
        scopeRT.anchorMax        = new Vector2(0f, 1f);
        scopeRT.pivot            = new Vector2(0f, 1f);
        scopeRT.anchoredPosition = new Vector2(10f, y);
        scopeRT.sizeDelta        = new Vector2(PANEL_W - 20f, SCOPE_H);
        _scopeImage              = scopeGO.AddComponent<RawImage>();
        _scopeImage.texture      = _scopeTex;
        y -= SCOPE_H + 8f;

        // ── Source summary label
        _lblSources = AddLabel(panelGO, "sources",
            new Vector2(10f, y), new Vector2(PANEL_W - 20f, 60f),
            "", 9, TextAnchor.UpperLeft, FontStyle.Normal,
            new Color(0.5f, 0.7f, 0.6f));

        // Resize panel to fit content, then fix to top-right corner
        _panel.sizeDelta        = new Vector2(PANEL_W, -y + 8f);
        // ── Fixed position: top-right corner with a margin
        _panel.anchorMin        = new Vector2(1f, 1f);
        _panel.anchorMax        = new Vector2(1f, 1f);
        _panel.pivot            = new Vector2(1f, 1f);
        _panel.anchoredPosition = new Vector2(-12f, -12f); // 12px inset from top-right
    }

    // ── UI helpers ─────────────────────────────────────────────────────────

    Text AddLabel(GameObject parent, string name,
                  Vector2 anchoredPos, Vector2 size,
                  string text, int fontSize,
                  TextAnchor anchor, FontStyle style, Color color)
    {
        var go  = new GameObject(name);
        go.transform.SetParent(parent.transform, false);
        var rt  = go.AddComponent<RectTransform>();
        rt.anchorMin        = new Vector2(0f, 1f);
        rt.anchorMax        = new Vector2(0f, 1f);
        rt.pivot            = new Vector2(0f, 1f);
        rt.anchoredPosition = anchoredPos;
        rt.sizeDelta        = size;
        var lbl = go.AddComponent<Text>();
        lbl.text      = text;
        lbl.fontSize  = fontSize;
        lbl.fontStyle = style;
        lbl.alignment = anchor;
        lbl.color     = color;
        lbl.supportRichText = true;
        lbl.font      = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        return lbl;
    }

    void AddSeparator(GameObject parent, float y)
    {
        var go = new GameObject("Sep");
        go.transform.SetParent(parent.transform, false);
        var rt = go.AddComponent<RectTransform>();
        rt.anchorMin        = new Vector2(0f, 1f);
        rt.anchorMax        = new Vector2(0f, 1f);
        rt.pivot            = new Vector2(0f, 1f);
        rt.anchoredPosition = new Vector2(8f, y);
        rt.sizeDelta        = new Vector2(PANEL_W - 16f, 1f);
        var img = go.AddComponent<Image>();
        img.color = new Color(0.2f, 0.5f, 0.35f, 0.4f);
    }

    void AddHRule(GameObject parent, float centreY, Color color)
    {
        var go = new GameObject("HRule");
        go.transform.SetParent(parent.transform, false);
        var rt = go.AddComponent<RectTransform>();
        rt.anchorMin        = new Vector2(0f, 1f);
        rt.anchorMax        = new Vector2(0f, 1f);
        rt.pivot            = new Vector2(0f, 0.5f);
        rt.anchoredPosition = new Vector2(PANEL_W * 0.5f - (PANEL_W - 20f) * 0.5f, centreY);
        rt.sizeDelta        = new Vector2(PANEL_W - 20f, 1f);
        var img = go.AddComponent<Image>();
        img.color = color;
    }

    void AddBorderOutline(GameObject parent)
    {
        // Four 1px edge images
        Color borderColor = new Color(0.2f, 0.7f, 0.45f, 0.5f);
        (string n, Vector2 ap, Vector2 sd)[] edges =
        {
            ("BL", new Vector2(0, 0),             new Vector2(PANEL_W, 1)),
            ("BR", new Vector2(0, -(PANEL_H - 1)), new Vector2(PANEL_W, 1)),
            ("LL", new Vector2(0, 0),             new Vector2(1, PANEL_H)),
            ("RL", new Vector2(PANEL_W - 1, 0),   new Vector2(1, PANEL_H)),
        };
        foreach (var (n, ap, sd) in edges)
        {
            var go  = new GameObject(n);
            go.transform.SetParent(parent.transform, false);
            var rt  = go.AddComponent<RectTransform>();
            rt.anchorMin        = new Vector2(0f, 1f);
            rt.anchorMax        = new Vector2(0f, 1f);
            rt.pivot            = new Vector2(0f, 1f);
            rt.anchoredPosition = ap;
            rt.sizeDelta        = sd;
            go.AddComponent<Image>().color = borderColor;
        }
    }

    void SetPanelVisible(bool v)
    {
        if (_panel != null) _panel.gameObject.SetActive(v);
        if (_crosshair != null) _crosshair.gameObject.SetActive(v);
    }

    // ── Pixel drawing helpers for oscilloscope ─────────────────────────────

    void DrawHLine(Color32[] px, int y, Color32 color)
    {
        if (y < 0 || y >= SCOPE_H) return;
        for (int x = 0; x < SCOPE_W; x++)
            px[y * SCOPE_W + x] = color;
    }

    void DrawVLine(Color32[] px, int x, Color32 color)
    {
        if (x < 0 || x >= SCOPE_W) return;
        for (int y = 0; y < SCOPE_H; y++)
            px[y * SCOPE_W + x] = color;
    }

    void DrawLine(Color32[] px, int x0, int y0, int x1, int y1, Color32 color)
    {
        // Bresenham line
        int dx = Mathf.Abs(x1 - x0), sx = x0 < x1 ? 1 : -1;
        int dy = Mathf.Abs(y1 - y0), sy = y0 < y1 ? 1 : -1;
        int err = (dx > dy ? dx : -dy) / 2;
        while (true)
        {
            if (x0 >= 0 && x0 < SCOPE_W && y0 >= 0 && y0 < SCOPE_H)
                px[y0 * SCOPE_W + x0] = color;
            if (x0 == x1 && y0 == y1) break;
            int e2 = err;
            if (e2 > -dx) { err -= dy; x0 += sx; }
            if (e2 <  dy) { err += dx; y0 += sy; }
        }
    }

    void OnDestroy()
    {
        if (_scopeTex) Destroy(_scopeTex);
    }
}
