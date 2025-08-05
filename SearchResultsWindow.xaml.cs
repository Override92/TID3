using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace TID3
{
    /// <summary>
    /// Interaction logic for SearchResultsWindow.xaml
    /// </summary>
    public partial class SearchResultsWindow : Window
    {
        private readonly MusicBrainzService _musicBrainzService;
        private MusicBrainzRelease _selectedRelease = null!;

        public MusicBrainzRelease SelectedRelease => _selectedRelease;

        public SearchResultsWindow(List<MusicBrainzRelease> releases, MusicBrainzService musicBrainzService)
        {
            InitializeComponent();
            _musicBrainzService = musicBrainzService;
            ResultsList.ItemsSource = releases;
        }

        private async void ResultItem_Click(object sender, MouseButtonEventArgs e)
        {
            if (sender is Border border && border.Tag is MusicBrainzRelease release)
            {
                _selectedRelease = release;

                // Highlight selected item
                foreach (Border item in FindVisualChildren<Border>(ResultsList))
                {
                    if (item.Tag == release)
                        item.Background = new SolidColorBrush(Color.FromRgb(0, 120, 212));
                    else
                        item.Background = new SolidColorBrush(Color.FromRgb(45, 45, 48));
                }

                // Load detailed information
                try
                {
                    var detailedRelease = await _musicBrainzService.GetReleaseDetails(release.Id);
                    if (detailedRelease != null)
                    {
                        _selectedRelease = detailedRelease;
                    }
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Error loading release details: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Warning);
                }
            }
        }

        private void Select_Click(object sender, RoutedEventArgs e)
        {
            if (_selectedRelease != null)
            {
                DialogResult = true;
                Close();
            }
            else
            {
                MessageBox.Show("Please select a release first.");
            }
        }

        private void Cancel_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }

        // Helper method to find visual children
        private static IEnumerable<T> FindVisualChildren<T>(DependencyObject? depObj) where T : DependencyObject
        {
            if (depObj != null)
            {
                for (int i = 0; i < VisualTreeHelper.GetChildrenCount(depObj); i++)
                {
                    DependencyObject child = VisualTreeHelper.GetChild(depObj, i);
                    if (child != null && child is T)
                        yield return (T)child;

                    foreach (T childOfChild in FindVisualChildren<T>(child))
                        yield return childOfChild;
                }
            }
        }
    }
}