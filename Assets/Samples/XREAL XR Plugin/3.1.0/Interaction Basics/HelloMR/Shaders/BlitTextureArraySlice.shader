Shader "Hidden/XREAL/BlitTextureArraySlice"
{
    Properties
    {
        _MainTex ("Texture", 2DArray) = "" {}
        _Slice ("Slice", Int) = 0
    }
    SubShader
    {
        Pass
        {
            ZTest Always
            ZWrite Off
            Cull Off

            CGPROGRAM
            #pragma vertex vert_img
            #pragma fragment frag
            #include "UnityCG.cginc"

            UNITY_DECLARE_TEX2DARRAY(_MainTex);
            int _Slice;

            fixed4 frag(v2f_img i) : SV_Target
            {
                return UNITY_SAMPLE_TEX2DARRAY(_MainTex, float3(i.uv, _Slice));
            }
            ENDCG
        }
    }
}
