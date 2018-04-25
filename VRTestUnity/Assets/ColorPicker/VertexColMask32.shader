Shader "Custom/VertexColMask32"
{
	Properties
	{
	}
	SubShader
	{
		Tags { "RenderType"="Opaque" "Queue"="Geometry+1" }
		LOD 100
        Offset 0.2, 0.2

		Pass
		{
            Stencil {
                Ref 32
                ReadMask 32
                Comp notequal
            }

			CGPROGRAM
			#pragma vertex vert
			#pragma fragment frag
			
			struct appdata
			{
				float4 vertex : POSITION;
                fixed4 vertexColor : COLOR;
			};

			struct v2f
			{
				float4 vertex : SV_POSITION;
                fixed4 color : COLOR;
			};

			v2f vert (appdata v)
			{
				v2f o;
				o.vertex = UnityObjectToClipPos(v.vertex);
                o.color = v.vertexColor;    /* sRGB */
				return o;
			}
			
			fixed4 frag (v2f i) : SV_Target
			{
				return pow(i.color, 2.2);   /* sRGB => Linear */
			}
			ENDCG
		}
	}
}
