using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using AICADAssistant.Core.Models;
using AICADAssistant.Core.Services;

namespace AICADAssistant.Core.Gemini
{
    public class ConnectionTestResult
    {
        public bool Success { get; private set; }
        public string Message { get; private set; }
        public int? StatusCode { get; private set; }

        public ConnectionTestResult(bool success, string message, int? statusCode = null)
        {
            Success = success;
            Message = message;
            StatusCode = statusCode;
        }
    }

    public class GeminiClient
    {
        private static readonly HttpClient DefaultHttpClient = new HttpClient(new HttpClientHandler { UseProxy = false })
        {
            Timeout = System.Threading.Timeout.InfiniteTimeSpan
        };
        private readonly HttpClient _httpClient;
        private readonly CredentialService _credentialService;
        private readonly AppSettings _settings;
        private readonly JavaScriptSerializer _serializer;

        public string CurrentModel
        {
            get
            {
                if (_settings == null || string.IsNullOrWhiteSpace(_settings.GeminiModel))
                    return AppSettings.DefaultGeminiModel;
                return _settings.GeminiModel.Trim();
            }
        }

        public GeminiClient(CredentialService credentialService, AppSettings settings)
            : this(credentialService, settings, (HttpMessageHandler)null)
        {
        }

        public GeminiClient(CredentialService credentialService, AppSettings settings, HttpMessageHandler handler)
        {
            if (credentialService == null)
                throw new ArgumentNullException("credentialService");
            if (settings == null)
                throw new ArgumentNullException("settings");

            _credentialService = credentialService;
            _settings = settings;
            _serializer = new JavaScriptSerializer
            {
                MaxJsonLength = 50 * 1024 * 1024
            };

            if (handler != null)
            {
                _httpClient = new HttpClient(handler, disposeHandler: true)
                {
                    Timeout = System.Threading.Timeout.InfiniteTimeSpan
                };
            }
            else
            {
                _httpClient = DefaultHttpClient;
            }

            // Initialize connection indicator with configured status
            bool hasKey = !string.IsNullOrWhiteSpace(_credentialService.LoadApiKey());
            GeminiConnectionService.Instance.Initialize(hasKey, CurrentModel);
        }

        public GeminiClient(CredentialService credentialService, AppSettings settings, HttpClient httpClient)
        {
            if (credentialService == null)
                throw new ArgumentNullException("credentialService");
            if (settings == null)
                throw new ArgumentNullException("settings");

            _credentialService = credentialService;
            _settings = settings;
            _serializer = new JavaScriptSerializer
            {
                MaxJsonLength = 50 * 1024 * 1024
            };

            _httpClient = httpClient ?? DefaultHttpClient;

            bool hasKey = !string.IsNullOrWhiteSpace(_credentialService.LoadApiKey());
            GeminiConnectionService.Instance.Initialize(hasKey, CurrentModel);
        }

        public async Task<bool> TestConnectionAsync(CancellationToken ct = default(CancellationToken))
        {
            var result = await TestConnectionDetailedAsync(ct).ConfigureAwait(false);
            return result.Success;
        }

        public async Task<ConnectionTestResult> TestConnectionDetailedAsync(CancellationToken ct = default(CancellationToken))
        {
            string apiKey = _credentialService.LoadApiKey();
            if (string.IsNullOrWhiteSpace(apiKey))
            {
                return new ConnectionTestResult(false, "API Key belum diatur. Silakan masukkan API Key Gemini Anda.");
            }

            try
            {
                string prompt = "Halo, respon dengan kata OK.";
                var request = CreateBaseRequest();
                var userContent = new GeminiContent();
                userContent.role = "user";
                userContent.parts.Add(new GeminiPart { text = prompt });
                request.contents.Add(userContent);

                string jsonBody = SerializeRequest(request);
                string responseJson = await ExecuteRequestAsync(jsonBody, ct).ConfigureAwait(false);
                return new ConnectionTestResult(true, string.Format("✓ Koneksi ke Google Gemini API Berhasil! Model aktif: '{0}'.", CurrentModel));
            }
            catch (GeminiException ex)
            {
                return new ConnectionTestResult(false, "✗ " + ex.Message, ex.StatusCode);
            }
            catch (Exception ex)
            {
                string safeMsg = LoggingService.RedactApiKey(ex.Message);
                return new ConnectionTestResult(false, "✗ Gagal terhubung: " + safeMsg);
            }
        }

