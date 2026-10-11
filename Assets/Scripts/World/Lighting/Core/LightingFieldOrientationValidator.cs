#nullable enable

using System;
using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using Kern.Core.Interfaces.WorldLighting;
using UnityEngine;
using UnityEngine.Rendering;

namespace Kern.World.Lighting;

/// <summary>
/// Proves once per domain that the field raster and its compute readers agree
/// on row order. A mismatch mirrors every lighting/AO field around its region
/// centre; that is an error, never a silently mirrored image.
/// </summary>
internal static class LightingFieldOrientationValidator
{
    private const int ProbeSize = 4;
    private const string ProbeShaderPath = "Shaders/Lighting/LightingFieldOrientationProbe";

    private static readonly int s_probeFieldId = Shader.PropertyToID("_ProbeField");
    private static readonly int s_probeRowsId = Shader.PropertyToID("_ProbeRows");
    private static readonly int s_probeRowCountId = Shader.PropertyToID("_ProbeRowCount");

    private static LightingOrientationValidationGate s_gate = new();

    public static bool IsPending => s_gate.IsPending;
    public static bool HasFailed => s_gate.IsFailed;
    public static bool IsValidated => s_gate.IsValidated;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetForDomainReload()
    {
        s_gate = new LightingOrientationValidationGate();
    }

    public static UniTask ValidateBeforeWorldEntryAsync()
    {
        if (s_gate.IsValidated)
        {
            return UniTask.CompletedTask;
        }

        if (s_gate.IsPending)
        {
            return s_gate.PendingTask;
        }

        if (!SystemInfo.supportsAsyncGPUReadback)
        {
            throw new NotSupportedException(
                $"Lighting field orientation validation requires AsyncGPUReadback on {SystemInfo.graphicsDeviceType}.");
        }

        UniTask validation = s_gate.Begin();
        ProbeResources? probe = null;
        try
        {
            probe = CreateProbeResources();
            SubmitProbe(probe);
            ProbeResources pendingProbe = probe;
            AsyncGPUReadback.Request(pendingProbe.Rows,
                request => CompleteAsyncProbe(pendingProbe, request));
        }
        catch (Exception exception)
        {
            Exception failure = exception;
            try
            {
                probe?.Dispose();
            }
            catch (Exception cleanupException)
            {
                failure = new AggregateException(
                    "Lighting orientation probe setup and cleanup failed.", failure, cleanupException);
            }
            s_gate.Fail(failure);
        }

        return validation;
    }

    public static void EnsureValidated()
    {
        s_gate.ThrowIfFailed();
        if (s_gate.IsValidated)
        {
            return;
        }

        throw new InvalidOperationException(
            "Lighting field orientation must pass its asynchronous startup validation before lighting resources are created.");
    }

    internal static void ValidateRows(IReadOnlyList<uint> covered)
    {
        bool topDown = LightingFieldOrientation.RowsTopDown;
        for (int row = 0; row < ProbeSize; row++)
        {
            bool upperHalf = topDown ? row < ProbeSize / 2 : row >= ProbeSize / 2;
            if ((covered[row] == 1u) != upperHalf)
            {
                throw new InvalidOperationException(
                    $"Lighting field raster row order disagrees with its readers on {SystemInfo.graphicsDeviceType}: " +
                    $"rows top-down={topDown}, covered=[{string.Join(",", covered)}]. Every field would be mirrored.");
            }
        }
    }

