using System;
using System.IO;
using System.Net.Http;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media.Imaging;
using TagLib;

namespace TID3.Utils
{
    public static class ImageHelper
    {
        private const int MAX_IMAGE_SIZE_BYTES = 10 * 1024 * 1024; // 10MB limit
        private const int DEFAULT_DECODE_PIXEL_WIDTH = 200;
        private const int MAX_DECODE_PIXEL_WIDTH = 800;

        // Use the existing dependency properties from TagService
        private static DependencyProperty OriginalWidthProperty => 
            TID3.Services.TagService.OriginalWidthProperty;
        private static DependencyProperty OriginalHeightProperty => 
            TID3.Services.TagService.OriginalHeightProperty;

        // Dependency property for storing original image bytes
        public static readonly DependencyProperty OriginalImageBytesProperty = 
            DependencyProperty.RegisterAttached("OriginalImageBytes", typeof(byte[]), typeof(ImageHelper));

        /// <summary>
        /// Creates a BitmapImage from byte array with proper memory management
        /// </summary>
        public static BitmapImage? CreateBitmapFromBytes(byte[] imageData, int decodePixelWidth = DEFAULT_DECODE_PIXEL_WIDTH)
        {
            if (imageData == null || imageData.Length == 0)
                return null;

            if (imageData.Length > MAX_IMAGE_SIZE_BYTES)
            {
                TID3Logger.Warning("Images", "Image too large, skipping", new { 
                    SizeBytes = imageData.Length, 
                    MaxSizeBytes = MAX_IMAGE_SIZE_BYTES 
                }, "ImageHelper");
                return null;
            }

            try
            {
                // Get original dimensions first from a separate stream
                int originalWidth, originalHeight;
                using (var dimensionStream = new MemoryStream(imageData))
                {
                    var frame = BitmapFrame.Create(dimensionStream, BitmapCreateOptions.DelayCreation, BitmapCacheOption.None);
                    originalWidth = frame.PixelWidth;
                    originalHeight = frame.PixelHeight;
                }

                // Create the bitmap using a fresh stream
                BitmapImage bitmap;
                using (var bitmapStream = new MemoryStream(imageData))
                {
                    bitmap = new BitmapImage();
                    bitmap.BeginInit();
                    bitmap.CacheOption = BitmapCacheOption.OnLoad;
                    bitmap.StreamSource = bitmapStream;
                    bitmap.DecodePixelWidth = Math.Min(decodePixelWidth, MAX_DECODE_PIXEL_WIDTH);
                    bitmap.EndInit();
                    
                    // Store original dimensions and bytes as attached properties BEFORE freezing
                    bitmap.SetValue(OriginalWidthProperty, originalWidth);
                    bitmap.SetValue(OriginalHeightProperty, originalHeight);
                    bitmap.SetValue(OriginalImageBytesProperty, imageData);
                    
                    // Note: bitmap.Freeze() must be called after EndInit() and after setting properties
                    bitmap.Freeze();
                }
                
                return bitmap;
            }
            catch (Exception ex)
            {
                TID3Logger.Error("Images", "Failed to create bitmap from bytes", ex, new { 
                    DataSize = imageData?.Length ?? 0,
                    DecodePixelWidth = decodePixelWidth 
                }, "ImageHelper");
                return null;
            }
        }

        /// <summary>
        /// Creates a BitmapImage from stream with proper memory management
        /// </summary>
        public static BitmapImage? CreateBitmapFromStream(Stream stream, int decodePixelWidth = DEFAULT_DECODE_PIXEL_WIDTH)
        {
            if (stream == null || !stream.CanRead)
                return null;

            if (stream.Length > MAX_IMAGE_SIZE_BYTES)
            {
                TID3Logger.Warning("Images", "Stream too large, skipping", new { 
                    StreamLength = stream.Length, 
                    MaxSizeBytes = MAX_IMAGE_SIZE_BYTES 
                }, "ImageHelper");
                return null;
            }

            try
            {
                var bitmap = new BitmapImage();
                bitmap.BeginInit();
                bitmap.CacheOption = BitmapCacheOption.OnLoad;
                bitmap.StreamSource = stream;
                bitmap.DecodePixelWidth = Math.Min(decodePixelWidth, MAX_DECODE_PIXEL_WIDTH);
                bitmap.EndInit();
                bitmap.Freeze();
                
                return bitmap;
            }
            catch (Exception ex)
            {
                TID3Logger.Error("Images", "Failed to create bitmap from stream", ex, new { 
                    StreamLength = stream?.Length ?? 0,
                    DecodePixelWidth = decodePixelWidth 
                }, "ImageHelper");
                return null;
            }
        }

