Shader "BlueCafe/BakedTexture"
{
    Properties { _MainTex ("Native texture", 2D) = "white" {} _Color ("Tint", Color) = (1,1,1,1) _Cutoff ("Alpha cutoff", Range(0,1)) = 0.08 _SrcBlend ("Source blend", Float) = 1 _DstBlend ("Destination blend", Float) = 0 _ZWrite ("Depth write", Float) = 1 _UseAlpha ("Texture alpha is opacity", Float) = 1 _PreviewOpacity ("Placement preview opacity", Range(0,1)) = 1 _Cull ("Cull", Float) = 0 _ZTest ("Depth test", Float) = 4 _StencilRef ("Stencil reference", Float) = 0 _StencilComp ("Stencil comparison", Float) = 8 _StencilPass ("Stencil pass", Float) = 0 }
    SubShader {
        Tags { "Queue"="AlphaTest" "RenderType"="TransparentCutout" }
        Cull [_Cull]
        ZTest [_ZTest]
        Stencil { Ref [_StencilRef] Comp [_StencilComp] Pass [_StencilPass] }
        Blend [_SrcBlend] [_DstBlend]
        ZWrite [_ZWrite]
        Pass {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"
            struct appdata { float4 vertex : POSITION; float2 uv : TEXCOORD0; };
            struct v2f { float4 vertex : SV_POSITION; float2 uv : TEXCOORD0; };
            sampler2D _MainTex; float4 _MainTex_ST; fixed4 _Color; fixed _Cutoff; float _UseAlpha; float _PreviewOpacity;
            v2f vert(appdata v) { v2f o; o.vertex = UnityObjectToClipPos(v.vertex); o.uv = TRANSFORM_TEX(v.uv,_MainTex); return o; }
            fixed4 frag(v2f i) : SV_Target { fixed4 col = tex2D(_MainTex,i.uv) * _Color; col.a = lerp(1.0, col.a, _UseAlpha); clip(col.a - _Cutoff); col.a *= _PreviewOpacity; return col; }
            ENDCG
        }
    }
}
