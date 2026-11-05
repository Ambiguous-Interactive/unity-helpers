Shader "Wallstop Studios/Unity Helpers/Bendable Line"
{
    Properties
    {
        _MainTex("Sprite", 2D) = "white" {}
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
            float4 _UvBounds;

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
                return tex2D(_MainTex, input.uv);
            }
            ENDCG
        }
    }
    FallBack Off
}
