Shader "Unlit/Wave"
{
    Properties
    {
        _SolidColor ("Solid Color", Color) = (1,1,1,1) // Default to white

    }
    SubShader
    {
        Tags { "RenderType"="Opaque" }
        LOD 100

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag

            #include "UnityCG.cginc"

            float Lx;
            float Ly;
            float Nx;
            float Ny;
            float dx;
            float dy;

            float min;
            float max;

            StructuredBuffer<float> displacements;
            StructuredBuffer<float> xs;
            StructuredBuffer<float> ys;


            // Simple solution for troubleshooting
            int closestX (float x)
            {
                return round(x * Lx / dx);
            }

            int closestY (float y)
            {
                return round(y * Ly / dy);
            }

            struct appdata
            {
                float4 vertex : POSITION;
                float2 uv : TEXCOORD0;
            };

            struct v2f
            {
                float2 uv : TEXCOORD0;
                float4 vertex : SV_POSITION;
                float3 world : TEXCOORD1; // Pass world position to fragment shader
            };


            v2f vert (appdata v)
            {
                v2f o;
                o.vertex = UnityObjectToClipPos(v.vertex);
                o.uv = v.uv;
                return o;
            }

            fixed4 frag (v2f i) : SV_Target
            {
                int x = closestX(i.uv.x * Lx);
                int y = closestY(i.uv.y * Lx);

                float shade = displacements[x + Nx * y] - min / (max - min);

            // If outside range, return red or blue
            if (shade > 1.0) { return float4(1, 0, 0, 1); }
            if (shade < 0) { return float4(0, 0, 1, 1); }                

            return fixed4(shade,shade,shade,1);
            }

            ENDCG
        }
    }
}
