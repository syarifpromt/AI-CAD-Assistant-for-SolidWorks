using System;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace AICADAssistant.AddIn.UI.Controls
{
    /// <summary>
    /// Native Windows Forms EDIT control wrapper designed for hosting within WPF
    /// via WindowsFormsHost inside the unmanaged SOLIDWORKS Taskpane.
    /// 
    /// Why this solves keyboard input in SOLIDWORKS:
    /// SOLIDWORKS' main MFC message pump inspects the focused window class (::GetFocus()).
    /// If the window class is an EDIT control, MFC skips TranslateAccelerator and does not steal
    /// hotkeys ('S'=Shortcut Bar, 'F'=Zoom to Fit, 'L'=Line Tool). Because WPF controls do not
    /// have separate Win32 HWNDs, pure WPF TextBoxes lose keys to SOLIDWORKS accelerators.
    /// This native WinForms TextBox has its own true Win32 HWND and returns DLGC_WANTALLKEYS,
    /// enabling 100% reliable native typing, Enter, Shift+Enter, and copy/paste inside SOLIDWORKS.
    /// </summary>
    [ComVisible(true)]
    [ClassInterface(ClassInterfaceType.AutoDispatch)]
    public class WinFormsChatTextBox : TextBox
    {
        private const int WM_GETDLGCODE = 0x0087;
        private const uint DLGC_WANTALLKEYS = 0x0004;
        private const uint DLGC_WANTARROWS = 0x0001;
        private const uint DLGC_WANTCHARS = 0x0080;
        private const uint DLGC_WANTTAB = 0x0002;

        private const int WM_KEYDOWN = 0x0100;
        private const int VK_RETURN = 0x0D;

        private DateTime _lastSubmitTime = DateTime.MinValue;

        public event EventHandler SubmitRequested;

        public WinFormsChatTextBox()
        {
            Multiline = true;
            WordWrap = true;
            ScrollBars = ScrollBars.Vertical;
            BorderStyle = BorderStyle.None;
            BackColor = Color.White;
            ForeColor = Color.FromArgb(33, 33, 33);
            Font = new Font("Segoe UI", 10F, FontStyle.Regular, GraphicsUnit.Point);
            Dock = DockStyle.Fill;
            Margin = new Padding(0);
        }

        private void TriggerSubmit()
        {
            // Debounce to prevent duplicate submission within 250ms
            if ((DateTime.Now - _lastSubmitTime).TotalMilliseconds < 250)
            {
                return;
            }
            _lastSubmitTime = DateTime.Now;

            var handler = SubmitRequested;
            if (handler != null)
            {
                handler(this, EventArgs.Empty);
            }
        }

        protected override bool IsInputKey(Keys keyData)
        {
            Keys keyCode = keyData & Keys.KeyCode;
            if (keyCode == Keys.Return || keyCode == Keys.Tab || keyCode == Keys.Back ||
                keyCode == Keys.Left || keyCode == Keys.Right || keyCode == Keys.Up || keyCode == Keys.Down)
            {
                return true;
            }
            return base.IsInputKey(keyData);
        }

        protected override bool IsInputChar(char charCode)
        {
            return true;
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Enter)
            {
                if (e.Shift)
                {
                    // Shift+Enter: allow base to insert newline
                    base.OnKeyDown(e);
                    return;
                }

                // Enter or Ctrl+Enter without Shift: Submit!
                e.Handled = true;
                e.SuppressKeyPress = true; // Prevents "ding" and suppresses newline character
                TriggerSubmit();
                return;
            }

            base.OnKeyDown(e);
        }

        protected override void OnKeyPress(KeyPressEventArgs e)
        {
            // Suppress character insertion for Enter without Shift
            if (e.KeyChar == (char)Keys.Return && (ModifierKeys & Keys.Shift) != Keys.Shift)
            {
                e.Handled = true;
                return;
            }

            base.OnKeyPress(e);
        }

        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            // Enter without Shift -> Submit chat
            if (keyData == Keys.Enter)
            {
                TriggerSubmit();
                return true; // Mark as handled
            }

            // Shift+Enter -> Insert newline
            if (keyData == (Keys.Shift | Keys.Enter))
            {
                SelectedText = Environment.NewLine;
                return true;
            }

            // Ctrl+Enter -> Submit chat
            if (keyData == (Keys.Control | Keys.Enter))
            {
                TriggerSubmit();
                return true;
            }

            return base.ProcessCmdKey(ref msg, keyData);
        }

        protected override void WndProc(ref Message m)
        {
            if (m.Msg == WM_GETDLGCODE)
            {
                m.Result = new IntPtr(DLGC_WANTALLKEYS | DLGC_WANTCHARS | DLGC_WANTARROWS | DLGC_WANTTAB);
                return;
            }

            if (m.Msg == WM_KEYDOWN && m.WParam.ToInt32() == VK_RETURN)
            {
                if ((ModifierKeys & Keys.Shift) != Keys.Shift)
                {
                    TriggerSubmit();
                    m.Result = IntPtr.Zero;
                    return;
                }
            }

            base.WndProc(ref m);
        }
    }
}
