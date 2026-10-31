using System;
using System.Collections.Generic;
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
using System.Windows.Threading;

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
        private string _activeOperationName;

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

        public string AppVersionDisplay
        {
            get
            {
                var ver = typeof(MainViewModel).Assembly.GetName().Version;
                return string.Format("v{0}.{1}.{2}", ver.Major, ver.Minor, ver.Build);
            }
        }

        public GeminiConnectionStatus ConnectionStatus
        {
            get { return GeminiConnectionService.Instance.Status; }
        }

        public string ConnectionStatusLabel
        {
            get { return GeminiConnectionService.Instance.GetStatusLabel(); }
        }

        public string ActiveModelName
        {
            get { return GeminiConnectionService.Instance.ActiveModel; }
        }

        public long LastResponseLatencyMs
        {
            get { return GeminiConnectionService.Instance.LastResponseTimeMs; }
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

            GeminiConnectionService.Instance.StateChanged += (s, e) =>
            {
                var dispatcher = System.Windows.Application.Current != null ? System.Windows.Application.Current.Dispatcher : null;
                if (dispatcher != null && !dispatcher.CheckAccess())
                {
                    dispatcher.InvokeAsync(() =>
                    {
                        OnPropertyChanged("ConnectionStatus");
                        OnPropertyChanged("ConnectionStatusLabel");
                        OnPropertyChanged("ActiveModelName");
                        OnPropertyChanged("LastResponseLatencyMs");
                    });
                }
                else
                {
                    OnPropertyChanged("ConnectionStatus");
                    OnPropertyChanged("ConnectionStatusLabel");
                    OnPropertyChanged("ActiveModelName");
                    OnPropertyChanged("LastResponseLatencyMs");
                }
            };

            SendMessageCommand = new RelayCommand(async o => await SendMessageAsync(), o => CanSendMessage);
            AnalyzeFileCommand = new RelayCommand(async o => await AnalyzeFileAsync(), o => CanAnalyzeFile);
            BuildModelCommand = new RelayCommand(o => BuildModel(), o => CanBuildModel);
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

            // Prior history MUST only contain messages prior to the current prompt
            var priorHistory = new List<ConversationMessage>(Messages);
            Messages.Add(ConversationMessage.UserText(text));

            IsBusy = true;
            _activeOperationName = "Menyusun Rencana CAD";
            OperationStatus = "Menyusun Rencana CAD...";
            StatusType = OperationStatusType.Planning;
            _cts = new CancellationTokenSource();

            var overallSw = System.Diagnostics.Stopwatch.StartNew();
            LoggingService.Instance.LogPipeline("STAGE_1_CHAT_RECEIVED", "SUCCESS",
                string.Format("Panjang teks: {0}, preview: '{1}'", text.Length, text.Substring(0, Math.Min(40, text.Length))));

            // Live progress timer to keep UI updated with elapsed seconds
            DispatcherTimer busyTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromSeconds(1)
            };
            busyTimer.Tick += (s, e) =>
            {
                if (_isBusy)
                {
                    int sec = (int)overallSw.Elapsed.TotalSeconds;
                    if (!string.IsNullOrEmpty(_activeOperationName))
                    {
                        OperationStatus = string.Format("{0}... ({1} dtk)", _activeOperationName, sec);
                    }
                }
            };
            busyTimer.Start();

            try
            {
                CadSpec spec = null;
                HashSet<string> existingCompletedOpIds = null;

                var activeDoc = _swService.GetActiveDocument();
                int activeBodyCount = activeDoc != null ? _swService.GetSolidBodyCount(activeDoc) : 0;
                double beforeVolume = activeDoc != null ? _swService.GetVolumeMm3(activeDoc) : 0;

                // 1. Deterministic Intent Classification
                UserIntent intent = IntentClassifier.Classify(text, _currentCadSpec, activeBodyCount);

                if (intent == UserIntent.Conversational)
                {
                    LoggingService.Instance.LogPipeline("STAGE_2_DIRECT_EXECUTION", "CONVERSATIONAL_INTENT",
                        "Pertanyaan percakapan terdeteksi. CAD Executor tidak akan dijalankan.");

                    if (_credentialService.HasApiKey())
                    {
                        _activeOperationName = "Menjawab Pertanyaan";
                        OperationStatus = "Menjawab Pertanyaan...";
                        StatusType = OperationStatusType.Planning;
                        var reqSw = System.Diagnostics.Stopwatch.StartNew();
                        string answer = await _geminiClient.SendChatAsync(text, priorHistory, _cts.Token).ConfigureAwait(true);
                        reqSw.Stop();
                        Messages.Add(ConversationMessage.AssistantText(answer));
                    }
                    else
                    {
                        Messages.Add(ConversationMessage.AssistantText("Halo! Saya asisten AI CAD. Silakan berikan instruksi pemodelan (contoh: 'Buat pelat 100x60 tebal 5 mm') atau atur API Key di menu Pengaturan."));
                    }
                    OperationStatus = "Siap";
                    StatusType = OperationStatusType.Ready;
                    IsBusy = false;
                    return;
                }

                if (intent == UserIntent.Export)
                {
                    LoggingService.Instance.LogPipeline("STAGE_2_DIRECT_EXECUTION", "EXPORT_INTENT", "Perintah ekspor model terdeteksi.");
                    if (activeDoc == null)
                    {
                        Messages.Add(ConversationMessage.ErrorMessage("Tidak ada model aktif di SOLIDWORKS yang dapat diekspor."));
                        OperationStatus = "Ekspor Gagal: Dokumen Kosong";
                        StatusType = OperationStatusType.Failed;
                    }
                    else
                    {
                        string outDir = !string.IsNullOrWhiteSpace(_settings.OutputDirectory) ? _settings.OutputDirectory : System.Environment.GetFolderPath(System.Environment.SpecialFolder.MyDocuments);
                        string baseName = Path.GetFileNameWithoutExtension(activeDoc.GetTitle());
                        if (string.IsNullOrWhiteSpace(baseName)) baseName = "Model";

                        string lowerText = text.ToLowerInvariant();
                        if (lowerText.Contains("stl"))
                        {
                            string exportPath = Path.Combine(outDir, baseName + ".stl");
                            bool ok = _swService.ExportStl(activeDoc, exportPath);
                            if (ok)
                            {
                                Messages.Add(ConversationMessage.AssistantText("✓ Model berhasil diekspor ke STL:\n" + exportPath));
                                OperationStatus = "Model Berhasil Diekspor ke STL";
                                StatusType = OperationStatusType.Success;
                            }
                            else
                            {
                                Messages.Add(ConversationMessage.ErrorMessage("Gagal mengekspor model ke berkas STL."));
                                OperationStatus = "Ekspor STL Gagal";
                                StatusType = OperationStatusType.Failed;
                            }
                        }
                        else
                        {
                            string exportPath = Path.Combine(outDir, baseName + ".step");
                            bool ok = _swService.ExportStep(activeDoc, exportPath);
                            if (ok)
                            {
                                Messages.Add(ConversationMessage.AssistantText("✓ Model berhasil diekspor ke STEP:\n" + exportPath));
                                OperationStatus = "Model Berhasil Diekspor ke STEP";
                                StatusType = OperationStatusType.Success;
                            }
                            else
                            {
                                Messages.Add(ConversationMessage.ErrorMessage("Gagal mengekspor model ke berkas STEP."));
                                OperationStatus = "Ekspor STEP Gagal";
                                StatusType = OperationStatusType.Failed;
                            }
                        }
                    }
                    IsBusy = false;
                    return;
                }

                // 2. CAD Modeling Execution (CreateNewModel or ModifyExistingModel)
                bool isModification = (intent == UserIntent.ModifyExistingModel);
                string activeRequestId = "req_" + DateTime.Now.ToString("yyyyMMdd_HHmmss");

                if (isModification)
                {
                    LoggingService.Instance.LogPipeline("STAGE_2_DIRECT_EXECUTION", "MODIFICATION_REQUEST",
                        string.Format("Konteks: Modifikasi model aktif. Bodies={0}, CurrentOps={1}",
                            activeBodyCount, _currentCadSpec != null && _currentCadSpec.Operations != null ? _currentCadSpec.Operations.Count : 0));

                    // Check Fast Path Revision first (< 10 ms latency)
                    CadSpec fastRevSpec;
                    if (CadFastPathEngine.TryCreateRevisionSpec(text, _currentCadSpec, out fastRevSpec))
                    {
                        LoggingService.Instance.LogPipeline("STAGE_2_DIRECT_EXECUTION", "FAST_PATH_REVISION",
                            string.Format("Revisi deterministik terdeteksi: {0}", fastRevSpec.Notes));
                        spec = fastRevSpec;
                    }
                    else
                    {
                        if (!_credentialService.HasApiKey())
                        {
                            LoggingService.Instance.LogPipeline("STAGE_2_DIRECT_EXECUTION", "BLOCKED", "API Key Gemini belum disetel");
                            Messages.Add(ConversationMessage.ErrorMessage(
                                "API Key Gemini belum diatur. Silakan buka Pengaturan (ikon roda gigi) untuk memasukkan API Key Anda."));
                            OperationStatus = "Perlu Pengaturan API Key";
                            StatusType = OperationStatusType.Warning;
                            IsBusy = false;
                            return;
                        }

                        LoggingService.Instance.LogPipeline("STAGE_2_DIRECT_EXECUTION", "INITIATED_REVISION",
                            string.Format("Model aktif: '{0}'", _geminiClient.CurrentModel));

                        string prompt = _cadPlanner.BuildPromptForRevision(text, _currentCadSpec);
                        var historyList = new System.Collections.Generic.List<ConversationMessage>(Messages);

                        LoggingService.Instance.LogPipeline("STAGE_3_GEMINI_REQUEST", "SENDING",
                            string.Format("Ukuran prompt revisi: {0} karakter, Riwayat: {1} pesan", prompt.Length, historyList.Count));

                        var reqSw = System.Diagnostics.Stopwatch.StartNew();
                        ThinkingLevel thinkingLevel = AdaptiveThinkingManager.DetermineLevel(text, true);
                        string responseText = await _geminiClient.SendCadPlanAsync(prompt, thinkingLevel, _cts.Token).ConfigureAwait(true);
                        reqSw.Stop();

                        LoggingService.Instance.LogPipeline("STAGE_4_GEMINI_RESPONSE", "SUCCESS",
                            string.Format("Panjang respons: {0} karakter", responseText.Length), reqSw.ElapsedMilliseconds);

                        spec = _cadPlanner.ParseCadSpec(responseText);
                        if (spec == null)
                        {
                            LoggingService.Instance.LogPipeline("STAGE_5_CAD_PARSE", "CONVERSATIONAL",
                                "Respons bukan CadSpec JSON (teks percakapan / penjelasan)");
                            Messages.Add(ConversationMessage.AssistantText(responseText));
                            OperationStatus = "Siap";
                            StatusType = OperationStatusType.Ready;
                            IsBusy = false;
                            return;
                        }
                    }

                    // Context Reconciliation: identify existing completed operations on active model
                    string reconcileNote;
                    CadContextReconciler.TryReconcileRevisionSpec(spec, _currentCadSpec, activeBodyCount, out existingCompletedOpIds, out reconcileNote);
                    if (!string.IsNullOrEmpty(reconcileNote))
                    {
                        LoggingService.Instance.Info("[CONTEXT_RECONCILE] " + reconcileNote);
                    }
                }
                else
                {
                    // New Model Request
                    LoggingService.Instance.LogPipeline("STAGE_2_DIRECT_EXECUTION", "NEW_MODEL_REQUEST",
                        "Konteks: Pembuatan model baru dari awal.");

                    CadSpec fastSpec;
                    if (CadFastPathEngine.TryCreateSpec(text, out fastSpec))
                    {
                        LoggingService.Instance.LogPipeline("STAGE_2_DIRECT_EXECUTION", "FAST_PATH",
                            string.Format("Primitive deterministik terdeteksi: {0}", fastSpec.Notes));
                        spec = fastSpec;
                    }
                    else
                    {
                        if (!_credentialService.HasApiKey())
                        {
                            LoggingService.Instance.LogPipeline("STAGE_2_DIRECT_EXECUTION", "BLOCKED", "API Key Gemini belum disetel");
                            Messages.Add(ConversationMessage.ErrorMessage(
                                "API Key Gemini belum diatur. Silakan buka Pengaturan (ikon roda gigi) untuk memasukkan API Key Anda."));
                            OperationStatus = "Perlu Pengaturan API Key";
                            StatusType = OperationStatusType.Warning;
                            IsBusy = false;
                            return;
                        }

                        LoggingService.Instance.LogPipeline("STAGE_2_DIRECT_EXECUTION", "INITIATED_NEW",
                            string.Format("Model aktif: '{0}'", _geminiClient.CurrentModel));

                        string prompt = _cadPlanner.BuildPromptForTextRequest(text);

                        LoggingService.Instance.LogPipeline("STAGE_3_GEMINI_REQUEST", "SENDING",
                            string.Format("Ukuran prompt model baru: {0} karakter", prompt.Length));

                        var reqSw = System.Diagnostics.Stopwatch.StartNew();
                        ThinkingLevel thinkingLevel = AdaptiveThinkingManager.DetermineLevel(text, false);
                        string responseText = await _geminiClient.SendCadPlanAsync(prompt, thinkingLevel, _cts.Token).ConfigureAwait(true);
                        reqSw.Stop();

                        LoggingService.Instance.LogPipeline("STAGE_4_GEMINI_RESPONSE", "SUCCESS",
                            string.Format("Panjang respons: {0} karakter", responseText.Length), reqSw.ElapsedMilliseconds);

                        spec = _cadPlanner.ParseCadSpec(responseText);
                        if (spec == null)
                        {
                            LoggingService.Instance.LogPipeline("STAGE_5_CAD_PARSE", "CONVERSATIONAL",
                                "Respons bukan CadSpec JSON (teks percakapan / penjelasan)");
                            Messages.Add(ConversationMessage.AssistantText(responseText));
                            OperationStatus = "Siap";
                            StatusType = OperationStatusType.Ready;
                            IsBusy = false;
                            return;
                        }
                    }
                }

                // Prevent stale plan execution: tag spec with current active RequestId
                spec.RequestId = activeRequestId;

                LoggingService.Instance.LogPipeline("STAGE_5_CAD_PARSE", "SUCCESS",
                    string.Format("RequestId: {0}, Operasi: {1}", spec.RequestId, spec.Operations != null ? spec.Operations.Count : 0));

                // Semantic validation with awareness of existing completed operations
                OperationStatus = "Memvalidasi Geometri...";
                StatusType = OperationStatusType.Validating;
                var validationResult = _validator.Validate(spec, existingCompletedOpIds);

                if (!validationResult.IsValid)
                {
                    if (validationResult.MissingDimensions != null && validationResult.MissingDimensions.Count > 0)
                    {
                        LoggingService.Instance.LogPipeline("STAGE_6_CAD_VALIDATE", "MISSING_DIMENSIONS",
                            string.Join("; ", validationResult.MissingDimensions));
                        string clarMsg = _cadPlanner.BuildClarificationMessage(validationResult.MissingDimensions);
                        Messages.Add(ConversationMessage.ClarificationRequest(clarMsg));
                        OperationStatus = "Menunggu Klarifikasi Dimensi";
                        StatusType = OperationStatusType.WaitingClarification;
                    }
                    else
                    {
                        LoggingService.Instance.LogPipeline("STAGE_6_CAD_VALIDATE", "INVALID",
                            string.Join("; ", validationResult.Errors));
                        string errs = string.Join("\n• ", validationResult.Errors);
                        Messages.Add(ConversationMessage.ErrorMessage("Rencana CAD tidak valid secara geometris:\n• " + errs));
                        OperationStatus = "Validasi Gagal";
                        StatusType = OperationStatusType.Failed;
                    }
                    IsBusy = false;
                    return;
                }

                LoggingService.Instance.LogPipeline("STAGE_6_CAD_VALIDATE", "VALID", "Rencana CAD 100% valid secara semantik");

                // Valid plan ready: Execute directly in SOLIDWORKS
                _activeOperationName = "Mengeksekusi di SOLIDWORKS";
                OperationStatus = "Mengeksekusi di SOLIDWORKS...";
                StatusType = OperationStatusType.BuildingModel;

                bool execSuccess = ExecuteSpecDirectly(spec, existingCompletedOpIds, beforeVolume, isModification);
                overallSw.Stop();
                if (execSuccess)
                {
                    // Cumulative spec maintenance: merge new operations into current spec for history
                    CurrentCadSpec = isModification
                        ? CadContextReconciler.MergeSpecs(_currentCadSpec, spec)
                        : spec;

                    LoggingService.Instance.LogPipeline("STAGE_COMPLETE", "SUCCESS", "Pipeline Chat-to-CAD selesai dengan sukses.", overallSw.ElapsedMilliseconds);
                }
                else
                {
                    LoggingService.Instance.LogPipeline("STAGE_COMPLETE", "FAILED", "Pipeline Chat-to-CAD gagal pada tahap eksekusi geometri.", overallSw.ElapsedMilliseconds);
                }
            }
            catch (GeminiException ex)
            {
                LoggingService.Instance.LogPipeline("GEMINI_ERROR", "FAILED",
                    string.Format("Status: {0}, Code: {1}, Msg: {2}", ex.ServerStatus, ex.StatusCode, ex.Message), overallSw.ElapsedMilliseconds, ex);

                Messages.Add(ConversationMessage.ErrorMessage(ex.Message));
                if (ex.IsServerUnavailable)
                    OperationStatus = "Server Gemini Sibuk (503)";
                else if (ex.IsModelNotFound)
                    OperationStatus = "Model Gemini Tidak Ditemukan";
                else if (ex.IsAuthError)
                    OperationStatus = "API Key Tidak Valid";
                else if (ex.IsRateLimit)
                    OperationStatus = "Batas Kuota Tercapai";
                else if (ex.IsTimeout)
                    OperationStatus = "Batas Waktu Habis";
                else
                    OperationStatus = "Error Gemini API";
                StatusType = OperationStatusType.Failed;
            }
            catch (OperationCanceledException)
            {
                LoggingService.Instance.LogPipeline("USER_CANCELLED", "CANCELLED", "Operasi dibatalkan oleh pengguna", overallSw.ElapsedMilliseconds);
                Messages.Add(ConversationMessage.AssistantText("Operasi dibatalkan."));
                OperationStatus = "Dibatalkan";
                StatusType = OperationStatusType.Cancelled;
            }
            catch (Exception ex)
            {
                LoggingService.Instance.LogPipeline("PIPELINE_FATAL_ERROR", "ERROR", ex.Message, overallSw.ElapsedMilliseconds, ex);
                Messages.Add(ConversationMessage.ErrorMessage("Terjadi kesalahan: " + ex.Message));
                OperationStatus = "Error";
                StatusType = OperationStatusType.Failed;
            }
            finally
            {
                busyTimer.Stop();
                IsBusy = false;
            }
        }

        public async Task AnalyzeFileAsync()
        {
            if (string.IsNullOrWhiteSpace(AttachedFilePath) || !File.Exists(AttachedFilePath))
                return;

            IsBusy = true;
            _activeOperationName = "Menganalisis Gambar dengan Gemini";
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
                    string.Format("Gambar berhasil dianalisis! Ditemukan {0} operasi geometri. Mengeksekusi model di SOLIDWORKS...", spec.Operations.Count)));

                OperationStatus = "Mengeksekusi Model dari Gambar...";
                StatusType = OperationStatusType.BuildingModel;

                ExecuteSpecDirectly(spec);
            }
            catch (GeminiException ex)
            {
                Messages.Add(ConversationMessage.ErrorMessage(ex.Message));
                if (ex.IsServerUnavailable)
                    OperationStatus = "Server Gemini Sibuk (503)";
                else if (ex.IsModelNotFound)
                    OperationStatus = "Model Gemini Tidak Ditemukan";
                else if (ex.IsAuthError)
                    OperationStatus = "API Key Tidak Valid";
                else if (ex.IsRateLimit)
                    OperationStatus = "Batas Kuota Tercapai";
                else if (ex.IsTimeout)
                    OperationStatus = "Batas Waktu Habis";
                else
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

        public void BuildModel()
        {
            if (CurrentCadSpec == null || _isBusy) return;

            IsBusy = true;
            OperationStatus = "Membangun Model di SOLIDWORKS...";
            StatusType = OperationStatusType.BuildingModel;

            try
            {
                ExecuteSpecDirectly(CurrentCadSpec);
            }
            finally
            {
                IsBusy = false;
            }
        }

        private bool ExecuteSpecDirectly(CadSpec spec, HashSet<string> existingCompletedOpIds = null, double previousVolume = 0, bool isModification = false)
        {
            if (spec == null) return false;

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

                int completedCount = existingCompletedOpIds != null ? existingCompletedOpIds.Count : 0;
                LoggingService.Instance.LogPipeline("STAGE_7_CAD_EXECUTOR", "INVOKING",
                    string.Format("Mengeksekusi {0} operasi CAD di thread STA SOLIDWORKS... (ExistingCompleted: {1})",
                        spec.Operations != null ? spec.Operations.Count : 0, completedCount));

                // CRITICAL STA THREAD SAFETY:
                // Execute synchronously on the SolidWorks STA thread, NEVER on Task.Run ThreadPool!
                ExecutionResult result = _swService.ExecuteSpec(spec, _settings.OutputDirectory, existingCompletedOpIds);

                // Run strict geometric verification
                var verifReport = GeometryVerifier.Verify(result, spec, previousVolume, isModification);

                LoggingService.Instance.LogPipeline("CAD_RESULT", (result.Success && verifReport.IsValid) ? "SUCCESS" : "FAILED",
                    string.Format("Doc='{0}', Feature='{1}', Bodies={2}, Rebuild={3}, Vol={4:N2} mm3, Detail='{5}'",
                        result.DocumentTitle, result.FeatureName, result.SolidBodyCount, result.RebuildSuccess, result.VolumeMm3,
                        verifReport.IsValid ? "OK" : string.Join("; ", verifReport.Errors)),
                    result.DurationMs);

                if (result.Success && verifReport.IsValid)
                {
                    var sb = new System.Text.StringBuilder();
                    sb.AppendLine("✓ Model 3D berhasil dibuat dan direbuild secara native di SOLIDWORKS!");

                    if (!string.IsNullOrWhiteSpace(result.DocumentTitle))
                    {
                        sb.AppendLine("• Dokumen: " + result.DocumentTitle);
                    }
                    if (!string.IsNullOrWhiteSpace(result.FeatureName))
                    {
                        sb.AppendLine("• Fitur: " + result.FeatureName);
                    }
                    sb.AppendLine(string.Format("• Status Rebuild: {0}", result.RebuildSuccess ? "Berhasil (100% Valid)" : "Peringatan"));
                    sb.AppendLine(string.Format("• Solid Body: {0}", result.SolidBodyCount));

                    if (result.BoundingBoxMm != null && result.BoundingBoxMm.Length >= 3 &&
                        (result.BoundingBoxMm[0] > 0 || result.BoundingBoxMm[1] > 0 || result.BoundingBoxMm[2] > 0))
                    {
                        sb.AppendLine(string.Format("• Dimensi Bounding Box: {0:F1} × {1:F1} × {2:F1} mm",
                            result.BoundingBoxMm[0], result.BoundingBoxMm[1], result.BoundingBoxMm[2]));
                    }

                    if (result.VolumeMm3 > 0)
                    {
                        sb.AppendLine(string.Format("• Volume Benda: {0:N2} mm³", result.VolumeMm3));
                    }

                    if (isModification && Math.Abs(verifReport.VolumeDeltaMm3) > 0.001)
                    {
                        sb.AppendLine(string.Format("• Perubahan Volume: {0:+0.00;-0.00;0.00} mm³", verifReport.VolumeDeltaMm3));
                    }

                    if (verifReport.Warnings != null && verifReport.Warnings.Count > 0)
                    {
                        foreach (var w in verifReport.Warnings)
                        {
                            sb.AppendLine("• Catatan: " + w);
                        }
                    }

                    sb.AppendLine(string.Format("• Waktu Eksekusi: {0} ms", result.DurationMs));

                    if (!string.IsNullOrWhiteSpace(result.SavedFilePath))
                    {
                        sb.AppendLine("• Berkas Tersimpan: " + result.SavedFilePath);
                        CurrentDocumentPath = result.SavedFilePath;
                    }

                    if (result.ExportedFilePaths != null && result.ExportedFilePaths.Count > 0)
                    {
                        sb.AppendLine("• Ekspor: " + string.Join(", ", result.ExportedFilePaths));
                    }

                    Messages.Add(ConversationMessage.AssistantText(sb.ToString().TrimEnd()));
                    OperationStatus = "Model Berhasil Dibuat";
                    StatusType = OperationStatusType.Success;
                    return true;
                }
                else
                {
                    string err = (verifReport.Errors != null && verifReport.Errors.Count > 0)
                        ? string.Join("\n• ", verifReport.Errors)
                        : (!string.IsNullOrWhiteSpace(result.ErrorMessage)
                            ? result.ErrorMessage
                            : (result.SolidBodyCount == 0 ? "Tidak ada Solid Body yang terbentuk (Body Count = 0)." : "Gagal mengeksekusi geometri di SOLIDWORKS."));

                    Messages.Add(ConversationMessage.ErrorMessage("Gagal membuat model:\n• " + err));
                    OperationStatus = "Pembuatan Model Gagal: " + (verifReport.Errors.Count > 0 ? verifReport.Errors[0] : err);
                    StatusType = OperationStatusType.Failed;
                    return false;
                }
            }
            catch (Exception ex)
            {
                LoggingService.Instance.Error("Kesalahan saat mengeksekusi model CAD", ex);
                Messages.Add(ConversationMessage.ErrorMessage("Terjadi kesalahan pembuatan model: " + ex.Message));
                OperationStatus = "Error: " + ex.Message;
                StatusType = OperationStatusType.Failed;
                return false;
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
