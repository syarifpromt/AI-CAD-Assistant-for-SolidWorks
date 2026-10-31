using System;
using System.Collections.Specialized;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Threading;
using SolidWorks.Interop.sldworks;
using AICADAssistant.AddIn.UI.ViewModels;

namespace AICADAssistant.AddIn.UI
{
    [ComVisible(true)]
    [ClassInterface(ClassInterfaceType.AutoDispatch)]
    public partial class MainTaskPaneControl : UserControl
    {
        private const int WM_GETDLGCODE = 0x0087;
        private const uint DLGC_WANTALLKEYS = 0x0004;
        private const uint DLGC_WANTARROWS = 0x0001;
        private const uint DLGC_WANTCHARS = 0x0080;
        private const uint DLGC_WANTTAB = 0x0002;

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

            // Two-way synchronization between WinForms native ChatBox and ViewModel.UserInput
            ChatBox.TextChanged += OnChatBoxTextChanged;
            ChatBox.SubmitRequested += OnChatBoxSubmitRequested;
            ViewModel.PropertyChanged += OnViewModelPropertyChanged;

            // Ensure keyboard focus and Win32 message hook are set once loaded
            Loaded += OnLoaded;
            Unloaded += OnUnloaded;
        }

        private void OnViewModelPropertyChanged(object sender, System.ComponentModel.PropertyChangedEventArgs e)
        {
            if (e.PropertyName == "UserInput")
            {
                if (ChatBox.Text != ViewModel.UserInput)
                {
                    ChatBox.Text = ViewModel.UserInput ?? string.Empty;
                    ChatBox.SelectionStart = ChatBox.Text.Length;
                }
                PlaceholderTextBlock.Visibility = string.IsNullOrEmpty(ChatBox.Text) ? Visibility.Visible : Visibility.Collapsed;
            }
        }

        private void OnChatBoxTextChanged(object sender, EventArgs e)
        {
            PlaceholderTextBlock.Visibility = string.IsNullOrEmpty(ChatBox.Text) ? Visibility.Visible : Visibility.Collapsed;
            if (ViewModel != null && ViewModel.UserInput != ChatBox.Text)
            {
                ViewModel.UserInput = ChatBox.Text;
            }
        }

        private void OnChatBoxSubmitRequested(object sender, EventArgs e)
        {
            if (ViewModel == null) return;

            string text = ChatBox.Text != null ? ChatBox.Text.Trim() : string.Empty;
            if (string.IsNullOrWhiteSpace(text))
            {
                return; // Do not send empty input
            }

            // Guarantee ViewModel has the current text before checking CanSendMessage
            ViewModel.UserInput = text;

            if (ViewModel.CanSendMessage)
            {
                ViewModel.SendMessageCommand.Execute(null);
                ChatBox.Text = string.Empty;
                ChatBox.Focus();
            }
        }

        private void OnLoaded(object sender, RoutedEventArgs e)
        {
            var source = PresentationSource.FromVisual(this) as HwndSource;
            if (source != null)
            {
                source.RemoveHook(HwndHook);
                source.AddHook(HwndHook);
            }

            Dispatcher.BeginInvoke(DispatcherPriority.Input, new Action(() =>
            {
                ChatBox.Focus();
            }));
        }

        private void OnUnloaded(object sender, RoutedEventArgs e)
        {
            var source = PresentationSource.FromVisual(this) as HwndSource;
            if (source != null)
            {
                source.RemoveHook(HwndHook);
            }

            ChatBox.TextChanged -= OnChatBoxTextChanged;
            ChatBox.SubmitRequested -= OnChatBoxSubmitRequested;
            if (ViewModel != null)
            {
                ViewModel.PropertyChanged -= OnViewModelPropertyChanged;
            }
        }

        private IntPtr HwndHook(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            if (msg == WM_GETDLGCODE)
            {
                handled = true;
                return new IntPtr(DLGC_WANTALLKEYS | DLGC_WANTCHARS | DLGC_WANTARROWS | DLGC_WANTTAB);
            }
            return IntPtr.Zero;
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
    }
}

