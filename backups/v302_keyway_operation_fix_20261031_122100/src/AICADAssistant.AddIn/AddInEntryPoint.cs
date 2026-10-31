using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using Microsoft.Win32;
using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;
using SolidWorks.Interop.swpublished;
using AICADAssistant.AddIn.UI;
using System.Windows.Forms.Integration;

namespace AICADAssistant.AddIn
{
    [ComVisible(true)]
    [Guid("a1b2c3d4-e5f6-7890-abcd-ef1234567890")]
    [ClassInterface(ClassInterfaceType.AutoDispatch)]
    public class SwAddIn : ISwAddin
    {
        private const string ADDIN_GUID = "a1b2c3d4-e5f6-7890-abcd-ef1234567890";
        private const string ADDIN_TITLE = "AI CAD Assistant";
        private const string ADDIN_DESCRIPTION = "AI CAD Assistant untuk SOLIDWORKS menggunakan Gemini AI";
        private const int MIN_SW_YEAR = 2023;

        private ISldWorks _swApp;
        private ITaskpaneView _taskpaneView;
        private int _addinCookie;
        private MainTaskPaneControl _wpfControl;

        #region COM Registration

        [ComRegisterFunction]
        public static void RegisterFunction(Type t)
        {
            try
            {
                string guidStr = t.GUID.ToString("B").ToUpper();

                // HKLM: SOLIDWORKS requires Title and Description in HKLM\SOFTWARE\SolidWorks\AddIns\{GUID}
                try
                {
                    using (RegistryKey hklmKey = Registry.LocalMachine.CreateSubKey(@"SOFTWARE\SolidWorks\AddIns\" + guidStr))
                    {
                        if (hklmKey != null)
                        {
                            hklmKey.SetValue(null, 1, RegistryValueKind.DWord);
                            hklmKey.SetValue("Title", ADDIN_TITLE, RegistryValueKind.String);
                            hklmKey.SetValue("Description", ADDIN_DESCRIPTION, RegistryValueKind.String);
                        }
                    }
                }
                catch
                {
                    // HKLM requires elevated admin rights
                }

                // HKCU: Startup key for the current user
                using (RegistryKey startupKey = Registry.CurrentUser.CreateSubKey(@"SOFTWARE\SolidWorks\AddInsStartup\" + guidStr))
                {
                    if (startupKey != null)
                        startupKey.SetValue(null, 1, RegistryValueKind.DWord);
                }

                // Clean up legacy HKCU AddIns key if it exists from older scripts
                try
                {
                    Registry.CurrentUser.DeleteSubKey(@"SOFTWARE\SolidWorks\AddIns\" + guidStr, false);
                }
                catch { }

                LogMessage("RegisterFunction: Add-in berhasil didaftarkan.");
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    "Gagal mendaftarkan add-in: " + ex.Message,
                    "AI CAD Assistant - Error Registrasi",
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        [ComUnregisterFunction]
        public static void UnregisterFunction(Type t)
        {
            try
            {
                string guidStr = t.GUID.ToString("B").ToUpper();

                try
                {
                    Registry.LocalMachine.DeleteSubKey(@"SOFTWARE\SolidWorks\AddIns\" + guidStr, false);
                }
                catch { }

                try
                {
                    Registry.CurrentUser.DeleteSubKey(@"SOFTWARE\SolidWorks\AddIns\" + guidStr, false);
                }
                catch { }

                try
                {
                    Registry.CurrentUser.DeleteSubKey(@"SOFTWARE\SolidWorks\AddInsStartup\" + guidStr, false);
                }
                catch { }

                LogMessage("UnregisterFunction: Add-in berhasil dihapus.");
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    "Gagal menghapus registrasi add-in: " + ex.Message,
                    "AI CAD Assistant - Error",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }

        #endregion

        #region ISwAddin Implementation

        public bool ConnectToSW(object ThisSW, int cookie)
        {
            try
            {
                var curAsm = System.Reflection.Assembly.GetExecutingAssembly();
                string asmPath = curAsm.Location;
                DateTime asmWriteTime = File.Exists(asmPath) ? File.GetLastWriteTime(asmPath) : DateTime.MinValue;
                LogMessage(string.Format("[Stage 1/6] ConnectToSW dimulai. AI CAD Assistant v{0} | Assembly: '{1}' (Last Modified: {2:yyyy-MM-dd HH:mm:ss}) | Cookie={3}",
                    curAsm.GetName().Version, asmPath, asmWriteTime, cookie));

                if (ThisSW == null)
                {
                    LogMessage("[Stage 1/6] ERROR: Parameter ThisSW bernilai null!");
                    return false;
                }
                LogMessage(string.Format("[Stage 1/6] Tipe objek ThisSW runtime: {0}", ThisSW.GetType().FullName));

                _swApp = (ISldWorks)ThisSW;
                _addinCookie = cookie;
                LogMessage("[Stage 2/6] Referensi ISldWorks berhasil diperoleh.");

                // Check SOLIDWORKS version
                if (!CheckSolidWorksVersion())
                {
                    MessageBox.Show(
                        string.Format("AI CAD Assistant membutuhkan SOLIDWORKS {0} atau lebih baru.\nAdd-in tidak akan dimuat.", MIN_SW_YEAR),
                        "Versi Tidak Didukung",
                        MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return false;
                }

                // Setup add-in registration callback
                LogMessage("[Stage 3/6] Mengatur callback addin via SetAddinCallbackInfo2...");
                try
                {
                    bool cbResult = _swApp.SetAddinCallbackInfo2(0L, this, cookie);
                    LogMessage(string.Format("[Stage 3/6] SetAddinCallbackInfo2 berhasil. Return value: {0}", cbResult));
                }
                catch (Exception cbEx)
                {
                    LogMessage(string.Format("[Stage 3/6] SetAddinCallbackInfo2 Exception: {0}\nStack trace: {1}", cbEx.ToString(), cbEx.StackTrace));
                    try
                    {
                        LogMessage("[Stage 3/6] Mencoba fallback SetAddinCallbackInfo legacy...");
                        bool cbLegacy = _swApp.SetAddinCallbackInfo(0, this, cookie);
                        LogMessage(string.Format("[Stage 3/6] Fallback SetAddinCallbackInfo return value: {0}", cbLegacy));
                    }
                    catch (Exception legEx)
                    {
                        LogMessage(string.Format("[Stage 3/6] Fallback SetAddinCallbackInfo gagal: {0}", legEx.ToString()));
                        throw;
                    }
                }

                // Add the task pane
                AddTaskPane();

                // Register SOLIDWORKS event handlers
                RegisterSwEvents();

                LogMessage("[Stage Complete] ConnectToSW: AI CAD Assistant berhasil terhubung ke SOLIDWORKS.");
                return true;
            }
            catch (Exception ex)
            {
                string errorDetails = string.Format("Tipe: {0}\nPesan: {1}\nStack Trace:\n{2}",
                    ex.GetType().FullName, ex.Message, ex.StackTrace);
                if (ex.InnerException != null)
                {
                    errorDetails += string.Format("\nInner Exception: {0} - {1}",
                        ex.InnerException.GetType().FullName, ex.InnerException.Message);
                }

                LogMessage("ConnectToSW FATAL ERROR:\n" + errorDetails);

                string logPath = Path.Combine(
                    System.Environment.GetFolderPath(System.Environment.SpecialFolder.ApplicationData),
                    "AICADAssistant", "addin.log");

                MessageBox.Show(
                    string.Format("Gagal menghubungkan AI CAD Assistant:\n{0}\n\nDetail diagnostik dicatat di:\n{1}",
                        ex.Message, logPath),
                    "AI CAD Assistant - Error",
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
                return false;
            }
        }

        public bool DisconnectFromSW()
        {
            try
            {
                UnregisterSwEvents();
                RemoveTaskPane();

                // Release COM references
                if (_swApp != null)
                {
                    Marshal.ReleaseComObject(_swApp);
                    _swApp = null;
                }

                LogMessage("DisconnectFromSW: AI CAD Assistant berhasil terputus dari SOLIDWORKS.");
                return true;
            }
            catch (Exception ex)
            {
                LogMessage("DisconnectFromSW ERROR: " + ex.Message);
                return true; // Always return true to avoid blocking SOLIDWORKS shutdown
            }
        }

        #endregion

        #region Task Pane

        private void AddTaskPane()
        {
            try
            {
                LogMessage("[Stage 4/6] Menyiapkan Task Pane...");
                string iconPath = GetIconPath();
                LogMessage(string.Format("[Stage 4/6] Icon path: '{0}' (Tersedia: {1})", iconPath, File.Exists(iconPath)));

                // Create a custom Windows Forms host and SafeElementHost to bridge WPF <-> WinForms
                // and preserve keyboard input against SOLIDWORKS accelerator key interception.
                TaskPaneWinFormsHost hostForm = new TaskPaneWinFormsHost();
                hostForm.Dock = DockStyle.Fill;
                hostForm.BackColor = System.Drawing.Color.White;

                // Create SafeElementHost to bridge WPF <-> WinForms
                SafeElementHost elementHost = new SafeElementHost();
                elementHost.Dock = DockStyle.Fill;
                elementHost.BackColor = System.Drawing.Color.White;

                // Create the WPF UserControl
                LogMessage("[Stage 5/6] Menginstansiasi WPF MainTaskPaneControl...");
                _wpfControl = new MainTaskPaneControl(_swApp);
                elementHost.Child = _wpfControl;

                hostForm.Controls.Add(elementHost);
                hostForm.CreateControl();
                IntPtr handle = hostForm.Handle;
                LogMessage(string.Format("[Stage 5/6] hostForm window handle dibuat: {0} (0x{0:X})", handle.ToInt64()));

                // Create SOLIDWORKS task pane
                LogMessage("[Stage 6/6] Memanggil CreateTaskpaneView2...");
                _taskpaneView = _swApp.CreateTaskpaneView2(iconPath, ADDIN_TITLE);

                if (_taskpaneView != null)
                {
                    LogMessage(string.Format("[Stage 6/6] Menghubungkan window handle via DisplayWindowFromHandlex64 ({0})...", handle.ToInt64()));
                    bool displayOk = _taskpaneView.DisplayWindowFromHandlex64(handle.ToInt64());
                    LogMessage(string.Format("[Stage 6/6] DisplayWindowFromHandlex64 return value: {0}", displayOk));
                    LogMessage("AddTaskPane: Task Pane berhasil dibuat dan dihubungkan.");
                }
                else
                {
                    LogMessage("AddTaskPane ERROR: CreateTaskpaneView2 mengembalikan null.");
                }
            }
            catch (Exception ex)
            {
                LogMessage(string.Format("AddTaskPane ERROR: {0}\nStack trace: {1}", ex.ToString(), ex.StackTrace));
                throw;
            }
        }

        private void RemoveTaskPane()
        {
            try
            {
                if (_taskpaneView != null)
                {
                    _taskpaneView.DeleteView();
                    Marshal.ReleaseComObject(_taskpaneView);
                    _taskpaneView = null;
                }
            }
            catch (Exception ex)
            {
                LogMessage("RemoveTaskPane ERROR: " + ex.Message);
            }
        }

        private string GetIconPath()
        {
            try
            {
                string addinDir = Path.GetDirectoryName(System.Reflection.Assembly.GetExecutingAssembly().Location);
                string iconPath = Path.Combine(addinDir, "Resources", "addin_icon.bmp");
                return File.Exists(iconPath) ? iconPath : string.Empty;
            }
            catch
            {
                return string.Empty;
            }
        }

        #endregion

        #region SOLIDWORKS Version Check

        private bool CheckSolidWorksVersion()
        {
            try
            {
                // RevisionNumber() returns e.g. "33.1.0" for SW2025 (version 33 = 2025)
                string revision = _swApp.RevisionNumber();
                if (string.IsNullOrEmpty(revision))
                    return true;

                string[] parts = revision.Split('.');
                int majorVer;
                if (parts.Length > 0 && int.TryParse(parts[0], out majorVer))
                {
                    int swYear = majorVer - 31 + 2023;
                    LogMessage(string.Format("CheckSolidWorksVersion: Revision={0}, Estimated Year={1}", revision, swYear));
                    return swYear >= MIN_SW_YEAR;
                }
                return true;
            }
            catch
            {
                return true;
            }
        }

        #endregion

        #region SOLIDWORKS Events

        private void RegisterSwEvents()
        {
            try
            {
                SldWorks swAppConcrete = _swApp as SldWorks;
                if (swAppConcrete != null)
                {
                    swAppConcrete.ActiveDocChangeNotify += OnActiveDocChanged;
                    LogMessage("RegisterSwEvents: Event handlers terdaftar.");
                }
            }
            catch (Exception ex)
            {
                LogMessage("RegisterSwEvents ERROR: " + ex.Message);
            }
        }

        private void UnregisterSwEvents()
        {
            try
            {
                SldWorks swAppConcrete = _swApp as SldWorks;
                if (swAppConcrete != null)
                {
                    swAppConcrete.ActiveDocChangeNotify -= OnActiveDocChanged;
                }
            }
            catch (Exception ex)
            {
                LogMessage("UnregisterSwEvents ERROR: " + ex.Message);
            }
        }

        private int OnActiveDocChanged()
        {
            try
            {
                if (_wpfControl != null)
                {
                    _wpfControl.OnActiveDocumentChanged();
                }
            }
            catch (Exception ex)
            {
                LogMessage("OnActiveDocChanged ERROR: " + ex.Message);
            }
            return 0;
        }

        #endregion

        #region Logging

        private static void LogMessage(string message)
        {
            try
            {
                string entry = string.Format("[{0:yyyy-MM-dd HH:mm:ss}] {1}{2}", DateTime.Now, message, System.Environment.NewLine);

                // 1. Roaming AppData log: %APPDATA%\AICADAssistant\addin.log
                string appDataDir = Path.Combine(System.Environment.GetFolderPath(System.Environment.SpecialFolder.ApplicationData), "AICADAssistant");
                if (!Directory.Exists(appDataDir)) Directory.CreateDirectory(appDataDir);
                File.AppendAllText(Path.Combine(appDataDir, "addin.log"), entry);

                // 2. Local AppData log: %LOCALAPPDATA%\AICADAssistant\logs\addin.log
                string localDataDir = Path.Combine(System.Environment.GetFolderPath(System.Environment.SpecialFolder.LocalApplicationData), "AICADAssistant", "logs");
                if (!Directory.Exists(localDataDir)) Directory.CreateDirectory(localDataDir);
                File.AppendAllText(Path.Combine(localDataDir, "addin.log"), entry);
            }
            catch
            {
            }
        }

        #endregion
    }

    /// <summary>
    /// Custom ElementHost that prevents SOLIDWORKS accelerator table from intercepting
    /// keyboard messages (typing, shortcuts, text navigation) meant for WPF input controls.
    /// </summary>
    [ComVisible(true)]
    [ClassInterface(ClassInterfaceType.AutoDispatch)]
    public class SafeElementHost : ElementHost
    {
        private const int WM_GETDLGCODE = 0x0087;
        private const uint DLGC_WANTALLKEYS = 0x0004;
        private const uint DLGC_WANTARROWS = 0x0001;
        private const uint DLGC_WANTCHARS = 0x0080;
        private const uint DLGC_WANTTAB = 0x0002;

        protected override bool IsInputKey(Keys keyData)
        {
            return true;
        }

        protected override bool IsInputChar(char charCode)
        {
            return true;
        }

        protected override void WndProc(ref Message m)
        {
            if (m.Msg == WM_GETDLGCODE)
            {
                m.Result = new IntPtr(DLGC_WANTALLKEYS | DLGC_WANTCHARS | DLGC_WANTARROWS | DLGC_WANTTAB);
                return;
            }
            base.WndProc(ref m);
        }
    }

    /// <summary>
    /// Custom Windows Forms UserControl wrapper that retains focus and prevents
    /// key-press swallowing across unmanaged Win32 / SolidWorks boundaries.
    /// </summary>
    [ComVisible(true)]
    [ClassInterface(ClassInterfaceType.AutoDispatch)]
    public class TaskPaneWinFormsHost : UserControl
    {
        private const int WM_GETDLGCODE = 0x0087;
        private const uint DLGC_WANTALLKEYS = 0x0004;
        private const uint DLGC_WANTARROWS = 0x0001;
        private const uint DLGC_WANTCHARS = 0x0080;
        private const uint DLGC_WANTTAB = 0x0002;

        protected override bool IsInputKey(Keys keyData)
        {
            return true;
        }

        protected override bool IsInputChar(char charCode)
        {
            return true;
        }

        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            // Do not allow WinForms container to intercept command or accelerator keys
            // when typing or navigating in the WPF ElementHost.
            return false;
        }

        protected override void WndProc(ref Message m)
        {
            if (m.Msg == WM_GETDLGCODE)
            {
                m.Result = new IntPtr(DLGC_WANTALLKEYS | DLGC_WANTCHARS | DLGC_WANTARROWS | DLGC_WANTTAB);
                return;
            }
            base.WndProc(ref m);
        }
    }
}
