using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Threading.Tasks;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media.Imaging;
using Newtonsoft.Json.Linq;
using Windows.Storage;
using Windows.Storage.Pickers;

namespace Danbooru_Viewer
{
    public partial class MainWindow : Window
    {
        private static readonly HttpClient client = new HttpClient();

        private readonly string _username;
        private readonly string _apiKey;

        private const string TempFilesDirectory = @"C:\Users\Impulsor\Pictures\Danbooru Temp Files";

        private int _currentPage = 1;
        private int ImagesPerPage = 20;
        private List<(string imageUrl, string tags)> _currentImages;
        private List<string> _selectedImages = new List<string>(); // Track selected images

        private bool _isSwitchingMode = false;
        private bool _skipClearTempImages = false;
        public MainWindow()
        {
            this.InitializeComponent();
            Directory.CreateDirectory(TempFilesDirectory); // Ensure temp directory exists

            AdjustButtonLayout();
        }
        private async void ImagesPerPageComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (ImagesPerPageComboBox.SelectedItem is ComboBoxItem selectedItem)
            {
                if (int.TryParse(selectedItem.Content.ToString(), out int imagesPerPage))
                {
                    ImagesPerPage = imagesPerPage; // Update the ImagesPerPage variable
                                                   // Optionally, refresh images if you want to apply the change immediately
                    _currentPage = 1; // Reset to the first page

                    _skipClearTempImages = true; // Set the flag
                    await FetchAndDisplayImages(TagInput.Text, _currentPage);
                    _skipClearTempImages = false; // Reset the flag after the method call
                }
            }
        }
        private async void TagInput_KeyDown(object sender, KeyRoutedEventArgs e)
        {
            if (e.Key == Windows.System.VirtualKey.Enter)
            {
                string tag = TagInput.Text;
                if (string.IsNullOrWhiteSpace(tag))
                {
                    await ShowMessageDialog("Error", "Please enter a tag.");
                    return;
                }

                _currentPage = 1;
                await FetchAndDisplayImages(tag, _currentPage);
            }
        }

        private async Task FetchAndDisplayImages(string tag, int page)
        {
            if (!_skipClearTempImages)
            {
                await ClearTempImages(); // Clear temp images only if the flag is false
                ImageGridView.ItemsSource = null; // Clear the ImageGridView before loading new images
            }

            _currentImages = await GetDanbooruImages(tag, ImagesPerPage, page);

            if (_currentImages != null && _currentImages.Count > 0)
            {
                // Start downloading images without waiting for all to finish
                await DownloadImages(_currentImages);
            }
        }

        private async Task ClearTempImages()
        {
            foreach (var file in Directory.GetFiles(TempFilesDirectory))
            {
                File.Delete(file);
            }
            await Task.Delay(500);
        }

        private async void OnNextClick(object sender, RoutedEventArgs e)
        {
            _currentPage++;
            await FetchAndDisplayImages(TagInput.Text, _currentPage);
        }

        private async void OnPreviousClick(object sender, RoutedEventArgs e)
        {
            if (_currentPage > 1)
            {
                _currentPage--;
                await FetchAndDisplayImages(TagInput.Text, _currentPage);
            }
        }

        private void LoadImagesFromTempDirectory(List<string> newImagePaths)
        {
            if (newImagePaths == null || newImagePaths.Count == 0)
                return;

            // Append the new images to the existing items in the ImageGridView
            var existingImages = (ImageGridView.ItemsSource as List<string>) ?? new List<string>();

            foreach (var newImagePath in newImagePaths)
            {
                // Only add the new image if it hasn't already been loaded
                if (!existingImages.Contains(newImagePath))
                {
                    existingImages.Add(newImagePath);
                }
            }

            // Refresh the UI
            ImageGridView.ItemsSource = null;
            ImageGridView.ItemsSource = existingImages;
        }

