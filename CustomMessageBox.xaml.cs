using System.Windows;

namespace Google_Bookmarks_Manager_for_GPOs
{
    public partial class CustomMessageBox : Window
    {
        public MessageBoxResult Result { get; private set; }

        public CustomMessageBox(string message, string caption, MessageBoxButton buttons)
        {
            InitializeComponent();

            Title = caption;
            MessageTextBlock.Text = message;

            if (buttons == MessageBoxButton.OK)
            {
                CancelButton.Visibility = Visibility.Collapsed;
            }
        }

        private void OKButton_Click(object sender, RoutedEventArgs e)
        {
            Result = MessageBoxResult.OK;
            Close();
        }

        private void CancelButton_Click(object sender, RoutedEventArgs e)
        {
            Result = MessageBoxResult.Cancel;
            Close();
        }
    }
}
