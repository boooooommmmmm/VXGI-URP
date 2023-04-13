#ifndef VXGI_RADIANCES_VOXEL
  #define VXGI_RADIANCES_VOXEL

#include "../Utilities/Variables.cginc"
#include "../Utilities/Utilities.cginc"
#include "../Utilities/Visibility.cginc"
#include "../Radiances/Sampler.cginc"
#include "../Structs/VoxelLightingData.hlsl"

  float3 DirectVoxelRadiance(VoxelLightingData data)
  {
    float3 radiance = 0.0;

    for (uint i = 0; i < LightCount; i++) {
      LightSource lightSource = LightSources[i];

      bool notInRange;
      float3 localPosition;

      //By default, there is only one direction light. The light type will not change during a draw call.
      //hence, we add a branch attribute to fast GPU branch judgement.
      [branch]
      if (lightSource.type == LIGHT_SOURCE_TYPE_DIRECTIONAL) {
        localPosition = -lightSource.direction;
        notInRange = false;
        lightSource.voxelPosition = mad(localPosition, Resolution << 1, data.voxelPosition);
      } else {
        localPosition = lightSource.worldposition - data.worldPosition;
        notInRange = lightSource.NotInRange(localPosition);
      }

      data.Prepare(normalize(localPosition));//save light direction and calculate normal dot light.

      float spotFalloff = lightSource.SpotFalloff(-data.vecL); //return 1 if light type is directional

      if (notInRange || (spotFalloff <= 0.0) || (data.NdotL <= 0.0)) continue;

      radiance +=
        //(data.vecL / data.NdotL) is light dir divided by cosine theta of light dir and normal dir.
        //the smaller of the cosine theta means lights should travel from further position to current position to detect visiblity.
        //Smaller cosine means light may travel further, and gather more light from voxel, I don't think it has physics behind.
        VXGI_VoxelVisibility((data.voxelPosition + data.vecL / data.NdotL) / Resolution, lightSource.voxelPosition / Resolution)
        * data.NdotL
        * spotFalloff
        * lightSource.Attenuation(localPosition); //Color comes from here: color / localPosition^2
    }

    return radiance;
  }

  float3 IndirectVoxelRadiance(VoxelLightingData data)
  {
    if (TextureSDF(data.voxelPosition / Resolution) < 0.0) return 0.0;

    float3 apex = mad(0.5, data.vecN, data.voxelPosition) / Resolution;
    float3 radiance = 0.0;
    uint cones = 0;

    //[unroll] //shader code may too long. Will discard more than 66% of cone trace.
    for (uint i = 0; i < 32; i++) {
      float3 unit = Directions[i];
      float NdotL = dot(data.vecN, unit);

      if (NdotL < ConeDirectionThreshold) continue; //ConeDirectionThreshold = sin(atan(1.0/3.0));

      float4 incoming = 0.0;
      float size = 1.0;
      float3 direction = 1.5 * size * unit / Resolution; //Why times 1.5? unit already be normalized...

      //Size will be converted to mip level
      //size and direction multiples 2 not size += sizeBase or direction += normalize(direction)
      //is because higher level of mipmap can cover the same distance than sum of previous distance. 
      for (
        float3 coordinate = apex + direction;
        incoming.a < 0.95 && TextureSDF(coordinate) > 0.0;//use sdf to ensure position is in side of voxel bound
        size *= 2, direction *= 2.0, coordinate = apex + direction
      ) {
        incoming += 0.5 * (1.0 - incoming.a) * SampleRadiance(coordinate, size);//alpha channel is Opacity/Visibility
      }

      radiance += incoming.rgb * NdotL;
      cones++;
    }

    return radiance / cones;
  }

  float3 VoxelRadiance(VoxelLightingData data)
  {
    return data.color * (DirectVoxelRadiance(data) + IndirectVoxelRadiance(data));
  }
#endif
