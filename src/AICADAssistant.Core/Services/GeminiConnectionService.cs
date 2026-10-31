using System;

namespace AICADAssistant.Core.Services
{
    public enum GeminiConnectionStatus
    {
        Unconfigured = 0,
        ConfiguredUnverified = 1,
        Connecting = 2,
        Connected = 3,
        AuthenticationFailed = 4,
        RateLimited = 5,
        ModelUnavailable = 6,
        NetworkError = 7,
        ServiceUnavailable = 8
    }

    public class GeminiConnectionService
    {
        private static readonly object _lock = new object();
        private static GeminiConnectionService _instance;

        public static GeminiConnectionService Instance
        {
            get
            {
                if (_instance == null)
                {
                    lock (_lock)
                    {
                        if (_instance == null)
                        {
                            _instance = new GeminiConnectionService();
                        }
                    }
                }
                return _instance;
            }
        }

        private GeminiConnectionStatus _status;
        private string _activeModel;
        private long _lastResponseTimeMs;
        private DateTime? _lastConnectedTime;
        private string _lastErrorMessage;

        public event EventHandler StateChanged;

        public GeminiConnectionStatus Status
        {
            get { return _status; }
            private set
            {
                if (_status != value)
                {
                    _status = value;
                    OnStateChanged();
                }
            }
        }

        public string ActiveModel
        {
            get { return _activeModel; }
            set
            {
                if (_activeModel != value)
                {
                    _activeModel = value;
                    OnStateChanged();
                }
            }
        }

        public long LastResponseTimeMs
        {
            get { return _lastResponseTimeMs; }
            private set
            {
                if (_lastResponseTimeMs != value)
                {
                    _lastResponseTimeMs = value;
                    OnStateChanged();
                }
            }
        }

        public DateTime? LastConnectedTime
        {
            get { return _lastConnectedTime; }
            private set
            {
                _lastConnectedTime = value;
                OnStateChanged();
            }
        }

        public string LastErrorMessage
        {
            get { return _lastErrorMessage; }
            private set
            {
                _lastErrorMessage = value;
                OnStateChanged();
            }
        }

        public GeminiConnectionService()
        {
            _status = GeminiConnectionStatus.Unconfigured;
            _activeModel = "gemini-3.8-flash";
            _lastResponseTimeMs = 0;
            _lastErrorMessage = null;
        }

        public void Initialize(bool hasApiKey, string activeModel)
        {
            lock (_lock)
            {
                if (!string.IsNullOrWhiteSpace(activeModel))
                {
                    _activeModel = activeModel.Trim();
                }

                // Credential availability alone NEVER implies Connected; it must start as ConfiguredUnverified
                Status = hasApiKey ? GeminiConnectionStatus.ConfiguredUnverified : GeminiConnectionStatus.Unconfigured;
                _lastErrorMessage = null;
            }
        }

        public void ReportConnecting(string model = null)
        {
            lock (_lock)
            {
                if (!string.IsNullOrWhiteSpace(model))
                {
                    _activeModel = model.Trim();
                }
                Status = GeminiConnectionStatus.Connecting;
            }
        }

        public void ReportConnected(string model, long durationMs)
        {
            lock (_lock)
            {
                if (!string.IsNullOrWhiteSpace(model))
                {
                    _activeModel = model.Trim();
                }
                _lastResponseTimeMs = durationMs;
                _lastConnectedTime = DateTime.Now;
                _lastErrorMessage = null;
                Status = GeminiConnectionStatus.Connected;
            }
        }

        public void ReportError(Exception ex, int statusCode, string model = null, long durationMs = 0)
        {
            lock (_lock)
            {
                if (!string.IsNullOrWhiteSpace(model))
                {
                    _activeModel = model.Trim();
                }
                _lastResponseTimeMs = durationMs;

                string rawMessage = ex != null ? ex.Message : "HTTP " + statusCode;
                _lastErrorMessage = LoggingService.RedactApiKey(rawMessage);

                if (statusCode == 401 || statusCode == 403)
                {
                    Status = GeminiConnectionStatus.AuthenticationFailed;
                }
                else if (statusCode == 429)
                {
                    Status = GeminiConnectionStatus.RateLimited;
                }
                else if (statusCode == 404)
                {
                    Status = GeminiConnectionStatus.ModelUnavailable;
                }
                else if (statusCode == 503 || statusCode == 500 || statusCode == 502)
                {
                    Status = GeminiConnectionStatus.ServiceUnavailable;
                }
                else if (ex is System.Net.Http.HttpRequestException || rawMessage.IndexOf("network", StringComparison.OrdinalIgnoreCase) >= 0 || rawMessage.IndexOf("koneksi", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    Status = GeminiConnectionStatus.NetworkError;
                }
                else
                {
                    Status = GeminiConnectionStatus.ServiceUnavailable;
                }
            }
        }

        public string GetStatusLabel()
        {
            switch (_status)
            {
                case GeminiConnectionStatus.Connected:
                    return _lastResponseTimeMs > 0
                        ? string.Format("Terhubung ({0} • {1:N0} ms)", _activeModel, _lastResponseTimeMs)
                        : string.Format("Terhubung ({0})", _activeModel);

                case GeminiConnectionStatus.Connecting:
                    return string.Format("Menghubungi ({0})...", _activeModel);

                case GeminiConnectionStatus.ConfiguredUnverified:
                    return string.Format("Terkonfigurasi (Belum Terverifikasi • {0})", _activeModel);

                case GeminiConnectionStatus.AuthenticationFailed:
                    return "Autentikasi Gagal (Periksa API Key)";

                case GeminiConnectionStatus.RateLimited:
                    return "Batas Kuota Tercapai (429)";

                case GeminiConnectionStatus.ModelUnavailable:
                    return string.Format("Model Tidak Tersedia ({0})", _activeModel);

                case GeminiConnectionStatus.NetworkError:
                    return "Kesalahan Jaringan / Internet";

                case GeminiConnectionStatus.ServiceUnavailable:
                    return "Layanan Gemini Tidak Tersedia (503)";

                case GeminiConnectionStatus.Unconfigured:
                default:
                    return "API Key Belum Dikonfigurasi";
            }
        }

        private void OnStateChanged()
        {
            var handler = StateChanged;
            if (handler != null)
            {
                try { handler(this, EventArgs.Empty); } catch { }
            }
        }
    }
}