        /// <summary>
        /// Creates a BitmapImage from TagLib picture with proper memory management
        /// </summary>
        public static BitmapImage? CreateBitmapFromTagPicture(IPicture picture, int decodePixelWidth = DEFAULT_DECODE_PIXEL_WIDTH)
        {
            using var scope = TID3Logger.BeginScope("Images", "CreateBitmapFromTagPicture", new { DecodePixelWidth = decodePixelWidth }, "ImageHelper");
            
            if (picture?.Data?.Data == null || picture.Data.Count == 0)
            {
                TID3Logger.Debug("Images", "Picture data is NULL or empty", component: "ImageHelper");
                return null;
            }

            TID3Logger.Debug("Images", "Processing picture data", new { 
                DataSizeBytes = picture.Data.Count,
                DecodePixelWidth = decodePixelWidth,
                PictureType = picture.Type.ToString()
            }, "ImageHelper");
            
            var result = CreateBitmapFromBytes(picture.Data.Data, decodePixelWidth);
            
            if (result != null)
            {
                var (width, height) = GetOriginalDimensions(result);
                TID3Logger.Debug("Images", "Bitmap created successfully from TagLib picture", new {
                    OriginalWidth = width,
                    OriginalHeight = height,
                    DecodedWidth = result.PixelWidth,
                    DecodedHeight = result.PixelHeight
                }, "ImageHelper");
            }
            else
            {
                TID3Logger.Warning("Images", "Failed to create bitmap from TagLib picture", component: "ImageHelper");
            }
            
            return result;
        }

        /// <summary>
        /// Downloads and creates a BitmapImage from URL with proper memory management
        /// </summary>
        public static async Task<BitmapImage?> CreateBitmapFromUrlAsync(string imageUrl, HttpClient httpClient, int decodePixelWidth = DEFAULT_DECODE_PIXEL_WIDTH)
        {
            if (string.IsNullOrEmpty(imageUrl) || httpClient == null)
                return null;

            try
            {
                var response = await httpClient.SafeGetAsync(imageUrl);
                if (response?.IsSuccessStatusCode != true)
                    return null;

                var imageData = await response.Content.ReadAsByteArrayAsync();
                return CreateBitmapFromBytes(imageData, decodePixelWidth);
            }
            catch (Exception ex)
            {
                TID3Logger.Error("Images", "Failed to download and create bitmap from URL", ex, new { Url = imageUrl }, "ImageHelper");
                return null;
            }
        }

        /// <summary>
        /// Downloads and creates a BitmapImage from stream response with proper memory management
        /// </summary>
        public static async Task<BitmapImage?> CreateBitmapFromHttpStreamAsync(string imageUrl, HttpClient httpClient, int decodePixelWidth = DEFAULT_DECODE_PIXEL_WIDTH)
        {
            if (string.IsNullOrEmpty(imageUrl) || httpClient == null)
                return null;

            try
            {
                var response = await httpClient.GetAsync(imageUrl);
                if (!response.IsSuccessStatusCode)
                    return null;

                // Check content length before downloading
                if (response.Content.Headers.ContentLength > MAX_IMAGE_SIZE_BYTES)
                {
                    TID3Logger.Warning("Images", "HTTP response too large, skipping", new { 
                        Url = imageUrl,
                        ContentLengthBytes = response.Content.Headers.ContentLength, 
                        MaxSizeBytes = MAX_IMAGE_SIZE_BYTES 
                    }, "ImageHelper");
                    return null;
                }

                // Convert to byte array so we can preserve original dimensions
                var imageData = await response.Content.ReadAsByteArrayAsync();
                return CreateBitmapFromBytes(imageData, decodePixelWidth);
            }
            catch (Exception ex)
            {
                TID3Logger.Error("Images", "Failed to download and create bitmap from HTTP stream", ex, new { Url = imageUrl }, "ImageHelper");
                return null;
            }
        }

        /// <summary>
        /// Safely disposes a BitmapImage if it implements IDisposable
        /// Note: BitmapImage doesn't implement IDisposable, but this provides a consistent API
        /// </summary>
        public static void SafeDispose(BitmapImage? bitmap)
        {
            // BitmapImage doesn't implement IDisposable, but Frozen images are automatically
            // garbage collected efficiently. This method is provided for API consistency
            // and potential future implementations.
            
            if (bitmap != null)
            {
                // Clear any references to help GC
                bitmap = null;
            }
        }

