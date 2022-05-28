using UnityEngine;
using UnityEngine.Rendering;
using VXGI_URP;

public class VoxelShader : System.IDisposable {
  public ComputeShader compute {
    get {
      if (_compute == null) _compute = (ComputeShader)Resources.Load("VXGI/Compute/VoxelShader");

      return _compute;
    }
  }

  const string sampleCleanup = "Cleanup";
  const string sampleComputeAggregate = "Compute.Aggregate";
  const string sampleComputeClear = "Compute.Clear";
  const string sampleComputeRender = "Compute.Render";
  const string sampleSetup = "Setup";

  int _kernelAggregate;
  int _kernelClear;
  int _kernelRender;
  CommandBuffer cmd;
  ComputeBuffer _arguments;
  ComputeBuffer _lightSources;
  ComputeShader _compute;
  NumThreads _threadsAggregate;
  NumThreads _threadsClear;
  NumThreads _threadsTrace;
  RenderTextureDescriptor _descriptor;
  VXGI_URP_Feature _vxgi;

  public VoxelShader(VXGI_URP_Feature vxgi, ComputeShader computeShader) {
    _vxgi = vxgi;

    cmd = CommandBufferPool.Get("VXGI_URP.VoxelShader");
    _compute = computeShader;

    _arguments = new ComputeBuffer(3, sizeof(int), ComputeBufferType.IndirectArguments);
    _arguments.SetData(new int[] { 1, 1, 1 });
    _lightSources = new ComputeBuffer(64, LightSource.size);

    _kernelAggregate = VXGI_Definition.isD3D11Supported ? 0 : 1;
    _kernelClear = compute.FindKernel("CSClear");
    _kernelRender = compute.FindKernel("CSRender");

    _threadsAggregate = new NumThreads(compute, _kernelAggregate);
    _threadsClear = new NumThreads(compute, _kernelClear);
    _threadsTrace = new NumThreads(compute, _kernelRender);

    _descriptor = new RenderTextureDescriptor() {
      colorFormat = RenderTextureFormat.RInt,
      dimension = TextureDimension.Tex3D,
      enableRandomWrite = true,
      msaaSamples = 1,
      sRGB = false
    };
  }

  public void Dispose() {
    _arguments.Dispose();
    cmd.Dispose();
    _lightSources.Dispose();
  }

  public void Render(ScriptableRenderContext renderContext) {
    Setup();
    ComputeClear();
    ComputeRender();
    ComputeAggregate();
    Cleanup();

    renderContext.ExecuteCommandBuffer(cmd);
    cmd.Clear();
  }

  void Cleanup()
  {
    cmd.BeginSample(sampleCleanup);

    cmd.ReleaseTemporaryRT(ShaderIDs.RadianceBA);
    cmd.ReleaseTemporaryRT(ShaderIDs.RadianceRG);
    cmd.ReleaseTemporaryRT(ShaderIDs.RadianceCount);

    cmd.EndSample(sampleCleanup);
  }

  void ComputeAggregate() {
    cmd.BeginSample(sampleComputeAggregate);

    cmd.SetComputeTextureParam(compute, _kernelAggregate, ShaderIDs.RadianceBA, ShaderIDs.RadianceBA);
    cmd.SetComputeTextureParam(compute, _kernelAggregate, ShaderIDs.RadianceRG, ShaderIDs.RadianceRG);
    cmd.SetComputeTextureParam(compute, _kernelAggregate, ShaderIDs.RadianceCount, ShaderIDs.RadianceCount);
    cmd.SetComputeTextureParam(compute, _kernelAggregate, ShaderIDs.Target, _vxgi.radiances[0]);
    cmd.DispatchCompute(compute, _kernelAggregate,
      Mathf.CeilToInt((float)_vxgi.resolution / _threadsAggregate.x),
      Mathf.CeilToInt((float)_vxgi.resolution / _threadsAggregate.y),
      Mathf.CeilToInt((float)_vxgi.resolution / _threadsAggregate.z)
    );

    cmd.EndSample(sampleComputeAggregate);
  }

  void ComputeClear() {
    cmd.BeginSample(sampleComputeClear);

    cmd.SetComputeTextureParam(compute, _kernelClear, ShaderIDs.RadianceBA, ShaderIDs.RadianceBA);
    cmd.SetComputeTextureParam(compute, _kernelClear, ShaderIDs.RadianceRG, ShaderIDs.RadianceRG);
    cmd.SetComputeTextureParam(compute, _kernelClear, ShaderIDs.RadianceCount, ShaderIDs.RadianceCount);
    cmd.DispatchCompute(compute, _kernelClear,
      Mathf.CeilToInt((float)_vxgi.resolution / _threadsClear.x),
      Mathf.CeilToInt((float)_vxgi.resolution / _threadsClear.y),
      Mathf.CeilToInt((float)_vxgi.resolution / _threadsClear.z)
    );

    cmd.EndSample(sampleComputeClear);
  }

  void ComputeRender() {
    cmd.BeginSample(sampleComputeRender);

    _lightSources.SetData(_vxgi.lights);

    cmd.SetComputeIntParam(compute, ShaderIDs.Resolution, (int)_vxgi.resolution);
    cmd.SetComputeIntParam(compute, ShaderIDs.LightCount, _vxgi.lights.Count);
    cmd.SetComputeBufferParam(compute, _kernelRender, ShaderIDs.LightSources, _lightSources);
    cmd.SetComputeBufferParam(compute, _kernelRender, ShaderIDs.VoxelBuffer, _vxgi.voxelBuffer);
    cmd.SetComputeMatrixParam(compute, ShaderIDs.VoxelToWorld, _vxgi.voxelToWorld);
    cmd.SetComputeMatrixParam(compute, ShaderIDs.WorldToVoxel, _vxgi.worldToVoxel);
    cmd.SetComputeTextureParam(compute, _kernelRender, ShaderIDs.RadianceBA, ShaderIDs.RadianceBA);
    cmd.SetComputeTextureParam(compute, _kernelRender, ShaderIDs.RadianceRG, ShaderIDs.RadianceRG);
    cmd.SetComputeTextureParam(compute, _kernelRender, ShaderIDs.RadianceCount, ShaderIDs.RadianceCount);

    for (var i = 0; i < 9; i++) {
      cmd.SetComputeTextureParam(compute, _kernelRender, ShaderIDs.Radiance[i], _vxgi.radiances[Mathf.Min(i, _vxgi.radiances.Length - 1)]);
    }

    cmd.CopyCounterValue(_vxgi.voxelBuffer, _arguments, 0);
    _vxgi.parameterizer.Parameterize(cmd, _arguments, _threadsTrace);
    cmd.DispatchCompute(compute, _kernelRender, _arguments, 0);

    cmd.EndSample(sampleComputeRender);
  }

  void Setup()
  {
    cmd.BeginSample(sampleSetup);

    UpdateNumThreads();
    _descriptor.height = _descriptor.width = _descriptor.volumeDepth = (int)_vxgi.resolution;
    cmd.GetTemporaryRT(ShaderIDs.RadianceCount, _descriptor);
    cmd.GetTemporaryRT(ShaderIDs.RadianceBA, _descriptor);
    cmd.GetTemporaryRT(ShaderIDs.RadianceRG, _descriptor);

    cmd.EndSample(sampleSetup);
  }

  [System.Diagnostics.Conditional("UNITY_EDITOR")]
  void UpdateNumThreads()
  {
    _threadsAggregate = new NumThreads(compute, _kernelAggregate);
    _threadsClear = new NumThreads(compute, _kernelClear);
    _threadsTrace = new NumThreads(compute, _kernelRender);
  }
}
