using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Google_Bookmarks_Manager_for_GPOs
{
    public class Bookmark : INotifyPropertyChanged
    {
        private string _name;
        private string _url;
        private bool _isFolder;
        private bool _isRootFolder;  // New Property
        private bool _isEditing;
        private ObservableCollection<Bookmark> _children;

        public string Name
        {
            get => _name;
            set
            {
                _name = value;
                OnPropertyChanged(nameof(Name));
            }
        }

        public string Url
        {
            get => _url;
            set
            {
                _url = value;
                OnPropertyChanged(nameof(Url));
            }
        }

        public bool IsFolder
        {
            get => _isFolder;
            set
            {
                _isFolder = value;
                OnPropertyChanged(nameof(IsFolder));
            }
        }

        public bool IsRootFolder  // New property to track root folders
        {
            get => _isRootFolder;
            set
            {
                _isRootFolder = value;
                OnPropertyChanged(nameof(IsRootFolder));
            }
        }

        public bool IsEditing
        {
            get => _isEditing;
            set
            {
                _isEditing = value;
                OnPropertyChanged(nameof(IsEditing));
            }
        }

        public ObservableCollection<Bookmark> Children
        {
            get => _children;
            set
            {
                _children = value;
                OnPropertyChanged(nameof(Children));
            }
        }

        public Bookmark()
        {
            Children = new ObservableCollection<Bookmark>();
        }

        public event PropertyChangedEventHandler PropertyChanged;

        protected void OnPropertyChanged(string propertyName)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }

}
