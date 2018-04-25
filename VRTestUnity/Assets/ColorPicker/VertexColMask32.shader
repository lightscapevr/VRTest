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

    // To receive or cast a shadow, shaders must implement the appropriate "Shadow Collector" or "Shadow Caster" pass.
    // Although we haven't explicitly done so in this shader, if these passes are missing they will be read from a fallback
    // shader instead, so specify one here to import the collector/caster passes used in that fallback.
    // In this case, we just want the "Shadow Caster" pass, as we don't do anything with shadow information ourselves.
    Fallback "VertexLit"
}
