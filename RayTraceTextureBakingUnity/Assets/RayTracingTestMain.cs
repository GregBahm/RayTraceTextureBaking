using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Experimental.Rendering.RenderGraphModule;
using UnityEngine.Rendering;
using UnityEngine.Rendering.HighDefinition;

public class RayTracingTestMain : MonoBehaviour
{
    [SerializeField]
    private LayerMask layerMask;
    [SerializeField]
    private RayTracingShader tracingShader;

    [SerializeField]
    private Camera theCamera;

    [SerializeField]
    private RenderTexture renderTex;

    [SerializeField]
    private Material outputDisplayMat;

    private RayTracingAccelerationStructure accelerationStructure;

    [SerializeField]
    public Texture2D owenScrambled256Tex;
    [SerializeField]
    public Texture2D scramblingTile256SPP;
    [SerializeField]
    public Texture2D rankingTile256SPP;
    [SerializeField]
    public Texture2D scramblingTex;

    private TextureHandle skyTexture;
    RenderGraph renderGraph;

    void Start()
    {
        List<RenderGraph> graphs = RenderGraph.GetRegisteredRenderGraphs();
        renderGraph = RenderGraph.GetRegisteredRenderGraphs().First(item => item.name == "HDRP");
        renderTex = CreateOutputTexture();
        skyTexture = CreateSkyTexture();
        accelerationStructure = CreateAccelerationStructure();
        accelerationStructure.Build();
    }

    private TextureHandle CreateSkyTexture()
    {
        TextureDesc desc = new TextureDesc(Vector2.one, true, true);
        desc.colorFormat = GraphicsFormat.R32G32B32A32_SFloat;
        desc.useMipMap = false;
        desc.autoGenerateMips = false;
        desc.enableRandomWrite = false;
        desc.name = "PathTracingSkyBackgroundBuffer";
        return renderGraph.CreateSharedTexture(desc);
    }

    private RenderTexture CreateOutputTexture()
    {
        RenderTexture ret = new RenderTexture(1024, 1024, 1, RenderTextureFormat.ARGBFloat);
        ret.enableRandomWrite = true;
        ret.Create();
        return ret;
    }

    private RayTracingAccelerationStructure CreateAccelerationStructure()
    {
        var settings = new RayTracingAccelerationStructure.RASSettings();
        settings.layerMask = layerMask;
        settings.managementMode = RayTracingAccelerationStructure.ManagementMode.Automatic;
        settings.rayTracingModeMask = RayTracingAccelerationStructure.RayTracingModeMask.Everything;
        return new RayTracingAccelerationStructure(settings);
    }

    private void OnDestroy()
    {
        renderGraph.Cleanup();
        renderTex.Release();
    }

    void Update()
    {
        List<RenderGraph> graphs = RenderGraph.GetRegisteredRenderGraphs();
        outputDisplayMat.SetTexture("_UnlitColorMap", renderTex);

        tracingShader.SetAccelerationStructure("_RaytracingAccelerationStructure", accelerationStructure);

        tracingShader.SetTexture("_OwenScrambledTexture", owenScrambled256Tex);
        tracingShader.SetTexture("_ScramblingTileXSPP", scramblingTile256SPP);
        tracingShader.SetTexture("_RankingTileXSPP", rankingTile256SPP);
        tracingShader.SetTexture("_ScramblingTexture", scramblingTex);

        tracingShader.SetInt("_RaytracingCameraSkyEnabled", 0);

        //ConstantBuffer.PushGlobal(ctx.cmd, data.shaderVariablesRaytracingCB, HDShaderIDs._ShaderVariablesRaytracing);
        //ctx.cmd.SetGlobalBuffer(HDShaderIDs._RaytracingLightCluster, data.lightCluster.GetCluster());
        //ctx.cmd.SetGlobalBuffer(HDShaderIDs._LightDatasRT, data.lightCluster.GetLightDatas());
        //ctx.cmd.SetRayTracingIntParam(data.pathTracingShader, HDShaderIDs._RaytracingCameraSkyEnabled, data.cameraData.skyEnabled ? 1 : 0);
        //ctx.cmd.SetRayTracingVectorParam(data.pathTracingShader, HDShaderIDs._RaytracingCameraClearColor, data.backgroundColor);
        //ctx.cmd.SetRayTracingTextureParam(data.pathTracingShader, HDShaderIDs._SkyCameraTexture, data.sky);
        //ctx.cmd.SetRayTracingTextureParam(data.pathTracingShader, HDShaderIDs._SkyTexture, data.skyReflection);
        tracingShader.SetTexture("_SkyCameraTexture", skyTexture);

        tracingShader.SetTexture("_Output", renderTex);
        
        tracingShader.SetAccelerationStructure("_RaytracingAccelerationStructure", accelerationStructure);

        //ctx.cmd.SetRayTracingMatrixParam(data.pathTracingShader, HDShaderIDs._PixelCoordToViewDirWS, data.pixelCoordToViewDirWS);
        //ctx.cmd.SetRayTracingVectorParam(data.pathTracingShader, HDShaderIDs._PathTracingDoFParameters, data.dofParameters);
        //ctx.cmd.SetRayTracingVectorParam(data.pathTracingShader, HDShaderIDs._PathTracingTilingParameters, data.tilingParameters);

        tracingShader.Dispatch("RayGen", renderTex.width, renderTex.height, 1, theCamera);
    }
}
