using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace VXGI_URP
{
    public enum AntiAliasing { X1 = 1, X2 = 2, X4 = 4, X8 = 8 }
    public enum Resolution {
        [InspectorName("Low (32^3)")] Low = 32,
        [InspectorName("Medium (64^3)")] Medium = 64,
        [InspectorName("High (128^3)")] High = 128,
        [InspectorName("VeryHigh (256^3)")] VeryHigh = 256
    }
}