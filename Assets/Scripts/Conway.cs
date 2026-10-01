using System;
using System.Threading;
using Unity.Collections;
using UnityEditor.PackageManager.UI;
using UnityEditor.UI;
using UnityEngine;

public class Controller : MonoBehaviour
{
    public int WindowWidth = 10;
    
    public int WindowHeight = 10;

    [Range(0.0f, 1f)]
    public float StartingRatio;
    public ComputeShader computeShader;


    private Rect positionRect;
    private RenderTexture texture;
    public int[] grid;

    private ComputeBuffer StateBuffer;
    private ComputeBuffer NextStateBuffer;

    void Start()
    {
        grid = new int[WindowWidth * WindowHeight];
        positionRect = new(0, 0, WindowWidth, WindowHeight);

        texture = new(WindowWidth, WindowHeight, 0);
        texture.enableRandomWrite = true;
        texture.Create();


        StateBuffer = new ComputeBuffer(WindowWidth * WindowHeight, sizeof(uint));
        NextStateBuffer = new ComputeBuffer(WindowWidth * WindowHeight, sizeof(uint));

        Randomize();
    }

    void OnDisable()
    {
        // Release the buffer when no longer needed
        if (StateBuffer != null)
        {
            StateBuffer.Release();
            StateBuffer = null;
        }

        if (NextStateBuffer != null)
        {
            NextStateBuffer.Release();
            NextStateBuffer = null;
        }

        if (texture != null)
        {
            texture.Release();
            texture = null;
        }

    }

    void Randomize()
    {
        for (int x = 0; x < WindowWidth; x++)
        {
            for (int y = 0; y < WindowHeight; y++)
            {
                grid[x + WindowWidth * y] = UnityEngine.Random.value > StartingRatio ? 1 : 0;
            }
        }
    }

    void Fill()
    {
        for (int i = 0; i < WindowWidth * WindowHeight; i++)
        {
            grid[i] = 1;
        }
    }

    // void UpdateTexture()
    // {
    //     for (int x = 0; x < WindowWidth; x++)
    //     {
    //         for (int y = 0; y < WindowHeight; y++)
    //         {
    //             if (grid[x + WindowWidth *  y] == 1)
    //             {
    //                 texture.SetPixel(x, y, Color.black);
    //             }
    //             else
    //             {
    //                 texture.SetPixel(x, y, Color.white);
    //             }
    //         }
    //     }
    //     texture.Apply();
    // }

    // void OnGUI()
    // {
    //     if (Event.current.type.Equals(EventType.Repaint))
    //     {
    //         Graphics.DrawTexture(positionRect, texture);
    //     }
    // }

    // void ApplyRules()
    // {

    //     for (int x = 0; x < WindowWidth; x++)
    //     {
    //         for (int y = 0; y < WindowHeight; y++)
    //         {
    //             int neighborsAlive = 0;
                
    //             for (int n = 0; n < 9; n++)
    //             {
    //                 int neighbor_x = n % 3 - 1;
    //                 int neighbor_y = n / 3 - 1;

    //                 if (neighbor_x == 0 && neighbor_y == 0)
    //                 {
    //                     continue;
    //                 }

    //                 if (x + neighbor_x < 0 || y + neighbor_y < 0 || x + neighbor_x >= WindowWidth || y + neighbor_y >= WindowHeight)
    //                 {
    //                     continue;
    //                 }

    //                 if (grid[x + neighbor_x, y + neighbor_y])
    //                 {
    //                     neighborsAlive++;
    //                 }
    //             }

    //             if (grid[x, y] && (neighborsAlive < 2 || neighborsAlive > 3))
    //             {
    //                 nextGrid[x, y] = false;
    //             } else if (!grid[x, y] && neighborsAlive == 3)
    //             {
    //                 nextGrid[x, y] = true;
    //             } else
    //             {
    //                 nextGrid[x, y] = grid[x, y];
    //             }

    //         }
    //     }
    //     (nextGrid, grid) = (grid, nextGrid);
    // }

    void Update()
    {        
        int kernelID = computeShader.FindKernel("NextStep");
        StateBuffer.SetData(grid);

        computeShader.SetBuffer(kernelID, "State", StateBuffer);
        computeShader.SetBuffer(kernelID, "NextState", NextStateBuffer);
        computeShader.SetTexture(kernelID, "Result", texture);

        computeShader.SetInt("WindowWidth", WindowWidth);
        computeShader.SetInt("WindowHeight", WindowHeight);

        computeShader.Dispatch(kernelID, WindowWidth / 32, WindowHeight / 32, 1);

        NextStateBuffer.GetData(grid);
        Graphics.DrawTexture(positionRect, texture);
        
    }
}

