using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Input;
using Microsoft.Win32;
using SolidWorks.Interop.sldworks;
using AICADAssistant.AddIn.UI.Converters;
using AICADAssistant.Core.CAD;
using AICADAssistant.Core.Gemini;
using AICADAssistant.Core.Models;
using AICADAssistant.Core.Services;
using AICADAssistant.Core.Validation;

namespace AICADAssistant.AddIn.UI.ViewModels
{
    public class MainViewModel : INotifyPropertyChanged
    {
        private readonly ISldWorks _swApp;
        private readonly GeminiClient _geminiClient;
        private readonly CadPlanner _cadPlanner;
        private readonly CadPlanValidator _validator;
        private readonly SolidWorksAutomationService _swService;
        private readonly AppSettings _settings;
        private readonly CredentialService _credentialService;
        private readonly BackupService _backupService;

        private string _userInput;
        private string _operationStatus;
        private OperationStatusType _statusType;
        private bool _isBusy;
        private string _attachedFilePath;
        private string _attachedFileName;
        private string _attachedFilePreview;
        private bool _hasAttachedFile;
        private CadSpec _currentCadSpec;
        private string _currentDocumentPath;
        private CancellationTokenSource _cts;

        public event PropertyChangedEventHandler PropertyChanged;

        public ObservableCollection<ConversationMessage> Messages { get; set; }

        public string UserInput
        {
            get { return _userInput; }
            set { _userInput = value; OnPropertyChanged("UserInput"); OnPropertyChanged("CanSendMessage"); }
        }

        public string OperationStatus
        {
            get { return _operationStatus; }
            set { _operationStatus = value; OnPropertyChanged("OperationStatus"); }
        }

        public OperationStatusType StatusType
        {
            get { return _statusType; }
            set { _statusType = value; OnPropertyChanged("StatusType"); }
        }

        public bool IsBusy
        {
            get { return _isBusy; }
            set
            {
                _isBusy = value;
                OnPropertyChanged("IsBusy");
                OnPropertyChanged("CanSendMessage");
                OnPropertyChanged("CanAnalyzeFile");
                OnPropertyChanged("CanBuildModel");
                OnPropertyChanged("CanCancelOperation");
            }
        }

        public bool CanSendMessage
        {
            get { return !_isBusy && !string.IsNullOrWhiteSpace(_userInput); }
        }

        public bool CanAnalyzeFile
        {
            get { return !_isBusy && _hasAttachedFile; }
        }

        public bool CanBuildModel
        {
            get { return !_isBusy && _currentCadSpec != null && _currentCadSpec.Operations != null && _currentCadSpec.Operations.Count > 0; }
        }

        public bool CanCancelOperation
        {
            get { return _isBusy; }
        }

        public string AttachedFilePath
        {
            get { return _attachedFilePath; }
            set
            {
                _attachedFilePath = value;
                OnPropertyChanged("AttachedFilePath");
                HasAttachedFile = !string.IsNullOrWhiteSpace(value);
            }
        }

        public string AttachedFileName
        {
            get { return _attachedFileName; }
            set { _attachedFileName = value; OnPropertyChanged("AttachedFileName"); }
        }

        public string AttachedFilePreview
        {
            get { return _attachedFilePreview; }
            set { _attachedFilePreview = value; OnPropertyChanged("AttachedFilePreview"); }
        }

        public bool HasAttachedFile
        {
            get { return _hasAttachedFile; }
            set
            {
                _hasAttachedFile = value;
                OnPropertyChanged("HasAttachedFile");
                OnPropertyChanged("CanAnalyzeFile");
            }
        }

        public CadSpec CurrentCadSpec
        {
            get { return _currentCadSpec; }
            set
            {
                _currentCadSpec = value;
                OnPropertyChanged("CurrentCadSpec");
                OnPropertyChanged("CanBuildModel");
            }
        }

        public string CurrentDocumentPath
        {
            get { return _currentDocumentPath; }
            set { _currentDocumentPath = value; OnPropertyChanged("CurrentDocumentPath"); }
        }

