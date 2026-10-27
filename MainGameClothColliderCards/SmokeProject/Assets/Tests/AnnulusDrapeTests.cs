using System.Collections;
using System.IO;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

public sealed class AnnulusDrapeTests
{
    [UnityTest]
    public IEnumerator AnnulusFallsUnderGravity()
    {
        const int segments = 24;
        const int radialSegments = 8;
        var bone = new GameObject("waist-bone");
        var skirt = new GameObject("annulus-cloth");
        var cameraObject = new GameObject("cloth-smoke-camera");
        var camera = cameraObject.AddComponent<Camera>();
        camera.targetTexture = new RenderTexture(768, 768, 24);
        camera.orthographic = true;
        camera.orthographicSize = 0.6f;
        camera.backgroundColor = new Color(0.1f, 0.12f, 0.17f);
        cameraObject.transform.position = new Vector3(1.2f, 0.8f, -1.5f);
        cameraObject.transform.LookAt(new Vector3(0f, -0.12f, 0f));
        var mesh = new Mesh { name = "AnnulusDrapeSmoke" };
        var vertices = new Vector3[(radialSegments + 1) * segments];
        var weights = new BoneWeight[vertices.Length];
        var triangles = new int[radialSegments * 12 * 6];
        for (int ring = 0; ring <= radialSegments; ring++)
        {
            float radius = Mathf.Lerp(0.11f, 0.46f, (float)ring / radialSegments);
            for (int around = 0; around < segments; around++)
            {
                int index = ring * segments + around;
                float angle = around * Mathf.PI * 2f / segments;
                vertices[index] = new Vector3(Mathf.Cos(angle) * radius,
                                              -0.02f * ring / radialSegments,
                                              Mathf.Sin(angle) * radius);
                weights[index] = new BoneWeight { boneIndex0 = 0, weight0 = 1f };
            }
        }
        int write = 0;
        for (int ring = 0; ring < radialSegments; ring++)
            for (int around = 0; around < segments - 1; around++)
            {
                if (around % 2 == 1) continue;
                int next = (around + 1) % segments;
                int a = ring * segments + around;
                int b = ring * segments + next;
                int c = (ring + 1) * segments + around;
                int d = (ring + 1) * segments + next;
                triangles[write++] = a; triangles[write++] = b; triangles[write++] = c;
                triangles[write++] = b; triangles[write++] = d; triangles[write++] = c;
            }
        mesh.vertices = vertices;
        mesh.boneWeights = weights;
        mesh.bindposes = new[] { bone.transform.worldToLocalMatrix * skirt.transform.localToWorldMatrix };
        mesh.triangles = triangles;
        mesh.RecalculateNormals();
        mesh.RecalculateBounds();
        var renderer = skirt.AddComponent<SkinnedMeshRenderer>();
        renderer.sharedMesh = mesh;
        renderer.sharedMaterial = new Material(Shader.Find("Unlit/Color"));
        renderer.sharedMaterial.color = new Color(0.1f, 0.9f, 1f);
        renderer.bones = new[] { bone.transform };
        renderer.rootBone = bone.transform;
        renderer.updateWhenOffscreen = true;
        renderer.localBounds = new Bounds(new Vector3(0f, -0.25f, 0f), new Vector3(1.2f, 0.8f, 1.2f));
        var cloth = skirt.AddComponent<Cloth>();
        renderer.localBounds = new Bounds(new Vector3(0f, -0.25f, 0f), new Vector3(1.2f, 0.8f, 1.2f));
        var coefficients = new ClothSkinningCoefficient[vertices.Length];
        for (int i = 0; i < coefficients.Length; i++)
            coefficients[i].maxDistance = i < segments ? 0f : 0.65f;
        cloth.coefficients = coefficients;
        cloth.useGravity = true;
        cloth.useTethers = false;
        cloth.externalAcceleration = new Vector3(0f, -20f, 0f);
        cloth.stretchingStiffness = 1f;
        cloth.bendingStiffness = 0f;
        cloth.sleepThreshold = 0f;

        float maxApplied = 0f;
        int free = 0;
        foreach (var coefficient in cloth.coefficients)
        {
            maxApplied = Mathf.Max(maxApplied, coefficient.maxDistance);
            if (coefficient.maxDistance > 0.001f) free++;
        }
        Debug.Log("ANNULUS_SETUP appliedMax=" + maxApplied + " free=" + free +
                  " gravity=" + cloth.useGravity + " physicsGravity=" + Physics.gravity +
                  " enabled=" + cloth.enabled + " visible=" + renderer.isVisible +
                  " bounds=" + renderer.bounds + " localBounds=" + renderer.localBounds +
                  " meshBounds=" + mesh.bounds +
                  " timeScale=" + Time.timeScale + " delta=" + Time.fixedDeltaTime);

        camera.Render();
        yield return new WaitForFixedUpdate();
        SaveFrame(camera, "annulus_stretch001_before.png");
        float startY = AverageY(cloth.vertices);
        float startHemY = RingY(cloth.vertices, segments, radialSegments);
        float startRadialEdge = MeanRadialEdge(cloth.vertices, segments, radialSegments);
        for (int i = 0; i < 150; i++)
        {
            camera.Render();
            yield return new WaitForFixedUpdate();
        }
        float endY = AverageY(cloth.vertices);
        SaveFrame(camera, "annulus_stretch001_after.png");
        Debug.Log("ANNULUS_DRAPE startY=" + startY + " endY=" + endY +
                  " startHemY=" + startHemY + " endHemY=" + RingY(cloth.vertices, segments, radialSegments) +
                  " startHemR=" + RingRadius(vertices, segments, radialSegments) + " endHemR=" + RingRadius(cloth.vertices, segments, radialSegments) +
                  " startRadialEdge=" + startRadialEdge + " endRadialEdge=" + MeanRadialEdge(cloth.vertices, segments, radialSegments) +
                  " simulatedVertices=" + cloth.vertices.Length +
                  " coefficients=" + cloth.coefficients.Length + " visible=" + renderer.isVisible);
        Assert.Less(endY, startY - 0.05f);
        Object.Destroy(cameraObject);
        Object.Destroy(skirt);
        Object.Destroy(bone);
        Object.Destroy(mesh);
    }

