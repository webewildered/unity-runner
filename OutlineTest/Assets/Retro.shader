﻿// Upgrade NOTE: replaced '_Object2World' with 'unity_ObjectToWorld'

Shader "Unlit/Retro"
{
	Properties
	{
		_MainTex ("Texture", 2D) = "white" {}
		_LightDirection ("LightDirection", Vector) = (0, 0, 0, 0)
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
			// make fog work
			#pragma multi_compile_fog
			
			#include "UnityCG.cginc"

			float4 _LightDirection;

			struct appdata
			{
				float4 vertex : POSITION;
				float3 normal : NORMAL;
				float2 uv : TEXCOORD0;
			};

			struct v2f
			{
				float2 uv : TEXCOORD0;
				UNITY_FOG_COORDS(1)
				float4 vertex : SV_POSITION;
				float3 normal : NORMAL;
			};

			sampler2D _MainTex;
			float4 _MainTex_ST;
			
			v2f vert (appdata v)
			{
				v2f o;
				o.vertex = mul(UNITY_MATRIX_MVP, v.vertex);
				o.uv = TRANSFORM_TEX(v.uv, _MainTex);
				o.normal = mul(unity_ObjectToWorld, v.normal);
				UNITY_TRANSFER_FOG(o,o.vertex);
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
				float bias = ((i.vertex.x + i.vertex.y - 1.0) % 2.0 );	// 0 or 1 for every other pixel
				float shadeFactor = floor((shade + bias) / 2.0);	// darken every pixel with shade=2, every other pixel with shade=1, no pixel with shade = 0
				shadeFactor = 0.0f; // TEMP TEST

				// Highlighting
				const float minHighlight = 1.98f;
				const float highlight = floor((1.0 - lightDotNormal) / minHighlight);

				// sample the texture
				const fixed4 col = fixed4(0.66, 0, 1.0, 1.0);
				const float darken = 0.3f;
				const float lighten = 0.0f;//0.8f;
				return saturate(col * (1-shadeFactor*darken) + lighten * float4(highlight, highlight, highlight, highlight));
			}
			ENDCG
		}
	}
	Fallback "Diffuse"
}
