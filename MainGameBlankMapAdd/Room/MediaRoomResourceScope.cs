using System;
using System.Collections.Generic;
using UnityEngine;

namespace MainGameBlankMapAdd.Room
{
    internal sealed class MediaRoomResourceScope : IDisposable
    {
        private readonly List<RenderTexture> _renderTextures = new List<RenderTexture>();
        private readonly List<Mesh> _meshes = new List<Mesh>();
        private readonly List<Material> _materials = new List<Material>();
        private bool _disposed;

        internal MediaRoomResourceScope(int sourceMapRootId)
        {
            SourceMapRootId = sourceMapRootId;
            Generation = Guid.NewGuid();
        }

        internal Guid Generation { get; }
        internal int SourceMapRootId { get; }
        internal GameObject Root { get; private set; }
        internal bool IsDisposed => _disposed;

        internal void OwnRoot(GameObject root)
        {
            ThrowIfDisposed();
            Root = root ?? throw new ArgumentNullException(nameof(root));
        }

        internal T Own<T>(T resource) where T : UnityEngine.Object
        {
            ThrowIfDisposed();
            if (resource == null) return null;
            if (resource is RenderTexture texture) _renderTextures.Add(texture);
            else if (resource is Mesh mesh) _meshes.Add(mesh);
            else if (resource is Material material) _materials.Add(material);
            else throw new ArgumentException("Unsupported media-room resource type: " + typeof(T).FullName);
            return resource;
        }

        internal bool Owns(Guid generation)
        {
            return !_disposed && generation == Generation;
        }

        private void ThrowIfDisposed()
        {
            if (_disposed) throw new ObjectDisposedException(nameof(MediaRoomResourceScope));
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;

            if (Root != null)
            {
                UnityEngine.Object.Destroy(Root);
                Root = null;
            }

            foreach (RenderTexture texture in _renderTextures)
            {
                if (texture == null) continue;
                texture.Release();
                UnityEngine.Object.Destroy(texture);
            }
            foreach (Material material in _materials)
            {
                if (material != null) UnityEngine.Object.Destroy(material);
            }
            foreach (Mesh mesh in _meshes)
            {
                if (mesh != null) UnityEngine.Object.Destroy(mesh);
            }

            _renderTextures.Clear();
            _materials.Clear();
            _meshes.Clear();
        }
    }
}
