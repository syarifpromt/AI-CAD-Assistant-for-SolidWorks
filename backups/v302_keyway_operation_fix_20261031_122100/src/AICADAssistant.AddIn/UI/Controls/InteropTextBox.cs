using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;

namespace AICADAssistant.AddIn.UI.Controls
{
    /// <summary>
    /// Custom TextBox designed specifically for hosting within an ElementHost inside
    /// unmanaged Win32/MFC host applications (SOLIDWORKS Taskpane).
    /// Hooks WM_GETDLGCODE to explicitly declare DLGC_WANTALLKEYS | DLGC_WANTCHARS | DLGC_WANTARROWS | DLGC_WANTTAB,
    /// preventing SOLIDWORKS MFC message loop from intercepting character keystrokes as global shortcuts.
    /// </summary>
    [ComVisible(true)]
    [ClassInterface(ClassInterfaceType.AutoDispatch)]
    public class InteropTextBox : TextBox
    {
        private const int WM_GETDLGCODE = 0x0087;
        private const uint DLGC_WANTALLKEYS = 0x0004;
        private const uint DLGC_WANTARROWS = 0x0001;
        private const uint DLGC_WANTCHARS = 0x0080;
        private const uint DLGC_WANTTAB = 0x0002;

        public InteropTextBox()
        {
            Loaded += OnLoaded;
            Unloaded += OnUnloaded;
        }

        private void OnLoaded(object sender, RoutedEventArgs e)
        {
            var source = PresentationSource.FromVisual(this) as HwndSource;
            if (source != null)
            {
                source.RemoveHook(HwndHook);
                source.AddHook(HwndHook);
            }
        }

        private void OnUnloaded(object sender, RoutedEventArgs e)
        {
            var source = PresentationSource.FromVisual(this) as HwndSource;
            if (source != null)
            {
                source.RemoveHook(HwndHook);
            }
        }

        private IntPtr HwndHook(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            if (msg == WM_GETDLGCODE)
            {
                handled = true;
                return new IntPtr(DLGC_WANTALLKEYS | DLGC_WANTCHARS | DLGC_WANTARROWS | DLGC_WANTTAB);
            }
            return IntPtr.Zero;
        }
    }
}

