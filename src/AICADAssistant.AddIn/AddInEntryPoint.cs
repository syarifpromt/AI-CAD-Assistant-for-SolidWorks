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
    [ClassInterface(ClassInterfaceType.None)]
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

                // HKLM: marks this add-in so SOLIDWORKS loads it at startup (value = 1)
                try
                {
                    using (RegistryKey hklmKey = Registry.LocalMachine.CreateSubKey(@"SOFTWARE\SolidWorks\AddIns\" + guidStr))
                    {
                        if (hklmKey != null)
                            hklmKey.SetValue(null, 1, RegistryValueKind.DWord);
                    }
                }
                catch
                {
                    // HKLM requires elevated admin rights, fallback to HKCU
                }

                // HKCU: per-user add-in info and startup flag
                using (RegistryKey hkcuKey = Registry.CurrentUser.CreateSubKey(@"SOFTWARE\SolidWorks\AddIns\" + guidStr))
                {
                    if (hkcuKey != null)
                    {
                        hkcuKey.SetValue("Description", ADDIN_DESCRIPTION, RegistryValueKind.String);
                        hkcuKey.SetValue("Title", ADDIN_TITLE, RegistryValueKind.String);
                        hkcuKey.SetValue("IsEnabled", 1, RegistryValueKind.DWord);
                    }
                }

                // Startup key
                using (RegistryKey startupKey = Registry.CurrentUser.CreateSubKey(@"SOFTWARE\SolidWorks\AddInsStartup\" + guidStr))
                {
                    if (startupKey != null)
                        startupKey.SetValue(null, 1, RegistryValueKind.DWord);
                }

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
                _swApp = (ISldWorks)ThisSW;
                _addinCookie = cookie;

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
                _swApp.SetAddinCallbackInfo2(0, this, cookie);

                // Add the task pane
                AddTaskPane();

                // Register SOLIDWORKS event handlers
                RegisterSwEvents();

                LogMessage("ConnectToSW: AI CAD Assistant berhasil terhubung ke SOLIDWORKS.");
                return true;
            }
            catch (Exception ex)
            {
                LogMessage("ConnectToSW ERROR: " + ex.Message);
                MessageBox.Show(
                    "Gagal menghubungkan AI CAD Assistant:\n" + ex.Message,
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
                string iconPath = GetIconPath();

                // Create a Windows Forms host for the WPF control
                UserControl hostForm = new UserControl();
                hostForm.Dock = DockStyle.Fill;
                hostForm.BackColor = System.Drawing.Color.White;

                // Create ElementHost to bridge WPF <-> WinForms
                ElementHost elementHost = new ElementHost();
                elementHost.Dock = DockStyle.Fill;
                elementHost.BackColor = System.Drawing.Color.White;

                // Create the WPF UserControl
                _wpfControl = new MainTaskPaneControl(_swApp);
                elementHost.Child = _wpfControl;

                hostForm.Controls.Add(elementHost);
                hostForm.CreateControl();

                // Create SOLIDWORKS task pane
                _taskpaneView = _swApp.CreateTaskpaneView2(iconPath, ADDIN_TITLE);

                if (_taskpaneView != null)
                {
                    // AddControl accepts the window handle or control identifier
                    _taskpaneView.AddControl(hostForm.Handle.ToString(), ADDIN_TITLE);
                    LogMessage("AddTaskPane: Task Pane berhasil dibuat.");
                }
                else
                {
                    LogMessage("AddTaskPane: Gagal membuat ITaskpaneView.");
                }
            }
            catch (Exception ex)
            {
                LogMessage("AddTaskPane ERROR: " + ex.Message);
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
                string logDir = Path.Combine(System.Environment.GetFolderPath(System.Environment.SpecialFolder.ApplicationData), "AICADAssistant");
                if (!Directory.Exists(logDir))
                    Directory.CreateDirectory(logDir);
                string logFile = Path.Combine(logDir, "addin.log");
                string entry = string.Format("[{0:yyyy-MM-dd HH:mm:ss}] {1}{2}", DateTime.Now, message, System.Environment.NewLine);
                File.AppendAllText(logFile, entry);
            }
            catch
            {
            }
        }

        #endregion
    }
}
