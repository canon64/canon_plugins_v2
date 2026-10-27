using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;

namespace MainGameVMDPlay
{
    public sealed partial class Plugin
    {
        private volatile bool _pickerDone;
        private bool _pickerBusy;
        private string _pickerResult;
        private string _pickerError;
        private bool _pickerAudio;
        private bool _pickerActor;
        private bool _pickerCoordinate;
        private ChaControl _pickerCoordinateTarget;
        private int _pickerTargetGeneration;
        private void BeginPickActorCard() { if (_pickerBusy) return; BeginPickFile(false, true); }
        private string _selectedVmdForPicker;
        private HSceneProc _pickerScene;

        private void BeginPickVmdFile() => BeginPickFile(false);
        private void BeginPickAudioFile() => BeginPickFile(true);

        private void BeginPickFile(bool audio, bool actor = false, bool coordinate = false)
        {
            if (AstralBlocked) return;
            if (_pickerBusy) return;
            string vmdPath = _selectedVmdForPicker ?? _settings.LastVmdAbsPath;
            string fallback = audio ? GetInitialPickerDir(_settings.LastAudioAbsPath, EffectiveVmdDir) : EffectiveVmdDir;
            string folder = GetInitialPickerDir(vmdPath, fallback);
            if (actor) folder = _settings.LastActorCardDirectory;
            if (coordinate) folder = Path.Combine(BepInEx.Paths.GameRootPath, "UserData", "coordinate");
            _pickerCoordinate = coordinate;
            _pickerCoordinateTarget = coordinate ? _trialChara : null;
            _pickerActor = actor; _pickerTargetGeneration = _target.Generation;
            IntPtr owner = System.Diagnostics.Process.GetCurrentProcess().MainWindowHandle;
            _pickerBusy = true;
            _pickerAudio = audio;
            _pickerCamera = false;
            _pickerScene = _hSceneProc;
            _pickerDone = false;
            _pickerResult = null;
            _pickerError = null;
            var thread = new Thread(() => {
                try { _pickerResult = ShowShellVmdPicker(folder, owner, audio, actor, coordinate); }
                catch (Exception ex) { _pickerError = ex.ToString(); }
                finally { _pickerDone = true; }
            });
            thread.IsBackground = true;
            thread.SetApartmentState(ApartmentState.STA);
            thread.Start();
            LogInfo("[Picker] Windows Shell dialog started");
        }

        private void TickFilePicker()
        {
            if (!_pickerBusy || !_pickerDone) return;
            _pickerBusy = false;
            _pickerDone = false;
            if (_pickerError != null) { LogError("[Picker] " + _pickerError); return; }
            if (string.IsNullOrEmpty(_pickerResult)) { LogInfo("[Picker] cancelled"); return; }
            LogInfo("[Picker] selected=" + _pickerResult);
            if (_insideHScene && _cfgEnabled.Value && _pickerScene != null && _pickerScene == _hSceneProc && _pickerTargetGeneration == _target.Generation)
            {
                if (_pickerCoordinate) { if (ReferenceEquals(_pickerCoordinateTarget, _trialChara)) ApplyActorCoordinate(_pickerResult); }
                else if (_pickerActor) RequestActorCard(_pickerResult);
                else if (_pickerCamera) LoadCameraMotion(_pickerResult);
                else if (_pickerAudio) PlaybackLoadAudio(_pickerResult, _state == PluginState.Playing);
                else PlaybackLoadAndPlay(_pickerResult, _settings.AutoPlayOnLoad);
            }
            else LogWarn("[Picker] scene unavailable; selection not loaded");
        }

        private static string ShowShellVmdPicker(string folder, IntPtr owner, bool audio, bool actor, bool coordinate)
        {
            IFileOpenDialog dialog = null;
            IShellItem initial = null;
            IShellItem result = null;
            IntPtr path = IntPtr.Zero;
            try {
                dialog = (IFileOpenDialog)new FileOpenDialogComObject();
                dialog.GetOptions(out uint options);
                dialog.SetOptions(options | 0x40u | 0x800u | 0x1000u | 0x8u);
                var filters = new[] { new FileDialogFilterSpec(coordinate ? "衣装カード (*.png)" : actor ? "キャラカード (*.png)" : audio ? "Media (Music / Video)" : "VMD (*.vmd)", actor || coordinate ? "*.png" : audio ? "*.wav;*.ogg;*.mp3;*.mp4;*.webm;*.mov;*.avi;*.m4v;*.wmv" : "*.vmd"), new FileDialogFilterSpec("All files", "*.*") };
                dialog.SetFileTypes((uint)filters.Length, filters);
                dialog.SetFileTypeIndex(1);
                dialog.SetTitle(coordinate ? "衣装カードを選択" : actor ? "キャラカードを選択" : audio ? "音源ファイルを選択" : "VMDファイルを選択");
                if (Directory.Exists(folder)) {
                    var iid = typeof(IShellItem).GUID;
                    SHCreateItemFromParsingName(folder, IntPtr.Zero, ref iid, out initial);
                    dialog.SetFolder(initial);
                }
                int hr = dialog.Show(owner);
                if (hr == HResultCanceled) return null;
                Marshal.ThrowExceptionForHR(hr);
                dialog.GetResult(out result);
                result.GetDisplayName(ShellItemDisplayName.FileSystemPath, out path);
                return Marshal.PtrToStringUni(path);
            }
            finally {
                if (path != IntPtr.Zero) Marshal.FreeCoTaskMem(path);
                if (result != null) Marshal.ReleaseComObject(result);
                if (initial != null) Marshal.ReleaseComObject(initial);
                if (dialog != null) Marshal.ReleaseComObject(dialog);
            }
        }