        /// <summary>
        /// Creates a BitmapImage from file path with proper memory management
        /// </summary>
        public static BitmapImage? CreateBitmapFromFile(string filePath, int decodePixelWidth = DEFAULT_DECODE_PIXEL_WIDTH)
        {
            if (string.IsNullOrEmpty(filePath) || !System.IO.File.Exists(filePath))
                return null;

            try
            {
                var fileInfo = new FileInfo(filePath);
                if (fileInfo.Length > MAX_IMAGE_SIZE_BYTES)
                {
                    TID3Logger.Warning("Images", "File too large, skipping", new { 
                        FilePath = filePath,
                        SizeBytes = fileInfo.Length, 
                        MaxSizeBytes = MAX_IMAGE_SIZE_BYTES 
                    }, "ImageHelper");
                    return null;
                }

                // First get original dimensions and read file bytes
                var (originalWidth, originalHeight) = GetImageDimensions(filePath);
                var originalBytes = System.IO.File.ReadAllBytes(filePath);

                // Create the decoded image
                var bitmap = new BitmapImage();
                bitmap.BeginInit();
                bitmap.UriSource = new Uri(filePath);
                bitmap.CacheOption = BitmapCacheOption.OnLoad;
                bitmap.DecodePixelWidth = Math.Min(decodePixelWidth, MAX_DECODE_PIXEL_WIDTH);
                bitmap.EndInit();
                
                // Store original dimensions and bytes as attached properties BEFORE freezing
                bitmap.SetValue(OriginalWidthProperty, originalWidth);
                bitmap.SetValue(OriginalHeightProperty, originalHeight);
                bitmap.SetValue(OriginalImageBytesProperty, originalBytes);
                
                bitmap.Freeze();
                
                return bitmap;
            }
            catch (Exception ex)
            {
                TID3Logger.Error("Images", "Failed to create bitmap from file", ex, new { FilePath = filePath }, "ImageHelper");
                return null;
            }
        }

        /// <summary>
        /// Gets the original dimensions of an image file without loading it into memory
        /// </summary>
        public static (int width, int height) GetImageDimensions(string filePath)
        {
            if (string.IsNullOrEmpty(filePath) || !System.IO.File.Exists(filePath))
                return (0, 0);

            try
            {
                using var stream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read);
                var tempImage = BitmapFrame.Create(stream, BitmapCreateOptions.DelayCreation, BitmapCacheOption.None);
                return (tempImage.PixelWidth, tempImage.PixelHeight);
            }
            catch (Exception ex)
            {
                TID3Logger.Error("Images", "Failed to get image dimensions", ex, new { FilePath = filePath }, "ImageHelper");
                return (0, 0);
            }
        }

        /// <summary>
        /// Gets the original dimensions stored in the BitmapImage attached properties
        /// </summary>
        public static (int width, int height) GetOriginalDimensions(BitmapImage? bitmap)
        {
            if (bitmap == null)
                return (0, 0);

            var originalWidth = bitmap.GetValue(OriginalWidthProperty) as int? ?? bitmap.PixelWidth;
            var originalHeight = bitmap.GetValue(OriginalHeightProperty) as int? ?? bitmap.PixelHeight;
            
            return (originalWidth, originalHeight);
        }

        /// <summary>
        /// Gets the original image bytes stored in the BitmapImage attached properties
        /// </summary>
        public static byte[]? GetOriginalImageBytes(BitmapImage? bitmap)
        {
            if (bitmap == null)
                return null;

            return bitmap.GetValue(OriginalImageBytesProperty) as byte[];
        }

        /// <summary>
        /// Gets a string describing the image size for debugging (shows original dimensions)
        /// </summary>
        public static string GetImageSizeInfo(BitmapImage? bitmap)
        {
            if (bitmap == null)
                return "null";

            var (originalWidth, originalHeight) = GetOriginalDimensions(bitmap);
            return $"{originalWidth}x{originalHeight} (displayed as {bitmap.DecodePixelWidth}px width)";
        }

        /// <summary>
        /// Simple bitmap creation from bytes without dimension detection (for debugging)
        /// </summary>
        public static BitmapImage? CreateSimpleBitmapFromBytes(byte[] imageData, int decodePixelWidth = DEFAULT_DECODE_PIXEL_WIDTH)
        {
            if (imageData == null || imageData.Length == 0)
                return null;

            try
            {
                var bitmap = new BitmapImage();
                using (var stream = new MemoryStream(imageData))
                {
                    bitmap.BeginInit();
                    bitmap.CacheOption = BitmapCacheOption.OnLoad;
                    bitmap.StreamSource = stream;
                    bitmap.DecodePixelWidth = Math.Min(decodePixelWidth, MAX_DECODE_PIXEL_WIDTH);
                    bitmap.EndInit();
                }
                bitmap.Freeze();
                
                TID3Logger.Debug("Images", "Simple bitmap created", new { 
                    PixelWidth = bitmap.PixelWidth, 
                    PixelHeight = bitmap.PixelHeight 
                }, "ImageHelper");
                return bitmap;
            }
            catch (Exception ex)
            {
                TID3Logger.Error("Images", "CreateSimpleBitmapFromBytes failed", ex, new { 
                    DataSize = imageData?.Length ?? 0 
                }, "ImageHelper");
                return null;
            }
        }
    }
}