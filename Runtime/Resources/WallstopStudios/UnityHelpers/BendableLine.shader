Shader "Wallstop Studios/Unity Helpers/Bendable Line"
{
    Properties
    {
        [MainTexture] _MainTex("Sprite", 2D) = "white" {}
        _UvBounds("Sprite UV Bounds", Vector) = (0,0,1,1)
    }
    SubShader
    {
        Tags { "Queue"="Transparent" "RenderType"="Transparent" }
        Blend SrcAlpha OneMinusSrcAlpha
        Cull Off
        ZWrite Off
        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 3.0
            #include "UnityCG.cginc"

            struct appdata
            {
                float4 vertex : POSITION;
                float2 uv : TEXCOORD0;
            };

            struct v2f
            {
                float4 position : SV_POSITION;
                float2 uv : TEXCOORD0;
            };

            sampler2D _MainTex;
            float4 _MainTex_TexelSize;
            float4 _UvBounds;
            static const float MinimumSampleLod = -16.0;

            v2f vert(appdata input)
            {
                v2f output;
                output.position = UnityObjectToClipPos(input.vertex);
                output.uv = input.uv;
                return output;
            }

            fixed4 frag(v2f input) : SV_Target
            {
                clip(input.uv - _UvBounds.xy);
                clip(_UvBounds.zw - input.uv);
                float2 halfTexel = abs(_MainTex_TexelSize.xy) * 0.5;
                float2 center = (_UvBounds.xy + _UvBounds.zw) * 0.5;
                float2 minimum = min(_UvBounds.xy + halfTexel, center);
                float2 maximum = max(_UvBounds.zw - halfTexel, center);
                float2 sampleUv = clamp(input.uv, minimum, maximum);
                return tex2Dlod(_MainTex, float4(sampleUv, 0, MinimumSampleLod));
            }
            ENDCG
        }
    }
    FallBack Off
}
