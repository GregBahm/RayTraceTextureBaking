using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;

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

    void Start()
    {
        renderTex = new RenderTexture(1024, 1024, 1, RenderTextureFormat.ARGBFloat);
        renderTex.enableRandomWrite = true;
        renderTex.Create();

        var settings = new RayTracingAccelerationStructure.RASSettings();
        settings.layerMask = layerMask;
        settings.managementMode = RayTracingAccelerationStructure.ManagementMode.Automatic;
        settings.rayTracingModeMask = RayTracingAccelerationStructure.RayTracingModeMask.Everything;

        accelerationStructure = new RayTracingAccelerationStructure(settings);

        accelerationStructure.Build();
    }

    private void OnDestroy()
    {
        renderTex.Release();
    }

    void Update()
    {
        outputDisplayMat.SetTexture("_UnlitColorMap", renderTex);

        tracingShader.SetAccelerationStructure("_SceneAccelStruct", accelerationStructure);
        tracingShader.SetFloat("_Zoom", Mathf.Tan(Mathf.Deg2Rad * Camera.main.fieldOfView * 0.5f));
        tracingShader.SetTexture("_Output", renderTex);
        tracingShader.Dispatch("MainRayGenShader", renderTex.width, renderTex.height, 1, theCamera);
    }
}
