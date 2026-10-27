using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;
using MainGameBlankMapAdd.Media;
using MainGameBlankMapAdd.Media.Images;

namespace MainGameBlankMapAdd
{
    public sealed partial class Plugin
    {
        private const string ImageSlideshowQuadName = "__ImageSlideshowQuad";
        private const string ImageSlideshowQuadMeshName = "ImageSlideshowQuadMesh";
        private const float ImageSlideshowSurfaceOffset = 0.0015f;

        private static readonly string[] ImageSlideshowExtensions =
            { ".png", ".jpg", ".jpeg", ".bmp" };

        private readonly SlideshowRuntimeState _slideshowState = new SlideshowRuntimeState();
        private float _lastStationarySlideshowOpacity = float.NaN;
        private Material _lastStationarySlideshowMaterial;
        private Material _lastStationaryTransitionMaterial;
        private List<GameObject> _imageSlideshowQuads => _slideshowState.Quads;
        private List<GameObject> _imageSlideshowTransitionQuads => _slideshowState.TransitionQuads;
        private Material _imageSlideshowMaterial { get => _slideshowState.Material; set => _slideshowState.Material = value; }
        private Material _imageSlideshowTransitionMaterial { get => _slideshowState.TransitionMaterial; set => _slideshowState.TransitionMaterial = value; }
        private Texture2D _imageSlideshowTexture { get => _slideshowState.Texture; set => _slideshowState.Texture = value; }
        private Texture2D _imageSlideshowTransitionTexture { get => _slideshowState.TransitionTexture; set => _slideshowState.TransitionTexture = value; }
        private string _imageSlideshowTexturePath { get => _slideshowState.TexturePath; set => _slideshowState.TexturePath = value; }
        private DateTime _imageSlideshowTextureWriteTimeUtc { get => _slideshowState.TextureWriteTimeUtc; set => _slideshowState.TextureWriteTimeUtc = value; }
        private string _imageSlideshowTransitionPath { get => _slideshowState.TransitionPath; set => _slideshowState.TransitionPath = value; }
        private DateTime _imageSlideshowTransitionWriteTimeUtc { get => _slideshowState.TransitionWriteTimeUtc; set => _slideshowState.TransitionWriteTimeUtc = value; }
        private string _imageSlideshowPendingPath { get => _slideshowState.PendingPath; set => _slideshowState.PendingPath = value; }
        private string[] _imageSlideshowFiles { get => _slideshowState.Files; set => _slideshowState.Files = value; }
        private int _imageSlideshowIndex { get => _slideshowState.Index; set => _slideshowState.Index = value; }
        private int _imageSlideshowTransitionIndex { get => _slideshowState.TransitionIndex; set => _slideshowState.TransitionIndex = value; }
        private string _imageSlideshowFolderResolved { get => _slideshowState.FolderResolved; set => _slideshowState.FolderResolved = value; }
        private float _nextImageSlideshowScanTime { get => _slideshowState.NextScanTime; set => _slideshowState.NextScanTime = value; }
        private float _nextImageSlideshowSlideTime { get => _slideshowState.NextSlideTime; set => _slideshowState.NextSlideTime = value; }
        private bool _imageSlideshowTransitionActive { get => _slideshowState.TransitionActive; set => _slideshowState.TransitionActive = value; }
        private float _imageSlideshowTransitionStartTime { get => _slideshowState.TransitionStartTime; set => _slideshowState.TransitionStartTime = value; }
        private float _imageSlideshowTransitionDuration { get => _slideshowState.TransitionDuration; set => _slideshowState.TransitionDuration = value; }
        private float _imageSlideshowDisplayStartTime { get => _slideshowState.DisplayStartTime; set => _slideshowState.DisplayStartTime = value; }
        private float _imageSlideshowTransitionDisplayStartTime { get => _slideshowState.TransitionDisplayStartTime; set => _slideshowState.TransitionDisplayStartTime = value; }
        private string _imageSlideshowTransitionRuntimeMode { get => _slideshowState.TransitionRuntimeMode; set => _slideshowState.TransitionRuntimeMode = value; }
        private bool _imageSlideshowLoggedMissingFolder { get => _slideshowState.LoggedMissingFolder; set => _slideshowState.LoggedMissingFolder = value; }
        private bool _imageSlideshowLoggedNoImages { get => _slideshowState.LoggedNoImages; set => _slideshowState.LoggedNoImages = value; }
        private bool _lastImageSlideshowEnabledState { get => _slideshowState.LastEnabledState; set => _slideshowState.LastEnabledState = value; }
        private string _lastBuiltImageSlideshowSurface { get => _slideshowState.LastBuiltSurface; set => _slideshowState.LastBuiltSurface = value; }
        private string _lastBuiltImageSlideshowMode { get => _slideshowState.LastBuiltMode; set => _slideshowState.LastBuiltMode = value; }
        private int _lastBuiltImageSlideshowChildCount { get => _slideshowState.LastBuiltChildCount; set => _slideshowState.LastBuiltChildCount = value; }
        private GameObject _lastBuiltImageSlideshowRoot { get => _slideshowState.LastBuiltRoot; set => _slideshowState.LastBuiltRoot = value; }
        private string _imageSlideshowFolderInput { get => _slideshowState.FolderInput; set => _slideshowState.FolderInput = value; }
        private string _imageSlideshowSecondsInput { get => _slideshowState.SecondsInput; set => _slideshowState.SecondsInput = value; }
        private string _imageSlideshowScanInput { get => _slideshowState.ScanInput; set => _slideshowState.ScanInput = value; }
        private string _imageSlideshowOpacityInput { get => _slideshowState.OpacityInput; set => _slideshowState.OpacityInput = value; }
        private string _imageSlideshowTransitionSecondsInput { get => _slideshowState.TransitionSecondsInput; set => _slideshowState.TransitionSecondsInput = value; }

        private bool EnsureImageSlideshowFolderPathListConsistency()
        {
            if (_settings == null)
                return false;

            bool changed = false;
            if (_settings.ImageSlideshowFolderPaths == null)
            {
                _settings.ImageSlideshowFolderPaths = new List<string>();
                changed = true;
            }

            var cleaned = new List<string>();
            for (int i = 0; i < _settings.ImageSlideshowFolderPaths.Count; i++)
            {
                string normalized = NormalizeFolderPathForList(_settings.ImageSlideshowFolderPaths[i]);
                if (string.IsNullOrWhiteSpace(normalized))
                {
                    changed = true;
                    continue;
                }

                bool exists = false;
                for (int j = 0; j < cleaned.Count; j++)
                {
                    if (!string.Equals(cleaned[j], normalized, StringComparison.OrdinalIgnoreCase))
                        continue;
                    exists = true;
                    break;
                }

                if (exists)
                {
                    changed = true;
                    continue;
                }

                cleaned.Add(normalized);
            }

            if (cleaned.Count != _settings.ImageSlideshowFolderPaths.Count)
                changed = true;
            else
            {
                for (int i = 0; i < cleaned.Count; i++)
                {
                    if (string.Equals(cleaned[i], _settings.ImageSlideshowFolderPaths[i], StringComparison.Ordinal))
                        continue;
                    changed = true;
                    break;
                }
            }

            if (changed)
                _settings.ImageSlideshowFolderPaths = cleaned;

            string currentPath = NormalizeFolderPathForList(_settings.ImageSlideshowFolderPath);
            if (!string.IsNullOrWhiteSpace(currentPath))
            {
                bool existsInList = false;
                for (int i = 0; i < _settings.ImageSlideshowFolderPaths.Count; i++)
                {
                    if (!string.Equals(_settings.ImageSlideshowFolderPaths[i], currentPath, StringComparison.OrdinalIgnoreCase))
                        continue;
                    existsInList = true;
                    break;
                }

                if (!existsInList)
                {
                    _settings.ImageSlideshowFolderPaths.Add(currentPath);
                    changed = true;
                }
            }
            else if (_settings.ImageSlideshowFolderPaths.Count > 0)
            {
                _settings.ImageSlideshowFolderPath = _settings.ImageSlideshowFolderPaths[0];
                changed = true;
            }

            string normalizedCurrent = NormalizeFolderPathForList(_settings.ImageSlideshowFolderPath);
            if (!string.Equals(_settings.ImageSlideshowFolderPath, normalizedCurrent, StringComparison.Ordinal))
            {
                _settings.ImageSlideshowFolderPath = normalizedCurrent;
                changed = true;
            }

            return changed;
        }

        private void SetCurrentImageSlideshowFolderPath(string folderPath)
        {
            if (_settings == null)
                return;

            string normalized = NormalizeFolderPathForList(folderPath);
            if (string.IsNullOrWhiteSpace(normalized))
                return;

            string current = NormalizeFolderPathForList(_settings.ImageSlideshowFolderPath);
            bool changed = !string.Equals(current, normalized, StringComparison.OrdinalIgnoreCase);
            _settings.ImageSlideshowFolderPath = normalized;
            _imageSlideshowFolderInput = normalized;
            EnsureImageSlideshowFolderPathListConsistency();

            if (!changed)
                return;

            SaveImageSlideshowSettings(true, false, "[slideshow] selected folder path=" + normalized);
        }

        private void AddImageSlideshowFolderPath(string selectedPath)
        {
            if (_settings == null)
                return;

            string normalized = NormalizeFolderPathForList(selectedPath);
            if (string.IsNullOrWhiteSpace(normalized))
                return;

            EnsureImageSlideshowFolderPathListConsistency();

            bool added = false;
            bool exists = false;
            for (int i = 0; i < _settings.ImageSlideshowFolderPaths.Count; i++)
            {
                if (!string.Equals(_settings.ImageSlideshowFolderPaths[i], normalized, StringComparison.OrdinalIgnoreCase))
                    continue;
                exists = true;
                break;
            }
            if (!exists)
            {
                _settings.ImageSlideshowFolderPaths.Add(normalized);
                added = true;
            }

            string current = NormalizeFolderPathForList(_settings.ImageSlideshowFolderPath);
            bool selectedChanged = !string.Equals(current, normalized, StringComparison.OrdinalIgnoreCase);
            if (selectedChanged)
            {
                _settings.ImageSlideshowFolderPath = normalized;
                _imageSlideshowFolderInput = normalized;
            }

            if (!added && !selectedChanged)
                return;

            SaveImageSlideshowSettings(true, false, "[slideshow] " + (added ? "added" : "selected") + " folder path=" + normalized);
        }