    private static ProbeResources CreateProbeResources(Shader? resolvedShader = null)
    {
        Shader shader = resolvedShader ?? Resources.Load<Shader>(ProbeShaderPath) ??
            throw new InvalidOperationException($"Required shader Resources/{ProbeShaderPath}.shader is missing.");
        ComputeShader compute = Resources.Load<ComputeShader>(ProbeShaderPath) ??
            throw new InvalidOperationException($"Required compute shader Resources/{ProbeShaderPath}.compute is missing.");
        if (!shader.isSupported)
        {
            throw new InvalidOperationException($"Lighting field orientation probe is unsupported on {SystemInfo.graphicsDeviceType}.");
        }
        if (!SystemInfo.supportsComputeShaders)
        {
            throw new NotSupportedException(
                $"Lighting field orientation validation requires compute shaders on {SystemInfo.graphicsDeviceType}.");
        }

        if (!compute.HasKernel("ReadProbeRows"))
        {
            throw new InvalidOperationException("Lighting field orientation compute shader has no ReadProbeRows kernel.");
        }
        int kernel = compute.FindKernel("ReadProbeRows");
        if (!compute.IsSupported(kernel))
        {
            throw new InvalidOperationException(
                $"Lighting field orientation compute kernel failed to compile for {SystemInfo.graphicsDeviceType}.");
        }

        Mesh? mesh = null;
        Material? material = null;
        RenderTexture? target = null;
        ComputeBuffer? rows = null;
        CommandBuffer? commands = null;
        try
        {
            mesh = new Mesh { name = "LightingFieldOrientationProbe" };
            mesh.SetVertices(new[]
            {
                new Vector3(0f, ProbeSize * 0.5f, 0f),
                new Vector3(ProbeSize, ProbeSize * 0.5f, 0f),
                new Vector3(ProbeSize, ProbeSize, 0f),
                new Vector3(0f, ProbeSize, 0f),
            });
            mesh.SetIndices(new[] { 0, 1, 2, 0, 2, 3 }, MeshTopology.Triangles, 0);
            material = new Material(shader) { name = "LightingFieldOrientationProbe" };
            target = new RenderTexture(ProbeSize, ProbeSize, 0, RenderTextureFormat.ARGB32)
            {
                name = "LightingFieldOrientationProbe",
                filterMode = FilterMode.Point,
            };
            rows = new ComputeBuffer(ProbeSize, sizeof(uint));
            commands = new CommandBuffer { name = "Kern Lighting Field Orientation Probe" };
            if (!target.Create())
            {
                throw new InvalidOperationException("Failed to create the lighting field orientation probe target.");
            }

            return new ProbeResources(mesh, material, target, rows, commands, compute);
        }
        catch (Exception setupException)
        {
            List<Exception>? cleanupFailures = null;
            Release(() => commands?.Release());
            Release(() => rows?.Release());
            if (target != null)
            {
                Release(() =>
                {
                    RenderTexture? failedTarget = target;
                    LightingTexturePool.ReleaseTexture(ref failedTarget);
                });
            }
            if (material != null)
            {
                Release(() => LightingTexturePool.DestroyLightingObject(material));
            }
            if (mesh != null)
            {
                Release(() => LightingTexturePool.DestroyLightingObject(mesh));
            }

            if (cleanupFailures != null)
            {
                var failures = new List<Exception>(cleanupFailures.Count + 1) { setupException };
                failures.AddRange(cleanupFailures);
                throw new AggregateException(
                    "Lighting orientation probe setup and cleanup failed.",
                    failures);
            }

            throw;

            void Release(Action release)
            {
                try
                {
                    release();
                }
                catch (Exception cleanupException)
                {
                    (cleanupFailures ??= new List<Exception>()).Add(cleanupException);
                }
            }
        }
    }

    private static void SubmitProbe(ProbeResources probe)
    {
        var worldRect = new Vector4(0f, 0f, ProbeSize, ProbeSize);
        probe.Commands.SetRenderTarget(probe.Target);
        probe.Commands.ClearRenderTarget(clearDepth: false, clearColor: true, backgroundColor: Color.clear);
        probe.Commands.SetViewport(new Rect(0f, 0f, ProbeSize, ProbeSize));
        LightingFieldOrientation.BindRaster(probe.Commands, worldRect, Matrix4x4.identity);
        probe.Commands.DrawMesh(probe.Mesh, Matrix4x4.identity, probe.Material, 0, 0);

        int kernel = probe.Compute.FindKernel("ReadProbeRows");
        probe.Commands.SetComputeTextureParam(probe.Compute, kernel, s_probeFieldId, probe.Target);
        probe.Commands.SetComputeBufferParam(probe.Compute, kernel, s_probeRowsId, probe.Rows);
        probe.Commands.SetComputeIntParam(probe.Compute, s_probeRowCountId, ProbeSize);
        probe.Commands.DispatchCompute(probe.Compute, kernel, 1, 1, 1);
        Graphics.ExecuteCommandBuffer(probe.Commands);
    }