        public async Task<string> SendChatAsync(string prompt, List<ConversationMessage> history, CancellationToken ct = default(CancellationToken))
        {
            var request = new GeminiTextRequest();
            request.generationConfig.temperature = 0.7;
            request.generationConfig.maxOutputTokens = 4096;
            request.generationConfig.responseMimeType = null; // Plain text / markdown (Conversational Fast Lane)
            request.generationConfig.thinkingLevel = "low"; // Conversational: low thinking

            var sysInstruction = new GeminiContent();
            sysInstruction.role = "system";
            sysInstruction.parts.Add(new GeminiPart
            {
                text = "Kamu adalah asisten percakapan AI CAD Assistant for SOLIDWORKS yang ramah, profesional, dan berpengetahuan teknik tinggi. " +
                       "Berikan jawaban dan penjelasan teknis dalam bahasa Indonesia yang jelas, ringkas, terstruktur, dan mudah dibaca dalam format teks atau markdown biasa. " +
                       "JANGAN membalas dalam format JSON atau CadSpec untuk pertanyaan percakapan umum."
            });
            request.systemInstruction = sysInstruction;

            // Append history if present (limit to last 4 messages to reduce payload and latency)
            if (history != null && history.Count > 0)
            {
                // Ensure history only contains messages prior to the current prompt.
                // If caller passed a collection whose last element is the current prompt, exclude it to prevent duplicate messages.
                int historyCount = history.Count;
                if (historyCount > 0 &&
                    history[historyCount - 1].Role == MessageRole.User &&
                    string.Equals(history[historyCount - 1].Content, prompt, StringComparison.Ordinal))
                {
                    historyCount--;
                }

                int startIdx = Math.Max(0, historyCount - 4);
                for (int i = startIdx; i < historyCount; i++)
                {
                    var msg = history[i];
                    if (msg.Type == MessageType.Error) continue;
                    string role = msg.Role == MessageRole.User ? "user" : "model";
                    var historyContent = new GeminiContent();
                    historyContent.role = role;
                    historyContent.parts.Add(new GeminiPart { text = msg.Content });
                    request.contents.Add(historyContent);
                }
            }

            // Append current prompt (exactly once)
            var currentContent = new GeminiContent();
            currentContent.role = "user";
            currentContent.parts.Add(new GeminiPart { text = prompt });
            request.contents.Add(currentContent);

            GeminiConnectionService.Instance.ReportConnecting(CurrentModel);
            string jsonBody = SerializeRequest(request);
            string responseJson = await ExecuteRequestAsync(jsonBody, ct).ConfigureAwait(false);
            return ParseResponseText(responseJson);
        }

        public async Task<string> SendCadPlanAsync(string prompt, ThinkingLevel thinkingLevel = ThinkingLevel.Low, CancellationToken ct = default(CancellationToken))
        {
            var request = new GeminiTextRequest();
            request.generationConfig.temperature = 0.1;
            request.generationConfig.maxOutputTokens = 8192;
            request.generationConfig.responseMimeType = "application/json"; // Structured JSON for CAD Planner
            request.generationConfig.thinkingLevel = AdaptiveThinkingManager.ToApiString(thinkingLevel);

            var sysInstruction = new GeminiContent();
            sysInstruction.role = "system";
            sysInstruction.parts.Add(new GeminiPart
            {
                text = "Kamu adalah AI CAD Assistant untuk SOLIDWORKS. " +
                       "Tugasmu adalah menganalisis teks instruksi atau gambar teknik dari pengguna, " +
                       "mengidentifikasi dimensi, satuan, dan fitur geometri, " +
                       "serta menyusun rencana CAD terstruktur (CadSpec) dalam format JSON murni. " +
                       "JANGAN mengarang dimensi yang tidak ada. Jika ada dimensi kritis yang hilang, cantumkan di array 'unknowns'. " +
                       "Gunakan satuan mm secara default."
            });
            request.systemInstruction = sysInstruction;

            var currentContent = new GeminiContent();
            currentContent.role = "user";
            currentContent.parts.Add(new GeminiPart { text = prompt });
            request.contents.Add(currentContent);

            GeminiConnectionService.Instance.ReportConnecting(CurrentModel);
            string jsonBody = SerializeRequest(request);
            string responseJson = await ExecuteRequestAsync(jsonBody, ct).ConfigureAwait(false);
            return ParseResponseText(responseJson);
        }

