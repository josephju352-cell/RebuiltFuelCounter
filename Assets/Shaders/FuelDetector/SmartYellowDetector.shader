Shader "Hidden/FuelDetector/SmartYellowDetector"
{
    Properties
    {
        _MainTex ("Main Texture", 2D) = "white" {}
        _BackTex ("Background Texture", 2D) = "black" {}
        _YellowSens ("Yellow Sensitivity", Range(0,1)) = 0.2
        _MotionSens ("Motion Sensitivity", Range(0,1)) = 0.1
        _MinBrightness ("Min Brightness", Range(0,1)) = 0.1
        _ROI ("Region of Interest", Vector) = (0,0,1,1) // xMin, yMin, xMax, yMax
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

            struct appdata
            {
                float4 vertex : POSITION;
                float2 uv : TEXCOORD0;
            };

            struct v2f
            {
                float2 uv : TEXCOORD0;
                float4 vertex : SV_POSITION;
            };

            sampler2D _MainTex;
            sampler2D _BackTex;
            fixed _YellowSens;
            fixed _MotionSens;
            fixed _MinBrightness;
            float4 _ROI; // xMin, yMin, xMax, yMax

            v2f vert (appdata v)
            {
                v2f o;
                o.vertex = UnityObjectToClipPos(v.vertex);
                o.uv = v.uv; // No TRANSFORM_TEX needed usually for blits
                return o;
            }

            fixed CalculateYellowScore(fixed4 col)
            {
                // Score = (R + G) - (2.0 * B)
                return (col.r + col.g) - (2.0 * col.b);
            }

            fixed4 frag (v2f i) : SV_Target
            {
                // 1. ROI Check
                if (i.uv.x < _ROI.x || i.uv.x > _ROI.z || i.uv.y < _ROI.y || i.uv.y > _ROI.w)
                {
                    return fixed4(0,0,0,1);
                }

                fixed4 current = tex2D(_MainTex, i.uv);

                // 2. Brightness Gate (Luma check)
                fixed luma = dot(current.rgb, fixed3(0.299, 0.587, 0.114));
                if (luma < _MinBrightness)
                {
                    return fixed4(0,0,0,1);
                }

                fixed4 background = tex2D(_BackTex, i.uv);

                // 3. Motion Gate
                fixed dist = distance(current.rgb, background.rgb);
                if (dist < _MotionSens)
                {
                    return fixed4(0,0,0,1);
                }

                // 4. Yellow Dominance Score
                fixed currentScore = CalculateYellowScore(current);
                fixed backScore = CalculateYellowScore(background);

                // 5. Delta Check
                fixed delta = currentScore - backScore;

                if (delta > _YellowSens)
                {
                    return fixed4(1,1,1,1); // White
                }

                return fixed4(0,0,0,1); // Black
            }
            ENDCG
        }
    }
}
