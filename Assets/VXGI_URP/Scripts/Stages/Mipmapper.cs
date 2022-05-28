using UnityEngine;
using UnityEngine.Rendering;
using VXGI_URP;

public class Mipmapper {
  public enum Mode { Box = 0, Gaussian3x3x3 = 1, Gaussian4x4x4 = 2 }

  public ComputeShader compute {
    get {
      //if (_compute == null) _compute = (ComputeShader)Resources.Load("VXGI/Compute/Mipmapper");

      return _compute;
    }
  }

  const string _sampleFilter = "Filter.";
  const string _sampleShift = "Shift";

  int _kernelFilter;
  int _kernelShift;
  CommandBuffer cmd;
  ComputeShader _compute;
  NumThreads _threadsFilter;
  NumThreads _threadsShift;
  VXGI_URP_Feature _vxgi;

  public Mipmapper(VXGI_URP_Feature vxgi, ComputeShader computeShader) {
    _vxgi = vxgi;

    cmd = CommandBufferPool.Get("VXGI_URP.Mipmapper");
    _compute = computeShader;

    InitializeKernel();
  }

  public void Dispose() {
    cmd.Dispose();
  }

  public void Filter(ScriptableRenderContext renderContext) {
    UpdateKernel();

    var radiances = _vxgi.radiances;

    for (var i = 1; i < radiances.Length; i++) {
      int resolution = radiances[i].volumeDepth;

      cmd.BeginSample(_sampleFilter + _vxgi.mipmapFilterMode.ToString() + '.' + resolution.ToString("D3"));
      cmd.SetComputeIntParam(compute, ShaderIDs.Resolution, resolution);
      cmd.SetComputeTextureParam(compute, _kernelFilter, ShaderIDs.Source, radiances[i - 1]);
      cmd.SetComputeTextureParam(compute, _kernelFilter, ShaderIDs.Target, radiances[i]);
      cmd.DispatchCompute(compute, _kernelFilter,
         Mathf.CeilToInt((float)resolution /_threadsFilter.x),
         Mathf.CeilToInt((float)resolution /_threadsFilter.y),
         Mathf.CeilToInt((float)resolution /_threadsFilter.z)
      );
      cmd.EndSample(_sampleFilter + _vxgi.mipmapFilterMode.ToString() + '.' + resolution.ToString("D3"));
    }

    renderContext.ExecuteCommandBuffer(cmd);
    cmd.Clear();
  }

  public void Shift(ScriptableRenderContext renderContext, Vector3Int displacement) {
    UpdateKernel();

    cmd.BeginSample(_sampleShift);
    cmd.SetComputeIntParam(compute, ShaderIDs.Resolution, (int)_vxgi.resolution);
    cmd.SetComputeIntParams(compute, ShaderIDs.Displacement, new[] { displacement.x, displacement.y, displacement.z });
    cmd.SetComputeTextureParam(compute, _kernelShift, ShaderIDs.Target, _vxgi.radiances[0]);
    cmd.DispatchCompute(compute, _kernelShift,
      Mathf.CeilToInt((float)_vxgi.resolution / _threadsShift.x),
      Mathf.CeilToInt((float)_vxgi.resolution / _threadsShift.y),
      Mathf.CeilToInt((float)_vxgi.resolution / _threadsShift.z)
    );
    cmd.EndSample(_sampleShift);
    renderContext.ExecuteCommandBuffer(cmd);
    cmd.Clear();

    Filter(renderContext);
  }

  void InitializeKernel() {
    _kernelFilter = 2 * (int)_vxgi.mipmapFilterMode;

    if (!VXGI_Definition.isD3D11Supported) _kernelFilter += 1;

    _kernelShift = compute.FindKernel("CSShift");
    _threadsFilter = new NumThreads(compute, _kernelFilter);
    _threadsShift = new NumThreads(compute, _kernelShift);
  }

  [System.Diagnostics.Conditional("UNITY_EDITOR")]
  void UpdateKernel() {
    InitializeKernel();
  }
}