        private static string GetImageSlideshowFolderDisplayName(string folderPath)
        {
            string normalized = NormalizeFolderPathForList(folderPath);
            if (string.IsNullOrWhiteSpace(normalized))
                return "(none)";

            string name = Path.GetFileName(normalized.TrimEnd('\\', '/'));
            if (string.IsNullOrWhiteSpace(name))
                return normalized;
            return name;
        }

        private void UpdateImageSlideshow()
        {
            bool enabled = _settings != null && _settings.ImageSlideshowEnabled;
            if (!enabled || _videoRoomRoot == null)
            {
                if (_slideshowIoActive) ResetImageSlideshowIo();
                if (_imageSlideshowQuads.Count > 0)
                    HideAllImageSlideshowQuads();
                _lastImageSlideshowEnabledState = false;
                return;
            }

            NormalizeImageSlideshowSettings();

            ScanImageSlideshowIfNeeded(false);
            PollImageSlideshowLoad();
            string folder = _imageSlideshowFolderResolved;

            if (_imageSlideshowFiles.Length == 0)
            {
                if (!_imageSlideshowLoggedNoImages)
                {
                    LogImageSlideshowVerbose("[slideshow] no images path=" + folder);
                    _imageSlideshowLoggedNoImages = true;
                }
                HideAllImageSlideshowQuads();
                return;
            }
            _imageSlideshowLoggedNoImages = false;

            EnsureImageSlideshowMaterial();
            if (_imageSlideshowMaterial == null)
                return;
            EnsureImageSlideshowTransitionMaterial();

            EnsureImageSlideshowQuads();
            if (_imageSlideshowQuads.Count == 0)
                return;

            if (_imageSlideshowTexture == null || _imageSlideshowIndex < 0 || _imageSlideshowIndex >= _imageSlideshowFiles.Length)
            {
                if (string.IsNullOrEmpty(_slideshowLoadPath))
                    TryShowImageSlideshowIndex(Mathf.Clamp(_imageSlideshowIndex, 0, _imageSlideshowFiles.Length - 1), true);
            }
            else if (!_settings.ImageSlideshowLatestOnly && Time.unscaledTime >= _nextImageSlideshowSlideTime)
            {
                ShowNextImageSlideshow(autoAdvance: true);
            }

            UpdateImageSlideshowTexturePan();
            TickImageSlideshowTransition();
            ApplyImageSlideshowOpacity();
            for (int i = 0; i < _imageSlideshowQuads.Count; i++)
            {
                GameObject quad = _imageSlideshowQuads[i];
                if (quad != null && !quad.activeSelf)
                    quad.SetActive(true);
            }

            if (_imageSlideshowTransitionActive)
            {
                for (int i = 0; i < _imageSlideshowTransitionQuads.Count; i++)
                {
                    GameObject quad = _imageSlideshowTransitionQuads[i];
                    if (quad != null && !quad.activeSelf)
                        quad.SetActive(true);
                }
            }

            _lastImageSlideshowEnabledState = true;
        }

        private void NormalizeImageSlideshowSettings()
        {
            if (_settings == null)
                return;

            if (_settings.ImageSlideshowFolderPath == null)
                _settings.ImageSlideshowFolderPath = string.Empty;
            _settings.ImageSlideshowSeconds = Mathf.Clamp(_settings.ImageSlideshowSeconds, 0.5f, 120f);
            _settings.ImageSlideshowScanIntervalSec = Mathf.Clamp(_settings.ImageSlideshowScanIntervalSec, 0.25f, 60f);
            _settings.ImageSlideshowOpacity = Mathf.Clamp(_settings.ImageSlideshowOpacity, 0.05f, 1f);
            _settings.ImageSlideshowSortMode = NormalizeImageSlideshowSortMode(_settings.ImageSlideshowSortMode);
            _settings.ImageSlideshowFitMode = NormalizeImageSlideshowFitMode(_settings.ImageSlideshowFitMode);
            _settings.ImageSlideshowVerticalFocus = NormalizeImageSlideshowVerticalFocus(_settings.ImageSlideshowVerticalFocus);
            _settings.ImageSlideshowHorizontalFocus = NormalizeImageSlideshowHorizontalFocus(_settings.ImageSlideshowHorizontalFocus);
            _settings.ImageSlideshowPanMode = NormalizeImageSlideshowPanMode(_settings.ImageSlideshowPanMode);
            _settings.ImageSlideshowTileMode = NormalizeImageSlideshowTileMode(_settings.ImageSlideshowTileMode);
            _settings.ImageSlideshowTransitionMode = NormalizeImageSlideshowTransitionMode(_settings.ImageSlideshowTransitionMode);
            _settings.ImageSlideshowTransitionSeconds = Mathf.Clamp(_settings.ImageSlideshowTransitionSeconds, 0.01f, 10f);
            _settings.ImageSlideshowTargetSurface = NormalizeImageSlideshowTargetSurface(_settings.ImageSlideshowTargetSurface);
            _settings.ImageSlideshowPlayMode = NormalizeImageSlideshowPlayMode(_settings.ImageSlideshowPlayMode);
            _settings.ImageSlideshowLatestOnly = (_settings.ImageSlideshowPlayMode == "Latest");
        }

        private void ApplyImageSlideshowCatalog(SlideshowCatalogSnapshot snapshot, bool force)
        {
            _slideshowCatalog = snapshot;
            string folder = snapshot.Folder;
            bool folderChanged = !string.Equals(folder, _imageSlideshowFolderResolved, StringComparison.OrdinalIgnoreCase);
            if (folderChanged)
            {
                _slideshowImageRead.Reset();
                _slideshowLoadPath = null;
            }
            if (!snapshot.Available)
            {
                _imageSlideshowFolderResolved = folder ?? string.Empty;
                ClearImageSlideshowFileList();
                _slideshowImageRead.Reset();
                _slideshowLoadPath = null;
                HideAllImageSlideshowQuads();
                if (!_imageSlideshowLoggedMissingFolder)
                    LogWarn("[slideshow] image folder unavailable: " + folder + " " + snapshot.Error);
                _imageSlideshowLoggedMissingFolder = true;
                return;
            }
            _imageSlideshowLoggedMissingFolder = false;

            string currentPath = GetCurrentImageSlideshowPath();
            string[] previousFiles = _imageSlideshowFiles ?? Array.Empty<string>();
            string[] nextFiles = snapshot.Files;
            bool changed = folderChanged || !AreSamePathSet(previousFiles, nextFiles);
            if (!force && !changed)
            {
                if (_settings.ImageSlideshowLatestOnly)
                    TryShowLatestImageSlideshow();
                return;
            }

            _imageSlideshowFolderResolved = folder;
            _imageSlideshowFiles = nextFiles;
            if (_imageSlideshowFiles.Length == 0)
            {
                _imageSlideshowIndex = -1;
                return;
            }

            if (_settings.ImageSlideshowLatestOnly)
            {
                TryShowLatestImageSlideshow();
                return;
            }

            string newestAdded = FindNewestAddedImage(previousFiles, _imageSlideshowFiles);
            if (!string.IsNullOrWhiteSpace(newestAdded))
            {
                int addedIndex = IndexOfPath(_imageSlideshowFiles, newestAdded);
                if (addedIndex >= 0)
                {
                    if (_imageSlideshowTexture == null && !_imageSlideshowTransitionActive)
                    {
                        _imageSlideshowIndex = addedIndex;
                        TryShowImageSlideshowIndex(addedIndex, true);
                        LogImageSlideshowVerbose("[slideshow] new image shown path=" + newestAdded);
                        return;
                    }

                    _imageSlideshowPendingPath = newestAdded;
                    int currentAddedIndex = IndexOfPath(_imageSlideshowFiles, currentPath);
                    if (currentAddedIndex >= 0)
                    {
                        _imageSlideshowIndex = currentAddedIndex;
                        LogImageSlideshowVerbose("[slideshow] new image queued path=" + newestAdded);
                        return;
                    }
                }
            }

            int currentIndex = IndexOfPath(_imageSlideshowFiles, currentPath);
            if (currentIndex >= 0)
            {
                _imageSlideshowIndex = currentIndex;
                return;
            }

            _imageSlideshowIndex = 0;
            TryShowImageSlideshowIndex(_imageSlideshowIndex, true);
        }

        private static bool IsSupportedImageFile(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
                return false;
            string ext = Path.GetExtension(path);
            return ImageSlideshowExtensions.Contains(ext, StringComparer.OrdinalIgnoreCase);
        }

        private void ForceImageSlideshowRescan()
        {
            _nextImageSlideshowScanTime = 0f;
            ScanImageSlideshowIfNeeded(true);
        }

        private void ShowNextImageSlideshow(bool autoAdvance = false)
        {
            if (_imageSlideshowFiles.Length == 0)
                return;

            if (!string.IsNullOrWhiteSpace(_imageSlideshowPendingPath))
            {
                string pendingPath = _imageSlideshowPendingPath;
                _imageSlideshowPendingPath = string.Empty;
                int pendingIndex = IndexOfPath(_imageSlideshowFiles, pendingPath);
                if (pendingIndex >= 0 && TryShowImageSlideshowIndex(pendingIndex, true))
                    return;
            }

            // Queueモードの自動送り: 控えが無ければ古い絵に回さず現在の絵を放置し、次の間隔まで待つ
            if (autoAdvance)
            {
                ScheduleNextImageSlideshowSlideTime(Time.unscaledTime);
                return;
            }

            int start = string.IsNullOrEmpty(_slideshowLoadPath) ? _imageSlideshowIndex : IndexOfPath(_imageSlideshowFiles, _slideshowLoadPath);
            for (int attempt = 0; attempt < _imageSlideshowFiles.Length; attempt++)
            {
                int next = start < 0
                    ? 0
                    : (start + 1 + attempt) % _imageSlideshowFiles.Length;
                if (TryShowImageSlideshowIndex(next, true))
                    return;
            }

            _nextImageSlideshowSlideTime = Time.unscaledTime + 1f;
        }

        private void ShowPreviousImageSlideshow()
        {
            if (_imageSlideshowFiles.Length == 0)
                return;

            int start = string.IsNullOrEmpty(_slideshowLoadPath) ? _imageSlideshowIndex : IndexOfPath(_imageSlideshowFiles, _slideshowLoadPath);
            for (int attempt = 0; attempt < _imageSlideshowFiles.Length; attempt++)
            {
                int previous = start < 0
                    ? _imageSlideshowFiles.Length - 1
                    : (start - 1 - attempt + _imageSlideshowFiles.Length) % _imageSlideshowFiles.Length;
                if (TryShowImageSlideshowIndex(previous, true))
                    return;
            }

            _nextImageSlideshowSlideTime = Time.unscaledTime + 1f;
        }

