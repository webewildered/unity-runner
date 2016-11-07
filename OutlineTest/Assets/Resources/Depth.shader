Shader "Unlit/Depth"
{
	Properties
	{
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
			};

			struct v2f
			{
				float4 vertex : SV_POSITION;
				float depth : TEXCOORD0;
			};

			
			v2f vert (appdata v)
			{
				v2f o;
				o.vertex = UnityObjectToClipPos(v.vertex);
				o.depth = o.vertex.z / _ProjectionParams.z;
				return o;
			}
			
			fixed4 frag (v2f i) : SV_Target
			{
				float depth = i.depth;// Linear01Depth(i.depth);
				float4 c;
				c.xy = EncodeFloatRG(depth);
				c.zw = float2(0, 1);
				return c;
			}
			ENDCG
		}
	}
}