        public ICommand SendMessageCommand { get; private set; }
        public ICommand AnalyzeFileCommand { get; private set; }
        public ICommand BuildModelCommand { get; private set; }
        public ICommand CancelCommand { get; private set; }
        public ICommand AttachFileCommand { get; private set; }
        public ICommand RemoveAttachmentCommand { get; private set; }
        public ICommand OpenSettingsCommand { get; private set; }
        public ICommand ExportStepCommand { get; private set; }
        public ICommand ExportStlCommand { get; private set; }

        public MainViewModel(ISldWorks swApp)
        {
            _swApp = swApp;
            _settings = AppSettings.Load();
            _credentialService = new CredentialService();
            _geminiClient = new GeminiClient(_credentialService, _settings);
            _cadPlanner = new CadPlanner();
            _validator = new CadPlanValidator();
            _swService = new SolidWorksAutomationService(swApp);
            _backupService = new BackupService();

            Messages = new ObservableCollection<ConversationMessage>();
            OperationStatus = "Siap";
            StatusType = OperationStatusType.Ready;

            SendMessageCommand = new RelayCommand(async o => await SendMessageAsync(), o => CanSendMessage);
            AnalyzeFileCommand = new RelayCommand(async o => await AnalyzeFileAsync(), o => CanAnalyzeFile);
            BuildModelCommand = new RelayCommand(async o => await BuildModelAsync(), o => CanBuildModel);
            CancelCommand = new RelayCommand(o => Cancel(), o => CanCancelOperation);
            AttachFileCommand = new RelayCommand(o => AttachFile());
            RemoveAttachmentCommand = new RelayCommand(o => RemoveAttachment());
            OpenSettingsCommand = new RelayCommand(o => OpenSettings());
            ExportStepCommand = new RelayCommand(o => ExportCurrentModel("step"));
            ExportStlCommand = new RelayCommand(o => ExportCurrentModel("stl"));

            Messages.Add(ConversationMessage.AssistantText(
                "Halo! Saya AI CAD Assistant. Anda dapat mendeskripsikan komponen mekanik yang ingin dibuat (misal: 'Buat pelat 100x60 mm dengan tebal 5 mm dan 4 lubang 8 mm') atau melampirkan gambar teknik."));
        }