        private float GetImageSlideshowSlideInterval()
        {
            return Mathf.Max(0.5f, _settings?.ImageSlideshowSeconds ?? 5f);
        }

        private float GetImageSlideshowEffectiveTransitionDuration()
        {
            float configured = Mathf.Clamp(_settings?.ImageSlideshowTransitionSeconds ?? 0.5f, 0.01f, 10f);
            return configured;
        }

        private float GetImageSlideshowPanDuration()
        {
            float duration = GetImageSlideshowSlideInterval();
            string mode = NormalizeImageSlideshowTransitionMode(_settings?.ImageSlideshowTransitionMode);
            if (!string.Equals(mode, "None", StringComparison.Ordinal))
                duration += GetImageSlideshowEffectiveTransitionDuration();
            return Mathf.Max(0.5f, duration);
        }

        private float GetImageSlideshowNextDelay()
        {
            return GetImageSlideshowSlideInterval();
        }

        private void ScheduleNextImageSlideshowSlideTime(float displayStartTime)
        {
            float startTime = displayStartTime >= 0f ? displayStartTime : Time.unscaledTime;
            _nextImageSlideshowSlideTime = startTime + GetImageSlideshowNextDelay();
        }

        private bool TryShowImageSlideshowIndex(int index, bool resetTimer)
        {
            if (index < 0 || index >= _imageSlideshowFiles.Length)
                return false;

            string path = _imageSlideshowFiles[index];
            DateTime writeTimeUtc = _slideshowCatalog.WriteTime(path);
            if (_imageSlideshowTexture != null &&
                !_imageSlideshowTransitionActive &&
                string.Equals(path, _imageSlideshowTexturePath, StringComparison.OrdinalIgnoreCase) &&
                writeTimeUtc == _imageSlideshowTextureWriteTimeUtc)
            {
                CancelImageSlideshowLoad();
                _imageSlideshowIndex = index;
                if (resetTimer)
                {
                    RestartImageSlideshowTimer();
                }
                return true;
            }
            // 進行中の transition と同じターゲット → 再フェードを発動しない
            if (_imageSlideshowTransitionActive &&
                string.Equals(path, _imageSlideshowTransitionPath, StringComparison.OrdinalIgnoreCase) &&
                writeTimeUtc == _imageSlideshowTransitionWriteTimeUtc)
            {
                CancelImageSlideshowLoad();
                _imageSlideshowIndex = index;
                if (resetTimer) RestartImageSlideshowTimer();
                return true;
            }
            return RequestImageSlideshowLoad(path, writeTimeUtc, resetTimer);
        }

        private Texture2D LoadImageSlideshowTexture(string path, byte[] bytes)
        {
            var texture = new Texture2D(2, 2, TextureFormat.RGBA32, false)
            {
                name = "ImageSlideshow_" + Path.GetFileNameWithoutExtension(path),
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear
            };

            try
            {
                // Unity texture creation/upload stays on the main thread. The compressed
                // bytes were read by the worker; discard the unused CPU pixel copy.
                if (ImageConversion.LoadImage(texture, bytes, true)) return texture;
                throw new InvalidDataException("Image decode failed: " + path);
            }
            catch { Destroy(texture); throw; }
        }

        private bool TryStartImageSlideshowTransition(int index, string path, DateTime writeTimeUtc, Texture2D texture)
        {
            string mode = NormalizeImageSlideshowTransitionMode(_settings?.ImageSlideshowTransitionMode);
            float duration = GetImageSlideshowEffectiveTransitionDuration();
            if (string.Equals(mode, "None", StringComparison.Ordinal) || _imageSlideshowTexture == null || duration <= 0.01f)
                return false;

            if (_imageSlideshowTransitionActive)
                CompleteImageSlideshowTransition(true);

            if (_imageSlideshowTransitionTexture != null)
                Destroy(_imageSlideshowTransitionTexture);

            _imageSlideshowTransitionTexture = texture;
            _imageSlideshowTransitionPath = path;
            _imageSlideshowTransitionWriteTimeUtc = writeTimeUtc;
            _imageSlideshowTransitionIndex = index;
            _imageSlideshowTransitionRuntimeMode = mode;
            _imageSlideshowTransitionStartTime = Time.unscaledTime;
            _imageSlideshowTransitionDuration = duration;
            _imageSlideshowTransitionDisplayStartTime = _imageSlideshowTransitionStartTime;
            _imageSlideshowTransitionActive = true;

            EnsureImageSlideshowTransitionMaterial();
            if (_imageSlideshowTransitionMaterial != null)
            {
                _imageSlideshowTransitionMaterial.mainTexture = texture;
                ApplyImageSlideshowTextureFit(_imageSlideshowTransitionMaterial, texture.width, texture.height, _imageSlideshowTransitionDisplayStartTime);
            }

            for (int i = 0; i < _imageSlideshowTransitionQuads.Count; i++)
            {
                GameObject quad = _imageSlideshowTransitionQuads[i];
                if (quad != null)
                    quad.SetActive(true);
            }

            TickImageSlideshowTransition();
            return true;
        }

        private void ApplyImageSlideshowTextureImmediate(int index, string path, DateTime writeTimeUtc, Texture2D texture)
        {
            if (_imageSlideshowTransitionActive)
                CompleteImageSlideshowTransition(true);

            if (_imageSlideshowTexture != null)
                Destroy(_imageSlideshowTexture);

            _imageSlideshowTexture = texture;
            _imageSlideshowTexturePath = path;
            _imageSlideshowTextureWriteTimeUtc = writeTimeUtc;
            _imageSlideshowIndex = index;
            _imageSlideshowDisplayStartTime = Time.unscaledTime;

            EnsureImageSlideshowMaterial();
            if (_imageSlideshowMaterial != null)
            {
                _imageSlideshowMaterial.mainTexture = texture;
                ApplyImageSlideshowTextureFit(_imageSlideshowMaterial, texture.width, texture.height, _imageSlideshowDisplayStartTime);
            }
            SetImageSlideshowMaterialOpacity(_imageSlideshowMaterial, Mathf.Clamp01(_settings?.ImageSlideshowOpacity ?? 1f));
            HideAllImageSlideshowTransitionQuads();
        }

        private void TickImageSlideshowTransition()
        {
            if (!_imageSlideshowTransitionActive)
                return;

            float progress = Mathf.Clamp01((Time.unscaledTime - _imageSlideshowTransitionStartTime) / Mathf.Max(0.01f, _imageSlideshowTransitionDuration));
            float eased = progress * progress * (3f - 2f * progress);
            float opacity = Mathf.Clamp01(_settings?.ImageSlideshowOpacity ?? 1f);
            float baseAlpha;
            float transitionAlpha;
            if (string.Equals(_imageSlideshowTransitionRuntimeMode, "Fade", StringComparison.Ordinal))
            {
                if (eased < 0.5f)
                {
                    float outT = eased * 2f;
                    baseAlpha = Mathf.Lerp(opacity, 0f, outT);
                    transitionAlpha = 0f;
                }
                else
                {
                    float inT = (eased - 0.5f) * 2f;
                    baseAlpha = 0f;
                    transitionAlpha = Mathf.Lerp(0f, opacity, inT);
                }
            }
            else
            {
                baseAlpha = Mathf.Lerp(opacity, 0f, eased);
                transitionAlpha = Mathf.Lerp(0f, opacity, eased);
            }

            SetImageSlideshowMaterialOpacity(_imageSlideshowMaterial, baseAlpha);
            SetImageSlideshowMaterialOpacity(_imageSlideshowTransitionMaterial, transitionAlpha);

            if (progress >= 1f)
                CompleteImageSlideshowTransition(false);
        }

        private void CompleteImageSlideshowTransition(bool force)
        {
            if (!_imageSlideshowTransitionActive && !force)
                return;
            if (_imageSlideshowTransitionTexture == null)
            {
                _imageSlideshowTransitionActive = false;
                _imageSlideshowTransitionDisplayStartTime = -1f;
                HideAllImageSlideshowTransitionQuads();
                return;
            }

            Texture2D oldTexture = _imageSlideshowTexture;
            _imageSlideshowTexture = _imageSlideshowTransitionTexture;
            _imageSlideshowTexturePath = _imageSlideshowTransitionPath;
            _imageSlideshowTextureWriteTimeUtc = _imageSlideshowTransitionWriteTimeUtc;
            _imageSlideshowIndex = _imageSlideshowTransitionIndex;
            _imageSlideshowDisplayStartTime = _imageSlideshowTransitionDisplayStartTime >= 0f
                ? _imageSlideshowTransitionDisplayStartTime
                : Time.unscaledTime;

            _imageSlideshowTransitionTexture = null;
            _imageSlideshowTransitionPath = string.Empty;
            _imageSlideshowTransitionWriteTimeUtc = DateTime.MinValue;
            _imageSlideshowTransitionIndex = -1;
            _imageSlideshowTransitionDisplayStartTime = -1f;
            _imageSlideshowTransitionActive = false;

            if (oldTexture != null && oldTexture != _imageSlideshowTexture)
                Destroy(oldTexture);

            if (_imageSlideshowMaterial != null)
            {
                _imageSlideshowMaterial.mainTexture = _imageSlideshowTexture;
                if (_imageSlideshowTexture != null)
                    ApplyImageSlideshowTextureFit(_imageSlideshowMaterial, _imageSlideshowTexture.width, _imageSlideshowTexture.height, _imageSlideshowDisplayStartTime);
            }

            SetImageSlideshowMaterialOpacity(_imageSlideshowMaterial, Mathf.Clamp01(_settings?.ImageSlideshowOpacity ?? 1f));
            SetImageSlideshowMaterialOpacity(_imageSlideshowTransitionMaterial, 0f);
            HideAllImageSlideshowTransitionQuads();
        }

