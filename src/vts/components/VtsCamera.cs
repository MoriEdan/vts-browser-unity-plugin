/**
 * Copyright (c) 2017 Melown Technologies SE
 *
 * Redistribution and use in source and binary forms, with or without
 * modification, are permitted provided that the following conditions are met:
 *
 * *  Redistributions of source code must retain the above copyright notice,
 *    this list of conditions and the following disclaimer.
 *
 * *  Redistributions in binary form must reproduce the above copyright
 *    notice, this list of conditions and the following disclaimer in the
 *    documentation and/or other materials provided with the distribution.
 *
 * THIS SOFTWARE IS PROVIDED BY THE COPYRIGHT HOLDERS AND CONTRIBUTORS "AS IS"
 * AND ANY EXPRESS OR IMPLIED WARRANTIES, INCLUDING, BUT NOT LIMITED TO, THE
 * IMPLIED WARRANTIES OF MERCHANTABILITY AND FITNESS FOR A PARTICULAR PURPOSE
 * ARE DISCLAIMED.  IN NO EVENT SHALL THE COPYRIGHT HOLDER OR CONTRIBUTORS BE
 * LIABLE FOR ANY DIRECT, INDIRECT, INCIDENTAL, SPECIAL, EXEMPLARY, OR
 * CONSEQUENTIAL DAMAGES (INCLUDING, BUT NOT LIMITED TO, PROCUREMENT OF
 * SUBSTITUTE GOODS OR SERVICES; LOSS OF USE, DATA, OR PROFITS; OR BUSINESS
 * INTERRUPTION) HOWEVER CAUSED AND ON ANY THEORY OF LIABILITY, WHETHER IN
 * CONTRACT, STRICT LIABILITY, OR TORT (INCLUDING NEGLIGENCE OR OTHERWISE)
 * ARISING IN ANY WAY OUT OF THE USE OF THIS SOFTWARE, EVEN IF ADVISED OF THE
 * POSSIBILITY OF SUCH DAMAGE.
 */

using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using vts;

public enum VtsDataControl
{
    Unity,
    Vts,
}

public class VtsCamera : MonoBehaviour
{
    public VtsCamera()
    {
        CamOverrideViewDel = CamOverrideView;
        CamOverrideParametersDel = CamOverrideParameters;
        draws = new Draws();
    }

    protected virtual void Start()
    {
        cam = GetComponent<Camera>();
        mapTrans = mapObject.GetComponent<Transform>();

        shaderPropertyMainTex = Shader.PropertyToID("_MainTex");
        shaderPropertyMaskTex = Shader.PropertyToID("_MaskTex");
        shaderPropertyUvMat = Shader.PropertyToID("_UvMat");
        shaderPropertyUvClip = Shader.PropertyToID("_UvClip");
        shaderPropertyColor = Shader.PropertyToID("_Color");
        shaderPropertyFlags = Shader.PropertyToID("_Flags");

        SetupCommandBuffers();
    }

    protected virtual void SetupCommandBuffers()
    {
        opaque = new CommandBuffer();
        opaque.name = "Vts Opaque";
        transparent = new CommandBuffer();
        transparent.name = "Vts Transparent";
        geodata = new CommandBuffer();
        geodata.name = "Vts Geodata";
        infographics = new CommandBuffer();
        infographics.name = "Vts Infographics";

        cam.AddCommandBuffer(CameraEvent.AfterForwardOpaque, opaque);
        cam.AddCommandBuffer(CameraEvent.AfterForwardAlpha, transparent);
        cam.AddCommandBuffer(CameraEvent.AfterImageEffects, geodata);
        cam.AddCommandBuffer(CameraEvent.AfterEverything, infographics);
    }

    private readonly Map.CameraOverrideHandler CamOverrideViewDel;
    private void CamOverrideView(ref double[] values)
    {
        Matrix4x4 Mu = mapTrans.localToWorldMatrix * VtsUtil.UnityToVtsAxes;
        // view matrix
        if (controlTransformation == VtsDataControl.Vts)
            cam.worldToCameraMatrix = VtsUtil.V2U44(Math.Mul44x44(values, Math.Inverse44(VtsUtil.U2V44(Mu))));
        else
            values = Math.Mul44x44(VtsUtil.U2V44(cam.worldToCameraMatrix), VtsUtil.U2V44(Mu));
    }

