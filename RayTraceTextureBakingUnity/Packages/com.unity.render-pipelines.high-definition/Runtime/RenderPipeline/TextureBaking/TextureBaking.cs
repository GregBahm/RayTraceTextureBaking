using System;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Experimental.Rendering.RenderGraphModule;

#if UNITY_EDITOR
using UnityEditor;
#endif // UNITY_EDITOR

namespace UnityEngine.Rendering.HighDefinition
{
    [Serializable, VolumeComponentMenuForRenderPipeline("Ray Tracing/Texture Baking", typeof(HDRenderPipeline))]
    [HDRPHelpURLAttribute("Ray-Tracing-Path-Tracing")]
    public sealed class TextureBaking : VolumeComponent
    {
        /// <summary>
        /// Enables path tracing (thus disabling most other passes).
        /// </summary>
        [Tooltip("Enables path tracing (thus disabling most other passes).")]
        public BoolParameter enable = new BoolParameter(false);

        /// <summary>
        /// Defines the layers that path tracing should include.
        /// </summary>
        [Tooltip("Defines the layers that path tracing should include.")]
        public LayerMaskParameter layerMask = new LayerMaskParameter(-1);

        /// <summary>
        /// Defines the maximum number of paths cast within each pixel, over time (one per frame).
        /// </summary>
        [Tooltip("Defines the maximum number of paths cast within each pixel, over time (one per frame).")]
        public ClampedIntParameter maximumSamples = new ClampedIntParameter(256, 1, 16384);

        /// <summary>
        /// Defines the minimum number of bounces for each path, in [1, 10].
        /// </summary>
        [Tooltip("Defines the minimum number of bounces for each path, in [1, 10].")]
        public ClampedIntParameter minimumDepth = new ClampedIntParameter(1, 1, 10);

        /// <summary>
        /// Defines the maximum number of bounces for each path, in [minimumDepth, 10].
        /// </summary>
        [Tooltip("Defines the maximum number of bounces for each path, in [minimumDepth, 10].")]
        public ClampedIntParameter maximumDepth = new ClampedIntParameter(4, 1, 10);

        /// <summary>
        /// Defines the maximum, post-exposed luminance computed for indirect path segments.
        /// </summary>
        [Tooltip("Defines the maximum, post-exposed luminance computed for indirect path segments. Lower values help against noise and fireflies (very bright pixels), but introduce bias by darkening the overall result. Increase this value if your image looks too dark.")]
        public MinFloatParameter maximumIntensity = new MinFloatParameter(10f, 0f);

        /// <summary>
        /// Defines the number of tiles (X: width, Y: height) and the indices of the current tile (Z: i in [0, width[, W: j in [0, height[) for interleaved tiled rendering.
        /// </summary>
        [Tooltip("Defines the number of tiles (X: width, Y: height) and the indices of the current tile (Z: i in [0, width[, W: j in [0, height[) for interleaved tiled rendering.")]
        public Vector4Parameter tilingParameters = new Vector4Parameter(new Vector4(1, 1, 0, 0));

        /// <summary>
        /// Default constructor for the path tracing volume component.
        /// </summary>
        public TextureBaking()
        {
            displayName = "Texture Baking!";
        }
    }

    public partial class HDRenderPipeline
    {
        TextureBaking m_TextureBakingSettings = null;

#if UNITY_EDITOR
        uint m_TextureBakingCacheMaxIteration = 0;
#endif // UNITY_EDITOR
        uint m_TextureBakingCacheLightCount = 0;
        int m_TextureBakingCameraID = 0;    
        bool m_TextureBakingRenderSky = true;

        TextureHandle m_TextureBakingFrameTexture; // stores the per-pixel results of path tracing for one frame
        TextureHandle m_TextureBakingSkyTexture; // stores the sky background