        private bool TryShowLatestImageSlideshow(bool resetTimer = false)
        {
            if (_imageSlideshowFiles == null || _imageSlideshowFiles.Length == 0)
                return false;

            int latestIndex = -1;
            DateTime latestWriteTime = DateTime.MinValue;
            for (int i = 0; i < _imageSlideshowFiles.Length; i++)
            {
                string path = _imageSlideshowFiles[i];
                DateTime writeTime = _slideshowCatalog.WriteTime(path);
                if (latestIndex < 0 || writeTime >= latestWriteTime)
                {
                    latestIndex = i;
                    latestWriteTime = writeTime;
                }
            }

            if (latestIndex < 0)
                return false;

            string latestPath = _imageSlideshowFiles[latestIndex];
            bool sameTexture =
                _imageSlideshowTexture != null &&
                string.Equals(latestPath, _imageSlideshowTexturePath, StringComparison.OrdinalIgnoreCase) &&
                latestWriteTime == _imageSlideshowTextureWriteTimeUtc;
            bool sameTransition =
                _imageSlideshowTransitionActive &&
                string.Equals(latestPath, _imageSlideshowTransitionPath, StringComparison.OrdinalIgnoreCase) &&
                latestWriteTime == _imageSlideshowTransitionWriteTimeUtc;
            if (sameTexture || sameTransition)
            {
                CancelImageSlideshowLoad();
                _imageSlideshowIndex = latestIndex;
                if (resetTimer) RestartImageSlideshowTimer();
                return true;
            }

            return TryShowImageSlideshowIndex(latestIndex, resetTimer);
        }

        // 外部命令(HTTP /slideshow/show-latest)から呼ぶ: 今すぐ最新画像へ飛び、送りタイマーを今から再起動する
        private void ForceShowLatestImageSlideshow()
        {
            if (_settings == null || !_settings.ImageSlideshowEnabled)
                return;

            _slideshowShowLatestAfterScan = true;
            ForceImageSlideshowRescan();
        }

        private void EnsureImageSlideshowQuads()
        {
            if (_videoRoomRoot == null)
                return;

            string targetSurface = NormalizeImageSlideshowTargetSurface(_settings.ImageSlideshowTargetSurface);
            string tileMode = NormalizeImageSlideshowTileMode(_settings.ImageSlideshowTileMode);
            int currentChildCount = _videoRoomRoot.transform.childCount;
            bool surfaceChanged = !string.Equals(targetSurface, _lastBuiltImageSlideshowSurface, StringComparison.Ordinal);
            bool modeChanged = !string.Equals(tileMode, _lastBuiltImageSlideshowMode, StringComparison.Ordinal);
            bool roomChanged = _lastBuiltImageSlideshowRoot != _videoRoomRoot
                || currentChildCount != _lastBuiltImageSlideshowChildCount;
            bool enabledRising = !_lastImageSlideshowEnabledState;

            if (!surfaceChanged && !modeChanged && !roomChanged && !enabledRising && _imageSlideshowQuads.Count > 0)
                return;

            RebuildImageSlideshowQuads(targetSurface, tileMode);
            _lastBuiltImageSlideshowSurface = targetSurface;
            _lastBuiltImageSlideshowMode = tileMode;
            _lastBuiltImageSlideshowChildCount = _videoRoomRoot != null ? _videoRoomRoot.transform.childCount : -1;
            _lastBuiltImageSlideshowRoot = _videoRoomRoot;
            if (_imageSlideshowTexture != null)
                ApplyImageSlideshowTextureFit(_imageSlideshowMaterial, _imageSlideshowTexture.width, _imageSlideshowTexture.height, _imageSlideshowDisplayStartTime);
            if (_imageSlideshowTransitionTexture != null)
                ApplyImageSlideshowTextureFit(_imageSlideshowTransitionMaterial, _imageSlideshowTransitionTexture.width, _imageSlideshowTransitionTexture.height, _imageSlideshowTransitionDisplayStartTime);
        }

        private void RebuildImageSlideshowQuads(string targetSurfaceCsv, string tileMode)
        {
            DestroyImageSlideshowQuads();
            DestroyImageSlideshowTransitionQuads();
            if (_videoRoomRoot == null)
                return;

            EnsureImageSlideshowMaterial();
            EnsureImageSlideshowTransitionMaterial();

            HashSet<string> selected = ParseLogicalFaceSet(targetSurfaceCsv);
            if (selected.Count == 0)
                return;

            foreach (string logicalName in OverlayLogicalFaceNames)
            {
                if (!selected.Contains(logicalName))
                    continue;

                List<Transform> targets = ResolveTargetVideoSurfaces(logicalName);
                if (targets.Count == 0)
                    continue;

                if (string.Equals(tileMode, "tile", StringComparison.Ordinal))
                {
                    for (int ti = 0; ti < targets.Count; ti++)
                    {
                        CreateImageSlideshowQuadForTile(
                            targets[ti],
                            _imageSlideshowMaterial,
                            _imageSlideshowQuads,
                            ImageSlideshowSurfaceOffset);
                        CreateImageSlideshowQuadForTile(
                            targets[ti],
                            _imageSlideshowTransitionMaterial,
                            _imageSlideshowTransitionQuads,
                            ImageSlideshowSurfaceOffset + 0.0006f);
                    }
                }
                else
                {
                    CreateImageSlideshowQuadForLogicalGroup(
                        targets,
                        _imageSlideshowMaterial,
                        _imageSlideshowQuads,
                        ImageSlideshowSurfaceOffset);
                    CreateImageSlideshowQuadForLogicalGroup(
                        targets,
                        _imageSlideshowTransitionMaterial,
                        _imageSlideshowTransitionQuads,
                        ImageSlideshowSurfaceOffset + 0.0006f);
                }
            }

            if (string.Equals(tileMode, "tile", StringComparison.Ordinal))
            {
                CombineImageSlideshowTiles(_imageSlideshowQuads, ref _slideshowUvMesh,
                    _imageSlideshowMaterial, ImageSlideshowQuadName + "_Batch");
                CombineImageSlideshowTiles(_imageSlideshowTransitionQuads, ref _slideshowTransitionUvMesh,
                    _imageSlideshowTransitionMaterial, ImageSlideshowQuadName + "_TransitionBatch");
            }

            HideAllImageSlideshowTransitionQuads();
            LogImageSlideshowVerbose("[slideshow] built quads count=" + _imageSlideshowQuads.Count + " surfaces=" + targetSurfaceCsv + " mode=" + tileMode);
        }

        private void CombineImageSlideshowTiles(
            List<GameObject> quads, ref SlideshowUvMesh uvMesh, Material material, string name)
        {
            if (_videoRoomRoot == null || quads == null || quads.Count < 2 || material == null)
                return;

            // The old per-tile path derives UVs from quad positions, not the
            // primitive mesh's UV layout. Capture those exact UVs before merging.
            uvMesh?.Apply(Vector2.one, Vector2.zero);
            Transform room = _videoRoomRoot.transform;
            var instances = new List<CombineInstance>(quads.Count);
            for (int i = 0; i < quads.Count; i++)
            {
                GameObject quad = quads[i];
                MeshFilter filter = quad != null ? quad.GetComponent<MeshFilter>() : null;
                if (filter == null || filter.sharedMesh == null)
                    continue;
                instances.Add(new CombineInstance
                {
                    mesh = filter.sharedMesh,
                    transform = room.worldToLocalMatrix * quad.transform.localToWorldMatrix
                });
            }
            if (instances.Count < 2)
                return;

            Mesh combined = new Mesh { name = name + "_SourceMesh" };
            SlideshowUvMesh combinedUv = null;
            GameObject batch = null;
            try
            {
                combined.CombineMeshes(instances.ToArray(), true, true);
                combinedUv = new SlideshowUvMesh(combined, name + "_Mesh", preserveTemplateUvs: true);
                batch = new GameObject(name, typeof(MeshFilter), typeof(MeshRenderer));
                batch.transform.SetParent(room, worldPositionStays: false);
                batch.GetComponent<MeshFilter>().sharedMesh = combinedUv.Mesh;
                MeshRenderer renderer = batch.GetComponent<MeshRenderer>();
                renderer.sharedMaterial = material;
                renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                renderer.receiveShadows = false;

                for (int i = 0; i < quads.Count; i++)
                    DestroyImageSlideshowQuad(quads[i]);
                quads.Clear();
                quads.Add(batch);
                uvMesh?.Dispose();
                uvMesh = combinedUv;
            }
            catch (Exception ex)
            {
                if (batch != null) Destroy(batch);
                combinedUv?.Dispose();
                LogWarn("[slideshow] tile batching failed: " + ex.Message);
            }
            finally
            {
                Destroy(combined);
            }
        }

        private void CreateImageSlideshowQuadForTile(
            Transform target,
            Material material,
            List<GameObject> destination,
            float surfaceOffset)
        {
            if (target == null || destination == null)
                return;

            GameObject quad = GameObject.CreatePrimitive(PrimitiveType.Quad);
            quad.name = ImageSlideshowQuadName;
            AssignImageSlideshowQuadMesh(quad, material);
            Collider collider = quad.GetComponent<Collider>();
            if (collider != null)
                Destroy(collider);

            quad.transform.SetParent(_videoRoomRoot.transform, worldPositionStays: false);
            Vector3 normal = target.rotation * Vector3.back;
            quad.transform.SetPositionAndRotation(target.position + normal * surfaceOffset, target.rotation);
            Vector3 targetScale = target.lossyScale;
            float side = Mathf.Min(Mathf.Abs(targetScale.x), Mathf.Abs(targetScale.y));
            quad.transform.localScale = ConvertWorldScaleToLocalScale(
                quad.transform.parent,
                new Vector3(side, side, 1f));

            ApplyImageSlideshowMaterial(quad, material);
            destination.Add(quad);
        }

        private void CreateImageSlideshowQuadForLogicalGroup(
            List<Transform> targets,
            Material material,
            List<GameObject> destination,
            float surfaceOffset)
        {
            if (targets == null || targets.Count == 0 || destination == null)
                return;

            Quaternion rotation = targets[0].rotation;
            Quaternion invRotation = Quaternion.Inverse(rotation);

            float minX = float.MaxValue;
            float maxX = float.MinValue;
            float minY = float.MaxValue;
            float maxY = float.MinValue;
            Vector3 sumPos = Vector3.zero;
            int count = 0;
            foreach (Transform t in targets)
            {
                if (t == null)
                    continue;

                Vector3 local = invRotation * t.position;
                Vector3 size = t.lossyScale;
                float halfX = size.x * 0.5f;
                float halfY = size.y * 0.5f;
                if (local.x - halfX < minX) minX = local.x - halfX;
                if (local.x + halfX > maxX) maxX = local.x + halfX;
                if (local.y - halfY < minY) minY = local.y - halfY;
                if (local.y + halfY > maxY) maxY = local.y + halfY;
                sumPos += t.position;
                count++;
            }

            if (count == 0)
                return;

            Vector3 centerLocal = new Vector3(
                (minX + maxX) * 0.5f,
                (minY + maxY) * 0.5f,
                (invRotation * (sumPos / count)).z);
            Vector3 centerWorld = rotation * centerLocal;
            float width = maxX - minX;
            float height = maxY - minY;
            float side = Mathf.Min(Mathf.Abs(width), Mathf.Abs(height));

            GameObject quad = GameObject.CreatePrimitive(PrimitiveType.Quad);
            quad.name = ImageSlideshowQuadName;
            AssignImageSlideshowQuadMesh(quad, material);
            Collider collider = quad.GetComponent<Collider>();
            if (collider != null)
                Destroy(collider);

            quad.transform.SetParent(_videoRoomRoot.transform, worldPositionStays: false);
            Vector3 normal = rotation * Vector3.back;
            quad.transform.SetPositionAndRotation(centerWorld + normal * surfaceOffset, rotation);
            quad.transform.localScale = ConvertWorldScaleToLocalScale(
                quad.transform.parent,
                new Vector3(side, side, 1f));

            ApplyImageSlideshowMaterial(quad, material);

            destination.Add(quad);
        }