    private readonly Map.CameraOverrideParamsHandler CamOverrideParametersDel;
    private void CamOverrideParameters(ref double fov, ref double aspect, ref double near, ref double far)
    {
        // fov
        if (controlFov == VtsDataControl.Vts)
            cam.fieldOfView = (float)fov;
        else
            fov = cam.fieldOfView;
        // near & far
        if (controlNearFar == VtsDataControl.Vts)
        {
            cam.nearClipPlane = (float)near;
            cam.farClipPlane = (float)far;
        }
        else
        {
            near = cam.nearClipPlane;
            far = cam.farClipPlane;
        }
    }

    protected virtual void Update()
    {
        Map map = mapObject.GetComponent<VtsMap>().map;
        map.SetWindowSize((uint)cam.pixelWidth, (uint)cam.pixelHeight);
        map.EventCameraView += CamOverrideViewDel;
        map.EventCameraFovAspectNearFar += CamOverrideParametersDel;
        map.RenderTickRender();
        map.EventCameraView -= CamOverrideViewDel;
        map.EventCameraFovAspectNearFar -= CamOverrideParametersDel;
        draws.Load(map);
        RegenerateCommandBuffers();
    }

    protected virtual void RegenerateCommandBuffer(CommandBuffer buffer, List<DrawTask> tasks)
    {
        buffer.Clear();
        buffer.SetProjectionMatrix(cam.projectionMatrix);
        foreach (DrawTask t in tasks)
        {
            if (t.mesh == null)
                continue;
            MaterialPropertyBlock mat = new MaterialPropertyBlock();
            bool monochromatic = false;
            if (t.texColor != null)
            {
                var tt = t.texColor as VtsTexture;
                mat.SetTexture(shaderPropertyMainTex, tt.Get());
                monochromatic = tt.monochromatic;
            }
            if (t.texMask != null)
            {
                var tt = t.texMask as VtsTexture;
                mat.SetTexture(shaderPropertyMaskTex, tt.Get());
            }
            mat.SetMatrix(shaderPropertyUvMat, VtsUtil.V2U33(t.data.uvm));
            mat.SetVector(shaderPropertyUvClip, VtsUtil.V2U4(t.data.uvClip));
            mat.SetVector(shaderPropertyColor, VtsUtil.V2U4(t.data.color));
            // flags: mask, monochromatic, flat shading, uv source
            mat.SetVector(shaderPropertyFlags, new Vector4(t.texMask == null ? 0 : 1, monochromatic ? 1 : 0, 0, t.data.externalUv ? 1 : 0));
            buffer.DrawMesh((t.mesh as VtsMesh).Get(), VtsUtil.V2U44(t.data.mv), material, 0, -1, mat);
        }
    }

    protected virtual void RegenerateCommandBuffers()
    {
        RegenerateCommandBuffer(opaque, draws.opaque);
        RegenerateCommandBuffer(transparent, draws.transparent);
        RegenerateCommandBuffer(geodata, draws.geodata);
        RegenerateCommandBuffer(infographics, draws.infographics);
    }

    public GameObject mapObject;

    public VtsDataControl controlTransformation;
    public VtsDataControl controlNearFar;
    public VtsDataControl controlFov;

    public Material material;

    protected int shaderPropertyMainTex;
    protected int shaderPropertyMaskTex;
    protected int shaderPropertyUvMat;
    protected int shaderPropertyUvClip;
    protected int shaderPropertyColor;
    protected int shaderPropertyFlags;

    protected readonly Draws draws;
    protected Camera cam;
    protected Transform mapTrans;

    protected CommandBuffer opaque;
    protected CommandBuffer transparent;
    protected CommandBuffer geodata;
    protected CommandBuffer infographics;
}