        public async Task SendMessageAsync()
        {
            if (string.IsNullOrWhiteSpace(UserInput)) return;

            string text = UserInput.Trim();
            UserInput = string.Empty;
            Messages.Add(ConversationMessage.UserText(text));

            IsBusy = true;
            OperationStatus = "Menyusun Rencana CAD...";
            StatusType = OperationStatusType.Planning;
            _cts = new CancellationTokenSource();

            try
            {
                if (!_credentialService.HasApiKey())
                {
                    Messages.Add(ConversationMessage.ErrorMessage(
                        "API Key Gemini belum diatur. Silakan buka Pengaturan (ikon roda gigi) untuk memasukkan API Key Anda."));
                    OperationStatus = "Perlu Pengaturan API Key";
                    StatusType = OperationStatusType.Warning;
                    IsBusy = false;
                    return;
                }

                string prompt = _currentCadSpec != null
                    ? _cadPlanner.BuildPromptForRevision(text, _currentCadSpec)
                    : _cadPlanner.BuildPromptForTextRequest(text);

                var historyList = new System.Collections.Generic.List<ConversationMessage>(Messages);
                string responseText = await _geminiClient.SendTextAsync(prompt, historyList, _cts.Token).ConfigureAwait(true);

                // Parse CadSpec
                var spec = _cadPlanner.ParseCadSpec(responseText);
                if (spec == null)
                {
                    Messages.Add(ConversationMessage.AssistantText(responseText));
                    OperationStatus = "Siap";
                    StatusType = OperationStatusType.Ready;
                    IsBusy = false;
                    return;
                }

                // Semantic validation
                OperationStatus = "Memvalidasi Geometri...";
                StatusType = OperationStatusType.Validating;
                var validationResult = _validator.Validate(spec);

                if (!validationResult.IsValid)
                {
                    if (validationResult.MissingDimensions != null && validationResult.MissingDimensions.Count > 0)
                    {
                        string clarMsg = _cadPlanner.BuildClarificationMessage(validationResult.MissingDimensions);
                        Messages.Add(ConversationMessage.ClarificationRequest(clarMsg));
                        OperationStatus = "Menunggu Klarifikasi Dimensi";
                        StatusType = OperationStatusType.WaitingClarification;
                    }
                    else
                    {
                        string errs = string.Join("\n• ", validationResult.Errors);
                        Messages.Add(ConversationMessage.ErrorMessage("Rencana CAD tidak valid secara geometris:\n• " + errs));
                        OperationStatus = "Validasi Gagal";
                        StatusType = OperationStatusType.Failed;
                    }
                    IsBusy = false;
                    return;
                }

                // Valid plan ready
                CurrentCadSpec = spec;
                string notes = !string.IsNullOrWhiteSpace(spec.Notes) ? "\nCatatan: " + spec.Notes : "";
                Messages.Add(ConversationMessage.AssistantText(
                    string.Format("Rencana CAD berhasil disusun ({0} operasi, satuan: {1}).{2}\nTekan tombol 'Buat Model' untuk mengeksekusi di SOLIDWORKS.",
                    spec.Operations.Count, spec.Units, notes)));

                OperationStatus = "Rencana Siap Dieksekusi";
                StatusType = OperationStatusType.Success;
            }
            catch (GeminiException ex)
            {
                Messages.Add(ConversationMessage.ErrorMessage(ex.Message));
                OperationStatus = "Error Gemini API";
                StatusType = OperationStatusType.Failed;
            }
            catch (OperationCanceledException)
            {
                Messages.Add(ConversationMessage.AssistantText("Operasi dibatalkan."));
                OperationStatus = "Dibatalkan";
                StatusType = OperationStatusType.Cancelled;
            }
            catch (Exception ex)
            {
                Messages.Add(ConversationMessage.ErrorMessage("Terjadi kesalahan: " + ex.Message));
                OperationStatus = "Error";
                StatusType = OperationStatusType.Failed;
            }
            finally
            {
                IsBusy = false;
            }
        }

        public async Task AnalyzeFileAsync()
        {
            if (string.IsNullOrWhiteSpace(AttachedFilePath) || !File.Exists(AttachedFilePath))
                return;

            IsBusy = true;
            OperationStatus = "Menganalisis Gambar dengan Gemini...";
            StatusType = OperationStatusType.Analyzing;
            _cts = new CancellationTokenSource();

            try
            {
                if (!_credentialService.HasApiKey())
                {
                    Messages.Add(ConversationMessage.ErrorMessage(
                        "API Key Gemini belum diatur. Silakan buka Pengaturan untuk memasukkan API Key Anda."));
                    OperationStatus = "Perlu Pengaturan API Key";
                    StatusType = OperationStatusType.Warning;
                    IsBusy = false;
                    return;
                }

                Messages.Add(ConversationMessage.UserImage(
                    "Menganalisis berkas: " + AttachedFileName, AttachedFilePath));

                string base64Data = ImageService.ToBase64(AttachedFilePath);
                string mimeType = ImageService.GetMimeType(AttachedFilePath);
                string prompt = _cadPlanner.BuildPromptForImageAnalysis(UserInput);
                UserInput = string.Empty;

                string responseText = await _geminiClient.SendWithImageAsync(prompt, base64Data, mimeType, _cts.Token).ConfigureAwait(true);

                var spec = _cadPlanner.ParseCadSpec(responseText);
                if (spec == null)
                {
                    Messages.Add(ConversationMessage.AssistantText(responseText));
                    OperationStatus = "Siap";
                    StatusType = OperationStatusType.Ready;
                    IsBusy = false;
                    return;
                }

                // Validate extracted specification
                var valResult = _validator.Validate(spec);
                if (!valResult.IsValid)
                {
                    if (valResult.MissingDimensions != null && valResult.MissingDimensions.Count > 0)
                    {
                        string clarMsg = _cadPlanner.BuildClarificationMessage(valResult.MissingDimensions);
                        Messages.Add(ConversationMessage.ClarificationRequest(clarMsg));
                        OperationStatus = "Menunggu Klarifikasi";
                        StatusType = OperationStatusType.WaitingClarification;
                    }
                    else
                    {
                        string errs = string.Join("\n• ", valResult.Errors);
                        Messages.Add(ConversationMessage.ErrorMessage("Analisis gambar menghasilkan rencana yang memerlukan perbaikan:\n• " + errs));
                        OperationStatus = "Perlu Perhatian";
                        StatusType = OperationStatusType.Warning;
                    }
                    IsBusy = false;
                    return;
                }

                CurrentCadSpec = spec;
                Messages.Add(ConversationMessage.AssistantText(
                    string.Format("Gambar berhasil dianalisis! Ditemukan {0} operasi geometri. Klik 'Buat Model' untuk membangun part di SOLIDWORKS.", spec.Operations.Count)));

                OperationStatus = "Analisis Selesai";
                StatusType = OperationStatusType.Success;
            }
            catch (GeminiException ex)
            {
                Messages.Add(ConversationMessage.ErrorMessage(ex.Message));
                OperationStatus = "Error Analisis";
                StatusType = OperationStatusType.Failed;
            }
            catch (OperationCanceledException)
            {
                Messages.Add(ConversationMessage.AssistantText("Analisis dibatalkan."));
                OperationStatus = "Dibatalkan";
                StatusType = OperationStatusType.Cancelled;
            }
            catch (Exception ex)
            {
                Messages.Add(ConversationMessage.ErrorMessage("Terjadi kesalahan analisis: " + ex.Message));
                OperationStatus = "Error";
                StatusType = OperationStatusType.Failed;
            }
            finally
            {
                IsBusy = false;
            }
        }

