using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;

namespace MainGameVoiceBreastMorph
{
    // Raw COM vtable calls avoid Mono's System.__ComObject activation failure.
    // The caller uses an STA thread; the dialog itself is the standard Windows Explorer picker.
    internal static class NativePngFileDialog
    {
        private static readonly Guid DialogClsid = new Guid("DC1C5A9C-E88A-4DDE-A5A1-60F82A20AEF7");
        private static readonly Guid DialogIid = new Guid("D57C7288-D4AD-4768-BE02-9D969532D960");
        private static readonly Guid ShellItemIid = new Guid("43826D1E-E718-42EE-BC55-A1E261C37BFE");
        private const uint FileSystemPath = 0x80058000;
        private const uint Options = 0x40 | 0x1000 | 0x800; // filesystem, file exists, path exists
        private const int Cancelled = unchecked((int)0x800704C7);

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct FilterSpec
        {
            [MarshalAs(UnmanagedType.LPWStr)] internal string Name;
            [MarshalAs(UnmanagedType.LPWStr)] internal string Spec;
        }

        [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate uint ReleaseDelegate(IntPtr self);
        [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate int ShowDelegate(IntPtr self, IntPtr owner);
        [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate int SetFileTypesDelegate(IntPtr self, uint count, ref FilterSpec filter);
        [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate int SetOptionsDelegate(IntPtr self, uint options);
        [UnmanagedFunctionPointer(CallingConvention.StdCall, CharSet = CharSet.Unicode)]
        private delegate int SetStringDelegate(IntPtr self, [MarshalAs(UnmanagedType.LPWStr)] string value);
        [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate int SetFolderDelegate(IntPtr self, IntPtr folder);
        [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate int GetResultDelegate(IntPtr self, out IntPtr item);
        [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate int GetDisplayNameDelegate(IntPtr self, uint kind, out IntPtr name);

        [DllImport("ole32.dll", PreserveSig = true)]
        private static extern int CoInitializeEx(IntPtr reserved, uint concurrencyModel);
        [DllImport("ole32.dll")]
        private static extern void CoUninitialize();
        [DllImport("ole32.dll", PreserveSig = true)]
        private static extern int CoCreateInstance(ref Guid clsid, IntPtr outer, uint context, ref Guid iid, out IntPtr instance);
        [DllImport("shell32.dll", CharSet = CharSet.Unicode, PreserveSig = true)]
        private static extern int SHCreateItemFromParsingName(string path, IntPtr bindingContext, ref Guid iid, out IntPtr item);

        private static T Method<T>(IntPtr instance, int slot) where T : class
        {
            IntPtr vtable = Marshal.ReadIntPtr(instance);
            IntPtr method = Marshal.ReadIntPtr(vtable, slot * IntPtr.Size);
            return Marshal.GetDelegateForFunctionPointer(method, typeof(T)) as T;
        }

        private static void Check(int result)
        {
            if (result < 0) Marshal.ThrowExceptionForHR(result);
        }

        internal static string Show(string current)
        {
            int init = CoInitializeEx(IntPtr.Zero, 2); // COINIT_APARTMENTTHREADED
            Check(init);
            IntPtr dialog = IntPtr.Zero, folder = IntPtr.Zero, selected = IntPtr.Zero, pathPtr = IntPtr.Zero;
            try
            {
                Guid clsid = DialogClsid, iid = DialogIid;
                Check(CoCreateInstance(ref clsid, IntPtr.Zero, 1, ref iid, out dialog));
                var filter = new FilterSpec { Name = "PNG 画像 (*.png)", Spec = "*.png" };
                Check(Method<SetFileTypesDelegate>(dialog, 4)(dialog, 1, ref filter));
                Check(Method<SetOptionsDelegate>(dialog, 9)(dialog, Options));
                Check(Method<SetStringDelegate>(dialog, 17)(dialog, "スキンオーバーレイ PNG を選択"));
                Check(Method<SetStringDelegate>(dialog, 22)(dialog, "png"));

                if (File.Exists(current))
                {
                    string directory = Path.GetDirectoryName(current);
                    Guid shellIid = ShellItemIid;
                    if (!string.IsNullOrEmpty(directory)
                        && SHCreateItemFromParsingName(directory, IntPtr.Zero, ref shellIid, out folder) == 0)
                    {
                        Method<SetFolderDelegate>(dialog, 12)(dialog, folder);
                        Method<SetStringDelegate>(dialog, 15)(dialog, Path.GetFileName(current));
                    }
                }

                IntPtr owner = Process.GetCurrentProcess().MainWindowHandle;
                int shown = Method<ShowDelegate>(dialog, 3)(dialog, owner);
                if (shown == Cancelled) return null;
                Check(shown);
                Check(Method<GetResultDelegate>(dialog, 20)(dialog, out selected));
                Check(Method<GetDisplayNameDelegate>(selected, 5)(selected, FileSystemPath, out pathPtr));
                return Marshal.PtrToStringUni(pathPtr);
            }
            finally
            {
                if (pathPtr != IntPtr.Zero) Marshal.FreeCoTaskMem(pathPtr);
                if (selected != IntPtr.Zero) Method<ReleaseDelegate>(selected, 2)(selected);
                if (folder != IntPtr.Zero) Method<ReleaseDelegate>(folder, 2)(folder);
                if (dialog != IntPtr.Zero) Method<ReleaseDelegate>(dialog, 2)(dialog);
                CoUninitialize();
            }
        }
    }
}