        public async Task<string> SendTextAsync(string prompt, List<ConversationMessage> history, CancellationToken ct = default(CancellationToken))
        {
            if (history != null && history.Count > 0)
            {
                return await SendChatAsync(prompt, history, ct).ConfigureAwait(false);
            }
            ThinkingLevel level = AdaptiveThinkingManager.DetermineLevel(prompt, false);
            return await SendCadPlanAsync(prompt, level, ct).ConfigureAwait(false);
        }

        public async Task<string> SendWithImageAsync(string prompt, string base64ImageData, string mimeType, CancellationToken ct = default(CancellationToken))
        {
            var request = CreateBaseRequest();
            var content = new GeminiContent();
            content.role = "user";

            // Add media part
            content.parts.Add(new GeminiPart
            {
                inline_data = new GeminiInlineData
                {
                    mime_type = mimeType ?? "image/png",
                    data = base64ImageData
                }
            });

            // Add text prompt part
            content.parts.Add(new GeminiPart { text = prompt });
            request.contents.Add(content);

            string jsonBody = SerializeRequest(request);
            string responseJson = await ExecuteRequestAsync(jsonBody, ct).ConfigureAwait(false);
            return ParseResponseText(responseJson);
        }

        public async Task<string> SendWithPdfAsync(string prompt, string base64PdfData, CancellationToken ct = default(CancellationToken))
        {
            return await SendWithImageAsync(prompt, base64PdfData, "application/pdf", ct).ConfigureAwait(false);
        }

        private GeminiTextRequest CreateBaseRequest()
        {
            var req = new GeminiTextRequest();
            req.generationConfig.temperature = 0.1;
            req.generationConfig.maxOutputTokens = 8192;
            req.generationConfig.responseMimeType = "application/json";

            var sysInstruction = new GeminiContent();
            sysInstruction.role = "system";
            sysInstruction.parts.Add(new GeminiPart
            {
                text = "Kamu adalah AI CAD Assistant untuk SOLIDWORKS. " +
                       "Tugasmu adalah menganalisis teks instruksi atau gambar teknik dari pengguna, " +
                       "mengidentifikasi dimensi, satuan, dan fitur geometri, " +
                       "serta menyusun rencana CAD terstruktur (CadSpec) dalam format JSON murni. " +
                       "JANGAN mengarang dimensi yang tidak ada. Jika ada dimensi kritis yang hilang, cantumkan di array 'unknowns'. " +
                       "Gunakan satuan mm secara default."
            });
            req.systemInstruction = sysInstruction;
            return req;
        }

