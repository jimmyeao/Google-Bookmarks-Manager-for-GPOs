using System.Windows;

namespace Google_Bookmarks_Manager_for_GPOs
{
    public partial class ImportWindow : Window
    {
        public string Json { get; set; }

        public ImportWindow()
        {
            InitializeComponent();
        }

        private void importButton_Click(object sender, RoutedEventArgs e)
        {
            Json = jsonTextBox.Text;
            DialogResult = true;
            Close();
        }

        private void cancelButton_Click(object sender, RoutedEventArgs e)
        {
            Json = null;
            DialogResult = false;
            Close();
        }
    }
}
