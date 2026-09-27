#nullable enable

using System;
using Kern.Core;
using Kern.Core.Lifecycle;
using UnityEngine;
using UnityEngine.Rendering;

namespace Kern.World;

internal static class SurfaceMeshUtilities
{
    public static Mesh CreateDynamic(string meshName)
    {
        var mesh = new Mesh
        {
            name = meshName,
            hideFlags = HideFlags.DontSave,
        };
        mesh.MarkDynamic();
        return mesh;
    }

    public static T GetOrAddComponent<T>(GameObject gameObject)
        where T : Component
    {
        bool found = gameObject.TryGetComponent(out T? component);
        if (!found || component == null)
        {
            component = gameObject.AddComponent<T>();
        }

        if (component == null)
        {
            throw new MissingComponentException(
                $"Failed to attach required component {typeof(T).Name} to " +
                $"surface object '{gameObject.name}'.");
        }

        return component;
    }

    public static MeshRenderer BindBandObject(
        ISceneObjectFactory sceneObjects,
        Transform owner,
        int layer,
        string objectName,
        Mesh mesh,
        Material material,
        int sortingOrder)
    {
        Transform? existing = owner.Find(objectName);
        GameObject bandObject;
        if (existing == null)
        {
            bandObject = sceneObjects.Create(objectName, RuntimeOwner.General);
            bandObject.transform.SetParent(owner, worldPositionStays: false);
        }
        else
        {
            bandObject = existing.gameObject;
        }

        bandObject.layer = layer;
        bandObject.transform.SetLocalPositionAndRotation(Vector3.zero, Quaternion.identity);
        bandObject.transform.localScale = Vector3.one;
        MeshFilter meshFilter = GetOrAddComponent<MeshFilter>(bandObject);
        MeshRenderer meshRenderer = GetOrAddComponent<MeshRenderer>(bandObject);
        meshFilter.sharedMesh = mesh;
        meshRenderer.sharedMaterial = material;
        meshRenderer.sortingOrder = sortingOrder;
        bandObject.SetActive(true);
        return meshRenderer;
    }

    public static void DrawLightingField(
        CommandBuffer commandBuffer,
        Mesh mesh,
        Material material,
        string shaderPassName)
    {
        if (mesh.vertexCount == 0)
        {
            return;
        }

        int pass = material.FindPass(shaderPassName);
        if (pass < 0)
        {
            throw new InvalidOperationException(
                $"Surface material '{material.name}' is missing {shaderPassName} pass.");
        }

        commandBuffer.DrawMesh(
            mesh,
            Matrix4x4.identity,
            material,
            submeshIndex: 0,
            shaderPass: pass);
    }

    public static void DestroyOwned(UnityEngine.Object? ownedObject)
    {
        if (ownedObject == null)
        {
            return;
        }

        if (Application.isPlaying)
        {
            UnityEngine.Object.Destroy(ownedObject);
        }
        else
        {
            UnityEngine.Object.DestroyImmediate(ownedObject);
        }
    }

    public static void DestroyOwnedChild(Transform owner, string objectName)
    {
        Transform? ownedChild = owner.Find(objectName);
        if (ownedChild != null)
        {
            DestroyOwned(ownedChild.gameObject);
        }
    }
}