        private void AssignImageSlideshowQuadMesh(GameObject quad, Material material)
        {
            if (quad == null)
                return;

            MeshFilter meshFilter = quad.GetComponent<MeshFilter>();
            if (meshFilter == null || meshFilter.sharedMesh == null)
                return;

            if (material == _imageSlideshowTransitionMaterial)
            {
                if (_slideshowTransitionUvMesh == null)
                    _slideshowTransitionUvMesh = new SlideshowUvMesh(meshFilter.sharedMesh, ImageSlideshowQuadMeshName);
                meshFilter.sharedMesh = _slideshowTransitionUvMesh.Mesh;
            }
            else
            {
                if (_slideshowUvMesh == null)
                    _slideshowUvMesh = new SlideshowUvMesh(meshFilter.sharedMesh, ImageSlideshowQuadMeshName);
                meshFilter.sharedMesh = _slideshowUvMesh.Mesh;
            }
        }

        private static void DestroyImageSlideshowQuad(GameObject quad)
        {
            SlideshowUvMesh.DestroyTile(quad);
        }

        private void ApplyImageSlideshowMaterial(GameObject quad, Material material)
        {
            if (quad == null)
                return;

            MeshRenderer renderer = quad.GetComponent<MeshRenderer>();
            if (renderer == null)
                return;

            renderer.sharedMaterial = material;
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;
        }

        private void EnsureImageSlideshowMaterial()
        {
            if (_imageSlideshowMaterial != null)
                return;

            _imageSlideshowMaterial = CreateImageSlideshowMaterial("ImageSlideshowMat", -10);
            ApplyImageSlideshowOpacity();
        }

        private void EnsureImageSlideshowTransitionMaterial()
        {
            if (_imageSlideshowTransitionMaterial != null)
                return;

            _imageSlideshowTransitionMaterial = CreateImageSlideshowMaterial("ImageSlideshowTransitionMat", -9);
            SetImageSlideshowMaterialOpacity(_imageSlideshowTransitionMaterial, 0f);
        }

