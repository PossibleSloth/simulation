using System;
using UnityEditor.PackageManager.UI;
using UnityEngine;

public class Wave1D : MonoBehaviour
{
    public enum StartingConditions { Random, HalfAndHalf, Line, Point, Sine }
    public StartingConditions startingConditions;

    public enum BoundaryConditions { Zero }
    public BoundaryConditions boundaryConditions;

    public int WindowWidth = 10;
    public int WindowHeight = 10;

    public float MaxAmplitude = 1;

    public float C;
    private float C2; // precompute C squared

    [Range(0.0f, 1f)]
    public float StartingRatio;


    private Rect positionRect;
    private Texture2D texture;

    private float[,] grid;
    private float[,] grid_1;
    private float[,] grid_2;

    void Start()
    {
        C2 = Mathf.Pow(C, 2);

        grid = new float[WindowWidth, WindowHeight];
        grid_1 = new float[WindowWidth, WindowHeight];
        grid_2 = new float[WindowWidth, WindowHeight];

        positionRect = new(0, 0, WindowWidth, WindowHeight);

        texture = new(WindowWidth, WindowHeight);

        switch (startingConditions)
        {
            case StartingConditions.Random:
                Randomize();
                break;
            case StartingConditions.HalfAndHalf:
                HalfAndHalf();
                break;
            case StartingConditions.Line:
                Line();
                break;
            case StartingConditions.Point:
                Point();
                break;
            case StartingConditions.Sine:
                Sine();
                break;
        }

        FirstStep();
        (grid_2, grid_1) = (grid_1, grid);
    }


    void Randomize()
    {
        for (int x = 0; x < WindowWidth; x++)
        {
            float value = UnityEngine.Random.value * MaxAmplitude;
            for (int y = 0; y < WindowHeight; y++)
            {
                grid_1[x, y] = value;
            }
        }
    }

    void Line()
    {
        for (int x = 0; x < WindowWidth; x++)
        {
            float value;
            if (x > WindowWidth / 2)
            {
                value = (WindowWidth - x) / (float)WindowWidth * MaxAmplitude;
            } else
            {
                value = x / (float)WindowWidth * MaxAmplitude;
            }
            for (int y = 0; y < WindowHeight; y++)
            {
                grid_1[x, y] = value;
            }
        }
    }

    void Point()
    {
        for (int x = 0; x < WindowWidth; x++)
        {
            float value;
            if (x == WindowWidth / 2)
            {
                value = MaxAmplitude;
            } else
            {
                value = 0;
            }
            for (int y = 0; y < WindowHeight; y++)
            {
                grid_1[x, y] = value;
            }
        }
    }

    void Sine()
    {
        for (int x = 0; x < WindowWidth; x++)
        {
            float value;
            if (x == 0 || x == WindowWidth - 1)
                value = 0;
            else
                value = Mathf.Sin(Mathf.PI * x / (WindowWidth - 1)) * MaxAmplitude;
            for (int y = 0; y < WindowHeight; y++)
            {
                grid_1[x, y] = value;
            }
            Debug.Log($"x={x}, value={value}");
        }
    }

    void HalfAndHalf()
    {
        for (int x = 0; x < WindowWidth; x++)
        {
            for (int y = 0; y < WindowHeight; y++)
            {
                grid_1[x, y] = x > WindowWidth / 2 ? MaxAmplitude : 0;
            }
        }
    }

    void UpdateTexture()
    {
        for (int x = 0; x < WindowWidth; x++)
        {
            for (int y = 0; y < WindowHeight; y++)
            {
                Color pixelColor;
                if (grid[x, y] >= 0)
                {
                    pixelColor = new Color(0, 0, grid[x, y] / MaxAmplitude);
                } else
                {
                    pixelColor = new Color(-grid[x, y] / MaxAmplitude, 0, 0);
                }
                texture.SetPixel(x, y, pixelColor);
            }
        }
        texture.Apply();
    }

    void OnGUI()
    {
        if (Event.current.type.Equals(EventType.Repaint))
        {
            Graphics.DrawTexture(positionRect, texture);
        }
    }

    void FirstStep()
    {
        for (int x = 0; x < WindowWidth; x++)
        {
            float xValue = 0;
            // Boundary conditions
            if (x == 0 || x == WindowWidth - 1)
            {
                if (boundaryConditions == BoundaryConditions.Zero)
                {
                    xValue = 0;
                }
            } else
            {
                // u[i] = u_1[i] - 0.5*C**2(u_1[i+1] - 2*u_1[i] + u_1[i-1])
                xValue = grid_1[x, 0] - 0.5f * C2 * (grid_1[x+1, 0] - 2 * grid_1[x, 0] + grid_1[x - 1, 0]);
            }

            for (int y = 0; y < WindowHeight; y++)
            {
                grid[x, y] = xValue;
            }
        }
    }

    void NextStep()
    {
        for (int x = 0; x < WindowWidth; x++)
        {
            float xValue = 0;
            // Boundary conditions
            if (x == 0 || x == WindowWidth - 1)
            {
                if (boundaryConditions == BoundaryConditions.Zero)
                {
                    xValue = 0;
                }
            } else
            {
                // u[i] = 2u_1[i] - u_2[i] + C**2(u_1[i+1] - 2*u_1[i] + u_1[i-1])
                xValue = 2 * grid_1[x, 0] - grid_2[x, 0] + C2 * (grid_1[x + 1, 0] - 2 * grid_1[x, 0] + grid_1[x - 1, 0]) ;
            }

            for (int y = 0; y < WindowHeight; y++)
            {
                grid[x, y] = xValue;
            }
        }
    }

    void Update()
    {
        NextStep();
        UpdateTexture();
        (grid_2, grid_1) = (grid_1, grid);
    }
}