        private async Task<List<(string imageUrl, string tags)>> GetDanbooruImages(string tag, int limit, int page)
        {
            try
            {
                string apiUrl = $"https://safebooru.donmai.us/posts.json?tags={tag}&limit={limit}&page={page}";

                var request = new HttpRequestMessage(HttpMethod.Get, apiUrl);
                var byteArray = System.Text.Encoding.ASCII.GetBytes($"{_username}:{_apiKey}");
                request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Basic", Convert.ToBase64String(byteArray));
                request.Headers.UserAgent.ParseAdd("Mozilla/5.0 (compatible; AcmeInc/1.0)");

                var response = await client.SendAsync(request);
                response.EnsureSuccessStatusCode();

                var responseBody = await response.Content.ReadAsStringAsync();
                var jsonArray = JArray.Parse(responseBody);
                List<(string imageUrl, string tags)> imageUrlsAndTags = new List<(string, string)>();

                foreach (var post in jsonArray)
                {
                    string imageUrl = post["file_url"]?.ToString();
                    string tags = post["tag_string"]?.ToString();
                    if (!string.IsNullOrEmpty(imageUrl))
                    {
                        imageUrlsAndTags.Add((imageUrl, tags));
                    }
                }
                return imageUrlsAndTags;
            }
            catch (Exception ex)
            {
                await ShowMessageDialog("Error", $"Error fetching images: {ex.Message}");
                return null;
            }
        }

        private async Task DownloadImages(List<(string imageUrl, string tags)> imagesAndTags)
        {
            int downloadedCount = 0;
            List<string> newImagePaths = new List<string>();

            foreach (var (imageUrl, tags) in imagesAndTags)
            {
                try
                {
                    string fileName = Path.GetFileNameWithoutExtension(imageUrl);
                    string imageFilePath = Path.Combine(TempFilesDirectory, fileName + Path.GetExtension(imageUrl));
                    string tagFilePath = Path.Combine(TempFilesDirectory, fileName + ".txt");

                    if (File.Exists(imageFilePath))
                    {
                        newImagePaths.Add(imageFilePath);
                        continue;
                    }

                    var request = new HttpRequestMessage(HttpMethod.Get, imageUrl);
                    request.Headers.Referrer = new Uri("https://danbooru.donmai.us");
                    request.Headers.UserAgent.ParseAdd("Mozilla/5.0 (compatible; AcmeInc/1.0)");

                    var response = await client.SendAsync(request);
                    response.EnsureSuccessStatusCode();

                    var imageBytes = await response.Content.ReadAsByteArrayAsync();
                    await File.WriteAllBytesAsync(imageFilePath, imageBytes);
                    await File.WriteAllTextAsync(tagFilePath, tags); // Write the tags to a .txt file

                    newImagePaths.Add(imageFilePath);
                    downloadedCount++;

                    if (downloadedCount % 5 == 0)
                    {
                        LoadImagesFromTempDirectory(newImagePaths);
                        newImagePaths.Clear();
                    }
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"Error downloading image from URL: {imageUrl}, Exception: {ex.Message}");
                }
            }

