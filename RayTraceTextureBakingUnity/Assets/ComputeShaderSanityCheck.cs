using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class ComputeShaderSanityCheck : MonoBehaviour
{
    [SerializeField]
    private ComputeShader computeShader;

    [SerializeField]
    private RenderTexture renderTex;

    int kernel;

    void Start()
    {
        renderTex = new RenderTexture(256, 256, 16, RenderTextureFormat.ARGBFloat);
        renderTex.enableRandomWrite = true;
        renderTex.Create();

        kernel = computeShader.FindKernel("CSMain");
    }

    void Update()
    {
        computeShader.SetTexture(kernel, "Result", renderTex);
        computeShader.Dispatch(kernel, renderTex.width / 8, renderTex.height / 8, 1);
    }
}