        private Material CreateImageSlideshowMaterial(string name, int renderQueueOffset)
        {
            string shaderSource = string.Empty;
            Shader shader = FindImageSlideshowShader(out shaderSource);
            if (shader == null)
                shader = Shader.Find("Unlit/Transparent");
            if (shader == null)
                shader = Shader.Find("Unlit/Texture");

            var material = new Material(shader)
            {
                name = name,
                renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent + renderQueueOffset
            };

            material.SetOverrideTag("Queue", "Transparent");
            if (material.HasProperty("_ZWrite"))
                material.SetInt("_ZWrite", 0);
            if (material.HasProperty("_Cull"))
                material.SetInt("_Cull", (int)UnityEngine.Rendering.CullMode.Off);
            if (material.HasProperty("_Mode"))
                material.SetFloat("_Mode", 3f);
            if (material.HasProperty("_Surface"))
                material.SetFloat("_Surface", 1f);
            if (material.HasProperty("_Blend"))
                material.SetFloat("_Blend", 0f);
            if (material.HasProperty("_SrcBlend"))
                material.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
            if (material.HasProperty("_DstBlend"))
                material.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            if (material.HasProperty("_BlendOp"))
                material.SetInt("_BlendOp", (int)UnityEngine.Rendering.BlendOp.Add);
            if (material.HasProperty("_AlphaSrcBlend"))
                material.SetInt("_AlphaSrcBlend", (int)UnityEngine.Rendering.BlendMode.One);
            if (material.HasProperty("_AlphaDstBlend"))
                material.SetInt("_AlphaDstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            if (material.HasProperty("_DstBlendAlpha"))
                material.SetInt("_DstBlendAlpha", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            if (material.HasProperty("_SrcBlendAlpha"))
                material.SetInt("_SrcBlendAlpha", (int)UnityEngine.Rendering.BlendMode.One);
            if (material.HasProperty("_AlphaToMask"))
                material.SetInt("_AlphaToMask", 0);
            material.EnableKeyword("_ALPHABLEND_ON");
            material.DisableKeyword("_ALPHATEST_ON");
            material.DisableKeyword("_ALPHAPREMULTIPLY_ON");
            material.SetOverrideTag("RenderType", "Transparent");
            LogImageSlideshowVerbose(
                "[slideshow] material created name=" + name +
                " shader=" + (shader != null ? shader.name : "(null)") +
                " source=" + shaderSource +
                " queue=" + material.renderQueue +
                " hasColor=" + material.HasProperty("_Color") +
                " hasTint=" + material.HasProperty("_TintColor") +
                " hasBaseColor=" + material.HasProperty("_BaseColor") +
                " hasSrcBlend=" + material.HasProperty("_SrcBlend") +
                " hasDstBlend=" + material.HasProperty("_DstBlend"));
            return material;
        }

        private static Shader FindImageSlideshowShader(out string source)
        {
            string[] names =
            {
                "Unlit/Transparent Colored",
                "Sprites/Default",
                "Particles/Alpha Blended",
                "Mobile/Particles/Alpha Blended",
                "Unlit/Transparent"
            };

            for (int i = 0; i < names.Length; i++)
            {
                Shader shader = Shader.Find(names[i]);
                if (shader == null)
                    continue;

                source = names[i];
                return shader;
            }

            source = "(fallback)";
            return null;
        }

        private void ApplyImageSlideshowOpacity()
        {
            if (_imageSlideshowMaterial == null || _settings == null)
                return;

            float opacity = Mathf.Clamp01(_settings.ImageSlideshowOpacity);
            if (_imageSlideshowTransitionActive)
            {
                _lastStationarySlideshowOpacity = float.NaN;
                return;
            }

            if (_lastStationarySlideshowOpacity == opacity &&
                _lastStationarySlideshowMaterial == _imageSlideshowMaterial &&
                _lastStationaryTransitionMaterial == _imageSlideshowTransitionMaterial)
                return;

            SetImageSlideshowMaterialOpacity(_imageSlideshowMaterial, opacity);
            SetImageSlideshowMaterialOpacity(_imageSlideshowTransitionMaterial, 0f);
            _lastStationarySlideshowOpacity = opacity;
            _lastStationarySlideshowMaterial = _imageSlideshowMaterial;
            _lastStationaryTransitionMaterial = _imageSlideshowTransitionMaterial;
        }

        private static void SetImageSlideshowMaterialOpacity(Material material, float opacity)
        {
            if (material == null)
                return;

            float clamped = Mathf.Clamp01(opacity);
            bool applied = false;
            if (material.HasProperty("_TintColor"))
            {
                Color current = material.GetColor("_TintColor");
                Color color = current;
                color.r = 1f;
                color.g = 1f;
                color.b = 1f;
                color.a = clamped;
                if (current != color)
                    material.SetColor("_TintColor", color);
                applied = true;
            }
            if (material.HasProperty("_Color"))
            {
                Color current = material.color;
                Color color = current;
                color.r = 1f;
                color.g = 1f;
                color.b = 1f;
                color.a = clamped;
                if (current != color)
                    material.color = color;
                applied = true;
            }
            if (!applied && material.HasProperty("_BaseColor"))
            {
                Color current = material.GetColor("_BaseColor");
                Color color = current;
                color.r = 1f;
                color.g = 1f;
                color.b = 1f;
                color.a = clamped;
                if (current != color)
                    material.SetColor("_BaseColor", color);
            }
        }

        private void UpdateImageSlideshowTexturePan()
        {
            if (_imageSlideshowTexture != null)
                ApplyImageSlideshowTextureFit(_imageSlideshowMaterial, _imageSlideshowTexture.width, _imageSlideshowTexture.height, _imageSlideshowDisplayStartTime);
            if (_imageSlideshowTransitionActive && _imageSlideshowTransitionTexture != null)
                ApplyImageSlideshowTextureFit(_imageSlideshowTransitionMaterial, _imageSlideshowTransitionTexture.width, _imageSlideshowTransitionTexture.height, _imageSlideshowTransitionDisplayStartTime);
        }

        private void ApplyImageSlideshowTextureFit(Material material, int textureWidth, int textureHeight, float displayStartTime = -1f)
        {
            if (material == null || textureWidth <= 0 || textureHeight <= 0)
                return;

            Vector2 scale = Vector2.one;
            Vector2 offset = Vector2.zero;
            string fitMode = NormalizeImageSlideshowFitMode(_settings?.ImageSlideshowFitMode);
            if (string.Equals(fitMode, "Cover", StringComparison.Ordinal))
            {
                const float squareAspect = 1f;
                float targetAspect = squareAspect;
                float imageAspect = (float)textureWidth / textureHeight;
                if (imageAspect > targetAspect)
                {
                    scale.x = Mathf.Clamp01(targetAspect / imageAspect);
                    offset.x = ResolveImageSlideshowHorizontalOffset(1f - scale.x, displayStartTime);
                }
                else if (imageAspect < targetAspect)
                {
                    scale.y = Mathf.Clamp01(imageAspect / targetAspect);
                    offset.y = ResolveImageSlideshowVerticalOffset(1f - scale.y, displayStartTime);
                }
            }

            if (material.mainTextureScale != Vector2.one)
                material.mainTextureScale = Vector2.one;
            if (material.mainTextureOffset != Vector2.zero)
                material.mainTextureOffset = Vector2.zero;

            if (material == _imageSlideshowTransitionMaterial)
                _slideshowTransitionUvMesh?.Apply(scale, offset);
            else
                _slideshowUvMesh?.Apply(scale, offset);
        }

        private float ResolveImageSlideshowHorizontalOffset(float maxOffset, float displayStartTime)
        {
            if (maxOffset <= 0f)
                return 0f;

            string panMode = NormalizeImageSlideshowPanMode(_settings?.ImageSlideshowPanMode);
            float progress = GetImageSlideshowPanProgress(displayStartTime);
            if (string.Equals(panMode, "LeftToRight", StringComparison.Ordinal))
                return Mathf.Lerp(0f, maxOffset, progress);
            if (string.Equals(panMode, "RightToLeft", StringComparison.Ordinal))
                return Mathf.Lerp(maxOffset, 0f, progress);

            string focus = NormalizeImageSlideshowHorizontalFocus(_settings?.ImageSlideshowHorizontalFocus);
            if (string.Equals(focus, "Left", StringComparison.Ordinal))
                return 0f;
            if (string.Equals(focus, "Right", StringComparison.Ordinal))
                return maxOffset;
            return maxOffset * 0.5f;
        }

        private float ResolveImageSlideshowVerticalOffset(float maxOffset, float displayStartTime)
        {
            if (maxOffset <= 0f)
                return 0f;

            string panMode = NormalizeImageSlideshowPanMode(_settings?.ImageSlideshowPanMode);
            float progress = GetImageSlideshowPanProgress(displayStartTime);
            if (string.Equals(panMode, "TopToBottom", StringComparison.Ordinal))
                return Mathf.Lerp(maxOffset, 0f, progress);
            if (string.Equals(panMode, "BottomToTop", StringComparison.Ordinal))
                return Mathf.Lerp(0f, maxOffset, progress);

            string focus = NormalizeImageSlideshowVerticalFocus(_settings?.ImageSlideshowVerticalFocus);
            if (string.Equals(focus, "Bottom", StringComparison.Ordinal))
                return 0f;
            if (string.Equals(focus, "Top", StringComparison.Ordinal))
                return maxOffset;
            return maxOffset * 0.5f;
        }

        private float GetImageSlideshowPanProgress(float displayStartTime)
        {
            if (displayStartTime < 0f)
                return 0f;

            float duration = GetImageSlideshowPanDuration();
            return Mathf.Clamp01((Time.unscaledTime - displayStartTime) / duration);
        }

        private void LogImageSlideshowVerbose(string message)
        {
            if (_settings == null || !_settings.VerboseLog)
                return;
            LogInfo(message);
        }

        private string GetCurrentImageSlideshowPath()
        {
            if (_imageSlideshowIndex < 0 || _imageSlideshowIndex >= _imageSlideshowFiles.Length)
                return _imageSlideshowTexturePath;
            return _imageSlideshowFiles[_imageSlideshowIndex];
        }

        private static bool AreSamePathSet(string[] left, string[] right)
        {
            if ((left?.Length ?? 0) != (right?.Length ?? 0))
                return false;
            var set = new HashSet<string>(left ?? Array.Empty<string>(), StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < (right?.Length ?? 0); i++)
            {
                if (!set.Contains(right[i]))
                    return false;
            }
            return true;
        }

        private string FindNewestAddedImage(string[] previous, string[] current)
        {
            var oldSet = new HashSet<string>(previous ?? Array.Empty<string>(), StringComparer.OrdinalIgnoreCase);
            string newest = null;
            DateTime newestWrite = DateTime.MinValue;
            for (int i = 0; i < (current?.Length ?? 0); i++)
            {
                string path = current[i];
                if (oldSet.Contains(path))
                    continue;
                DateTime write = _slideshowCatalog.WriteTime(path);
                if (newest == null || write >= newestWrite)
                {
                    newest = path;
                    newestWrite = write;
                }
            }
            return newest;
        }

        private static int IndexOfPath(string[] files, string path)
        {
            if (files == null || string.IsNullOrWhiteSpace(path))
                return -1;
            for (int i = 0; i < files.Length; i++)
            {
                if (string.Equals(files[i], path, StringComparison.OrdinalIgnoreCase))
                    return i;
            }
            return -1;
        }

        private static string NormalizeImageSlideshowSortMode(string value)
        {
            return SlideshowFileCatalog.NormalizeSortMode(value);
        }

        private static string GetNextImageSlideshowSortMode(string value)
        {
            string mode = NormalizeImageSlideshowSortMode(value);
            if (mode == "Name") return "Date";
            if (mode == "Date") return "Random";
            return "Name";
        }

        private static string NormalizeImageSlideshowFitMode(string value)
        {
            if (string.Equals(value, "Stretch", StringComparison.OrdinalIgnoreCase))
                return "Stretch";
            return "Cover";
        }

        private static string GetNextImageSlideshowFitMode(string value)
        {
            return NormalizeImageSlideshowFitMode(value) == "Cover" ? "Stretch" : "Cover";
        }

        private static string NormalizeImageSlideshowVerticalFocus(string value)
        {
            if (string.Equals(value, "Top", StringComparison.OrdinalIgnoreCase))
                return "Top";
            if (string.Equals(value, "Bottom", StringComparison.OrdinalIgnoreCase))
                return "Bottom";
            return "Center";
        }

        private static string GetNextImageSlideshowVerticalFocus(string value)
        {
            string focus = NormalizeImageSlideshowVerticalFocus(value);
            if (focus == "Center") return "Top";
            if (focus == "Top") return "Bottom";
            return "Center";
        }

        private static string NormalizeImageSlideshowHorizontalFocus(string value)
        {
            if (string.Equals(value, "Left", StringComparison.OrdinalIgnoreCase))
                return "Left";
            if (string.Equals(value, "Right", StringComparison.OrdinalIgnoreCase))
                return "Right";
            return "Center";
        }

        private static string GetNextImageSlideshowHorizontalFocus(string value)
        {
            string focus = NormalizeImageSlideshowHorizontalFocus(value);
            if (focus == "Center") return "Left";
            if (focus == "Left") return "Right";
            return "Center";
        }

        private static string NormalizeImageSlideshowPanMode(string value)
        {
            if (string.Equals(value, "TopToBottom", StringComparison.OrdinalIgnoreCase))
                return "TopToBottom";
            if (string.Equals(value, "BottomToTop", StringComparison.OrdinalIgnoreCase))
                return "BottomToTop";
            if (string.Equals(value, "LeftToRight", StringComparison.OrdinalIgnoreCase))
                return "LeftToRight";
            if (string.Equals(value, "RightToLeft", StringComparison.OrdinalIgnoreCase))
                return "RightToLeft";
            return "None";
        }

        private static string GetNextImageSlideshowPanMode(string value)
        {
            string mode = NormalizeImageSlideshowPanMode(value);
            if (mode == "None") return "TopToBottom";
            if (mode == "TopToBottom") return "BottomToTop";
            if (mode == "BottomToTop") return "LeftToRight";
            if (mode == "LeftToRight") return "RightToLeft";
            return "None";
        }

        private static string GetShortImageSlideshowPanModeLabel(string value)
        {
            string mode = NormalizeImageSlideshowPanMode(value);
            if (mode == "TopToBottom") return "T>B";
            if (mode == "BottomToTop") return "B>T";
            if (mode == "LeftToRight") return "L>R";
            if (mode == "RightToLeft") return "R>L";
            return "None";
        }

        private static string NormalizeImageSlideshowPlayMode(string value)
        {
            if (string.Equals(value, "Queue", StringComparison.OrdinalIgnoreCase))
                return "Queue";
            return "Latest";
        }

        private static string NormalizeImageSlideshowTileMode(string value)
        {
            if (string.Equals(value, "tile", StringComparison.OrdinalIgnoreCase))
                return "tile";
            return "single";
        }

        private static string GetNextImageSlideshowTileMode(string value)
        {
            return NormalizeImageSlideshowTileMode(value) == "single" ? "tile" : "single";
        }

        private static string NormalizeImageSlideshowTransitionMode(string value)
        {
            if (string.Equals(value, "Fade", StringComparison.OrdinalIgnoreCase))
                return "Fade";
            if (string.Equals(value, "None", StringComparison.OrdinalIgnoreCase))
                return "None";
            return "CrossFade";
        }

        private static string GetNextImageSlideshowTransitionMode(string value)
        {
            string mode = NormalizeImageSlideshowTransitionMode(value);
            if (mode == "CrossFade") return "Fade";
            if (mode == "Fade") return "None";
            return "CrossFade";
        }

        private static string NormalizeImageSlideshowTargetSurface(string raw)
        {
            if (string.IsNullOrWhiteSpace(raw))
                return "All";
            if (string.Equals(raw, "All", StringComparison.OrdinalIgnoreCase))
                return "All";
            if (string.Equals(raw, "None", StringComparison.OrdinalIgnoreCase))
                return "None";
            if (string.Equals(raw.Trim(), "None", StringComparison.OrdinalIgnoreCase))
                return "None";

            HashSet<string> set = ParseLogicalFaceSet(raw);
            if (set.Count == 0)
                return "None";
            if (set.Count == OverlayLogicalFaceNames.Length)
                return "All";

            var ordered = new List<string>();
            foreach (string name in OverlayLogicalFaceNames)
            {
                if (set.Contains(name))
                    ordered.Add(name);
            }
            return string.Join(",", ordered.ToArray());
        }

        private void ClearImageSlideshowFileList()
        {
            _imageSlideshowFiles = Array.Empty<string>();
            _imageSlideshowIndex = -1;
            _imageSlideshowTexturePath = string.Empty;
            _imageSlideshowTextureWriteTimeUtc = DateTime.MinValue;
            _imageSlideshowPendingPath = string.Empty;
            _imageSlideshowDisplayStartTime = -1f;
        }

        private void HideAllImageSlideshowQuads()
        {
            for (int i = 0; i < _imageSlideshowQuads.Count; i++)
            {
                GameObject quad = _imageSlideshowQuads[i];
                if (quad != null && quad.activeSelf)
                    quad.SetActive(false);
            }
            HideAllImageSlideshowTransitionQuads();
        }

        private void HideAllImageSlideshowTransitionQuads()
        {
            for (int i = 0; i < _imageSlideshowTransitionQuads.Count; i++)
            {
                GameObject quad = _imageSlideshowTransitionQuads[i];
                if (quad != null && quad.activeSelf)
                    quad.SetActive(false);
            }
        }

        private void DestroyImageSlideshowQuads()
        {
            for (int i = 0; i < _imageSlideshowQuads.Count; i++)
                DestroyImageSlideshowQuad(_imageSlideshowQuads[i]);

            _imageSlideshowQuads.Clear();
            _slideshowUvMesh?.Dispose();
            _slideshowUvMesh = null;
        }

        private void DestroyImageSlideshowTransitionQuads()
        {
            for (int i = 0; i < _imageSlideshowTransitionQuads.Count; i++)
                DestroyImageSlideshowQuad(_imageSlideshowTransitionQuads[i]);

            _imageSlideshowTransitionQuads.Clear();
            _slideshowTransitionUvMesh?.Dispose();
            _slideshowTransitionUvMesh = null;
        }

        private void DestroyImageSlideshowResources()
        {
            ResetImageSlideshowIo();
            DestroyImageSlideshowQuads();
            DestroyImageSlideshowTransitionQuads();

            if (_imageSlideshowMaterial != null)
            {
                Destroy(_imageSlideshowMaterial);
                _imageSlideshowMaterial = null;
            }

            if (_imageSlideshowTransitionMaterial != null)
            {
                Destroy(_imageSlideshowTransitionMaterial);
                _imageSlideshowTransitionMaterial = null;
            }

            if (_imageSlideshowTexture != null)
            {
                Destroy(_imageSlideshowTexture);
                _imageSlideshowTexture = null;
            }

            if (_imageSlideshowTransitionTexture != null)
            {
                Destroy(_imageSlideshowTransitionTexture);
                _imageSlideshowTransitionTexture = null;
            }

            _imageSlideshowTexturePath = string.Empty;
            _imageSlideshowTextureWriteTimeUtc = DateTime.MinValue;
            _imageSlideshowTransitionPath = string.Empty;
            _imageSlideshowTransitionWriteTimeUtc = DateTime.MinValue;
            _imageSlideshowPendingPath = string.Empty;
            _imageSlideshowFiles = Array.Empty<string>();
            _imageSlideshowIndex = -1;
            _imageSlideshowTransitionIndex = -1;
            _imageSlideshowFolderResolved = string.Empty;
            _nextImageSlideshowScanTime = 0f;
            _nextImageSlideshowSlideTime = 0f;
            _imageSlideshowTransitionActive = false;
            _imageSlideshowTransitionStartTime = 0f;
            _imageSlideshowTransitionDuration = 0f;
            _imageSlideshowDisplayStartTime = -1f;
            _imageSlideshowTransitionDisplayStartTime = -1f;
            _imageSlideshowTransitionRuntimeMode = "CrossFade";
            _imageSlideshowLoggedMissingFolder = false;
            _imageSlideshowLoggedNoImages = false;
            _lastImageSlideshowEnabledState = false;
            _lastBuiltImageSlideshowSurface = string.Empty;
            _lastBuiltImageSlideshowMode = string.Empty;
            _lastBuiltImageSlideshowChildCount = -1;
            _lastBuiltImageSlideshowRoot = null;
        }

        private void SaveImageSlideshowSettings(bool rescanNow, bool rebuildQuads, string logMessage)
        {
            if (_settings == null)
                return;

            NormalizeImageSlideshowSettings();
            try
            {
                _settingsRepository.SaveCurrent(_settings);
            }
            catch (Exception ex)
            {
                LogWarn("[slideshow] save settings failed: " + ex.Message);
            }

            if (rebuildQuads)
            {
                _lastBuiltImageSlideshowSurface = string.Empty;
                _lastBuiltImageSlideshowMode = string.Empty;
                _lastBuiltImageSlideshowChildCount = -1;
                DestroyImageSlideshowQuads();
            }

            if (rescanNow)
                ForceImageSlideshowRescan();

            if (!string.IsNullOrWhiteSpace(logMessage))
                LogImageSlideshowVerbose(logMessage);
        }

        private void DrawImageSlideshowSection(float boxX, float boxY, float boxW, float boxH)
        {
            GUI.Box(new Rect(boxX, boxY, boxW, boxH), "IMAGE SLIDESHOW");
            if (_settings == null)
                return;

            EnsureImageSlideshowFolderPathListConsistency();

            float rowH = 20f;
            float contentX = boxX + 8f;
            float contentW = Mathf.Max(40f, boxW - 16f);
            float row1Y = boxY + 18f;
            float row2Y = row1Y + rowH + 2f;
            float row3Y = row2Y + rowH + 2f;
            float row4Y = row3Y + rowH + 2f;
            float row5Y = row4Y + rowH + 2f;
            Vector2 mouseGui = Event.current != null ? Event.current.mousePosition : Vector2.zero;
            Rect folderDropdownButtonRect = Rect.zero;
            Rect folderDropdownListRect = Rect.zero;

            bool currentEnabled = _settings.ImageSlideshowEnabled;
            bool nextEnabled = GUI.Toggle(new Rect(contentX, row1Y, 78f, rowH), currentEnabled, "Enabled");
            if (nextEnabled != currentEnabled)
            {
                _settings.ImageSlideshowEnabled = nextEnabled;
                SaveImageSlideshowSettings(rescanNow: nextEnabled, rebuildQuads: false, logMessage: "[slideshow] enabled=" + nextEnabled);
                if (!nextEnabled)
                    HideAllImageSlideshowQuads();
            }

            float x = contentX + 84f;
            if (GUI.Button(new Rect(x, row1Y, 64f, rowH), "Browse"))
            {
                string currentFolder = NormalizeFolderPathForList(_settings.ImageSlideshowFolderPath);
                if (TryOpenFolderDialog(currentFolder, out string selectedPath, out string error))
                {
                    if (!string.IsNullOrWhiteSpace(selectedPath))
                        AddImageSlideshowFolderPath(selectedPath);
                }
                else
                {
                    LogWarn("[slideshow] folder browse failed: " + error);
                }
            }

            x += 70f;
            folderDropdownButtonRect = new Rect(x, row1Y, 168f, rowH);
            string folderDropdownLabel = "Folder:" + GetImageSlideshowFolderDisplayName(_settings.ImageSlideshowFolderPath);
            if (GUI.Button(folderDropdownButtonRect, folderDropdownLabel))
                _imageSlideshowFolderDropdownOpen = !_imageSlideshowFolderDropdownOpen;

            x += 174f;
            if (GUI.Button(new Rect(x, row1Y, 52f, rowH), "Scan"))
            {
                ForceImageSlideshowRescan();
                SaveImageSlideshowSettings(false, false, "[slideshow] scan requested from bar");
            }

            x += 58f;
            if (GUI.Button(new Rect(x, row1Y, 48f, rowH), "Prev"))
                ShowPreviousImageSlideshow();

            x += 54f;
            if (GUI.Button(new Rect(x, row1Y, 48f, rowH), "Next"))
                ShowNextImageSlideshow();

            x += 54f;
            string playMode = NormalizeImageSlideshowPlayMode(_settings.ImageSlideshowPlayMode);
            if (GUI.Button(new Rect(x, row1Y, 90f, rowH), "Play:" + playMode))
            {
                string nextMode = playMode == "Latest" ? "Queue" : "Latest";
                _settings.ImageSlideshowPlayMode = nextMode;
                _settings.ImageSlideshowLatestOnly = (nextMode == "Latest");
                SaveImageSlideshowSettings(true, false, "[slideshow] playMode=" + nextMode);
            }
            x += 18f;

            x += 72f;
            if (GUI.Button(new Rect(x, row1Y, 78f, rowH), "Mode:" + (NormalizeImageSlideshowTileMode(_settings.ImageSlideshowTileMode) == "tile" ? "Tile" : "Single")))
            {
                _settings.ImageSlideshowTileMode = GetNextImageSlideshowTileMode(_settings.ImageSlideshowTileMode);
                SaveImageSlideshowSettings(false, true, "[slideshow] tile mode=" + _settings.ImageSlideshowTileMode);
            }

            x += 84f;
            if (GUI.Button(new Rect(x, row1Y, 90f, rowH), "Sort:" + NormalizeImageSlideshowSortMode(_settings.ImageSlideshowSortMode)))
            {
                _settings.ImageSlideshowSortMode = GetNextImageSlideshowSortMode(_settings.ImageSlideshowSortMode);
                SaveImageSlideshowSettings(true, false, "[slideshow] sort mode=" + _settings.ImageSlideshowSortMode);
            }

            x += 96f;
            string ascLabel = _settings.ImageSlideshowSortAscending ? "Asc" : "Desc";
            if (GUI.Button(new Rect(x, row1Y, 52f, rowH), ascLabel))
            {
                _settings.ImageSlideshowSortAscending = !_settings.ImageSlideshowSortAscending;
                SaveImageSlideshowSettings(true, false, "[slideshow] sort ascending=" + _settings.ImageSlideshowSortAscending);
            }

            x += 58f;
            if (GUI.Button(new Rect(x, row1Y, 82f, rowH), "Fit:" + NormalizeImageSlideshowFitMode(_settings.ImageSlideshowFitMode)))
            {
                _settings.ImageSlideshowFitMode = GetNextImageSlideshowFitMode(_settings.ImageSlideshowFitMode);
                SaveImageSlideshowSettings(false, false, "[slideshow] fit=" + _settings.ImageSlideshowFitMode);
                if (_imageSlideshowTexture != null)
                    ApplyImageSlideshowTextureFit(_imageSlideshowMaterial, _imageSlideshowTexture.width, _imageSlideshowTexture.height, _imageSlideshowDisplayStartTime);
                if (_imageSlideshowTransitionTexture != null)
                    ApplyImageSlideshowTextureFit(_imageSlideshowTransitionMaterial, _imageSlideshowTransitionTexture.width, _imageSlideshowTransitionTexture.height, _imageSlideshowTransitionDisplayStartTime);
            }

            x += 88f;
            if (GUI.Button(new Rect(x, row1Y, 94f, rowH), "Tr:" + NormalizeImageSlideshowTransitionMode(_settings.ImageSlideshowTransitionMode)))
            {
                _settings.ImageSlideshowTransitionMode = GetNextImageSlideshowTransitionMode(_settings.ImageSlideshowTransitionMode);
                SaveImageSlideshowSettings(false, false, "[slideshow] transition=" + _settings.ImageSlideshowTransitionMode);
            }

            string status = _imageSlideshowFiles.Length > 0
                ? (_settings.ImageSlideshowLatestOnly ? "Latest " : "Images ") + (_imageSlideshowIndex + 1) + "/" + _imageSlideshowFiles.Length
                : "Images 0";
            GUI.Label(new Rect(x + 100f, row1Y, Mathf.Max(80f, contentX + contentW - (x + 100f)), rowH), status);

            if (_imageSlideshowFolderDropdownOpen)
            {
                var folderPaths = _settings.ImageSlideshowFolderPaths ?? new List<string>();
                const float listPad = 3f;
                int visibleCount = Mathf.Clamp(folderPaths.Count, 1, 8);
                float listHeight = visibleCount * rowH + listPad * 2f;
                float listY = Mathf.Max(2f, folderDropdownButtonRect.y - listHeight - 2f);
                folderDropdownListRect = new Rect(
                    folderDropdownButtonRect.x,
                    listY,
                    folderDropdownButtonRect.width,
                    listHeight);
                GUI.Box(folderDropdownListRect, GUIContent.none);

                if (folderPaths.Count == 0)
                {
                    GUI.Label(
                        new Rect(folderDropdownListRect.x + 6f, folderDropdownListRect.y + 3f, folderDropdownListRect.width - 12f, rowH),
                        "(empty)");
                }
                else
                {
                    var viewport = new Rect(
                        folderDropdownListRect.x + 2f,
                        folderDropdownListRect.y + 2f,
                        folderDropdownListRect.width - 4f,
                        folderDropdownListRect.height - 4f);
                    var viewRect = new Rect(
                        0f,
                        0f,
                        Mathf.Max(24f, viewport.width - 18f),
                        folderPaths.Count * rowH);
                    _imageSlideshowFolderDropdownScroll = GUI.BeginScrollView(viewport, _imageSlideshowFolderDropdownScroll, viewRect, false, true);
                    for (int i = 0; i < folderPaths.Count; i++)
                    {
                        string path = folderPaths[i];
                        bool isCurrentFolder = string.Equals(
                            NormalizeFolderPathForList(path),
                            NormalizeFolderPathForList(_settings.ImageSlideshowFolderPath),
                            StringComparison.OrdinalIgnoreCase);
                        string itemLabel = isCurrentFolder
                            ? "> " + GetImageSlideshowFolderDisplayName(path)
                            : GetImageSlideshowFolderDisplayName(path);
                        var rowRect = new Rect(0f, i * rowH, viewRect.width, rowH - 1f);
                        if (!GUI.Button(rowRect, itemLabel))
                            continue;

                        SetCurrentImageSlideshowFolderPath(path);
                        _imageSlideshowFolderDropdownOpen = false;
                    }
                    GUI.EndScrollView();
                }
            }

            if (_imageSlideshowFolderDropdownOpen &&
                Event.current != null &&
                Event.current.type == EventType.MouseDown)
            {
                bool insideFolderUi =
                    folderDropdownButtonRect.Contains(mouseGui) ||
                    folderDropdownListRect.Contains(mouseGui);
                if (!insideFolderUi)
                    _imageSlideshowFolderDropdownOpen = false;
            }

            string folderControl = "ImageSlideshowFolderInput";
            if (!string.Equals(GUI.GetNameOfFocusedControl(), folderControl, StringComparison.Ordinal))
                _imageSlideshowFolderInput = _settings.ImageSlideshowFolderPath ?? string.Empty;
            GUI.Label(new Rect(contentX, row2Y, 44f, rowH), "Folder");
            GUI.SetNextControlName(folderControl);
            string nextFolderInput = GUI.TextField(
                new Rect(contentX + 48f, row2Y, contentW - 48f, rowH),
                _imageSlideshowFolderInput ?? string.Empty);
            if (!string.Equals(nextFolderInput, _imageSlideshowFolderInput, StringComparison.Ordinal))
            {
                _imageSlideshowFolderInput = nextFolderInput;
                _settings.ImageSlideshowFolderPath = NormalizeFolderPathForList(nextFolderInput);
                EnsureImageSlideshowFolderPathListConsistency();
                SaveImageSlideshowSettings(rescanNow: false, rebuildQuads: false, logMessage: null);
            }

            float groupGap = 8f;
            float groupW = (contentW - groupGap * 3f) / 4f;
            string secondsInput = _imageSlideshowSecondsInput;
            DrawImageSlideshowSlider(
                "SlideSec",
                "Slide",
                ref _settings.ImageSlideshowSeconds,
                ref secondsInput,
                0.5f,
                60f,
                contentX,
                row3Y,
                groupW,
                saveRescan: false);
            _imageSlideshowSecondsInput = secondsInput;

            string scanInput = _imageSlideshowScanInput;
            DrawImageSlideshowSlider(
                "ScanSec",
                "Scan",
                ref _settings.ImageSlideshowScanIntervalSec,
                ref scanInput,
                0.25f,
                30f,
                contentX + groupW + groupGap,
                row3Y,
                groupW,
                saveRescan: false);
            _imageSlideshowScanInput = scanInput;

            string opacityInput = _imageSlideshowOpacityInput;
            DrawImageSlideshowSlider(
                "SlideOpacity",
                "Opacity",
                ref _settings.ImageSlideshowOpacity,
                ref opacityInput,
                0.05f,
                1f,
                contentX + (groupW + groupGap) * 2f,
                row3Y,
                groupW,
                saveRescan: false);
            _imageSlideshowOpacityInput = opacityInput;

            string transitionSecondsInput = _imageSlideshowTransitionSecondsInput;
            DrawImageSlideshowSlider(
                "TransSec",
                "Trans",
                ref _settings.ImageSlideshowTransitionSeconds,
                ref transitionSecondsInput,
                0.01f,
                5f,
                contentX + (groupW + groupGap) * 3f,
                row3Y,
                groupW,
                saveRescan: false);
            _imageSlideshowTransitionSecondsInput = transitionSecondsInput;

            GUI.Label(new Rect(contentX, row4Y, 44f, rowH), "Crop");
            x = contentX + 48f;
            if (GUI.Button(new Rect(x, row4Y, 96f, rowH), "V:" + NormalizeImageSlideshowVerticalFocus(_settings.ImageSlideshowVerticalFocus)))
            {
                _settings.ImageSlideshowVerticalFocus = GetNextImageSlideshowVerticalFocus(_settings.ImageSlideshowVerticalFocus);
                SaveImageSlideshowSettings(false, false, "[slideshow] vertical focus=" + _settings.ImageSlideshowVerticalFocus);
                UpdateImageSlideshowTexturePan();
            }

            x += 102f;
            if (GUI.Button(new Rect(x, row4Y, 96f, rowH), "H:" + NormalizeImageSlideshowHorizontalFocus(_settings.ImageSlideshowHorizontalFocus)))
            {
                _settings.ImageSlideshowHorizontalFocus = GetNextImageSlideshowHorizontalFocus(_settings.ImageSlideshowHorizontalFocus);
                SaveImageSlideshowSettings(false, false, "[slideshow] horizontal focus=" + _settings.ImageSlideshowHorizontalFocus);
                UpdateImageSlideshowTexturePan();
            }

            x += 102f;
            if (GUI.Button(new Rect(x, row4Y, 96f, rowH), "Pan:" + GetShortImageSlideshowPanModeLabel(_settings.ImageSlideshowPanMode)))
            {
                _settings.ImageSlideshowPanMode = GetNextImageSlideshowPanMode(_settings.ImageSlideshowPanMode);
                SaveImageSlideshowSettings(false, false, "[slideshow] pan mode=" + _settings.ImageSlideshowPanMode);
                UpdateImageSlideshowTexturePan();
            }

            HashSet<string> selected = ParseLogicalFaceSet(_settings.ImageSlideshowTargetSurface);
            float faceLabelW = 36f;
            GUI.Label(new Rect(contentX, row5Y, faceLabelW, rowH), "Faces");

            float faceX = contentX + faceLabelW;
            float faceW = Mathf.Clamp((contentW - faceLabelW - 108f) / OverlayLogicalFaceNames.Length, 52f, 78f);
            bool facesChanged = false;
            for (int i = 0; i < OverlayLogicalFaceNames.Length; i++)
            {
                string face = OverlayLogicalFaceNames[i];
                bool isOn = selected.Contains(face);
                bool nextOn = GUI.Toggle(new Rect(faceX, row5Y, faceW, rowH), isOn, face);
                if (nextOn != isOn)
                {
                    if (nextOn) selected.Add(face);
                    else selected.Remove(face);
                    facesChanged = true;
                }
                faceX += faceW;
            }

            if (GUI.Button(new Rect(faceX + 4f, row5Y, 46f, rowH), "All"))
            {
                selected.Clear();
                foreach (string face in OverlayLogicalFaceNames)
                    selected.Add(face);
                facesChanged = true;
            }
            if (GUI.Button(new Rect(faceX + 54f, row5Y, 50f, rowH), "None"))
            {
                selected.Clear();
                facesChanged = true;
            }

            if (facesChanged)
            {
                _settings.ImageSlideshowTargetSurface = SerializeLogicalFaceSet(selected);
                SaveImageSlideshowSettings(false, true, "[slideshow] faces=" + _settings.ImageSlideshowTargetSurface);
            }

            if (_imageSlideshowFolderDropdownOpen && Event.current != null)
            {
                bool overFolderUi =
                    folderDropdownButtonRect.Contains(mouseGui) ||
                    folderDropdownListRect.Contains(mouseGui);
                if (overFolderUi &&
                    (Event.current.type == EventType.MouseDown ||
                     Event.current.type == EventType.MouseUp ||
                     Event.current.type == EventType.MouseDrag ||
                     Event.current.type == EventType.ScrollWheel))
                {
                    Event.current.Use();
                }
            }
        }

        private void DrawImageSlideshowSlider(
            string controlName,
            string label,
            ref float value,
            ref string input,
            float min,
            float max,
            float x,
            float y,
            float width,
            bool saveRescan)
        {
            float labelW = 48f;
            float inputW = 48f;
            float sliderX = x + labelW + 2f;
            float sliderW = Mathf.Max(20f, width - labelW - inputW - 8f);
            GUI.Label(new Rect(x, y, labelW, 20f), label);

            if (DrawRoomNumericInput(controlName, new Rect(x + width - inputW, y, inputW, 18f), ref input, value, out float typed))
            {
                float clamped = Mathf.Clamp(typed, min, max);
                if (Mathf.Abs(clamped - value) > 0.0001f)
                {
                    value = clamped;
                    SaveImageSlideshowSettings(saveRescan, false, null);
                    if (string.Equals(label, "Opacity", StringComparison.Ordinal))
                        ApplyImageSlideshowOpacity();
                }
            }

            float next = GUI.HorizontalSlider(new Rect(sliderX, y + 4f, sliderW, 12f), value, min, max);
            if (Mathf.Abs(next - value) <= 0.0001f)
                return;

            value = next;
            input = FormatRoomNumeric(next);
            SaveImageSlideshowSettings(saveRescan, false, null);
            if (string.Equals(label, "Opacity", StringComparison.Ordinal))
                ApplyImageSlideshowOpacity();
        }
    }
}