    private static void CompleteAsyncProbe(
        ProbeResources probe,
        AsyncGPUReadbackRequest request)
    {
        Exception? failure = null;
        try
        {
            if (request.hasError)
            {
                throw new InvalidOperationException(
                    $"Lighting field orientation readback failed on {SystemInfo.graphicsDeviceType}.");
            }

            var covered = new uint[ProbeSize];
            var readback = request.GetData<uint>();
            if (readback.Length != ProbeSize)
            {
                throw new InvalidOperationException(
                    $"Lighting field orientation readback returned {readback.Length} rows; expected {ProbeSize}.");
            }
            for (int row = 0; row < ProbeSize; row++)
            {
                covered[row] = readback[row];
            }

            ValidateRows(covered);
        }
        catch (Exception exception)
        {
            failure = exception;
        }

        try
        {
            probe.Dispose();
        }
        catch (Exception exception)
        {
            failure = failure == null
                ? exception
                : new AggregateException("Lighting orientation validation and probe cleanup failed.", failure, exception);
        }

        if (failure != null)
        {
            s_gate.Fail(failure);
            return;
        }

        s_gate.MarkValidated();
    }

    internal sealed class LightingOrientationValidationGate
    {
        private UniTaskCompletionSource? _completion;

        public bool IsValidated { get; private set; }
        public bool IsPending => _completion != null;
        public bool IsFailed => _failure != null;
        public UniTask PendingTask => _completion?.Task ?? UniTask.CompletedTask;
        private Exception? _failure;

        public UniTask Begin()
        {
            if (IsValidated)
            {
                return UniTask.CompletedTask;
            }

            _failure = null;

            if (_completion != null)
            {
                return _completion.Task;
            }

            _completion = new UniTaskCompletionSource();
            return _completion.Task;
        }

        public void MarkValidated()
        {
            if (IsValidated)
            {
                return;
            }

            IsValidated = true;
            _completion?.TrySetResult();
            _completion = null;
        }

        public void Fail(Exception exception)
        {
            UniTaskCompletionSource? completion = _completion;
            _completion = null;
            _failure = exception;
            completion?.TrySetException(exception);
        }

        public void ThrowIfFailed()
        {
            if (_failure != null)
            {
                throw new InvalidOperationException(
                    "Lighting field orientation validation previously failed.", _failure);
            }
        }
    }

    private sealed class ProbeResources : IDisposable
    {
        public ProbeResources(Mesh mesh, Material material, RenderTexture target, ComputeBuffer rows,
            CommandBuffer commands, ComputeShader compute)
        {
            Mesh = mesh;
            Material = material;
            Target = target;
            Rows = rows;
            Commands = commands;
            Compute = compute;
        }

        public Mesh Mesh { get; }
        public Material Material { get; }
        public RenderTexture Target { get; }
        public ComputeBuffer Rows { get; }
        public CommandBuffer Commands { get; }
        public ComputeShader Compute { get; }
        private bool _disposed;

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            List<Exception>? failures = null;
            Release(Commands.Release);
            Release(Rows.Release);
            Release(() =>
            {
                RenderTexture? target = Target;
                LightingTexturePool.ReleaseTexture(ref target);
            });
            Release(() => LightingTexturePool.DestroyLightingObject(Material));
            Release(() => LightingTexturePool.DestroyLightingObject(Mesh));
            if (failures != null)
            {
                throw new AggregateException("Failed to release lighting orientation probe resources.", failures);
            }

            void Release(Action release)
            {
                try
                {
                    release();
                }
                catch (Exception exception)
                {
                    (failures ??= new List<Exception>()).Add(exception);
                }
            }
        }
    }
}