        private string SerializeRequest(GeminiTextRequest request)
        {
            var rootDict = new Dictionary<string, object>();

            // contents
            var contentsList = new List<object>();
            if (request.contents != null)
            {
                for (int i = 0; i < request.contents.Count; i++)
                {
                    var c = request.contents[i];
                    var cDict = new Dictionary<string, object>();
                    cDict["role"] = c.role;

                    var partsList = new List<object>();
                    if (c.parts != null)
                    {
                        for (int j = 0; j < c.parts.Count; j++)
                        {
                            var p = c.parts[j];
                            var pDict = new Dictionary<string, object>();
                            if (p.inline_data != null)
                            {
                                var idDict = new Dictionary<string, object>();
                                idDict["mime_type"] = p.inline_data.mime_type;
                                idDict["data"] = p.inline_data.data;
                                pDict["inline_data"] = idDict;
                            }
                            if (p.text != null)
                            {
                                pDict["text"] = p.text;
                            }
                            partsList.Add(pDict);
                        }
                    }
                    cDict["parts"] = partsList;
                    contentsList.Add(cDict);
                }
            }
            rootDict["contents"] = contentsList;

            // systemInstruction
            if (request.systemInstruction != null && request.systemInstruction.parts != null && request.systemInstruction.parts.Count > 0)
            {
                var sDict = new Dictionary<string, object>();
                sDict["role"] = request.systemInstruction.role;
                var sParts = new List<object>();
                for (int i = 0; i < request.systemInstruction.parts.Count; i++)
                {
                    var p = request.systemInstruction.parts[i];
                    var pDict = new Dictionary<string, object>();
                    pDict["text"] = p.text;
                    sParts.Add(pDict);
                }
                sDict["parts"] = sParts;
                rootDict["systemInstruction"] = sDict;
            }

            // generationConfig
            if (request.generationConfig != null)
            {
                var gDict = new Dictionary<string, object>();
                gDict["temperature"] = request.generationConfig.temperature;
                gDict["maxOutputTokens"] = request.generationConfig.maxOutputTokens;
                if (!string.IsNullOrEmpty(request.generationConfig.responseMimeType))
                {
                    gDict["responseMimeType"] = request.generationConfig.responseMimeType;
                }

                // Adaptive thinking level (low / medium / high)
                string thinkingLevel = !string.IsNullOrEmpty(request.generationConfig.thinkingLevel)
                    ? request.generationConfig.thinkingLevel
                    : "low";

                if (CurrentModel.IndexOf("flash-lite", StringComparison.OrdinalIgnoreCase) >= 0 && thinkingLevel == "low")
                {
                    thinkingLevel = "minimal";
                }

                var tDict = new Dictionary<string, object>();
                tDict["thinkingLevel"] = thinkingLevel;
                gDict["thinkingConfig"] = tDict;

                rootDict["generationConfig"] = gDict;
            }

            return _serializer.Serialize(rootDict);
        }

        private string BuildApiUrl(string modelOverride = null)
        {
            string apiKey = _credentialService.LoadApiKey();
            if (string.IsNullOrWhiteSpace(apiKey))
            {
                throw GeminiException.AuthException("API Key belum disetel.");
            }

            string model = !string.IsNullOrWhiteSpace(modelOverride) ? modelOverride.Trim() : CurrentModel;
            return string.Format("https://generativelanguage.googleapis.com/v1beta/models/{0}:generateContent?key={1}", model, apiKey);
        }

        private string RedactSensitiveData(string input)
        {
            if (string.IsNullOrEmpty(input))
                return input;

            string sanitized = LoggingService.RedactApiKey(input);
            try
            {
                string currentKey = _credentialService != null ? _credentialService.LoadApiKey() : null;
                if (!string.IsNullOrWhiteSpace(currentKey) && currentKey.Length >= 6)
                {
                    sanitized = sanitized.Replace(currentKey, "[REDACTED_API_KEY]");
                }
            }
            catch { }
            return sanitized;
        }

