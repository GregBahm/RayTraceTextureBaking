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

    private RayTracingAccelerationStructure accelerationStructure;

    void Start()
    {
        renderTex = new RenderTexture(256, 256, 16, RenderTextureFormat.ARGBFloat);
        renderTex.enableRandomWrite = true;
        renderTex.Create();

        var settings = new RayTracingAccelerationStructure.RASSettings();
        settings.layerMask = layerMask;
        settings.managementMode = RayTracingAccelerationStructure.ManagementMode.Automatic;
        settings.rayTracingModeMask = RayTracingAccelerationStructure.RayTracingModeMask.Everything;

        accelerationStructure = new RayTracingAccelerationStructure(settings);

        //accelerationStructure.Update();
        accelerationStructure.Build();

        CommandBuffer commandBuffer = CreateCommandBuffer();
        theCamera.AddCommandBuffer(CameraEvent.AfterGBuffer, commandBuffer);
    }

    private void OnDestroy()
    {
        renderTex.Release();
    }

    CommandBuffer CreateCommandBuffer()
    {
        CommandBuffer commandBuffer = new CommandBuffer();
        commandBuffer.name = "Greg's Ray Tracer";
        commandBuffer.SetRayTracingShaderPass(tracingShader, "Test Tracing Pass");
        commandBuffer.SetRayTracingAccelerationStructure(tracingShader, "_SceneAccelStruct", accelerationStructure);
        commandBuffer.SetRayTracingTextureParam(tracingShader, "_Output", renderTex);
        commandBuffer.DispatchRays(tracingShader, "MainRayGenShader", (uint)renderTex.width, (uint)renderTex.height, 1, theCamera);
        return commandBuffer;
    }


    void Update()
    {
    }
}