        public async Task BuildModelAsync()
        {
            if (CurrentCadSpec == null) return;

            IsBusy = true;
            OperationStatus = "Membangun Model di SOLIDWORKS...";
            StatusType = OperationStatusType.BuildingModel;

            try
            {
                // Checkpoint active document if available
                var activeDoc = _swService.GetActiveDocument();
                if (activeDoc != null)
                {
                    string path = activeDoc.GetPathName();
                    if (!string.IsNullOrWhiteSpace(path) && File.Exists(path))
                    {
                        _backupService.CreateCheckpoint(path);
                    }
                }

                // Execute deterministically on SOLIDWORKS COM
                ExecutionResult result = await Task.Run(() => _swService.ExecuteSpec(CurrentCadSpec, _settings.OutputDirectory)).ConfigureAwait(true);

                if (result.Success)
                {
                    string bboxInfo = result.BoundingBoxMm != null && result.BoundingBoxMm.Length >= 3
                        ? string.Format(" (Ukuran: {0:F1} × {1:F1} × {2:F1} mm)", result.BoundingBoxMm[0], result.BoundingBoxMm[1], result.BoundingBoxMm[2])
                        : "";

                    string msg = string.Format("Model 3D berhasil dibuat dan direbuild secara native di SOLIDWORKS!{0}\nJumlah solid body: {1}\nWaktu pemodelan: {2} ms",
                        bboxInfo, result.SolidBodyCount, result.DurationMs);

                    if (!string.IsNullOrWhiteSpace(result.SavedFilePath))
                    {
                        msg += "\nTersimpan di: " + result.SavedFilePath;
                        CurrentDocumentPath = result.SavedFilePath;
                    }

                    if (result.ExportedFilePaths != null && result.ExportedFilePaths.Count > 0)
                    {
                        msg += "\nBerkas ekspor: " + string.Join(", ", result.ExportedFilePaths);
                    }

                    Messages.Add(ConversationMessage.AssistantText(msg));
                    OperationStatus = "Model Berhasil Dibuat";
                    StatusType = OperationStatusType.Success;
                }
                else
                {
                    Messages.Add(ConversationMessage.ErrorMessage(
                        "Gagal membuat model: " + result.ErrorMessage));
                    OperationStatus = "Pembuatan Model Gagal";
                    StatusType = OperationStatusType.Failed;
                }
            }
            catch (Exception ex)
            {
                Messages.Add(ConversationMessage.ErrorMessage("Terjadi kesalahan pembuatan model: " + ex.Message));
                OperationStatus = "Error";
                StatusType = OperationStatusType.Failed;
            }
            finally
            {
                IsBusy = false;
            }
        }

