Shader "Hidden/RetroPost"
{
	Properties
	{
		_MainTex ("Texture", 2D) = "white" {}
		_EdgeTexture("EdgeTexture", 2D) = "white" {}
		_InvScreenWidth ("InvScreenWidth", Float) = 0
		_InvScreenHeight ("InvScreenHeight", Float) = 0
	}
	SubShader
	{
		// No culling or depth
		Cull Off ZWrite Off ZTest Always

		Pass
		{
			CGPROGRAM
			#pragma enable_d3d11_debug_symbols
			#pragma vertex vert
			#pragma fragment frag
			
			#include "UnityCG.cginc"

			float _InvScreenWidth;
			float _InvScreenHeight;
			float4x4 _InvProjection;

			sampler2D _MainTex;
			sampler2D _EdgeTexture;
			sampler2D _CameraDepthNormalsTexture;

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

			v2f vert (appdata v)
			{
				v2f o;
				o.vertex = mul(UNITY_MATRIX_MVP, v.vertex);
				o.uv = v.uv;
				return o;
			}

			struct ViewPixel
			{
				float3 position;
				float3 normal;
				float priority;
			};

			ViewPixel getViewPixel(float2 texCoord)
			{
				ViewPixel vpx;
				float rawDepth;
				DecodeDepthNormal(tex2D(_CameraDepthNormalsTexture, texCoord), rawDepth, vpx.normal);

				// Depth texture contains -z / farPlane, _ProjectionParams.z = farPlane
				float minusZ = _ProjectionParams.z * rawDepth;

				// we don't really need the full inv projection matrix, just 1,1 and 2,2
				float2 clipCoord = texCoord * 2.0 - float2(1.0, 1.0);
				float4 pos = mul(_InvProjection, float4(-clipCoord.x, clipCoord.y, 0.0, 1.0)); // why -x? works but not sure. left hand / right hand thing maybe?
				vpx.position = float3(pos.xy, 1.0f) * -minusZ;
				
				vpx.priority = tex2D(_EdgeTexture, texCoord).x;

				return vpx;
			}

			// returns 1.0 if pixels are different enough to have an outline between them, 0.0 otherwise.
			float diffViewPixel(ViewPixel vpxA, ViewPixel vpxB)
			{
				// Check difference in position along the normal, draws edges between non-continuous surfaces
				float posDiff = abs(dot(vpxA.position - vpxB.position, vpxA.normal));

				// Check difference in normals, draws edges at sharp turns in a continuous surface
				float3 crossNormal = cross(vpxA.normal, vpxB.normal);
				float sinNormalAngle = length(crossNormal);
				float normDiff = sinNormalAngle * sinNormalAngle * sinNormalAngle;

				// Never outline against a higher priority neighbor
				// Possible input priorities are 0, 0.5, 1; priority is 1.0 if A.priority >= B.priority, else 0.0
				float priority = ceil(((vpxA.priority - vpxB.priority) + 0.1) / 1.2);

				float diff = max(posDiff, normDiff) * priority;
				const float threshold = 0.5;
				return floor(saturate(diff / threshold));
			}

			// Returns 1.0 if priority is greater than the one at texCoord, otherwise 0.0
			float diffPriority(float priority, float2 texCoord)
			{
				// Possible input priorities are 0, 0.5, 1
				float priorityB = tex2D(_EdgeTexture, texCoord).x;
				return max(0, ceil(priority - priorityB));
			}

			fixed4 frag (v2f i) : SV_Target
			{
				// Debug - draw viewspace normals
				//float4 dn = tex2D(_CameraDepthNormalsTexture, i.uv);
				//return fixed4(dn.r, dn.g, 0, 1);

				float2 coordLeft = i.uv + float2(-_InvScreenWidth, 0);
				float2 coordUp = i.uv + float2(0, _InvScreenHeight);
				float2 coordUpLeft = i.uv + float2(-_InvScreenWidth, _InvScreenHeight);
				float2 coordRight = i.uv + float2(_InvScreenWidth, 0);
				float2 coordDown = i.uv + float2(0, -_InvScreenHeight);
				
				// Depth texture samples
				ViewPixel vpx = getViewPixel(i.uv);
				ViewPixel vpxLeft = getViewPixel(coordLeft);
				ViewPixel vpxUp = getViewPixel(coordUp);
				ViewPixel vpxUpLeft = getViewPixel(coordUpLeft);

				// 4 comparisons: this vs left, this vs up, this vs upleft, up vs upleft.
				// Pixel is outlined if left || (up && (upleft || upUpLeft)).
				// This produces a single pixel wide outline.
				float dUp = diffViewPixel(vpx, vpxUp);
				float dLeft = diffViewPixel(vpx, vpxLeft);
				float dUpLeft = diffViewPixel(vpx, vpxUpLeft);
				float dUpUpLeft = diffViewPixel(vpxUp, vpxUpLeft);

				// Pixel is also outlined if it's higher priority than a neighbor to the right or below,
				// since those neighbors won't outline against this pixel
				float pRight = diffPriority(vpx.priority, coordRight);
				float pDown = diffPriority(vpx.priority, coordDown);

				float edgeFactor = min(1.0, dLeft + dUp * (1.0 - dUpUpLeft + dUpLeft) + pRight + pDown);

				const float darken = 0.3f;
				fixed4 col = tex2D(_MainTex, i.uv);
				return fixed4(saturate(col.xyz * (1 - edgeFactor * darken)), 1);
			}
			ENDCG
		}
	}
}