        void InitTextureBaking(RenderGraph renderGraph)
        {
#if UNITY_EDITOR
            Undo.postprocessModifications += OnUndoRecorded;
            Undo.undoRedoPerformed += OnSceneEdit;
            SceneView.duringSceneGui += OnSceneGui;
#endif // UNITY_EDITOR

            TextureDesc td = new TextureDesc(Vector2.one, true, true);
            td.colorFormat = GraphicsFormat.R32G32B32A32_SFloat;
            td.useMipMap = false;
            td.autoGenerateMips = false;

            td.name = "TextureBakingFrameBuffer";
            td.enableRandomWrite = true;
            m_TextureBakingFrameTexture = renderGraph.CreateSharedTexture(td);

            td.name = "TextureBakingSkyBuffer";
            td.enableRandomWrite = false;
            m_TextureBakingSkyTexture = renderGraph.CreateSharedTexture(td);
        }

        void ReleaseTextureBaking()
        {
#if UNITY_EDITOR
            Undo.postprocessModifications -= OnUndoRecorded;
            Undo.undoRedoPerformed -= OnSceneEdit;
            SceneView.duringSceneGui -= OnSceneGui;
#endif // UNITY_EDITOR
        }

        /// <summary>
        /// Resets path tracing accumulation for all cameras.
        /// </summary>
        public void ResetTextureBaking()
        {
            m_TextureBakingRenderSky = true;
            m_SubFrameManager.Reset();
        }

        /// <summary>
        /// Resets path tracing accumulation for a specific camera.
        /// </summary>
        /// <param name="hdCamera">Camera for which the accumulation is reset.</param>
        public void ResetTextureBaking(HDCamera hdCamera)
        {
            int camID = hdCamera.camera.GetInstanceID();
            CameraData camData = m_SubFrameManager.GetCameraData(camID);
            ResetTextureBaking(camID, camData);
        }

        internal CameraData ResetTextureBaking(int camID, CameraData camData)
        {
            m_RenderSky = true;
            camData.ResetIteration();
            m_SubFrameManager.SetCameraData(camID, camData);

            return camData;
        }

#if UNITY_EDITOR

#endif // UNITY_EDITOR

        private CameraData CheckTextureBakingDirtiness(HDCamera hdCamera, int camID, CameraData camData)
        {
            // Check resolution dirtiness
            if (hdCamera.actualWidth != camData.width || hdCamera.actualHeight != camData.height)
            {
                camData.width = (uint)hdCamera.actualWidth;
                camData.height = (uint)hdCamera.actualHeight;
                return ResetTextureBaking(camID, camData);
            }

            // Check sky dirtiness
            bool enabled = (hdCamera.clearColorMode == HDAdditionalCameraData.ClearColorMode.Sky);
            if (enabled != camData.skyEnabled)
            {
                camData.skyEnabled = enabled;
                return ResetTextureBaking(camID, camData);
            }

            // Check fog dirtiness
            enabled = Fog.IsFogEnabled(hdCamera);
            if (enabled != camData.fogEnabled)
            {
                camData.fogEnabled = enabled;
                return ResetTextureBaking(camID, camData);
            }

            // Check acceleration structure dirtiness
            ulong accelSize = RequestAccelerationStructure(hdCamera).GetSize();
            if (accelSize != camData.accelSize)
            {
                camData.accelSize = accelSize;
                return ResetTextureBaking(camID, camData);
            }

            // Check materials dirtiness
            if (GetMaterialDirtiness(hdCamera))
            {
                ResetMaterialDirtiness(hdCamera);
                ResetTextureBaking();
                return camData;
            }

            // Check light or geometry transforms dirtiness
            if (GetTransformDirtiness(hdCamera))
            {
                ResetTransformDirtiness(hdCamera);
                ResetTextureBaking();
                return camData;
            }

            // Check lights dirtiness
            if (m_CacheLightCount != m_RayTracingLights.lightCount)
            {
                m_CacheLightCount = (uint)m_RayTracingLights.lightCount;
                ResetTextureBaking();
                return camData;
            }

            // Check camera matrix dirtiness
            if (hdCamera.mainViewConstants.nonJitteredViewProjMatrix != (hdCamera.mainViewConstants.prevViewProjMatrix))
            {
                return ResetTextureBaking(camID, camData);
            }

            // If nothing but the camera has changed, re-render the sky texture
            if (camID != m_CameraID)
            {
                m_RenderSky = true;
                m_CameraID = camID;
            }

            return camData;
        }

