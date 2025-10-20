using System.Windows;

namespace Google_Bookmarks_Manager_for_GPOs
{
    public partial class InputDialog : Window
    {
        public string InputText
        {
            get => InputTextBox.Text;
            set => InputTextBox.Text = value;
        }

        public string InputLabel
        {
            get => LabelTextBlock.Text;
            set => LabelTextBlock.Text = value;
        }

        public InputDialog()
        {
            InitializeComponent();

            if (Application.Current.MainWindow != null)
            {
                Owner = Application.Current.MainWindow;
            }
        }

        private void OkButton_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = true;
            Close();
        }

        private void CancelButton_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }
    }
}