        private const int HResultCanceled = unchecked((int)0x800704C7);

        [Flags]
        private enum FileDialogOptions : uint
        {
            NoChangeDir = 0x00000008,
            PickFolders = 0x00000020,
            ForceFileSystem = 0x00000040,
            PathMustExist = 0x00000800,
            FileMustExist = 0x00001000,
            ForcePreviewPaneOn = 0x40000000
        }

        private enum ShellItemDisplayName : uint
        {
            FileSystemPath = 0x80058000
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct FileDialogFilterSpec
        {
            [MarshalAs(UnmanagedType.LPWStr)]
            public string Name;

            [MarshalAs(UnmanagedType.LPWStr)]
            public string Spec;

            public FileDialogFilterSpec(string name, string spec)
            {
                Name = name;
                Spec = spec;
            }
        }

        [ComImport]
        [Guid("DC1C5A9C-E88A-4DDE-A5A1-60F82A20AEF7")]
        private class FileOpenDialogComObject
        {
        }

        [ComImport]
        [Guid("42F85136-DB7E-439C-85F1-E4075D135FC8")]
        [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IFileOpenDialog
        {
            [PreserveSig]
            int Show(IntPtr parent);
            void SetFileTypes(uint fileTypeCount, [MarshalAs(UnmanagedType.LPArray)] FileDialogFilterSpec[] filterSpec);
            void SetFileTypeIndex(uint fileTypeIndex);
            void GetFileTypeIndex(out uint fileTypeIndex);
            void Advise(IntPtr events, out uint cookie);
            void Unadvise(uint cookie);
            void SetOptions(uint options);
            void GetOptions(out uint options);
            void SetDefaultFolder(IShellItem shellItem);
            void SetFolder(IShellItem shellItem);
            void GetFolder(out IShellItem shellItem);
            void GetCurrentSelection(out IShellItem shellItem);
            void SetFileName([MarshalAs(UnmanagedType.LPWStr)] string name);
            void GetFileName([MarshalAs(UnmanagedType.LPWStr)] out string name);
            void SetTitle([MarshalAs(UnmanagedType.LPWStr)] string title);
            void SetOkButtonLabel([MarshalAs(UnmanagedType.LPWStr)] string text);
            void SetFileNameLabel([MarshalAs(UnmanagedType.LPWStr)] string label);
            void GetResult(out IShellItem shellItem);
            void AddPlace(IShellItem shellItem, int alignment);
            void SetDefaultExtension([MarshalAs(UnmanagedType.LPWStr)] string defaultExtension);
            void Close(int hr);
            void SetClientGuid(ref Guid guid);
            void ClearClientData();
            void SetFilter(IntPtr filter);
            void GetResults(out IntPtr items);
            void GetSelectedItems(out IntPtr items);
        }

        [ComImport]
        [Guid("43826D1E-E718-42EE-BC55-A1E261C37BFE")]
        [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IShellItem
        {
            void BindToHandler(IntPtr pbc, ref Guid bhid, ref Guid riid, out IntPtr ppv);
            void GetParent(out IShellItem ppsi);
            void GetDisplayName(ShellItemDisplayName sigdnName, out IntPtr ppszName);
            void GetAttributes(uint sfgaoMask, out uint psfgaoAttribs);
            void Compare(IShellItem psi, uint hint, out int piOrder);
        }

        [DllImport("shell32.dll", CharSet = CharSet.Unicode, PreserveSig = false)]
        private static extern void SHCreateItemFromParsingName(
            [MarshalAs(UnmanagedType.LPWStr)] string path,
            IntPtr pbc,
            ref Guid riid,
            [MarshalAs(UnmanagedType.Interface)] out IShellItem shellItem);

        private static string GetInitialPickerDir(string lastPath, string fallback)
        {
            try
            {
                if (!string.IsNullOrEmpty(lastPath))
                {
                    string dir = Path.GetDirectoryName(lastPath);
                    if (!string.IsNullOrEmpty(dir) && Directory.Exists(dir)) return dir;
                }
            }
            catch { }
            return fallback;
        }
    }
}
