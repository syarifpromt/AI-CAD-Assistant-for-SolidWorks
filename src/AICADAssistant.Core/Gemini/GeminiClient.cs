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
        private static readonly HttpClient HttpClient = new HttpClient();
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

        public async Task<string> SendTextAsync(string prompt, List<ConversationMessage> history, CancellationToken ct = default(CancellationToken))
        {
            var request = CreateBaseRequest();

            // Append history if present
            if (history != null)
            {
                for (int i = 0; i < history.Count; i++)
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

            // Append current prompt
            var currentContent = new GeminiContent();
            currentContent.role = "user";
            currentContent.parts.Add(new GeminiPart { text = prompt });
            request.contents.Add(currentContent);

            string jsonBody = SerializeRequest(request);
            string responseJson = await ExecuteRequestAsync(jsonBody, ct).ConfigureAwait(false);
            return ParseResponseText(responseJson);
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
                gDict["responseMimeType"] = request.generationConfig.responseMimeType;
                rootDict["generationConfig"] = gDict;
            }

            return _serializer.Serialize(rootDict);
        }

        private string BuildApiUrl()
        {
            string apiKey = _credentialService.LoadApiKey();
            if (string.IsNullOrWhiteSpace(apiKey))
            {
                throw GeminiException.AuthException("API Key belum disetel.");
            }

            string model = CurrentModel;
            return string.Format("https://generativelanguage.googleapis.com/v1beta/models/{0}:generateContent?key={1}", model, apiKey);
        }

        private async Task<string> ExecuteRequestAsync(string jsonBody, CancellationToken ct)
        {
            int maxRetries = Math.Max(0, _settings.MaxRetryAttempts);
            int attempt = 0;

            while (true)
            {
                attempt++;
                string url = BuildApiUrl();
                bool shouldRetry = false;
                int retryDelayMs = 0;

                try
                {
                    using (var content = new StringContent(jsonBody, Encoding.UTF8, "application/json"))
                    using (var timeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(_settings.RequestTimeoutSeconds)))
                    using (var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(ct, timeoutCts.Token))
                    {
                        var response = await HttpClient.PostAsync(url, content, linkedCts.Token).ConfigureAwait(false);

                        if (response.IsSuccessStatusCode)
                        {
                            return await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                        }

                        int statusCode = (int)response.StatusCode;
                        string errorContent = await response.Content.ReadAsStringAsync().ConfigureAwait(false);

                        // Parse structured Gemini error response if possible
                        string serverMessage = null;
                        string serverStatus = null;
                        try
                        {
                            var errResp = _serializer.Deserialize<GeminiErrorResponse>(errorContent);
                            if (errResp != null && errResp.error != null)
                            {
                                serverMessage = errResp.error.message;
                                serverStatus = errResp.error.status;
                            }
                        }
                        catch { }

                        // 404: Model Not Found
                        if (statusCode == 404 || string.Equals(serverStatus, "NOT_FOUND", StringComparison.OrdinalIgnoreCase))
                        {
                            throw GeminiException.ModelNotFoundException(CurrentModel, serverMessage ?? errorContent);
                        }

                        // 401/403: Authentication or Permission error
                        if (statusCode == 401 || statusCode == 403 ||
                            string.Equals(serverStatus, "UNAUTHENTICATED", StringComparison.OrdinalIgnoreCase) ||
                            string.Equals(serverStatus, "PERMISSION_DENIED", StringComparison.OrdinalIgnoreCase))
                        {
                            throw GeminiException.AuthException(serverMessage ?? "Kredensial atau hak akses ditolak.");
                        }

                        // 429: Rate Limit / Quota Exceeded
                        if (statusCode == 429 || string.Equals(serverStatus, "RESOURCE_EXHAUSTED", StringComparison.OrdinalIgnoreCase))
                        {
                            if (attempt <= maxRetries)
                            {
                                LoggingService.Instance.Warning(string.Format("Rate limit Gemini tercapai untuk model '{0}'. Mencoba ulang ({1}/{2})...", CurrentModel, attempt, maxRetries));
                                shouldRetry = true;
                                retryDelayMs = 2000 * attempt;
                            }
                            else
                            {
                                throw GeminiException.RateLimitException(serverMessage);
                            }
                        }
                        // 5xx: Server Error
                        else if (statusCode >= 500 && attempt <= maxRetries)
                        {
                            LoggingService.Instance.Warning(string.Format("Server Gemini mengalami gangguan ({0}). Mencoba ulang ({1}/{2})...", statusCode, attempt, maxRetries));
                            shouldRetry = true;
                            retryDelayMs = 2000 * attempt;
                        }
                        else
                        {
                            string detail = serverMessage ?? errorContent;
                            throw new GeminiException(string.Format("Gemini API Error ({0}): {1}", statusCode, detail), statusCode, serverStatus: serverStatus);
                        }
                    }
                }
                catch (OperationCanceledException)
                {
                    if (!ct.IsCancellationRequested)
                    {
                        throw GeminiException.TimeoutException(_settings.RequestTimeoutSeconds);
                    }
                    throw;
                }
                catch (HttpRequestException ex)
                {
                    if (attempt <= maxRetries)
                    {
                        shouldRetry = true;
                        retryDelayMs = 2000 * attempt;
                    }
                    else
                    {
                        string safeMsg = LoggingService.RedactApiKey(ex.Message);
                        throw GeminiException.NetworkException(safeMsg);
                    }
                }

                if (shouldRetry)
                {
                    await Task.Delay(retryDelayMs, ct).ConfigureAwait(false);
                    continue;
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