            if (newImagePaths.Count > 0)
            {
                LoadImagesFromTempDirectory(newImagePaths);
            }
        }
        private async void SaveSelectedClick(object sender, RoutedEventArgs e)
        {
            if (_selectedImages.Count == 0)
            {
                await ShowMessageDialog("No Images Selected", "Please select some images to save.");
                return;
            }

            // Create a folder picker to allow the user to choose the save location.
            var folderPicker = new FolderPicker();
            folderPicker.SuggestedStartLocation = PickerLocationId.PicturesLibrary;
            folderPicker.FileTypeFilter.Add("*");
            var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(this);
            WinRT.Interop.InitializeWithWindow.Initialize(folderPicker, hwnd);
            StorageFolder folder = await folderPicker.PickSingleFolderAsync();

            if (folder != null)
            {
                // Copy each selected image to the chosen folder.
                foreach (var imagePath in _selectedImages)
                {
                    string fileName = Path.GetFileName(imagePath);
                    string sourceFilePath = Path.Combine(TempFilesDirectory, fileName);

                    if (File.Exists(sourceFilePath))
                    {
                        // Copy the file to the user's selected folder.
                        StorageFile destinationFile = await folder.CreateFileAsync(fileName, CreationCollisionOption.GenerateUniqueName);
                        await FileIO.WriteBytesAsync(destinationFile, File.ReadAllBytes(sourceFilePath));
                    }
                }

                await ShowMessageDialog("Success", "Selected images saved successfully.");
            }
            else
            {
                await ShowMessageDialog("Cancelled", "Operation cancelled. No images were saved.");
            }
        }

        private void ImageGridView_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_isSwitchingMode) return; // Exit if we're in the middle of switching modes

            // Clear the previously selected images list
            _selectedImages.Clear();

            // Loop through the newly selected items and add them to the list
            foreach (var selectedItem in ImageGridView.SelectedItems)
            {
                string imagePath = selectedItem as string;
                if (!string.IsNullOrEmpty(imagePath))
                {
                    _selectedImages.Add(imagePath);
                }
            }

            // Update SelectedCountTextBlock
            SelectedCountTextBlock.Text = $"Selected Images: {_selectedImages.Count}";
            SelectedCountTextBlock.Visibility = _selectedImages.Count > 0 ? Visibility.Visible : Visibility.Collapsed;

            // Temporarily set flag to prevent double processing
            _isSwitchingMode = true;

            // Dynamically switch between Single and Multiple selection modes
            if (_selectedImages.Count > 0 && ImageGridView.SelectionMode != ListViewSelectionMode.Multiple)
            {
                // Switch to Multiple selection mode if at least one item is selected
                ImageGridView.SelectionMode = ListViewSelectionMode.Multiple;
            }
            else if (_selectedImages.Count == 0 && ImageGridView.SelectionMode != ListViewSelectionMode.Single)
            {
                // Switch back to Single selection mode if no items are selected
                ImageGridView.SelectionMode = ListViewSelectionMode.Single;
            }

            // Reset the flag after mode switching is complete
            _isSwitchingMode = false;
        }

        private async Task ShowMessageDialog(string title, string content)
        {
            ContentDialog dialog = new ContentDialog
            {
                Title = title,
                Content = content,
                CloseButtonText = "OK",
                XamlRoot = this.Content.XamlRoot
            };

            await dialog.ShowAsync();
        }

        private void ImageGridView_RightTapped(object sender, RightTappedRoutedEventArgs e)
        {
            // Get the clicked item
            var clickedItem = ((FrameworkElement)e.OriginalSource).DataContext as string;

            if (clickedItem != null)
            {
                // Set it as the selected item in the GridView
                ImageGridView.SelectedItem = clickedItem;

                // Set the source of the enlarged image
                EnlargedImage.Source = new BitmapImage(new Uri(clickedItem));

                // Hide the GridView and show the enlarged image
                ImageGridView.Visibility = Visibility.Collapsed;
                EnlargedImageGrid.Visibility = Visibility.Visible;
                BackButton.Visibility = Visibility.Visible;
                ImageGridView.SelectionMode = ListViewSelectionMode.Single;
                AdjustButtonLayout();
            }
        }

        private void BackToGallery_Click(object sender, RoutedEventArgs e)
        {
            EnlargedImageGrid.Visibility = Visibility.Collapsed;
            ImageGridView.Visibility = Visibility.Visible;
            BackButton.Visibility = Visibility.Collapsed;
            AdjustButtonLayout();
        }
        private void AdjustButtonLayout()
        {
            if (BackButton.Visibility == Visibility.Visible)
            {
                SaveColumn.Width = new GridLength(1, GridUnitType.Star);
                BackColumn.Width = new GridLength(1, GridUnitType.Star);
            }
            else
            {
                SaveColumn.Width = new GridLength(1, GridUnitType.Star);
                BackColumn.Width = new GridLength(0); // Hide the BackButton's column
            }
        }
    }
}