        static RTHandle TextureBakingHistoryBufferAllocatorFunction(string viewName, int frameIndex, RTHandleSystem rtHandleSystem)
        {
            return rtHandleSystem.Alloc(Vector2.one, TextureXR.slices, colorFormat: GraphicsFormat.R32G32B32A32_SFloat, dimension: TextureXR.dimension,
                enableRandomWrite: true, useMipMap: false, autoGenerateMips: false,
                name: string.Format("{0}_PathTracingHistoryBuffer{1}", viewName, frameIndex)); // Greg, this may be a problem
        }

        class RenderTextureBakingData
        {
            public RayTracingShader textureBakingShader;
            public CameraData cameraData;
            public BlueNoise.DitheredTextureSet ditheredTextureSet;
            public ShaderVariablesRaytracing shaderVariablesRaytracingCB;
            public Color backgroundColor;
            public Texture skyReflection;
            public Matrix4x4 pixelCoordToViewDirWS;
            public Vector4 dofParameters;
            public Vector4 tilingParameters;
            public int width, height;
            public RayTracingAccelerationStructure accelerationStructure;
            public HDRaytracingLightCluster lightCluster;

            public TextureHandle output;
            public TextureHandle sky;

#if ENABLE_SENSOR_SDK
            public Action<UnityEngine.Rendering.CommandBuffer> prepareDispatchRays;
#endif
        }

        private Vector4 ComputeDoFConstants(HDCamera hdCamera, TextureBaking settings)
        {
            var dofSettings = hdCamera.volumeStack.GetComponent<DepthOfField>();
            bool enableDof = (dofSettings.focusMode.value == DepthOfFieldMode.UsePhysicalCamera) && !(hdCamera.camera.cameraType == CameraType.SceneView);

            // focalLength is in mm, so we need to convert to meters. We also want the aperture radius, not diameter, so we divide by two.
            float apertureRadius = (enableDof && hdCamera.physicalParameters.aperture > 0) ? 0.5f * 0.001f * hdCamera.camera.focalLength / hdCamera.physicalParameters.aperture : 0.0f;

            float focusDistance = (dofSettings.focusDistanceMode.value == FocusDistanceMode.Volume) ? dofSettings.focusDistance.value : hdCamera.physicalParameters.focusDistance;

            return new Vector4(apertureRadius, focusDistance, 0.0f, 0.0f);
        }

