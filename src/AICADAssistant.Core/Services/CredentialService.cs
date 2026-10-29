using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace AICADAssistant.Core.Services
{
    /// <summary>
    /// Secure storage for Gemini API key using Windows DPAPI (Data Protection API).
    /// Keys are encrypted specifically for the current Windows user profile.
    /// </summary>
    public class CredentialService
    {
        private static readonly byte[] Entropy = Encoding.UTF8.GetBytes("AICADAssistant_SW_2026");
        private readonly string _credentialFilePath;

        public CredentialService()
        {
            string appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            string folder = Path.Combine(appData, "AICADAssistant");
            if (!Directory.Exists(folder))
            {
                Directory.CreateDirectory(folder);
            }
            _credentialFilePath = Path.Combine(folder, "credential.dat");
        }

        public CredentialService(string customCredentialPath)
        {
            _credentialFilePath = customCredentialPath;
        }

        public void SaveApiKey(string apiKey)
        {
            if (string.IsNullOrWhiteSpace(apiKey))
            {
                DeleteApiKey();
                return;
            }

            try
            {
                byte[] plainBytes = Encoding.UTF8.GetBytes(apiKey.Trim());
                byte[] cipherBytes = ProtectedData.Protect(plainBytes, Entropy, DataProtectionScope.CurrentUser);
                File.WriteAllBytes(_credentialFilePath, cipherBytes);
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException("Gagal menyimpan API Key dengan enkripsi Windows DPAPI: " + ex.Message, ex);
            }
        }

        public string LoadApiKey()
        {
            if (!File.Exists(_credentialFilePath))
                return null;

            try
            {
                byte[] cipherBytes = File.ReadAllBytes(_credentialFilePath);
                byte[] plainBytes = ProtectedData.Unprotect(cipherBytes, Entropy, DataProtectionScope.CurrentUser);
                return Encoding.UTF8.GetString(plainBytes);
            }
            catch
            {
                // Decryption failure or corrupted file
                return null;
            }
        }

        public void DeleteApiKey()
        {
            try
            {
                if (File.Exists(_credentialFilePath))
                {
                    File.Delete(_credentialFilePath);
                }
            }
            catch
            {
                // Silently handle deletion error
            }
        }

        public bool HasApiKey()
        {
            string key = LoadApiKey();
            return !string.IsNullOrWhiteSpace(key);
        }
    }
}

