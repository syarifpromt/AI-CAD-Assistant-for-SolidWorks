using System.Collections.Specialized;
using System.Windows.Controls;
using System.Windows.Input;
using SolidWorks.Interop.sldworks;
using AICADAssistant.AddIn.UI.ViewModels;

namespace AICADAssistant.AddIn.UI
{
    public partial class MainTaskPaneControl : UserControl
    {
        public MainViewModel ViewModel { get; private set; }

        public MainTaskPaneControl(ISldWorks swApp)
        {
            InitializeComponent();
            ViewModel = new MainViewModel(swApp);
            DataContext = ViewModel;

            // Auto-scroll chat to bottom when new messages arrive
            if (ViewModel.Messages != null)
            {
                ViewModel.Messages.CollectionChanged += OnMessagesCollectionChanged;
            }
        }

        public void OnActiveDocumentChanged()
        {
            if (ViewModel != null)
            {
                ViewModel.OnActiveDocumentChanged();
            }
        }

        private void OnMessagesCollectionChanged(object sender, NotifyCollectionChangedEventArgs e)
        {
            ChatScrollViewer.ScrollToEnd();
        }

        private void OnInputKeyDown(object sender, KeyEventArgs e)
        {
            // Ctrl+Enter sends the message
            if (e.Key == Key.Enter && (Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.Control)
            {
                if (ViewModel.CanSendMessage)
                {
                    ViewModel.SendMessageCommand.Execute(null);
                    e.Handled = true;
                }
            }
        }
    }
}