        TextureHandle RenderTextureBaking(RenderGraph renderGraph, HDCamera hdCamera, in CameraData cameraData, TextureHandle textureBakingBuffer, TextureHandle skyBuffer)
        {
            using (var builder = renderGraph.AddRenderPass<RenderTextureBakingData>("Render Texture Baking", out var passData))
            {
#if ENABLE_SENSOR_SDK
                passData.pathTracingShader = hdCamera.pathTracingShaderOverride ? hdCamera.pathTracingShaderOverride : m_GlobalSettings.renderPipelineRayTracingResources.pathTracing;
                passData.prepareDispatchRays = hdCamera.prepareDispatchRays;
#else
                passData.textureBakingShader = m_GlobalSettings.renderPipelineRayTracingResources.textureBaking;
#endif
                passData.cameraData = cameraData;
                passData.ditheredTextureSet = GetBlueNoiseManager().DitheredTextureSet256SPP();
                passData.backgroundColor = hdCamera.backgroundColorHDR;
                passData.skyReflection = m_SkyManager.GetSkyReflection(hdCamera);
                passData.pixelCoordToViewDirWS = hdCamera.mainViewConstants.pixelCoordToViewDirWS;
                passData.dofParameters = ComputeDoFConstants(hdCamera, m_TextureBakingSettings);
                passData.tilingParameters = m_TextureBakingSettings.tilingParameters.value;
                passData.width = hdCamera.actualWidth;
                passData.height = hdCamera.actualHeight;
                passData.accelerationStructure = RequestAccelerationStructure(hdCamera);
                passData.lightCluster = RequestLightCluster();

                passData.shaderVariablesRaytracingCB = m_ShaderVariablesRayTracingCB;
                passData.shaderVariablesRaytracingCB._RaytracingNumSamples = (int)m_SubFrameManager.subFrameCount;
                passData.shaderVariablesRaytracingCB._RaytracingMinRecursion = m_TextureBakingSettings.minimumDepth.value;
#if NO_RAY_RECURSION
                passData.shaderVariablesRaytracingCB._RaytracingMaxRecursion = 1;
#else
                passData.shaderVariablesRaytracingCB._RaytracingMaxRecursion = m_TextureBakingSettings.maximumDepth.value;
#endif
                passData.shaderVariablesRaytracingCB._RaytracingIntensityClamp = m_TextureBakingSettings.maximumIntensity.value;
                passData.shaderVariablesRaytracingCB._RaytracingSampleIndex = (int)cameraData.currentIteration;

                passData.output = builder.WriteTexture(textureBakingBuffer);
                passData.sky = builder.ReadTexture(skyBuffer);

                builder.SetRenderFunc(
                    (RenderTextureBakingData data, RenderGraphContext ctx) =>
                    {
                        // Define the shader pass to use for the path tracing pass
                        ctx.cmd.SetRayTracingShaderPass(data.textureBakingShader, "PathTracingDXR");

                        // Set the acceleration structure for the pass
                        ctx.cmd.SetRayTracingAccelerationStructure(data.textureBakingShader, HDShaderIDs._RaytracingAccelerationStructureName, data.accelerationStructure);

                        // Inject the ray-tracing sampling data
                        BlueNoise.BindDitheredTextureSet(ctx.cmd, data.ditheredTextureSet);

                        // Update the global constant buffer
                        ConstantBuffer.PushGlobal(ctx.cmd, data.shaderVariablesRaytracingCB, HDShaderIDs._ShaderVariablesRaytracing);

                        // LightLoop data
                        ctx.cmd.SetGlobalBuffer(HDShaderIDs._RaytracingLightCluster, data.lightCluster.GetCluster());
                        ctx.cmd.SetGlobalBuffer(HDShaderIDs._LightDatasRT, data.lightCluster.GetLightDatas());

                        // Set the data for the ray miss
                        ctx.cmd.SetRayTracingIntParam(data.textureBakingShader, HDShaderIDs._RaytracingCameraSkyEnabled, data.cameraData.skyEnabled ? 1 : 0);
                        ctx.cmd.SetRayTracingVectorParam(data.textureBakingShader, HDShaderIDs._RaytracingCameraClearColor, data.backgroundColor);
                        ctx.cmd.SetRayTracingTextureParam(data.textureBakingShader, HDShaderIDs._SkyCameraTexture, data.sky);
                        ctx.cmd.SetRayTracingTextureParam(data.textureBakingShader, HDShaderIDs._SkyTexture, data.skyReflection);

                        // Additional data for path tracing
                        ctx.cmd.SetRayTracingTextureParam(data.textureBakingShader, HDShaderIDs._FrameTexture, data.output);
                        ctx.cmd.SetRayTracingMatrixParam(data.textureBakingShader, HDShaderIDs._PixelCoordToViewDirWS, data.pixelCoordToViewDirWS);
                        ctx.cmd.SetRayTracingVectorParam(data.textureBakingShader, HDShaderIDs._PathTracingDoFParameters, data.dofParameters);
                        ctx.cmd.SetRayTracingVectorParam(data.textureBakingShader, HDShaderIDs._PathTracingTilingParameters, data.tilingParameters);

#if ENABLE_SENSOR_SDK
                        // SensorSDK can do its own camera rays generation
                        data.prepareDispatchRays?.Invoke(ctx.cmd);
#endif
                        // Run the computation
                        ctx.cmd.DispatchRays(data.textureBakingShader, "RayGen", (uint)data.width, (uint)data.height, 1);
                        //ctx.cmd.SetGlobalTexture("_GregsBakedTexture", data.output);
                    });

                return passData.output;
            }
        }

