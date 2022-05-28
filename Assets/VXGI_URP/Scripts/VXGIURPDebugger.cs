using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
using UnityEngine.Experimental.Rendering.Universal;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using VXGI_URP;

[ExecuteAlways]
public class VXGIURPDebugger : MonoBehaviour
{
    [Min(0.001f), Tooltip("The size of the voxel volume in World Space.")]
    public float bound = 10f;
    
    private VXGI_URP_Feature m_Feature;

    private void OnEnable()
    {
        GetVXGIFeature();
        bound = m_Feature.bound;
    }

    private void Update()
    {
        m_Feature.bound = bound;
    }

    void OnDrawGizmosSelected() {
        Gizmos.color = Color.green;
        Gizmos.DrawWireCube(m_Feature.voxelSpaceCenter, Vector3.one * m_Feature.bound);
    }

    void GetVXGIFeature()
    {
        UniversalRenderPipelineAsset pipeline = ((UniversalRenderPipelineAsset)GraphicsSettings.renderPipelineAsset);
        FieldInfo propertyInfo = pipeline.GetType().GetField("m_RendererDataList", BindingFlags.Instance | BindingFlags.NonPublic);
        ScriptableRendererData scriptableRendererData = ((ScriptableRendererData[])propertyInfo?.GetValue(pipeline))?[0];//default get the first renderer 

        // VXGI_URP_Feature feature = null;
        foreach (var rendererFeature in scriptableRendererData.rendererFeatures)
        {
            if (rendererFeature is VXGI_URP_Feature)
            {
                m_Feature = (VXGI_URP_Feature)rendererFeature;
                break;
            }
        }
    }
}
