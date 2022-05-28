#ifndef VXGI_BRDFS_GENERAL
  #define VXGI_BRDFS_GENERAL

  #include "../BRDFs/Diffuse.cginc"
  #include "../BRDFs/Specular.cginc"
  #include "../Structs/LightingData.cginc"

  float3 GeneralBRDF(LightingData data)
  {
    //Add saturate for specular, there might have an API bug, add a temporal protection.
    // return DiffuseBRDF(data) + SpecularBRDF(data);
    return DiffuseBRDF(data) + saturate(SpecularBRDF(data));
  }
#endif