    private static float AverageY(Vector3[] vertices)
    {
        float sum = 0f;
        foreach (var vertex in vertices) sum += vertex.y;
        return sum / vertices.Length;
    }

    private static void SaveFrame(Camera camera, string name)
    {
        camera.Render();
        RenderTexture previous = RenderTexture.active;
        RenderTexture.active = camera.targetTexture;
        var image = new Texture2D(camera.targetTexture.width, camera.targetTexture.height, TextureFormat.RGB24, false);
        image.ReadPixels(new Rect(0, 0, image.width, image.height), 0, 0);
        image.Apply();
        string path = Path.Combine(Path.GetDirectoryName(Application.dataPath), "Logs");
        Directory.CreateDirectory(path);
        File.WriteAllBytes(Path.Combine(path, name), image.EncodeToPNG());
        RenderTexture.active = previous;
        Object.Destroy(image);
    }

    private static float RingY(Vector3[] vertices, int segments, int ring)
    {
        float sum = 0f;
        for (int i = 0; i < segments; i++) sum += vertices[ring * segments + i].y;
        return sum / segments;
    }

    private static float RingRadius(Vector3[] vertices, int segments, int ring)
    {
        float sum = 0f;
        for (int i = 0; i < segments; i++)
        {
            Vector3 v = vertices[ring * segments + i];
            sum += Mathf.Sqrt(v.x * v.x + v.z * v.z);
        }
        return sum / segments;
    }

    private static float MeanRadialEdge(Vector3[] vertices, int segments, int radialSegments)
    {
        float sum = 0f;
        for (int ring = 0; ring < radialSegments; ring++)
            for (int i = 0; i < segments; i++)
                sum += Vector3.Distance(vertices[ring * segments + i], vertices[(ring + 1) * segments + i]);
        return sum / (radialSegments * segments);
    }
}
