using System.Windows;

namespace Google_Bookmarks_Manager_for_GPOs
{
    public partial class ImportWindow : Window
    {

        #region Constructors

        public ImportWindow()
        {
            InitializeComponent();
            if (Application.Current.MainWindow != null)
            {
                Owner = Application.Current.MainWindow; // Set the owner
                WindowStartupLocation = WindowStartupLocation.CenterOwner; // Center on owner
            }
            else
            {
                WindowStartupLocation = WindowStartupLocation.CenterScreen; // Fallback
            }
        }

        #endregion Constructors

        #region Properties

        public string Json { get; set; }

        #endregion Properties

        #region Methods

        private void cancelButton_Click(object sender, RoutedEventArgs e)
        {
            Json = null;
            DialogResult = false;
            Close();
        }

        private void importButton_Click(object sender, RoutedEventArgs e)
        {
            Json = jsonTextBox.Text;
            DialogResult = true;
            Close();
        }

        #endregion Methods

    }
}