        private async Task<string> ExecuteRequestAsync(string jsonBody, CancellationToken ct)
        {
            int timeoutSec = _settings != null && _settings.RequestTimeoutSeconds > 0
                ? _settings.RequestTimeoutSeconds
                : 120;

            int maxRetries = Math.Min(2, Math.Max(0, _settings != null ? _settings.MaxRetryAttempts : 2));
            int attempt = 0;
            string currentActiveModel = CurrentModel;
            bool triedFallback = false;

            var overallSw = System.Diagnostics.Stopwatch.StartNew();

            using (var timeoutCts = new CancellationTokenSource())
            using (var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(ct, timeoutCts.Token))
            {
                timeoutCts.CancelAfter(TimeSpan.FromSeconds(timeoutSec));
                CancellationToken effectiveToken = linkedCts.Token;

                while (true)
                {
                    attempt++;
                    string url = BuildApiUrl(currentActiveModel);
                    bool shouldRetry = false;
                    int retryDelayMs = 0;

                    LoggingService.Instance.Info(string.Format("[GEMINI_REQUEST] Mengirim request ke model '{0}' (Percobaan {1}/{2})...",
                        currentActiveModel, attempt, maxRetries + 1));

                    var attemptSw = System.Diagnostics.Stopwatch.StartNew();
                    try
                    {
                        using (var content = new StringContent(jsonBody, Encoding.UTF8, "application/json"))
                        using (var response = await _httpClient.PostAsync(url, content, effectiveToken).ConfigureAwait(false))
                        {
                            attemptSw.Stop();

                            // ── Deadline Check 1: Immediately after PostAsync finishes ──
                            if (ct.IsCancellationRequested)
                            {
                                overallSw.Stop();
                                LoggingService.Instance.Info(string.Format("[GEMINI_CANCEL] Permintaan dibatalkan oleh pengguna setelah PostAsync (elapsed: {0} ms).", overallSw.ElapsedMilliseconds));
                                throw new OperationCanceledException(ct);
                            }
                            if (timeoutCts.IsCancellationRequested)
                            {
                                overallSw.Stop();
                                string msg = string.Format("Permintaan ke Gemini API melebihi batas waktu ({0} detik) untuk model '{1}'.", timeoutSec, currentActiveModel);
                                LoggingService.Instance.Warning(string.Format("[GEMINI_TIMEOUT] {0} Durasi: {1} ms.", msg, overallSw.ElapsedMilliseconds));
                                GeminiConnectionService.Instance.ReportError(new Exception(msg), 408, currentActiveModel, overallSw.ElapsedMilliseconds);
                                throw GeminiException.TimeoutException(msg);
                            }

                            if (response.IsSuccessStatusCode)
                            {
                                string responseJson = await response.Content.ReadAsStringAsync().ConfigureAwait(false);

                                // ── Deadline Check 2: Immediately after reading response body ──
                                if (ct.IsCancellationRequested)
                                {
                                    overallSw.Stop();
                                    LoggingService.Instance.Info(string.Format("[GEMINI_CANCEL] Permintaan dibatalkan oleh pengguna setelah membaca respons (elapsed: {0} ms).", overallSw.ElapsedMilliseconds));
                                    throw new OperationCanceledException(ct);
                                }
                                if (timeoutCts.IsCancellationRequested)
                                {
                                    overallSw.Stop();
                                    string msg = string.Format("Permintaan ke Gemini API melebihi batas waktu ({0} detik) untuk model '{1}'.", timeoutSec, currentActiveModel);
                                    LoggingService.Instance.Warning(string.Format("[GEMINI_TIMEOUT] {0} Durasi: {1} ms.", msg, overallSw.ElapsedMilliseconds));
                                    GeminiConnectionService.Instance.ReportError(new Exception(msg), 408, currentActiveModel, overallSw.ElapsedMilliseconds);
                                    throw GeminiException.TimeoutException(msg);
                                }

                                overallSw.Stop();
                                int promptTokens = 0, candidateTokens = 0, thoughtTokens = 0, totalTokens = 0;
                                try
                                {
                                    var parsedResp = _serializer.Deserialize<GeminiResponse>(responseJson);
                                    if (parsedResp != null && parsedResp.usageMetadata != null)
                                    {
                                        promptTokens = parsedResp.usageMetadata.promptTokenCount;
                                        candidateTokens = parsedResp.usageMetadata.candidatesTokenCount;
                                        thoughtTokens = parsedResp.usageMetadata.thoughtsTokenCount;
                                        totalTokens = parsedResp.usageMetadata.totalTokenCount > 0
                                            ? parsedResp.usageMetadata.totalTokenCount
                                            : (promptTokens + candidateTokens + thoughtTokens);
                                    }
                                }
                                catch { }

                                LoggingService.Instance.Info(string.Format("[GEMINI_RESPONSE] Berhasil diterima dalam {0} ms dari model '{1}'. Status: {2}, PromptTokens: {3}, CandidateTokens: {4}, ThoughtTokens: {5}, TotalTokens: {6}",
                                    overallSw.ElapsedMilliseconds, currentActiveModel, (int)response.StatusCode, promptTokens, candidateTokens, thoughtTokens, totalTokens));

                                GeminiConnectionService.Instance.ReportConnected(currentActiveModel, overallSw.ElapsedMilliseconds);
                                return responseJson;
                            }

                            int statusCode = (int)response.StatusCode;
                            string rawErrorContent = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                            string errorContent = RedactSensitiveData(rawErrorContent);

                            // Parse structured Gemini error response if possible
                            string serverMessage = null;
                            string serverStatus = null;
                            try
                            {
                                var errResp = _serializer.Deserialize<GeminiErrorResponse>(rawErrorContent);
                                if (errResp != null && errResp.error != null)
                                {
                                    serverMessage = RedactSensitiveData(errResp.error.message);
                                    serverStatus = errResp.error.status;
                                }
                            }
                            catch { }

                            LoggingService.Instance.Warning(string.Format("[GEMINI_ERROR] HTTP {0} dalam {1} ms: {2}",
                                statusCode, attemptSw.ElapsedMilliseconds, errorContent));

                            // 404: Model Not Found
                            if (statusCode == 404 || string.Equals(serverStatus, "NOT_FOUND", StringComparison.OrdinalIgnoreCase))
                            {
                                overallSw.Stop();
                                string detail = serverMessage ?? errorContent;
                                GeminiConnectionService.Instance.ReportError(new Exception(detail), 404, currentActiveModel, overallSw.ElapsedMilliseconds);
                                throw GeminiException.ModelNotFoundException(currentActiveModel, detail);
                            }

                            // 401/403: Authentication or Permission error
                            if (statusCode == 401 || statusCode == 403 ||
                                string.Equals(serverStatus, "UNAUTHENTICATED", StringComparison.OrdinalIgnoreCase) ||
                                string.Equals(serverStatus, "PERMISSION_DENIED", StringComparison.OrdinalIgnoreCase))
                            {
                                overallSw.Stop();
                                string detail = serverMessage ?? "Kredensial atau hak akses ditolak.";
                                GeminiConnectionService.Instance.ReportError(new Exception(detail), statusCode, currentActiveModel, overallSw.ElapsedMilliseconds);
                                throw GeminiException.AuthException(detail);
                            }

                            // 429: Rate Limit / Quota Exceeded (e.g. daily free limit exceeded on 3.8-flash)
                            if (statusCode == 429 || string.Equals(serverStatus, "RESOURCE_EXHAUSTED", StringComparison.OrdinalIgnoreCase))
                            {
                                if (!triedFallback && currentActiveModel != AppSettings.DefaultGeminiModel && !timeoutCts.IsCancellationRequested && !ct.IsCancellationRequested)
                                {
                                    LoggingService.Instance.Warning(string.Format("Model '{0}' mencapai limit kuota harian (429). Beralih otomatis ke model fallback '{1}'...",
                                        currentActiveModel, AppSettings.DefaultGeminiModel));
                                    currentActiveModel = AppSettings.DefaultGeminiModel;
                                    triedFallback = true;
                                    shouldRetry = true;
                                    retryDelayMs = 1000;
                                }
                                else if (attempt <= maxRetries && !timeoutCts.IsCancellationRequested && !ct.IsCancellationRequested)
                                {
                                    LoggingService.Instance.Warning(string.Format("Rate limit Gemini tercapai untuk model '{0}'. Mencoba ulang ({1}/{2})...", currentActiveModel, attempt, maxRetries));
                                    shouldRetry = true;
                                    retryDelayMs = 2000 * attempt;
                                }
                                else
                                {
                                    overallSw.Stop();
                                    string detail = serverMessage ?? "Rate limit exceeded";
                                    GeminiConnectionService.Instance.ReportError(new Exception(detail), 429, currentActiveModel, overallSw.ElapsedMilliseconds);
                                    throw GeminiException.RateLimitException(detail);
                                }
                            }
                            // 503: High demand / Server unavailable - Attempt fallback model if not already attempted
                            else if (statusCode == 503 || string.Equals(serverStatus, "UNAVAILABLE", StringComparison.OrdinalIgnoreCase))
                            {
                                if (!triedFallback && currentActiveModel != AppSettings.DefaultGeminiModel && !timeoutCts.IsCancellationRequested && !ct.IsCancellationRequested)
                                {
                                    LoggingService.Instance.Warning(string.Format("Model '{0}' 503 High Demand. Beralih otomatis ke model fallback '{1}'...",
                                        currentActiveModel, AppSettings.DefaultGeminiModel));
                                    currentActiveModel = AppSettings.DefaultGeminiModel;
                                    triedFallback = true;
                                    shouldRetry = true;
                                    retryDelayMs = 1000;
                                }
                                else if (attempt <= maxRetries && !timeoutCts.IsCancellationRequested && !ct.IsCancellationRequested)
                                {
                                    LoggingService.Instance.Warning(string.Format("Server Gemini mengalami gangguan (503). Mencoba ulang ({1}/{2})...", attempt, maxRetries));
                                    shouldRetry = true;
                                    retryDelayMs = 2000 * attempt;
                                }
                                else
                                {
                                    overallSw.Stop();
                                    string detail = serverMessage ?? "High demand";
                                    GeminiConnectionService.Instance.ReportError(new Exception(detail), 503, currentActiveModel, overallSw.ElapsedMilliseconds);
                                    throw GeminiException.ServerUnavailableException(currentActiveModel, detail);
                                }
                            }
                            // Other 5xx: Server Error
                            else if (statusCode >= 500 && attempt <= maxRetries && !timeoutCts.IsCancellationRequested && !ct.IsCancellationRequested)
                            {
                                LoggingService.Instance.Warning(string.Format("Server Gemini mengalami gangguan ({0}). Mencoba ulang ({1}/{2})...", statusCode, attempt, maxRetries));
                                shouldRetry = true;
                                retryDelayMs = 2000 * attempt;
                            }
                            else
                            {
                                overallSw.Stop();
                                string detail = serverMessage ?? errorContent;
                                GeminiConnectionService.Instance.ReportError(new Exception(detail), statusCode, currentActiveModel, overallSw.ElapsedMilliseconds);
                                throw new GeminiException(string.Format("Gemini API Error ({0}): {1}", statusCode, detail), statusCode, serverStatus: serverStatus);
                            }
                        }
                    }
                    catch (OperationCanceledException)
                    {
                        attemptSw.Stop();
                        overallSw.Stop();

                        // Check if user cancellation was requested
                        if (ct.IsCancellationRequested)
                        {
                            LoggingService.Instance.Info(string.Format("[GEMINI_CANCEL] Permintaan dibatalkan oleh pengguna (elapsed: {0} ms).", overallSw.ElapsedMilliseconds));
                            throw;
                        }

                        // Check if request timeout expired
                        if (timeoutCts.IsCancellationRequested)
                        {
                            string msg = string.Format("Permintaan ke Gemini API melebihi batas waktu ({0} detik) untuk model '{1}'.", timeoutSec, currentActiveModel);
                            LoggingService.Instance.Warning(string.Format("[GEMINI_TIMEOUT] {0} Durasi: {1} ms.", msg, overallSw.ElapsedMilliseconds));
                            GeminiConnectionService.Instance.ReportError(new Exception(msg), 408, currentActiveModel, overallSw.ElapsedMilliseconds);
                            throw GeminiException.TimeoutException(msg);
                        }

                        throw;
                    }
                    catch (HttpRequestException ex)
                    {
                        attemptSw.Stop();

                        // 1. User cancellation: Highest priority
                        if (ct.IsCancellationRequested)
                        {
                            overallSw.Stop();
                            LoggingService.Instance.Info(string.Format("[GEMINI_CANCEL] Permintaan dibatalkan oleh pengguna (elapsed: {0} ms).", overallSw.ElapsedMilliseconds));
                            throw new OperationCanceledException(ct);
                        }

                        // 2. Request timeout: Second priority
                        if (timeoutCts.IsCancellationRequested)
                        {
                            overallSw.Stop();
                            string msg = string.Format("Permintaan ke Gemini API melebihi batas waktu ({0} detik) untuk model '{1}'.", timeoutSec, currentActiveModel);
                            LoggingService.Instance.Warning(string.Format("[GEMINI_TIMEOUT] {0} Durasi: {1} ms.", msg, overallSw.ElapsedMilliseconds));
                            GeminiConnectionService.Instance.ReportError(new Exception(msg), 408, currentActiveModel, overallSw.ElapsedMilliseconds);
                            throw GeminiException.TimeoutException(msg);
                        }

                        // 3. Network retry or network failure: Third priority
                        if (attempt <= maxRetries && !timeoutCts.IsCancellationRequested && !ct.IsCancellationRequested)
                        {
                            shouldRetry = true;
                            retryDelayMs = 1500 * attempt;
                        }
                        else
                        {
                            overallSw.Stop();
                            string safeMsg = RedactSensitiveData(ex.Message);
                            LoggingService.Instance.Error(string.Format("[GEMINI_NETWORK_ERROR] Gagal menghubungi server: {0}", safeMsg));
                            GeminiConnectionService.Instance.ReportError(new Exception(safeMsg), 0, currentActiveModel, overallSw.ElapsedMilliseconds);
                            throw GeminiException.NetworkException(safeMsg);
                        }
                    }

                    if (shouldRetry)
                    {
                        // Deadline check: do NOT retry if timeout or cancellation was triggered
                        if (ct.IsCancellationRequested)
                        {
                            overallSw.Stop();
                            LoggingService.Instance.Info(string.Format("[GEMINI_CANCEL] Permintaan dibatalkan oleh pengguna (elapsed: {0} ms).", overallSw.ElapsedMilliseconds));
                            throw new OperationCanceledException(ct);
                        }
                        if (timeoutCts.IsCancellationRequested)
                        {
                            overallSw.Stop();
                            string msg = string.Format("Permintaan ke Gemini API melebihi batas waktu ({0} detik) untuk model '{1}'.", timeoutSec, currentActiveModel);
                            LoggingService.Instance.Warning(string.Format("[GEMINI_TIMEOUT] {0} Durasi: {1} ms.", msg, overallSw.ElapsedMilliseconds));
                            GeminiConnectionService.Instance.ReportError(new Exception(msg), 408, currentActiveModel, overallSw.ElapsedMilliseconds);
                            throw GeminiException.TimeoutException(msg);
                        }

                        try
                        {
                            await Task.Delay(retryDelayMs, effectiveToken).ConfigureAwait(false);
                            continue;
                        }
                        catch (OperationCanceledException)
                        {
                            overallSw.Stop();
                            if (ct.IsCancellationRequested)
                            {
                                LoggingService.Instance.Info(string.Format("[GEMINI_CANCEL] Permintaan dibatalkan oleh pengguna saat jeda retry (elapsed: {0} ms).", overallSw.ElapsedMilliseconds));
                                throw;
                            }
                            if (timeoutCts.IsCancellationRequested)
                            {
                                string msg = string.Format("Permintaan ke Gemini API melebihi batas waktu ({0} detik) saat jeda retry untuk model '{1}'.", timeoutSec, currentActiveModel);
                                LoggingService.Instance.Warning(string.Format("[GEMINI_TIMEOUT] {0} Durasi: {1} ms.", msg, overallSw.ElapsedMilliseconds));
                                GeminiConnectionService.Instance.ReportError(new Exception(msg), 408, currentActiveModel, overallSw.ElapsedMilliseconds);
                                throw GeminiException.TimeoutException(msg);
                            }
                            throw;
                        }
                    }
                }
            }
        }

        private string ParseResponseText(string responseJson)
        {
            if (string.IsNullOrWhiteSpace(responseJson))
                throw GeminiException.InvalidResponseException("Respons kosong dari server.");

            try
            {
                var resp = _serializer.Deserialize<GeminiResponse>(responseJson);
                string text = resp != null ? resp.GetFirstText() : null;
                if (string.IsNullOrWhiteSpace(text))
                {
                    throw GeminiException.InvalidResponseException("Tidak ada teks dalam kandidat respons Gemini.");
                }
                return text;
            }
            catch (GeminiException)
            {
                throw;
            }
            catch (Exception ex)
            {
                throw GeminiException.InvalidResponseException(ex.Message);
            }
        }
    }
}
