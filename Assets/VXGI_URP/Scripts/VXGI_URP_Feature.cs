using System;
using System.Collections;
using System.Collections.Generic;
using UnityEditor.Rendering;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace VXGI_URP
{
    public class VXGI_URP_Feature : ScriptableRendererFeature
    {
        #region public properties

        //shaders
        public ComputeShader MipComputeShader;
        public ComputeShader Parameterizer;
        public ComputeShader VoxelShader;
        public Shader LightingShader;

        [Header("Voxel Volume")] [Tooltip("Make the voxel volume center follow the camera position.")]
        public bool followCamera = false;

        [Tooltip("The center of the voxel volume in World Space")]
        public Vector3 center;

        [Min(0.001f), Tooltip("The size of the voxel volume in World Space.")]
        public float bound = 10f;

        [Tooltip("The resolution of the voxel volume.")]
        public Resolution resolution = Resolution.Medium;

        [Tooltip("The anti-aliasing level of the voxelization process.")]
        public AntiAliasing antiAliasing = AntiAliasing.X1;

        [Tooltip(
            @"Specify the method to generate the voxel mipmap volume:
                Box: fast, 2^n voxel resolution.
                Gaussian 3x3x3: fast, 2^n+1 voxel resolution (recommended).
                Gaussian 4x4x4: slow, 2^n voxel resolution."
        )]
        public Mipmapper.Mode mipmapFilterMode = Mipmapper.Mode.Box;

        [Tooltip("Limit the voxel volume refresh rate.")]
        public bool limitRefreshRate = false;

        [Min(0f), Tooltip("The target refresh rate of the voxel volume.")]
        public float refreshRate = 30f;

        [Header("Rendering")] [Min(0f), Tooltip("How strong the diffuse cone tracing can affect the scene.")]
        public float indirectDiffuseModifier = 1f;

        [Min(0f), Tooltip("How strong the specular cone tracing can affect the scene.")]
        public float indirectSpecularModifier = 1f;

        [Range(.1f, 1f), Tooltip("Downscale the diffuse cone tracing pass.")]
        public float diffuseResolutionScale = 1f;

        public bool resolutionPlusOne => mipmapFilterMode == Mipmapper.Mode.Gaussian3x3x3;
        public float bufferScale => 64f / (_resolution - _resolution % 2);
        public float voxelSize => bound / (_resolution - _resolution % 2);
        public int volume => _resolution * _resolution * _resolution;
        public ComputeBuffer voxelBuffer => _voxelBuffer;
        public List<LightSource> lights => _lights;
        public Matrix4x4 voxelToWorld => Matrix4x4.TRS(origin, Quaternion.identity, Vector3.one * voxelSize);
        public Matrix4x4 worldToVoxel => voxelToWorld.inverse;
        public Mipmapper mipmapper => _mipmapper;
        public Parameterizer parameterizer => _parameterizer;
        public RenderTexture[] radiances => _radiances;
        public Vector3 origin => voxelSpaceCenter - Vector3.one * .5f * bound;

        public Vector3 lastVoxelSpaceCenter
        {
            get => _lastVoxelSpaceCenter;
            set => _lastVoxelSpaceCenter = value;
        }

        public Voxelizer voxelizer => _voxelizer;
        public VoxelShader voxelShader => _voxelShader;

        public ComputeBuffer lightSources => _lightSources;

        public Vector3 voxelSpaceCenter
        {
            get
            {
                var position = center;

                position /= voxelSize;
                position.x = Mathf.Floor(position.x);
                position.y = Mathf.Floor(position.y);
                position.z = Mathf.Floor(position.z);

                return position * voxelSize;
            }
        }

        #endregion


        int _resolution = 0;
        float _previousRefresh = 0f;
        CommandBuffer _command;
        ComputeBuffer _lightSources;
        ComputeBuffer _voxelBuffer;
        List<LightSource> _lights;
        Mipmapper _mipmapper;
        Parameterizer _parameterizer;
        RenderTexture[] _radiances;
        RenderTextureDescriptor _radianceDescriptor;
        Vector3 _lastVoxelSpaceCenter;
        Voxelizer _voxelizer;
        VoxelShader _voxelShader;

        private bool _isInitialized = false;
            
        private VXGI_PrePass prePass;
        private VXGI_DrawPass drawPass;

        public override void Create()
        {
            Initialize();
        }

        public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData)
        {
            UpdateResolution();
            
            var time = Time.realtimeSinceStartup;

            //check if need refresh
            if (!limitRefreshRate || (_previousRefresh + 1f / refreshRate < time))
            {
                _previousRefresh = time;
                
                renderer.EnqueuePass(prePass);
                renderer.EnqueuePass(drawPass);
            }

            return;
        }

        void Initialize()
        {
            // if (_isInitialized == false)
            //     _isInitialized = true;
            // else
            //     return;
            
            prePass = new VXGI_PrePass(this);
            prePass.renderPassEvent = RenderPassEvent.BeforeRenderingGbuffer;
            
            drawPass = new VXGI_DrawPass(this, LightingShader);
            // drawPass.renderPassEvent = RenderPassEvent.AfterRenderingDeferredLights;
            drawPass.renderPassEvent = RenderPassEvent.BeforeRenderingSkybox;

            _lights = new List<LightSource>(64);
            _lightSources = new ComputeBuffer(64, LightSource.size);
            _mipmapper = new Mipmapper(this, MipComputeShader);
            _parameterizer = new Parameterizer(this, Parameterizer);
            _voxelizer = new Voxelizer(this);
            _voxelShader = new VoxelShader(this, VoxelShader);
            _lastVoxelSpaceCenter = voxelSpaceCenter;

            _resolution = (int)resolution;

            CreateBuffers();
            CreateTextureDescriptor();
            CreateTextures();
        }

        protected override void Dispose(bool disposing)
        {
            // _isInitialized = false;
            //
            // DisposeTextures();
            // DisposeBuffers();
            //
            // _voxelShader.Dispose();
            // _voxelizer.Dispose();
            // _parameterizer.Dispose();
            // _mipmapper.Dispose();
            // _lightSources.Dispose();
            // _command.Dispose();

            prePass.Dispose();
        }

        #region Buffers

        void CreateBuffers()
        {
            _voxelBuffer = new ComputeBuffer((int)(bufferScale * volume), VoxelData.size, ComputeBufferType.Append);
        }

        void DisposeBuffers()
        {
            _voxelBuffer.Dispose();
        }

        void ResizeBuffers()
        {
            DisposeBuffers();
            CreateBuffers();
        }

        #endregion

        #region RenderTextures

        void CreateTextureDescriptor()
        {
            _radianceDescriptor = new RenderTextureDescriptor()
            {
                colorFormat = RenderTextureFormat.ARGBHalf,
                dimension = TextureDimension.Tex3D,
                enableRandomWrite = true,
                msaaSamples = 1,
                sRGB = false
            };
        }

        void CreateTextures()
        {
            int resolutionModifier = _resolution % 2; //resolutionModifier best always be 0

            _radiances = new RenderTexture[(int)Mathf.Log(_resolution, 2f)]; //Use log 2 to calculate mipmap levels

            for (
                int i = 0, currentResolution = _resolution;
                i < _radiances.Length;
                i++, currentResolution = (currentResolution - resolutionModifier) / 2 + resolutionModifier
            )
            {
                _radianceDescriptor.height =
                    _radianceDescriptor.width = _radianceDescriptor.volumeDepth = currentResolution;
                _radiances[i] = new RenderTexture(_radianceDescriptor);
                _radiances[i].Create();
            }

            for (int i = 0; i < 9; i++)
            {
                Shader.SetGlobalTexture(ShaderIDs.Radiance[i], radiances[Mathf.Min(i, _radiances.Length - 1)]);
            }
        }

        void DisposeTextures()
        {
            foreach (var radiance in _radiances)
            {
                radiance.DiscardContents();
                radiance.Release();
                DestroyImmediate(radiance);
            }
        }

        void ResizeTextures()
        {
            DisposeTextures();
            CreateTextures();
        }

        #endregion

        void UpdateResolution()
        {
            int newResolution = (int)resolution;

            if (resolutionPlusOne) newResolution++;

            if (_resolution != newResolution)
            {
                _resolution = newResolution;
                ResizeBuffers();
                ResizeTextures();
            }
        }
    }

    public class VXGI_PrePass : ScriptableRenderPass
    {
        private VXGI_URP_Feature m_Feature;
        CommandBuffer cmd;
        
        public VXGI_PrePass(VXGI_URP_Feature settings)
        {
            m_Feature = settings;
            Initialization();
        }

        public override void Execute(ScriptableRenderContext context, ref RenderingData renderingData)
        {
            PrePass(context, ref renderingData);
            SetupShader(context);
            return;
        }

        void Initialization()
        {
        }

        void PrePass(ScriptableRenderContext renderContext, ref RenderingData renderingData)
        {
            Camera camera = renderingData.cameraData.camera;
            if (camera.cameraType != CameraType.Game)
                return;
            if (m_Feature.followCamera && camera.cameraType == CameraType.Game)
                m_Feature.center = camera.transform.position;

            var displacement = (m_Feature.voxelSpaceCenter - m_Feature.lastVoxelSpaceCenter) / m_Feature.voxelSize;

            if (displacement.sqrMagnitude > 0f)
            {
                m_Feature.mipmapper.Shift(renderContext, Vector3Int.RoundToInt(displacement));
            }


            m_Feature.voxelizer.Voxelize(renderContext, ref renderingData);
            m_Feature.voxelShader.Render(renderContext);
            m_Feature.mipmapper.Filter(renderContext);

            m_Feature.lastVoxelSpaceCenter = m_Feature.voxelSpaceCenter;
            
            //Set camera matrix back, because it will be changed in Voxel pass
            cmd = CommandBufferPool.Get("VXGI_UPR.PrePass.RevertCameraMatrix");
            cmd.SetViewProjectionMatrices(camera.worldToCameraMatrix, camera.projectionMatrix);
            renderContext.ExecuteCommandBuffer(cmd);
            cmd.Clear();
            
        }
        
        void SetupShader(ScriptableRenderContext context)
        {
            CommandBuffer cmd = CommandBufferPool.Get("SetupShader");
            m_Feature.lightSources.SetData(m_Feature.lights);//prepass-voxel will fill lights data using voxel camera.

            cmd.SetGlobalBuffer(ShaderIDs.LightSources, m_Feature.lightSources);
            cmd.SetGlobalFloat(ShaderIDs.IndirectDiffuseModifier, m_Feature.indirectDiffuseModifier);
            cmd.SetGlobalFloat(ShaderIDs.IndirectSpecularModifier, m_Feature.indirectSpecularModifier);
            cmd.SetGlobalInt(ShaderIDs.LightCount, m_Feature.lights.Count);
            cmd.SetGlobalInt(ShaderIDs.Resolution, (int)m_Feature.resolution);
            cmd.SetGlobalMatrix(ShaderIDs.WorldToVoxel, m_Feature.worldToVoxel);
            context.ExecuteCommandBuffer(cmd);
            cmd.Clear();
            CommandBufferPool.Release(cmd);
        }

        public void Dispose()
        {
            CommandBufferPool.Release(cmd);
            cmd = null;
        }
    }
    
    public class VXGI_DrawPass : ScriptableRenderPass
    {
        private VXGI_URP_Feature m_Feature;
        CommandBuffer cmd;
        LightingPass[] m_LightingPasses; 
        float[] _renderScale = new float[] { 1f, 1f, 1f, 1f };

        private Shader m_LightingShader;
        private Material m_LightingMaterial;
        
        public VXGI_DrawPass(VXGI_URP_Feature settings, Shader shader)
        {
            m_Feature = settings;
            m_LightingShader = shader;
            m_LightingMaterial = new Material(m_LightingShader);
            
            m_LightingPasses = new LightingPass[] {
                // LightingPass.Emission,
                // LightingPass.DirectDiffuseSpecular,
                LightingPass.IndirectDiffuse,
                LightingPass.IndirectSpecular
            };
        }

        public override void Execute(ScriptableRenderContext context, ref RenderingData renderingData)
        {
            cmd = CommandBufferPool.Get("VXGI_URP.DrawPas");
            Camera camera = renderingData.cameraData.camera; 
            Matrix4x4 clipToWorld = camera.cameraToWorldMatrix * GL.GetGPUProjectionMatrix(camera.projectionMatrix, false).inverse;

            _renderScale[2] = m_Feature.diffuseResolutionScale;

            SetupShader(context);
            
            cmd.SetGlobalMatrix(ShaderIDs.ClipToVoxel, m_Feature.worldToVoxel * clipToWorld);
            cmd.SetGlobalMatrix(ShaderIDs.ClipToWorld, clipToWorld);
            cmd.SetGlobalMatrix(ShaderIDs.VoxelToWorld, m_Feature.voxelToWorld);
            cmd.SetGlobalMatrix(ShaderIDs.WorldToVoxel, m_Feature.worldToVoxel);
            
            for (int i = 0; i < m_LightingPasses.Length; i++)
            {
                if (_renderScale[i] == 1)
                {
                    RenderTargetIdentifier colorRT = renderingData.cameraData.renderer.cameraColorTarget;
                    //Normally, we should avoid source and destination target is the same target while doing blit.
                    //In this case, shader will not sample the source texture, so it should be fine.
                    Blit(cmd, colorRT, colorRT, m_LightingMaterial, (int)m_LightingPasses[i]);
                    //Due to we use blend [one one] to overlay/additive framebuffer values,
                    //it is not suitable to use A B buffer and swap method,
                    //because we don't want to sample source texture again. 
                    //Blit(cmd, ref renderingData, m_LightingMaterial, (int)m_LightingPasses[i]);
                }
                else
                {
                    //@TODO: will support upscale/downscale latter.
                    Debug.LogWarning("VXGI_URP: Current not support upscale/downscale!");
                    // int lowResWidth = (int)(scale * camera.pixelWidth);
                    // int lowResHeight = (int)(scale * camera.pixelHeight);
                    //
                    // cmd.GetTemporaryRT(ShaderIDs.LowResColor, lowResWidth, lowResHeight, 0, FilterMode.Bilinear, RenderTextureFormat.RGB111110Float, RenderTextureReadWrite.Linear);
                    // cmd.GetTemporaryRT(ShaderIDs.LowResDepth, lowResWidth, lowResHeight, 16, FilterMode.Bilinear, RenderTextureFormat.Depth, RenderTextureReadWrite.Linear);
                    //
                    // cmd.SetRenderTarget(ShaderIDs.LowResColor, (RenderTargetIdentifier)ShaderIDs.LowResDepth);
                    // cmd.ClearRenderTarget(true, true, Color.clear);
                    //
                    // cmd.Blit(ShaderIDs.Dummy, ShaderIDs.LowResColor, material, (int)_pass);
                    // cmd.Blit(ShaderIDs.Dummy, ShaderIDs.LowResDepth, UtilityShader.material, (int)UtilityShader.Pass.DepthCopy);
                    // cmd.Blit(ShaderIDs.LowResColor, destination, UtilityShader.material, (int)UtilityShader.Pass.LowResComposite);
                    //
                    // cmd.ReleaseTemporaryRT(ShaderIDs.LowResColor);
                    // cmd.ReleaseTemporaryRT(ShaderIDs.LowResDepth);
                }
            }
            
            context.ExecuteCommandBuffer(cmd);
            cmd.Clear();
        }
        
        void SetupShader(ScriptableRenderContext context)
        {
        }

        public void Dispose()
        {
            CommandBufferPool.Release(cmd);
            cmd = null;
        }
    }
    
}