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

    void Start()
    {
        renderTex = CreateOutputTexture();
        accelerationStructure = CreateAccelerationStructure();
        accelerationStructure.Build();
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
        renderTex.Release();
    }

    void Update()
    {
        List<RenderGraph> graphs = RenderGraph.GetRegisteredRenderGraphs();
        outputDisplayMat.SetTexture("_UnlitColorMap", renderTex);

        tracingShader.SetShaderPass("PathTracingDXR");
        tracingShader.SetAccelerationStructure("_RaytracingAccelerationStructure", accelerationStructure);

        //tracingShader.SetTexture("_OwenScrambledTexture", owenScrambled256Tex);
        //tracingShader.SetTexture("_ScramblingTileXSPP", scramblingTile256SPP);
        //tracingShader.SetTexture("_RankingTileXSPP", rankingTile256SPP);
        //tracingShader.SetTexture("_ScramblingTexture", scramblingTex);

        tracingShader.SetFloat("_Zoom", Mathf.Tan(Mathf.Deg2Rad * theCamera.fieldOfView * 0.5f));
        tracingShader.SetTexture("_Output", renderTex);

        tracingShader.Dispatch("RayGen", renderTex.width, renderTex.height, 1, theCamera);
    }
}
