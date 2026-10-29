using System.Windows;
using AICADAssistant.AddIn.UI.ViewModels;
using AICADAssistant.Core.Gemini;
using AICADAssistant.Core.Models;
using AICADAssistant.Core.Services;

namespace AICADAssistant.AddIn.UI
{
    public partial class SettingsWindow : Window
    {
        private readonly SettingsViewModel _viewModel;

        public SettingsWindow(AppSettings settings, CredentialService credentialService, GeminiClient geminiClient)
        {
            InitializeComponent();
            _viewModel = new SettingsViewModel(settings, credentialService, geminiClient);
            DataContext = _viewModel;
        }

        private void OnSaveApiKeyClicked(object sender, RoutedEventArgs e)
        {
            _viewModel.ApiKeyInput = ApiKeyBox.Password;
            _viewModel.SaveApiKey();
            ApiKeyBox.Clear();
        }

        private void OnSaveSettingsClicked(object sender, RoutedEventArgs e)
        {
            _viewModel.SaveSettings();
            MessageBox.Show("Pengaturan berhasil disimpan.", "AI CAD Assistant", MessageBoxButton.OK, MessageBoxImage.Information);
            Close();
        }

        private void OnCloseClicked(object sender, RoutedEventArgs e)
        {
            Close();
        }
    }
}

