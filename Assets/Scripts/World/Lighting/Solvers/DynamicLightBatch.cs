#nullable enable

using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using Kern.Core.Diagnostics;
using UnityEngine;
using UnityEngine.Rendering;

namespace Kern.World.Lighting;

// CPU dispatch grouping only. Each light keeps its own persistent cache slot,
// receiver rectangle and polar dimensions; no padded maximum-sized rectangles.
internal sealed class DynamicLightBatch
{
    [StructLayout(LayoutKind.Sequential)]
    internal readonly struct WorkItem
    {
        public readonly Vector2Int ReceiverOrigin;
        public readonly Vector2Int ReceiverSize;
        public readonly Vector2Int PolarSize;
        public readonly int LightIndex;
        public readonly int Slot;

        public WorkItem(RectInt receiver, Vector2Int polarSize, int lightIndex, int slot)
        {
            ReceiverOrigin = receiver.position;
            ReceiverSize = receiver.size;
            PolarSize = polarSize;
            LightIndex = lightIndex;
            Slot = slot;
        }
    }

    internal const int WorkItemStride = 32;
    private const int MaximumBatchSources = 65535;
    private static readonly int s_workItemsId = Shader.PropertyToID("_DynamicLightWorkItems");
    private static readonly int s_batchOffsetId = Shader.PropertyToID("_DynamicBatchOffset");
    private static readonly Comparison<WorkItem> s_polarOrder = (left, right) =>
        PolarGroups(left).CompareTo(PolarGroups(right));
    private static readonly Comparison<WorkItem> s_receiverOrder = CompareReceiverGroups;
    private readonly List<WorkItem> _polar = new();
    private readonly List<WorkItem> _receivers = new();
    private readonly List<WorkItem> _upload = new();
    private ComputeBuffer? _workItems;

    static DynamicLightBatch()
    {
        if (Marshal.SizeOf<WorkItem>() != WorkItemStride)
        {
            throw new InvalidOperationException("Dynamic light batch descriptor no longer matches its HLSL stride.");
        }
    }

    public int PolarDispatchCount { get; private set; }
    public int ReceiverDispatchCount { get; private set; }
    public int UploadedBytes { get; private set; }

    public void Begin()
    {
        _polar.Clear();
        _receivers.Clear();
        _upload.Clear();
        PolarDispatchCount = 0;
        ReceiverDispatchCount = 0;
        UploadedBytes = 0;
    }

    public void Add(WorkItem work, bool tracePolar)
    {
        if (work.ReceiverSize.x <= 0 || work.ReceiverSize.y <= 0 ||
            work.PolarSize.x <= 0 || work.PolarSize.y <= 0 || work.LightIndex < 0 || work.Slot < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(work), "A batch requires a valid source and nonempty transport domains.");
        }

        _receivers.Add(work);
        if (tracePolar)
        {
            _polar.Add(work);
        }
    }

    public void Record(CommandBuffer commands, ComputeShader compute, int polarKernel, int receiverKernel)
    {
        if (_receivers.Count == 0)
        {
            return;
        }

        _polar.Sort(s_polarOrder);
        _receivers.Sort(s_receiverOrder);
        _upload.AddRange(_polar);
        _upload.AddRange(_receivers);
        if (_workItems == null || _workItems.count < _upload.Count || _upload.Count <= _workItems.count / 4)
        {
            int capacity = Mathf.NextPowerOfTwo(_upload.Count);
            MemoryAllocationGuard.Require("Dynamic light batch descriptors", (long)capacity * WorkItemStride);
            _workItems?.Release();
            _workItems = new ComputeBuffer(capacity, WorkItemStride, ComputeBufferType.Structured);
        }

        _workItems.SetData(_upload);
        UploadedBytes = checked(_upload.Count * WorkItemStride);
        commands.SetComputeBufferParam(compute, polarKernel, s_workItemsId, _workItems);
        commands.SetComputeBufferParam(compute, receiverKernel, s_workItemsId, _workItems);
        PolarDispatchCount = RecordGroups(commands, compute, polarKernel, _polar, 0, polar: true);
        ReceiverDispatchCount = RecordGroups(commands, compute, receiverKernel, _receivers, _polar.Count, polar: false);
    }

    public void Release()
    {
        _workItems?.Release();
        _workItems = null;
        Begin();
    }

    private static int RecordGroups(CommandBuffer commands, ComputeShader compute, int kernel,
        List<WorkItem> work, int offset, bool polar)
    {
        int dispatches = 0;
        for (int first = 0; first < work.Count;)
        {
            int groupsX = polar ? PolarGroups(work[first]) : LightingComputeBinder.DispatchGroups(work[first].ReceiverSize.x);
            int groupsY = polar ? LightingComputeBinder.DynamicEmitterPointCount : LightingComputeBinder.DispatchGroups(work[first].ReceiverSize.y);
            int end = first + 1;
            while (end < work.Count && end - first < MaximumBatchSources &&
                (polar ? PolarGroups(work[end]) == groupsX : CompareReceiverGroups(work[first], work[end]) == 0))
            {
                end++;
            }

            commands.SetComputeIntParam(compute, s_batchOffsetId, offset + first);
            string marker = polar ? "Kern.Lighting.DynamicPolar" : "Kern.Lighting.DynamicReceiverTrace";
            commands.BeginSample(marker);
            commands.DispatchCompute(compute, kernel, groupsX, groupsY, end - first);
            commands.EndSample(marker);
            dispatches++;
            first = end;
        }

        return dispatches;
    }

    private static int PolarGroups(WorkItem work) => (work.PolarSize.x + 63) / 64;

    private static int CompareReceiverGroups(WorkItem left, WorkItem right)
    {
        int x = LightingComputeBinder.DispatchGroups(left.ReceiverSize.x).CompareTo(
            LightingComputeBinder.DispatchGroups(right.ReceiverSize.x));
        return x != 0 ? x : LightingComputeBinder.DispatchGroups(left.ReceiverSize.y).CompareTo(
            LightingComputeBinder.DispatchGroups(right.ReceiverSize.y));
    }
}
