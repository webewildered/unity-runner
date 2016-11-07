Shader "Unlit/Retro"
{
	Properties
	{
		_MainTex ("Texture", 2D) = "white" {}
		_Color ("Color", Color) = (0.66, 0, 1, 1)
		_ShadeDarkness ("ShadeDarkness", Float) = 0.3
		_LightDirection ("LightDirection", Vector) = (0, 0, 0, 0)
		_EdgePriority("EdgePriority", Float) = 0.0 // Used by Edge.shader
	}
	SubShader
	{
		Tags { "RenderType"="Opaque" }
		LOD 100

		Pass
		{
			ZWrite On

			CGPROGRAM
			#pragma vertex vert
			#pragma fragment frag
			
			#include "UnityCG.cginc"

			float4 _Color;
			float _ShadeDarkness;
			float4 _LightDirection;

			struct appdata
			{
				float4 vertex : POSITION;
				float3 normal : NORMAL;
			};

			struct v2f
			{
				float4 vertex : SV_POSITION;
				float3 normal : NORMAL;
				float3 position : TEXCOORD0;
			};

			sampler2D _MainTex;
			float4 _MainTex_ST;
			
			v2f vert (appdata v)
			{
				v2f o;
				o.vertex = mul(UNITY_MATRIX_MVP, v.vertex);
				o.normal = mul(unity_ObjectToWorld, v.normal);
				o.position = o.vertex.xyz;
				return o;
			}
			
			fixed4 frag (v2f i) : SV_Target
			{
				// Shading
				// Splits L.N values into three discrete bands
				//  0 if L.N <= lowShade -> no shading
				//  1 if lowShade < L.N <= highShade -> 50% shading (dither)
				//  2 if highShade < L.N -> full shading
				const float lowShade = 0.5;
				const float highShade = 0.7;
				float lightDotNormal = dot(_LightDirection, normalize(i.normal));	// TODO do we need to normalize i.normal?
				float shade = (lightDotNormal - lowShade) / (highShade - lowShade);	// <0 if dot<low, 0 to 1 if between low and high, >1 if over high
				shade = clamp(ceil(shade), 0, 2);	// 0 if dot<low, 1 if between low and high, 2 if over high
				float bias = ((i.position.x + i.position.y - 1.0) % 2.0 );	// 0 or 1 for every other pixel
				float shadeFactor = floor((shade + bias) / 2.0);	// darken every pixel with shade=2, every other pixel with shade=1, no pixel with shade = 0

				// Highlighting
				const float minHighlight = 1.98f;
				const float highlight = floor((1.0 - lightDotNormal) / minHighlight);

				// sample the texture
				const float lighten = 0.0f;//0.8f;
				return saturate(_Color * (1 - shadeFactor * _ShadeDarkness) + lighten * float4(highlight, highlight, highlight, highlight));
			}
			ENDCG
		}
	}
	Fallback "Diffuse"
}
