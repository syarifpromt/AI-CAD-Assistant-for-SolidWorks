using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows.Forms;
using System.Windows.Input;
using AICADAssistant.Core.Gemini;
using AICADAssistant.Core.Models;
using AICADAssistant.Core.Services;

namespace AICADAssistant.AddIn.UI.ViewModels
{
    public class SettingsViewModel : INotifyPropertyChanged
    {
        private readonly AppSettings _settings;
        private readonly CredentialService _credentialService;
        private readonly GeminiClient _geminiClient;

        private string _geminiModel;
        private string _defaultUnit;
        private string _outputDirectory;
        private int _requestTimeoutSeconds;
        private int _maxRetryAttempts;
        private string _apiKeyInput;
        private bool _hasApiKey;
        private string _connectionStatus;
        private bool _isTesting;

        public event PropertyChangedEventHandler PropertyChanged;

        public ObservableCollection<string> AvailableModels { get; set; }
        public ObservableCollection<string> AvailableUnits { get; set; }

        public string GeminiModel
        {
            get { return _geminiModel; }
            set { _geminiModel = value; OnPropertyChanged("GeminiModel"); }
        }

        public string DefaultUnit
        {
            get { return _defaultUnit; }
            set { _defaultUnit = value; OnPropertyChanged("DefaultUnit"); }
        }

        public string OutputDirectory
        {
            get { return _outputDirectory; }
            set { _outputDirectory = value; OnPropertyChanged("OutputDirectory"); }
        }

        public int RequestTimeoutSeconds
        {
            get { return _requestTimeoutSeconds; }
            set { _requestTimeoutSeconds = value; OnPropertyChanged("RequestTimeoutSeconds"); }
        }

        public int MaxRetryAttempts
        {
            get { return _maxRetryAttempts; }
            set { _maxRetryAttempts = value; OnPropertyChanged("MaxRetryAttempts"); }
        }

        public string ApiKeyInput
        {
            get { return _apiKeyInput; }
            set { _apiKeyInput = value; OnPropertyChanged("ApiKeyInput"); }
        }

        public bool HasApiKey
        {
            get { return _hasApiKey; }
            set { _hasApiKey = value; OnPropertyChanged("HasApiKey"); }
        }

        public string ConnectionStatus
        {
            get { return _connectionStatus; }
            set { _connectionStatus = value; OnPropertyChanged("ConnectionStatus"); }
        }

        public bool IsTesting
        {
            get { return _isTesting; }
            set { _isTesting = value; OnPropertyChanged("IsTesting"); }
        }

        public ICommand SaveCommand { get; private set; }
        public ICommand SaveApiKeyCommand { get; private set; }
        public ICommand DeleteApiKeyCommand { get; private set; }
        public ICommand TestConnectionCommand { get; private set; }
        public ICommand BrowseDirectoryCommand { get; private set; }

        public SettingsViewModel(AppSettings settings, CredentialService credentialService, GeminiClient geminiClient)
        {
            _settings = settings;
            _credentialService = credentialService;
            _geminiClient = geminiClient;

            AvailableModels = new ObservableCollection<string>
            {
                "gemini-1.5-pro",
                "gemini-1.5-flash",
                "gemini-2.0-flash"
            };

            AvailableUnits = new ObservableCollection<string>
            {
                "mm",
                "cm",
                "in"
            };

            GeminiModel = settings.GeminiModel;
            DefaultUnit = settings.DefaultUnit;
            OutputDirectory = string.IsNullOrWhiteSpace(settings.OutputDirectory)
                ? AppSettings.GetDefaultOutputDirectory()
                : settings.OutputDirectory;
            RequestTimeoutSeconds = settings.RequestTimeoutSeconds;
            MaxRetryAttempts = settings.MaxRetryAttempts;
            HasApiKey = _credentialService.HasApiKey();

            SaveCommand = new RelayCommand(o => SaveSettings());
            SaveApiKeyCommand = new RelayCommand(o => SaveApiKey());
            DeleteApiKeyCommand = new RelayCommand(o => DeleteApiKey());
            TestConnectionCommand = new RelayCommand(async o => await TestConnectionAsync());
            BrowseDirectoryCommand = new RelayCommand(o => BrowseDirectory());
        }

        public void SaveSettings()
        {
            _settings.GeminiModel = GeminiModel;
            _settings.DefaultUnit = DefaultUnit;
            _settings.OutputDirectory = OutputDirectory;
            _settings.RequestTimeoutSeconds = RequestTimeoutSeconds;
            _settings.MaxRetryAttempts = MaxRetryAttempts;
            _settings.Save();

            ConnectionStatus = "Pengaturan berhasil disimpan.";
        }

        public void SaveApiKey()
        {
            if (string.IsNullOrWhiteSpace(ApiKeyInput))
            {
                ConnectionStatus = "API Key tidak boleh kosong.";
                return;
            }

            _credentialService.SaveApiKey(ApiKeyInput.Trim());
            ApiKeyInput = string.Empty;
            HasApiKey = _credentialService.HasApiKey();
            ConnectionStatus = "API Key berhasil disimpan dengan enkripsi Windows DPAPI.";
        }

        public void DeleteApiKey()
        {
            _credentialService.DeleteApiKey();
            HasApiKey = false;
            ConnectionStatus = "API Key telah dihapus.";
        }

        public async System.Threading.Tasks.Task TestConnectionAsync()
        {
            IsTesting = true;
            ConnectionStatus = "Menghubungi Gemini API...";

            try
            {
                bool success = await _geminiClient.TestConnectionAsync();
                ConnectionStatus = success
                    ? "✓ Koneksi ke Google Gemini API Berhasil!"
                    : "✗ Gagal terhubung ke Gemini API. Periksa API Key dan internet Anda.";
            }
            catch (Exception ex)
            {
                ConnectionStatus = "Error koneksi: " + ex.Message;
            }
            finally
            {
                IsTesting = false;
            }
        }

        public void BrowseDirectory()
        {
            using (var dlg = new FolderBrowserDialog())
            {
                dlg.Description = "Pilih Direktori Penyimpanan CAD";
                dlg.SelectedPath = OutputDirectory;
                if (dlg.ShowDialog() == DialogResult.OK)
                {
                    OutputDirectory = dlg.SelectedPath;
                }
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
}
