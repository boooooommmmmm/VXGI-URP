using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using VXGI_URP;

public class Voxelizer : System.IDisposable {
  int _antiAliasing;
  int _resolution;
  Camera _camera;
  CommandBuffer cmd;
  DrawingSettings _drawingSettings;
  FilteringSettings _filteringSettings;
  RenderTextureDescriptor _descriptor;
  ScriptableCullingParameters _cullingParameters;
  VXGI_URP_Feature _vxgi;
  string _cameraTag = "VoxelizerCamera";

  public Voxelizer(VXGI_URP_Feature vxgi) {
    _vxgi = vxgi;

    cmd = CommandBufferPool.Get("VXGI_URP.Voxelizer");

    Initialize();
  }

  public void Dispose() {
#if UNITY_EDITOR
    GameObject.DestroyImmediate(_camera.gameObject);
#else
    GameObject.Destroy(_camera.gameObject);
#endif

    cmd.Dispose();
  }

  public void Voxelize(ScriptableRenderContext renderContext, ref RenderingData renderingData)
  {
    //add this protection to ensure voxel camera is always available. 
    CreateCamera();
    
    if (!_camera.TryGetCullingParameters(out _cullingParameters)) return;
  
    var cullingResults = renderContext.Cull(ref _cullingParameters);

    //fill visible lights data using voxel camera. 
    _vxgi.lights.Clear();
    foreach (var light in cullingResults.visibleLights) {
      if (VXGI_Definition.supportedLightTypes.Contains(light.lightType) && light.finalColor.maxColorComponent > 0f) {
        _vxgi.lights.Add(new LightSource(light, _vxgi.worldToVoxel));
      }
    }

    UpdateCamera();

    cmd.BeginSample(cmd.name);

    //Set render target, render target resolution is the render quality
    //In this case, each pixel (voxel) represent a voxel box. 
    cmd.GetTemporaryRT(ShaderIDs.Dummy, _descriptor);
    cmd.SetRenderTarget(ShaderIDs.Dummy, RenderBufferLoadAction.DontCare, RenderBufferStoreAction.DontCare);

    cmd.SetGlobalInt(ShaderIDs.Resolution, _resolution);
    cmd.SetRandomWriteTarget(1, _vxgi.voxelBuffer, false);
    cmd.SetViewProjectionMatrices(_camera.worldToCameraMatrix, _camera.projectionMatrix);

    _drawingSettings.perObjectData = renderingData.perObjectData;

    renderContext.ExecuteCommandBuffer(cmd);
    renderContext.DrawRenderers(cullingResults, ref _drawingSettings, ref _filteringSettings);

    cmd.Clear();

    cmd.ClearRandomWriteTargets();
    cmd.ReleaseTemporaryRT(ShaderIDs.Dummy);

    cmd.EndSample(cmd.name);

    renderContext.ExecuteCommandBuffer(cmd);

    cmd.Clear();
  }

  void Initialize()
  {
    CreateCamera();
    CreateCameraDescriptor();
    CreateCameraSettings();
  }

  void CreateCamera() {
    //Add multiple create protection
    if (_camera)
      return;

    DestroyCameras();
    
    //disable hidden flag for easier debugging
    var gameObject = new GameObject("__" + _vxgi.name + "_VOXELIZER__") { tag = _cameraTag/*hideFlags = HideFlags.HideAndDontSave*/ };
    gameObject.SetActive(false);

    _camera = gameObject.AddComponent<Camera>();
    _camera.allowMSAA = true;
    _camera.aspect = 1f;
    _camera.orthographic = true;
  }
  
  void DestroyCameras()
  {
#if UNITY_EDITOR
    GameObject[] gos = GameObject.FindGameObjectsWithTag(_cameraTag);
    if (gos == null || gos.Length == 0)
      return;
    foreach (GameObject go in gos)
    {
      if (Application.isPlaying)
        Object.DestroyImmediate(go);
      else
        Object.DestroyImmediate(go);
    }
#endif
  }


  void CreateCameraDescriptor() {
    _descriptor = new RenderTextureDescriptor() {
      colorFormat = RenderTextureFormat.R8,
      dimension = TextureDimension.Tex2D,
      memoryless = RenderTextureMemoryless.Color | RenderTextureMemoryless.Depth | RenderTextureMemoryless.MSAA,
      volumeDepth = 1,
      sRGB = false
    };
  }

  void CreateCameraSettings() {
    var sortingSettings = new SortingSettings(_camera) { criteria = SortingCriteria.OptimizeStateChanges };
    _drawingSettings = new DrawingSettings(ShaderTagIDs.Voxelization, sortingSettings);
    _filteringSettings = new FilteringSettings(RenderQueueRange.all);
  }

  void UpdateCamera() {
    if (_antiAliasing != (int)_vxgi.antiAliasing) {
      _antiAliasing = (int)_vxgi.antiAliasing;
      _descriptor.msaaSamples = _antiAliasing;
    }

    if (_resolution != (int)_vxgi.resolution) {
      _resolution = (int)_vxgi.resolution;
      _descriptor.height = _descriptor.width = _resolution;
    }

    _camera.farClipPlane = .5f * _vxgi.bound;
    _camera.nearClipPlane = -.5f * _vxgi.bound;
    _camera.orthographicSize = .5f * _vxgi.bound;
    _camera.transform.position = _vxgi.voxelSpaceCenter;
  }
}
