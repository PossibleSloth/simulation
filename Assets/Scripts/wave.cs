using System;
using Unity.Collections;
using Unity.Mathematics;
using Unity.VisualScripting;
using UnityEngine;

// Inspired by "Finite diﬀerence methods for wave motion"
// by Hans Petter Langtangen
// Github: https://github.com/hplgit/num-methods-for-PDEs

public class Wave : MonoBehaviour
{
    // Nx is the number of discrete points in the x dimension
    public int Nx;
    // Ny is the number of discrete points in the y dimension
    public int Ny;
    // Lx is the size in the x dimension
    public float Lx;

    // c is a physical constant that determines the speed of wave propogation
    public float LittleC;

    // C is the Courant number and is defined as c*dt/dx
    public float BigC;

    public float cameraSpeed;

    public Shader RenderShader;

    // dt is the timestep interval. It is derived from dx, c and C
    float dt;
    float dx;

    float[] u;      // u of n + 1
    float[] u_1;    // u of n
    float[] u_2;    // u of n - 1

    float[] xs;
    float[] ys;

    float min;
    float max;

    ComputeBuffer u_buffer;
    ComputeBuffer xs_buffer;
    ComputeBuffer ys_buffer;


    Material renderMaterial;

    void OnRenderImage(RenderTexture src, RenderTexture target)
    {
        Graphics.Blit(null, target, renderMaterial);
    }


    float InitialDisplacement(float x, float y)
    {
        return x + y;
    }

    float InitialVelocity(float x, float y)
    {
        // Set initial velocity to zero for now
        return 0f;
    }

    float Generator(float x, float y)
    {
        // Set to 0 for now
        return 0f;
    }

    void SetInitialConditions()
    {
        min = math.INFINITY;
        max = -1 * math.INFINITY;
        float C2 = BigC * BigC;
        // Set u_1 based on initial displacement values
        for (int i = 1; i < Nx - 1; i++)
        {
            for (int j = 1; j < Ny - 1; j++)
            {
                int index = i + Nx * j;
                u_1[index] = InitialDisplacement(xs[i], ys[j]);
                u[index] = u_1[index] +
                           dt * InitialVelocity(xs[i], ys[j]) +
                           0.5f * C2 * (u_1[index + 1] - 2 * u_1[index] + u_1[index - 1]) +
                           0.5f * C2 * (u_1[index + Nx] - 2 * u_1[index] + u_1[index - Nx]) +
                           0.5f * dt * dt * Generator(xs[i], ys[j]);

                if (u[index] > max) { max = u[index]; }
                if (u[index] < min) { min = u[index]; }
            }
        }
    }


    // Start is called once before the first execution of Update after the MonoBehaviour is created
    void Start()
    {
        renderMaterial = new Material(RenderShader);

        u = new float[Nx * Ny];
        u_1 = new float[Nx * Ny];
        u_2 = new float[Nx * Ny];

        // Compute constants that are derived from other defined values
        dx = Lx / Nx;
        dt = BigC * dx / LittleC;

        // Precalculate the x and y values of the points
        xs = new float[Nx]; // TODO see if this needs a plus 1?
        ys = new float[Ny];
        for (int i = 0; i < Nx; i++)
        {
            xs[i] = dx * i;
        }

        for (int i = 0; i < Ny; i++)
        {
            ys[i] = dx * i;
        }

        SetInitialConditions();
        SetShaderParameters();


    }

    void OnDisable()
    {
        if (u_buffer != null)
        {
            u_buffer.Release();
            u_buffer = null;
        }

        if (xs_buffer != null)
        {
            xs_buffer.Release();
            xs_buffer = null;
        }

        if (ys_buffer != null)
        {
            ys_buffer.Release();
            ys_buffer = null;
            
        }

    }


    void SetShaderParameters()
    {
        u_buffer = new ComputeBuffer(Nx * Ny, sizeof(float));
        u_buffer.SetData(u);

        xs_buffer = new ComputeBuffer(Nx * Ny, sizeof(float));
        xs_buffer.SetData(u);

        ys_buffer = new ComputeBuffer(Nx * Ny, sizeof(float));
        ys_buffer.SetData(u);

        renderMaterial.SetBuffer("displacements", u_buffer);
        renderMaterial.SetBuffer("xs", xs_buffer);
        renderMaterial.SetBuffer("ys", ys_buffer);

        renderMaterial.SetFloat("Lx", Lx);
        renderMaterial.SetFloat("Ly", Ny * dx);
        renderMaterial.SetFloat("Nx", Nx);
        renderMaterial.SetFloat("Ny", Ny);
        renderMaterial.SetFloat("dx", dx);
        renderMaterial.SetFloat("dy", dx); // TODO add a separate dy variable
        
        renderMaterial.SetFloat("min", min);
        renderMaterial.SetFloat("max", max);
    }

    // Update is called once per frame
    void Update()
    {
        if (Input.GetKey(KeyCode.W)) {
            transform.Translate(Vector3.up * Time.deltaTime * cameraSpeed);
        }

        if (Input.GetKey(KeyCode.A)) {
            transform.Translate(Vector3.left * Time.deltaTime * cameraSpeed);
        }

        if (Input.GetKey(KeyCode.S)) {
            transform.Translate(Vector3.down * Time.deltaTime * cameraSpeed);
        }

        if (Input.GetKey(KeyCode.D)) {
            transform.Translate(Vector3.right * Time.deltaTime * cameraSpeed);
        }
    }

}
