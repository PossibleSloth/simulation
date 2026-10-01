using UnityEngine;
using UnityEngine.UI;

public class dirac : MonoBehaviour
{

    [SerializeField] private ComputeShader fieldShader;
    [SerializeField] private RawImage displayImage;

    [Header("Simulation Settings")]
    [SerializeField] private int width = 512;
    [SerializeField] private int height = 512;
    [SerializeField] private float waveSpeed = 1.0f;
    [SerializeField] private float dt = 0.01f;
    [SerializeField] private int stepsPerFrame = 1;

    [Header("Plane Wave Settings")]
    [SerializeField] private float px = 1.0f;
    [SerializeField] private float py = 1.0f;
    [SerializeField] private float m = 1.0f;

    private RenderTexture fieldPrev;
    private RenderTexture fieldCurr;
    private RenderTexture fieldNext;
    private RenderTexture displayTex;

    private int stepKernel;
    private int renderKernel;
    private const int THREAD_GROUP_SIZE = 8;

    private float t = 0f;


    private void Awake()
    {
        fieldPrev = CreateFieldRT();
        fieldCurr = CreateFieldRT();
        fieldNext = CreateFieldRT();
        displayTex = CreateDisplayRT();
 
        stepKernel = fieldShader.FindKernel("Step");
        renderKernel = fieldShader.FindKernel("Render");
 
        SeedInitialCondition();
 
        displayImage.texture = displayTex;
        displayImage.rectTransform.sizeDelta = new Vector2(width, height);

    }


    private RenderTexture CreateFieldRT()
    {
        // RGFloat: RG = component 1, BA = component 2
        var rt = new RenderTexture(width, height, 0, RenderTextureFormat.ARGBFloat)
        {
            enableRandomWrite = true,
            filterMode = FilterMode.Bilinear,
            wrapMode = TextureWrapMode.Clamp
        };
        rt.Create();
        return rt;
    }

    private RenderTexture CreateDisplayRT()
    {
        var rt = new RenderTexture(width, height, 0, RenderTextureFormat.ARGB32)
        {
            enableRandomWrite = true,
            filterMode = FilterMode.Bilinear,
            wrapMode = TextureWrapMode.Clamp
        };
        rt.Create();
        return rt;
    }


    private void SeedInitialCondition()
    {
        // Example: a simple gaussian pulse in the center, written via a
        // tiny throwaway script-side pass. For anything fancier, do this
        // with its own compute kernel instead of CPU-side pixel writes.
        var seed = new Texture2D(width, height, TextureFormat.RGFloat, false);
        var pixels = new Color[width * height];
        float cx = width * 0.5f, cy = height * 0.5f;
        float sigma = width * 0.05f;
 
        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                pixels[y * width + x] = new Color(0f, 0f, 0f, 0f);
            }
        }
        seed.SetPixels(pixels);
        seed.Apply();
 
        Graphics.Blit(seed, fieldCurr);
        Destroy(seed); // one-time cost at startup only, not per-frame
    }

    void StepSimulation()
    {
        fieldShader.SetInt("_Width", width);
        fieldShader.SetInt("_Height", height);
        fieldShader.SetFloat("_DeltaTime", dt);
        fieldShader.SetFloat("_Time", t);
        fieldShader.SetFloat("_WaveSpeed", waveSpeed);
        fieldShader.SetFloat("_px", px);
        fieldShader.SetFloat("_py", py);
        fieldShader.SetFloat("_m", m);

        fieldShader.SetTexture(stepKernel, "_FieldPrev", fieldPrev);
        fieldShader.SetTexture(stepKernel, "_FieldCurr", fieldCurr);
        fieldShader.SetTexture(stepKernel, "_FieldNext", fieldNext);

        int groupsX = Mathf.CeilToInt(width / (float)THREAD_GROUP_SIZE);
        int groupsY = Mathf.CeilToInt(height / (float)THREAD_GROUP_SIZE);
        fieldShader.Dispatch(stepKernel, groupsX, groupsY, 1);

        t = t + dt;
 
        // Rotate buffers: prev <- curr, curr <- next (just swap references,
        // no data copy)
        var tmp = fieldPrev;
        fieldPrev = fieldCurr;
        fieldCurr = fieldNext;
        fieldNext = tmp;        
    }

    void Render()
    {
        fieldShader.SetInt("_Width", width);
        fieldShader.SetInt("_Height", height);
        fieldShader.SetTexture(renderKernel, "_FieldCurr", fieldCurr);
        fieldShader.SetTexture(renderKernel, "_DisplayTex", displayTex);
 
        int groupsX = Mathf.CeilToInt(width / (float)THREAD_GROUP_SIZE);
        int groupsY = Mathf.CeilToInt(height / (float)THREAD_GROUP_SIZE);
        fieldShader.Dispatch(renderKernel, groupsX, groupsY, 1);
    }

    // Update is called once per frame
    void Update()
    {
        for (int i = 0; i < stepsPerFrame; i++)
        {
            StepSimulation();
        }
        Render();
    }

}
