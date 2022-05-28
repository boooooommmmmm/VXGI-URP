using System.Collections;
using System.Collections.Generic;
using Mono.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

public static class VXGI_Definition
{
    public static readonly ReadOnlyCollection<LightType> supportedLightTypes = new ReadOnlyCollection<LightType>(new[] { LightType.Point, LightType.Directional, LightType.Spot });
    public static bool isD3D11Supported => _D3D11DeviceType.Contains(SystemInfo.graphicsDeviceType);

    static readonly ReadOnlyCollection<GraphicsDeviceType> _D3D11DeviceType =
        new ReadOnlyCollection<GraphicsDeviceType>(new[]
        {
            GraphicsDeviceType.Direct3D11,
            GraphicsDeviceType.Direct3D12,
            GraphicsDeviceType.XboxOne,
            GraphicsDeviceType.XboxOneD3D12
        });
}

public enum LightingPass {
    Emission = 0,
    DirectDiffuseSpecular = 1,
    IndirectDiffuse = 2,
    IndirectSpecular = 3
}