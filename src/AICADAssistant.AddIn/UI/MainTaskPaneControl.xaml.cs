using System;
using System.Collections.Specialized;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
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

            // Ensure keyboard focus is set to the input box once loaded
            Loaded += (s, e) =>
            {
                Dispatcher.BeginInvoke(DispatcherPriority.Input, new Action(() =>
                {
                    InputTextBox.Focus();
                    Keyboard.Focus(InputTextBox);
                }));
            };
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

        private void OnInputPreviewMouseDown(object sender, MouseButtonEventArgs e)
        {
            InputTextBox.Focus();
            Keyboard.Focus(InputTextBox);
        }

        private void OnInputPreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter)
            {
                // Shift+Enter creates a newline
                if ((Keyboard.Modifiers & ModifierKeys.Shift) == ModifierKeys.Shift)
                {
                    return;
                }

                // Normal Enter or Ctrl+Enter sends the message directly
                if (ViewModel != null && ViewModel.CanSendMessage)
                {
                    ViewModel.SendMessageCommand.Execute(null);
                    e.Handled = true;
                }
                else
                {
                    e.Handled = true;
                }
            }
        }
    }
}

