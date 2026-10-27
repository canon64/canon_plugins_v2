using System;
using MainGameBlankMapAdd.Media.Images;
using UnityEngine;

namespace MainGameBlankMapAdd.Characterization
{
    internal static class SlideshowUvMeshTests
    {
        internal static void Run()
        {
            var template = new Mesh(new[] { new Vector3(-.5f, -.5f, 0), new Vector3(.5f, -.5f, 0),
                new Vector3(.5f, .5f, 0), new Vector3(-.5f, .5f, 0) },
                new Bounds { min = new Vector3(-.5f, -.5f, 0), size = new Vector3(1, 1, 0) });
            var current = new SlideshowUvMesh(template, "test");
            var incoming = new SlideshowUvMesh(template, "next");
            current.Apply(new Vector2(1, .5f), new Vector2(0, .5f));
            Vector2[] top = current.Mesh.uv;
            Assert(top[0].Equals(new Vector2(0, .5f)) && top[2].Equals(new Vector2(1, 1)), "top cover crop");
            Vector2[] buffer = current.Mesh.LastUvArgument;
            int reads = current.Mesh.VertexReads, uvReads = current.Mesh.UvReads;
            for (int frame = 1; frame <= 600; frame++)
                current.Apply(new Vector2(1, .5f), new Vector2(0, .5f * (1 - frame / 600f)));
            Assert(ReferenceEquals(buffer, current.Mesh.LastUvArgument), "pan reuses buffer");
            Assert(current.Mesh.VertexReads == reads && current.Mesh.UvReads == uvReads, "pan does not read mesh arrays");
            Vector2[] bottom = current.Mesh.uv;
            Assert(bottom[0].Equals(new Vector2(0, 0)) && bottom[2].Equals(new Vector2(1, .5f)), "bottom cover crop");
            int writes = current.Mesh.UvWrites;
            for (int i = 0; i < 600; i++) current.Apply(new Vector2(1, .5f), new Vector2(0, 0));
            Assert(writes == current.Mesh.UvWrites, "stationary pan skips uploads");
            incoming.Apply(new Vector2(.5f, 1), new Vector2(.5f, 0));
            Assert(!ReferenceEquals(current.Mesh, incoming.Mesh) && current.Mesh.uv[0].Equals(bottom[0]), "crossfade layers have independent UVs");

            var tiledTemplate = new Mesh(new[] {
                new Vector3(-1.5f, -.5f, 0), new Vector3(-.5f, -.5f, 0),
                new Vector3(-.5f, .5f, 0), new Vector3(-1.5f, .5f, 0),
                new Vector3(.5f, -.5f, 0), new Vector3(1.5f, -.5f, 0),
                new Vector3(1.5f, .5f, 0), new Vector3(.5f, .5f, 0)
            }, new Bounds { min = new Vector3(-1.5f, -.5f, 0), size = new Vector3(3f, 1f, 0) });
            tiledTemplate.uv = new[] {
                new Vector2(0, 0), new Vector2(1, 0), new Vector2(1, 1), new Vector2(0, 1),
                new Vector2(0, 0), new Vector2(1, 0), new Vector2(1, 1), new Vector2(0, 1)
            };
            var batched = new SlideshowUvMesh(tiledTemplate, "batched", preserveTemplateUvs: true);
            batched.Apply(new Vector2(1, .5f), new Vector2(0, .5f));
            Vector2[] batchedUv = batched.Mesh.uv;
            Assert(batchedUv[0].Equals(batchedUv[4]) && batchedUv[2].Equals(batchedUv[6])
                && batchedUv[2].Equals(new Vector2(1, 1)), "batched tiles retain independent cover UVs");
            batched.Dispose();

            var room = new GameObject();
            var tiles = new GameObject[108];
            for (int i = 0; i < tiles.Length; i++) { tiles[i] = new GameObject(); tiles[i].transform.SetParent(room.transform, false); }
            Assert(room.transform.childCount == 108, "two layers have 108 tiles");
            foreach (GameObject tile in tiles) SlideshowUvMesh.DestroyTile(tile);
            Assert(room.transform.childCount == 0, "room count changes before deferred Destroy completes");
            foreach (GameObject tile in tiles) Assert(tile.DestroyQueued && !tile.activeSelf, "retired tiles cannot draw");
            Assert(!current.Mesh.DestroyQueued, "tile removal does not destroy shared mesh");
            current.Dispose(); incoming.Dispose();
            Assert(current.Mesh.DestroyQueued && incoming.Mesh.DestroyQueued && !template.DestroyQueued, "only owned meshes released");
            Console.WriteLine("PASS slideshow UV/lifecycle: pan coordinates, reusable arrays, stationary skip, independent fade, batched tile UV, deferred-destroy room count");
        }
        private static void Assert(bool condition, string message)
        {
            if (!condition) throw new Exception("Slideshow UV: " + message);
        }
    }
}
