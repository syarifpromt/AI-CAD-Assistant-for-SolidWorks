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
    public class GeminiClient
    {
        private static readonly HttpClient HttpClient = new HttpClient();
        private readonly CredentialService _credentialService;
        private readonly AppSettings _settings;
        private readonly JavaScriptSerializer _serializer;

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
            try
            {
                string apiKey = _credentialService.LoadApiKey();
                if (string.IsNullOrWhiteSpace(apiKey))
                    return false;

                string prompt = "Halo, respon dengan kata OK.";
                var request = CreateBaseRequest();
                request.contents.Add(new GeminiContent
                {
                    role = "user",
                    parts = new List<GeminiPart> { new GeminiPart { text = prompt } }
                });

                string jsonBody = _serializer.Serialize(request);
                string responseJson = await ExecuteRequestAsync(jsonBody, ct).ConfigureAwait(false);
                return !string.IsNullOrWhiteSpace(responseJson);
            }
            catch (Exception ex)
            {
                LoggingService.Instance.Warning("Test connection Gemini gagal: " + ex.Message);
                return false;
            }
        }

        public async Task<string> SendTextAsync(string prompt, List<ConversationMessage> history, CancellationToken ct = default(CancellationToken))
        {
            var request = CreateBaseRequest();

            // Append history if present
            if (history != null)
            {
                foreach (var msg in history)
                {
                    if (msg.Type == MessageType.Error) continue;
                    string role = msg.Role == MessageRole.User ? "user" : "model";
                    request.contents.Add(new GeminiContent
                    {
                        role = role,
                        parts = new List<GeminiPart> { new GeminiPart { text = msg.Content } }
                    });
                }
            }

            // Append current prompt
            request.contents.Add(new GeminiContent
            {
                role = "user",
                parts = new List<GeminiPart> { new GeminiPart { text = prompt } }
            });

            string jsonBody = _serializer.Serialize(request);
            string responseJson = await ExecuteRequestAsync(jsonBody, ct).ConfigureAwait(false);
            return ParseResponseText(responseJson);
        }

        public async Task<string> SendWithImageAsync(string prompt, string base64ImageData, string mimeType, CancellationToken ct = default(CancellationToken))
        {
            var request = CreateBaseRequest();
            request.contents.Add(new GeminiContent
            {
                role = "user",
                parts = new List<GeminiPart>
                {
                    new GeminiPart
                    {
                        inline_data = new GeminiInlineData
                        {
                            mime_type = mimeType ?? "image/png",
                            data = base64ImageData
                        }
                    },
                    new GeminiPart { text = prompt }
                }
            });

            string jsonBody = _serializer.Serialize(request);
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

        private string BuildApiUrl()
        {
            string apiKey = _credentialService.LoadApiKey();
            if (string.IsNullOrWhiteSpace(apiKey))
            {
                throw GeminiException.AuthException();
            }

            string model = string.IsNullOrWhiteSpace(_settings.GeminiModel) ? "gemini-1.5-pro" : _settings.GeminiModel.Trim();
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

                        if (statusCode == 429)
                        {
                            if (attempt <= maxRetries)
                            {
                                LoggingService.Instance.Warning(string.Format("Rate limit Gemini tercapai. Mencoba ulang ({0}/{1})...", attempt, maxRetries));
                                shouldRetry = true;
                                retryDelayMs = 2000 * attempt;
                            }
                            else
                            {
                                throw GeminiException.RateLimitException();
                            }
                        }
                        else if (statusCode == 401 || statusCode == 403)
                        {
                            throw GeminiException.AuthException();
                        }
                        else if (statusCode >= 500 && attempt <= maxRetries)
                        {
                            LoggingService.Instance.Warning(string.Format("Server error ({0}). Mencoba ulang ({1}/{2})...", statusCode, attempt, maxRetries));
                            shouldRetry = true;
                            retryDelayMs = 2000 * attempt;
                        }
                        else
                        {
                            throw new GeminiException(string.Format("Gemini API Error ({0}): {1}", statusCode, errorContent), statusCode);
                        }
                    }
                }
                catch (OperationCanceledException)
                {
                    if (!ct.IsCancellationRequested)
                    {
                        throw GeminiException.TimeoutException();
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
                        throw GeminiException.NetworkException(ex.Message);
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