        // Simpler variant used by path tracing, without depth buffer or volumetric computations
        void RenderTextureBakeSky(RenderGraph renderGraph, HDCamera hdCamera, TextureHandle skyBuffer)
        {
            if (m_CurrentDebugDisplaySettings.DebugHideSky(hdCamera))
                return;

            using (var builder = renderGraph.AddRenderPass<RenderSkyPassData>("Render Sky for Path Tracing", out var passData))
            {
                passData.visualEnvironment = hdCamera.volumeStack.GetComponent<VisualEnvironment>();
                passData.sunLight = GetMainLight();
                passData.hdCamera = hdCamera;
                passData.colorBuffer = builder.WriteTexture(skyBuffer);
                passData.depthTexture = builder.WriteTexture(CreateDepthBuffer(renderGraph, true, MSAASamples.None));
                passData.debugDisplaySettings = m_CurrentDebugDisplaySettings;
                passData.skyManager = m_SkyManager;

                builder.SetRenderFunc(
                    (RenderSkyPassData data, RenderGraphContext ctx) =>
                    {
                        // Override the exposure texture, as we need a neutral value for this render
                        ctx.cmd.SetGlobalTexture(HDShaderIDs._ExposureTexture, m_EmptyExposureTexture);

                        data.skyManager.RenderSky(data.hdCamera, data.sunLight, data.colorBuffer, data.depthTexture, data.debugDisplaySettings, ctx.cmd);

                        // Restore the regular exposure texture
                        ctx.cmd.SetGlobalTexture(HDShaderIDs._ExposureTexture, GetExposureTexture(hdCamera));
                    });
            }
        }

        TextureHandle RenderTextureBaking(RenderGraph renderGraph, HDCamera hdCamera, TextureHandle textureBakeBuffer)
        {
            RayTracingShader textureBakeShader = m_GlobalSettings.renderPipelineRayTracingResources.textureBaking;
            m_TextureBakingSettings = hdCamera.volumeStack.GetComponent<TextureBaking>();

            // Check the validity of the state before moving on with the computation
            if (!textureBakeShader || !m_TextureBakingSettings.enable.value)
                return TextureHandle.nullHandle;

            int camID = hdCamera.camera.GetInstanceID();
            CameraData camData = m_SubFrameManager.GetCameraData(camID);

            // Check if the camera has a valid history buffer and if not reset the accumulation.
            // This can happen if a script disables and re-enables the camera (case 1337843).
            if (!hdCamera.isPersistent && hdCamera.GetCurrentFrameRT((int)HDCameraFrameHistoryType.PathTracing) == null)
            {
                m_SubFrameManager.Reset(camID);
            }

            if (!m_SubFrameManager.isRecording)
            {
                // Check if things have changed and if we need to restart the accumulation
                camData = CheckTextureBakingDirtiness(hdCamera, camID, camData);

                // If we are recording, the max iteration is set/overridden by the subframe manager, otherwise we read it from the path tracing volume
                m_SubFrameManager.subFrameCount = (uint)m_TextureBakingSettings.maximumSamples.value;
            }
            else
            {
                // When recording, as be bypass dirtiness checks which update camData, we need to indicate whether we want to render a sky or not
                camData.skyEnabled = (hdCamera.clearColorMode == HDAdditionalCameraData.ClearColorMode.Sky);
                m_SubFrameManager.SetCameraData(camID, camData);
            }

#if UNITY_HDRP_DXR_TESTS_DEFINE
            if (Application.isPlaying)
            {
                camData.ResetIteration();
                m_SubFrameManager.subFrameCount = 1;
            }
#endif

            if (camData.currentIteration < m_SubFrameManager.subFrameCount)
            {
                // Keep a sky texture around, that we compute only once per accumulation (except when recording, with potential camera motion blur)
                if (m_RenderSky || m_SubFrameManager.isRecording)
                {
                    RenderSky(m_RenderGraph, hdCamera, m_SkyTexture);
                    m_RenderSky = false;
                }

                RenderTextureBaking(m_RenderGraph, hdCamera, camData, m_FrameTexture, m_SkyTexture);
            }

            RenderAccumulation(m_RenderGraph, hdCamera, m_FrameTexture, textureBakeBuffer, true);

            return textureBakeBuffer;
        }
    }
}