        public void Cancel()
        {
            if (_cts != null)
            {
                _cts.Cancel();
            }
        }

        private void AttachFile()
        {
            var dlg = new OpenFileDialog
            {
                Title = "Pilih Gambar Teknik atau Dokumen",
                Filter = "Gambar Teknik & Dokumen (*.jpg;*.jpeg;*.png;*.webp;*.pdf)|*.jpg;*.jpeg;*.png;*.webp;*.pdf|Semua Berkas (*.*)|*.*"
            };

            if (dlg.ShowDialog() == true)
            {
                var preview = ImageService.GetImagePreviewInfo(dlg.FileName);
                if (!preview.IsValid)
                {
                    Messages.Add(ConversationMessage.ErrorMessage(preview.ValidationError));
                    return;
                }

                AttachedFilePath = dlg.FileName;
                AttachedFileName = preview.FileName;
                string extra = preview.Extension == ".pdf"
                    ? string.Format(" ({0} halaman, {1})", preview.PageCount, preview.DisplaySize)
                    : string.Format(" ({0}x{1} px, {2})", preview.Width, preview.Height, preview.DisplaySize);
                AttachedFilePreview = extra;
            }
        }

        private void RemoveAttachment()
        {
            AttachedFilePath = null;
            AttachedFileName = null;
            AttachedFilePreview = null;
        }

        private void OpenSettings()
        {
            var win = new SettingsWindow(_settings, _credentialService, _geminiClient);
            win.ShowDialog();
        }

        private void ExportCurrentModel(string format)
        {
            var doc = _swService.GetActiveDocument();
            if (doc == null)
            {
                Messages.Add(ConversationMessage.ErrorMessage("Tidak ada dokumen aktif untuk diekspor."));
                return;
            }

            string ext = "." + format.ToLowerInvariant();
            var dlg = new SaveFileDialog
            {
                Title = "Ekspor Model " + format.ToUpperInvariant(),
                Filter = string.Format("Berkas {0} (*{1})|*{1}", format.ToUpperInvariant(), ext),
                FileName = Path.GetFileNameWithoutExtension(doc.GetPathName()) + ext
            };

            if (dlg.ShowDialog() == true)
            {
                bool ok = _swService.SaveAs(doc, dlg.FileName);
                if (ok)
                {
                    Messages.Add(ConversationMessage.AssistantText("Model berhasil diekspor ke: " + dlg.FileName));
                }
                else
                {
                    Messages.Add(ConversationMessage.ErrorMessage("Gagal mengekspor model."));
                }
            }
        }

        public void OnActiveDocumentChanged()
        {
            var doc = _swService.GetActiveDocument();
            if (doc != null)
            {
                CurrentDocumentPath = doc.GetPathName();
            }
            else
            {
                CurrentDocumentPath = null;
            }
        }

        private void OnPropertyChanged(string propName)
        {
            if (PropertyChanged != null)
            {
                PropertyChanged(this, new PropertyChangedEventArgs(propName));
            }
        }
    }

    public class RelayCommand : ICommand
    {
        private readonly Action<object> _execute;
        private readonly Predicate<object> _canExecute;

        public event EventHandler CanExecuteChanged
        {
            add { System.Windows.Input.CommandManager.RequerySuggested += value; }
            remove { System.Windows.Input.CommandManager.RequerySuggested -= value; }
        }

        public RelayCommand(Action<object> execute, Predicate<object> canExecute = null)
        {
            if (execute == null) throw new ArgumentNullException("execute");
            _execute = execute;
            _canExecute = canExecute;
        }

        public bool CanExecute(object parameter)
        {
            return _canExecute == null || _canExecute(parameter);
        }

        public void Execute(object parameter)
        {
            _execute(parameter);
        }
    }
}
