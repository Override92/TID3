using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace TID3
{
    /// <summary>
    /// Interaction logic for BatchEditWindow.xaml
    /// </summary>
    public partial class BatchEditWindow : Window
    {
        private readonly List<AudioFileInfo> _audioFiles;
        private readonly TagService _tagService;

        public BatchEditWindow(List<AudioFileInfo> audioFiles, TagService tagService)
        {
            InitializeComponent();
            _audioFiles = audioFiles;
            _tagService = tagService;
            FilesList.ItemsSource = audioFiles;
        }

        private void SelectAll_Click(object sender, RoutedEventArgs e)
        {
            SetAllCheckboxes(true);
        }

        private void SelectNone_Click(object sender, RoutedEventArgs e)
        {
            SetAllCheckboxes(false);
        }

        private void SetAllCheckboxes(bool isChecked)
        {
            foreach (CheckBox checkBox in FindVisualChildren<CheckBox>(FilesList))
            {
                if (checkBox.Tag is AudioFileInfo)
                    checkBox.IsChecked = isChecked;
            }
        }

        private void Preview_Click(object sender, RoutedEventArgs e)
        {
            var selectedFiles = GetSelectedFiles();
            if (!selectedFiles.Any())
            {
                MessageBox.Show("Please select at least one file to preview changes.");
                return;
            }

            var changes = GetChangesToApply();
            if (!changes.Any())
            {
                MessageBox.Show("Please select at least one field to update.");
                return;
            }

            var preview = GeneratePreviewText(selectedFiles, changes);
            MessageBox.Show(preview, "Preview Changes", MessageBoxButton.OK, MessageBoxImage.Information);
        }

        private void Apply_Click(object sender, RoutedEventArgs e)
        {
            var selectedFiles = GetSelectedFiles();
            if (!selectedFiles.Any())
            {
                MessageBox.Show("Please select at least one file to update.");
                return;
            }

            var changes = GetChangesToApply();
            if (!changes.Any())
            {
                MessageBox.Show("Please select at least one field to update.");
                return;
            }

            var result = MessageBox.Show($"Are you sure you want to update {selectedFiles.Count} files?",
                                       "Confirm Batch Update",
                                       MessageBoxButton.YesNo,
                                       MessageBoxImage.Question);

            if (result == MessageBoxResult.Yes)
            {
                ApplyChanges(selectedFiles, changes);
                MessageBox.Show($"Successfully updated {selectedFiles.Count} files!");
                DialogResult = true;
                Close();
            }
        }

        private void Cancel_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }

        private List<AudioFileInfo> GetSelectedFiles()
        {
            var selectedFiles = new List<AudioFileInfo>();
            foreach (CheckBox checkBox in FindVisualChildren<CheckBox>(FilesList))
            {
                if (checkBox.IsChecked == true && checkBox.Tag is AudioFileInfo file)
                {
                    selectedFiles.Add(file);
                }
            }
            return selectedFiles;
        }

        private Dictionary<string, object> GetChangesToApply()
        {
            var changes = new Dictionary<string, object>();

            if (UpdateAlbumCheck.IsChecked == true && !string.IsNullOrWhiteSpace(AlbumTextBox.Text))
                changes["Album"] = AlbumTextBox.Text.Trim();

            if (UpdateAlbumArtistCheck.IsChecked == true && !string.IsNullOrWhiteSpace(AlbumArtistTextBox.Text))
                changes["AlbumArtist"] = AlbumArtistTextBox.Text.Trim();

            if (UpdateGenreCheck.IsChecked == true && !string.IsNullOrWhiteSpace(GenreTextBox.Text))
                changes["Genre"] = GenreTextBox.Text.Trim();

            if (UpdateYearCheck.IsChecked == true && !string.IsNullOrWhiteSpace(YearTextBox.Text))
            {
                if (uint.TryParse(YearTextBox.Text.Trim(), out uint year))
                    changes["Year"] = year;
            }

            if (AutoNumberTracksCheck.IsChecked == true)
                changes["AutoNumberTracks"] = true;

            if (CleanupTagsCheck.IsChecked == true)
                changes["CleanupTags"] = true;

            if (UsePatternCheck.IsChecked == true && !string.IsNullOrWhiteSpace(FindTextBox.Text))
            {
                changes["FindPattern"] = FindTextBox.Text;
                changes["ReplacePattern"] = ReplaceTextBox.Text ?? "";
            }

            return changes;
        }

        private string GeneratePreviewText(List<AudioFileInfo> files, Dictionary<string, object> changes)
        {
            var preview = new System.Text.StringBuilder();
            preview.AppendLine($"Preview of changes for {files.Count} files:\n");

            foreach (var change in changes)
            {
                switch (change.Key)
                {
                    case "Album":
                        preview.AppendLine($"• Album will be set to: {change.Value}");
                        break;
                    case "AlbumArtist":
                        preview.AppendLine($"• Album Artist will be set to: {change.Value}");
                        break;
                    case "Genre":
                        preview.AppendLine($"• Genre will be set to: {change.Value}");
                        break;
                    case "Year":
                        preview.AppendLine($"• Year will be set to: {change.Value}");
                        break;
                    case "AutoNumberTracks":
                        preview.AppendLine("• Track numbers will be automatically assigned (1, 2, 3...)");
                        break;
                    case "CleanupTags":
                        preview.AppendLine("• Empty tags will be removed");
                        break;
                    case "FindPattern":
                        preview.AppendLine($"• Text pattern '{change.Value}' will be replaced with '{changes.GetValueOrDefault("ReplacePattern", "")}'");
                        break;
                }
            }

            preview.AppendLine("\nAffected files:");
            foreach (var file in files.Take(10))
            {
                preview.AppendLine($"• {file.FileName}");
            }

            if (files.Count > 10)
            {
                preview.AppendLine($"... and {files.Count - 10} more files");
            }

            return preview.ToString();
        }

        private void ApplyChanges(List<AudioFileInfo> files, Dictionary<string, object> changes)
        {
            for (int i = 0; i < files.Count; i++)
            {
                var file = files[i];

                // Apply basic field changes
                if (changes.ContainsKey("Album"))
                    file.Album = changes["Album"]?.ToString() ?? "";

                if (changes.ContainsKey("AlbumArtist"))
                    file.AlbumArtist = changes["AlbumArtist"]?.ToString() ?? "";

                if (changes.ContainsKey("Genre"))
                    file.Genre = changes["Genre"]?.ToString() ?? "";

                if (changes.ContainsKey("Year"))
                    file.Year = (uint)changes["Year"];

                // Auto-number tracks
                if (changes.ContainsKey("AutoNumberTracks"))
                    file.Track = (uint)(i + 1);

                // Pattern replacement
                if (changes.ContainsKey("FindPattern"))
                {
                    var findPattern = changes["FindPattern"]?.ToString() ?? "";
                    var replacePattern = changes.GetValueOrDefault("ReplacePattern", "")?.ToString() ?? "";

                    if (!string.IsNullOrEmpty(file.Title) && !string.IsNullOrEmpty(findPattern))
                        file.Title = file.Title.Replace(findPattern, replacePattern);
                    if (!string.IsNullOrEmpty(file.Artist) && !string.IsNullOrEmpty(findPattern))
                        file.Artist = file.Artist.Replace(findPattern, replacePattern);
                    if (!string.IsNullOrEmpty(file.Album) && !string.IsNullOrEmpty(findPattern))
                        file.Album = file.Album.Replace(findPattern, replacePattern);
                }

                // Cleanup empty tags
                if (changes.ContainsKey("CleanupTags"))
                {
                    if (string.IsNullOrWhiteSpace(file.Title)) file.Title = "";
                    if (string.IsNullOrWhiteSpace(file.Artist)) file.Artist = "";
                    if (string.IsNullOrWhiteSpace(file.Album)) file.Album = "";
                    if (string.IsNullOrWhiteSpace(file.Genre)) file.Genre = "";
                    if (string.IsNullOrWhiteSpace(file.AlbumArtist)) file.AlbumArtist = "";
                    if (string.IsNullOrWhiteSpace(file.Comment)) file.Comment = "";
                }

                // Save the file
                _tagService.SaveFile(file);
            }
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