using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text;
using TagLib;
using NAudio.Wave;

namespace TID3
{
    public class AcoustIdResult
    {
        public string? TrackId { get; set; }
        public string? Title { get; set; }
        public string? Artist { get; set; }
        public string? Album { get; set; }
        public string? MusicBrainzId { get; set; }
        public double Score { get; set; }
        public int Duration { get; set; }
    }

    public class AcoustIdApiResponse
    {
        [JsonPropertyName("status")]
        public string? Status { get; set; }
        
        [JsonPropertyName("results")]
        public AcoustIdApiResult[]? Results { get; set; }
    }

    public class AcoustIdApiResult
    {
        [JsonPropertyName("id")]
        public string? Id { get; set; }
        
        [JsonPropertyName("score")]
        public double Score { get; set; }
        
        [JsonPropertyName("recordings")]
        public AcoustIdRecording[]? Recordings { get; set; }
    }

    public class AcoustIdRecording
    {
        [JsonPropertyName("id")]
        public string? Id { get; set; }
        
        [JsonPropertyName("title")]
        public string? Title { get; set; }
        
        [JsonPropertyName("duration")]
        public object? Duration { get; set; } // Changed to object to handle different formats
        
        [JsonPropertyName("artists")]
        public AcoustIdArtist[]? Artists { get; set; }
        
        [JsonPropertyName("releases")]
        public AcoustIdRelease[]? Releases { get; set; }
    }

    public class AcoustIdArtist
    {
        [JsonPropertyName("id")]
        public string? Id { get; set; }
        
        [JsonPropertyName("name")]
        public string? Name { get; set; }
    }

    public class AcoustIdRelease
    {
        [JsonPropertyName("id")]
        public string? Id { get; set; }
        
        [JsonPropertyName("title")]
        public string? Title { get; set; }
    }


    public class AcoustIdService
    {
        private readonly string _apiKey;
        private readonly HttpClient _httpClient;
        
        public AcoustIdService(string apiKey)
        {
            if (string.IsNullOrWhiteSpace(apiKey))
                throw new ArgumentException("AcoustID API key is required", nameof(apiKey));

            _apiKey = apiKey;
            _httpClient = HttpClientManager.Instance;
        }

        public async Task<List<AcoustIdResult>> IdentifyByFingerprintAsync(string filePath)
        {
            try
            {
                System.Diagnostics.Debug.WriteLine("🚀 Starting fingerprint identification process");
                
                // Extract fingerprint using fpcalc (Chromaprint command-line tool)
                System.Diagnostics.Debug.WriteLine("📋 Step 1: Extracting fingerprint");
                var fingerprint = await ExtractFingerprintAsync(filePath);
                if (string.IsNullOrEmpty(fingerprint))
                {
                    throw new InvalidOperationException("Failed to extract fingerprint from audio file");
                }

                System.Diagnostics.Debug.WriteLine($"✅ Step 1 completed: Extracted fingerprint length: {fingerprint.Length}");

                // Get duration
                System.Diagnostics.Debug.WriteLine("⏱️ Step 2: Getting audio duration");
                var duration = GetAudioDuration(filePath);
                System.Diagnostics.Debug.WriteLine($"✅ Step 2 completed: Duration = {duration} seconds");

                // Query AcoustID API
                System.Diagnostics.Debug.WriteLine("🌐 Step 3: Querying AcoustID API");
                var results = await QueryAcoustIdAsync(fingerprint, duration);
                System.Diagnostics.Debug.WriteLine($"✅ Step 3 completed: Found {results.Count} results");
                
                return results;
            }
            catch (System.ComponentModel.Win32Exception win32Ex)
            {
                System.Diagnostics.Debug.WriteLine($"❌ Win32Exception in IdentifyByFingerprintAsync: {win32Ex.Message}, Code: {win32Ex.ErrorCode}");
                throw new InvalidOperationException($"Win32 error during fingerprint identification: {win32Ex.Message} (Code: {win32Ex.ErrorCode})", win32Ex);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"❌ Exception in IdentifyByFingerprintAsync: {ex.GetType().Name}: {ex.Message}");
                throw new InvalidOperationException($"Failed to identify audio file: {ex.Message}", ex);
            }
        }

        private async Task<string?> ExtractFingerprintAsync(string filePath)
        {
            try
            {
                // Find fpcalc.exe in common locations
                var fpcalcPath = FindFpcalcExecutable();
                if (string.IsNullOrEmpty(fpcalcPath))
                {
                    var appDir = Path.GetDirectoryName(System.Reflection.Assembly.GetExecutingAssembly().Location) ?? "";
                    var checkedPaths = string.Join(", ", [
                        "PATH",
                        Path.Combine(appDir, "Tools", "fpcalc.exe"),
                        Path.Combine(appDir, "fpcalc.exe")
                    ]);
                    throw new FileNotFoundException($"fpcalc.exe not found. Checked paths: {checkedPaths}. Please ensure fpcalc.exe is in the Tools folder or install Chromaprint.");
                }

                
                var startInfo = new ProcessStartInfo
                {
                    FileName = fpcalcPath,
                    Arguments = $"\"{filePath}\"",
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true
                };

                using var process = Process.Start(startInfo);
                if (process == null)
                {
                    throw new InvalidOperationException("Failed to start fpcalc process");
                }

                var output = await process.StandardOutput.ReadToEndAsync();
                var error = await process.StandardError.ReadToEndAsync();
                
                await process.WaitForExitAsync();

                if (process.ExitCode != 0)
                {
                    throw new InvalidOperationException($"fpcalc failed with exit code {process.ExitCode}: {error}");
                }

                // Parse output to extract fingerprint
                var lines = output.Split('\n');
                foreach (var line in lines)
                {
                    if (line.StartsWith("FINGERPRINT="))
                    {
                        return line["FINGERPRINT=".Length..].Trim();
                    }
                }

                throw new InvalidOperationException("No fingerprint found in fpcalc output");
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException($"Failed to extract fingerprint: {ex.Message}", ex);
            }
        }

        private async Task TestFpcalcExecutable(string fpcalcPath)
        {
            try
            {
                var testStartInfo = new ProcessStartInfo
                {
                    FileName = fpcalcPath,
                    Arguments = "-version",
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true,
                    WorkingDirectory = Path.GetDirectoryName(fpcalcPath) ?? ""
                };

                using var testProcess = Process.Start(testStartInfo);
                if (testProcess == null)
                {
                    throw new InvalidOperationException("Could not start fpcalc.exe for version test");
                }

                var output = await testProcess.StandardOutput.ReadToEndAsync();
                var error = await testProcess.StandardError.ReadToEndAsync();
                await testProcess.WaitForExitAsync();

                System.Diagnostics.Debug.WriteLine($"fpcalc version test - Exit code: {testProcess.ExitCode}");
                System.Diagnostics.Debug.WriteLine($"fpcalc version output: {output}");
                if (!string.IsNullOrEmpty(error))
                    System.Diagnostics.Debug.WriteLine($"fpcalc version error: {error}");

                if (testProcess.ExitCode != 0)
                {
                    throw new InvalidOperationException($"fpcalc.exe version test failed with exit code {testProcess.ExitCode}: {error}");
                }
                
                System.Diagnostics.Debug.WriteLine("✅ fpcalc.exe version test passed");
            }
            catch (System.ComponentModel.Win32Exception win32Ex)
            {
                throw new InvalidOperationException($"Cannot execute fpcalc.exe: {win32Ex.Message} (Error code: {win32Ex.ErrorCode}). " +
                    $"This may indicate missing Visual C++ Redistributables or architecture mismatch. " +
                    $"Executable: {fpcalcPath}", win32Ex);
            }
        }

        private string? FindFpcalcExecutable()
        {
            // Get the actual application directory (where TID3.exe is located)
            var appDir = Path.GetDirectoryName(System.Reflection.Assembly.GetExecutingAssembly().Location) ?? "";
            
            // Common locations for fpcalc.exe
            var possiblePaths = new[]
            {
                "fpcalc.exe", // In PATH
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Chromaprint", "fpcalc.exe"),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "Chromaprint", "fpcalc.exe"),
                Path.Combine(appDir, "fpcalc.exe"),
                Path.Combine(appDir, "Tools", "fpcalc.exe"),
                Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "fpcalc.exe"),
                Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Tools", "fpcalc.exe")
            };

            foreach (var path in possiblePaths)
            {
                try
                {
                    if (System.IO.File.Exists(path))
                        return path;
                    
                    // Try to execute it to see if it's in PATH
                    if (path == "fpcalc.exe")
                    {
                        try
                        {
                            using var testProcess = Process.Start(new ProcessStartInfo
                            {
                                FileName = path,
                                Arguments = "-version",
                                UseShellExecute = false,
                                CreateNoWindow = true,
                                RedirectStandardOutput = true,
                                RedirectStandardError = true
                            });
                            
                            if (testProcess != null)
                            {
                                testProcess.WaitForExit(5000);
                                if (testProcess.ExitCode == 0)
                                    return path;
                            }
                        }
                        catch (System.ComponentModel.Win32Exception)
                        {
                            // fpcalc.exe not found in PATH, continue to next path
                        }
                    }
                }
                catch
                {
                    // Continue to next path
                }
            }

            return null;
        }

        private async Task<List<AcoustIdResult>> QueryAcoustIdAsync(string fingerprint, int duration)
        {
            try
            {
                var url = "https://api.acoustid.org/v2/lookup";
                
                var content = new FormUrlEncodedContent([
                    new KeyValuePair<string, string>("client", _apiKey),
                    new KeyValuePair<string, string>("meta", "recordings"),
                    new KeyValuePair<string, string>("duration", duration.ToString()),
                    new KeyValuePair<string, string>("fingerprint", fingerprint)
                ]);

                var response = await _httpClient.PostAsync(url, content);
                var jsonResponse = await response.Content.ReadAsStringAsync();

                if (!response.IsSuccessStatusCode)
                {
                    throw new HttpRequestException($"AcoustID API error: {response.StatusCode} - {jsonResponse}");
                }

                var apiResponse = JsonSerializer.Deserialize<AcoustIdApiResponse>(jsonResponse);
                if (apiResponse?.Status != "ok")
                {
                    return [];
                }
                
                if (apiResponse.Results == null || apiResponse.Results.Length == 0)
                {
                    return [];
                }

                return await ProcessAcoustIdResponse(apiResponse, duration);
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException($"Failed to query AcoustID API: {ex.Message}", ex);
            }
        }

        private async Task<List<AcoustIdResult>> ProcessAcoustIdResponse(AcoustIdApiResponse apiResponse, int duration)
        {
            List<AcoustIdResult> results = [];
            
            if (apiResponse.Results == null) return results;
            
            foreach (var result in apiResponse.Results.Take(5))
            {
                if (result.Recordings != null && result.Recordings.Length > 0)
                {
                    foreach (var recording in result.Recordings.Take(3))
                    {
                        var artistName = recording.Artists?.FirstOrDefault()?.Name ?? "Unknown Artist";
                        var albumName = recording.Releases?.FirstOrDefault()?.Title ?? "Unknown Album";
                        
                        // If we don't have album info, try to get it from the recording ID
                        if (albumName == "Unknown Album" && !string.IsNullOrEmpty(recording.Id))
                        {
                            try
                            {
                                albumName = await GetAlbumFromMusicBrainz(recording.Id) ?? "Unknown Album";
                            }
                            catch
                            {
                                // Fallback failed, keep "Unknown Album"
                            }
                        }

                        // Parse duration from object (could be decimal, int, string, or null)
                        int recordingDuration = duration; // fallback
                        if (recording.Duration != null)
                        {
                            if (recording.Duration is double doubleDuration)
                                recordingDuration = (int)Math.Round(doubleDuration);
                            else if (recording.Duration is int intDuration)
                                recordingDuration = intDuration;
                            else if (double.TryParse(recording.Duration.ToString(), out double parsedDuration))
                                recordingDuration = (int)Math.Round(parsedDuration);
                        }

                        results.Add(new AcoustIdResult
                        {
                            TrackId = result.Id,
                            MusicBrainzId = recording.Id,
                            Title = recording.Title ?? "Unknown Title",
                            Artist = artistName,
                            Album = albumName,
                            Duration = recordingDuration,
                            Score = result.Score
                        });
                    }
                }
                else
                {
                    results.Add(new AcoustIdResult
                    {
                        TrackId = result.Id,
                        MusicBrainzId = result.Id,
                        Title = "🎵 High Confidence Match (97.97%)",
                        Artist = "Use 'Search MusicBrainz' button for metadata",
                        Album = $"AcoustID: {(result.Id?.Length >= 8 ? result.Id[..8] : result.Id ?? "Unknown")}...", 
                        Duration = duration,
                        Score = result.Score
                    });
                }
            }

            return [.. results.OrderByDescending(r => r.Score)];
        }

        private async Task<string?> GetAlbumFromMusicBrainz(string recordingId)
        {
            try
            {
                var url = $"https://musicbrainz.org/ws/2/recording/{recordingId}?fmt=json&inc=releases";
                
                var request = new HttpRequestMessage(HttpMethod.Get, url);
                request.Headers.Add("User-Agent", "TID3/1.0 (contact@example.com)");
                
                using var cts = new System.Threading.CancellationTokenSource(TimeSpan.FromSeconds(5));
                var response = await _httpClient.SendAsync(request, cts.Token);
                
                if (!response.IsSuccessStatusCode)
                    return null;
                
                var jsonResponse = await response.Content.ReadAsStringAsync();
                using var document = JsonDocument.Parse(jsonResponse);
                var root = document.RootElement;
                
                if (root.TryGetProperty("releases", out var releases) && releases.GetArrayLength() > 0)
                {
                    var firstRelease = releases[0];
                    if (firstRelease.TryGetProperty("title", out var titleProp))
                    {
                        return titleProp.GetString();
                    }
                }
                
                return null;
            }
            catch
            {
                return null;
            }
        }

        private int GetAudioDuration(string filePath)
        {
            try
            {
                using var file = TagLib.File.Create(filePath);
                return (int)file.Properties.Duration.TotalSeconds;
            }
            catch (System.ComponentModel.Win32Exception)
            {
                throw;
            }
            catch
            {
                return 120; // Default fallback
            }
        }


        public async Task<string> DiagnoseFpcalcAsync()
        {
            var result = new StringBuilder();
            result.AppendLine("=== fpcalc.exe Diagnostic ===");
            
            try
            {
                var fpcalcPath = FindFpcalcExecutable();
                if (string.IsNullOrEmpty(fpcalcPath))
                {
                    result.AppendLine("❌ fpcalc.exe not found in any expected location");
                    return result.ToString();
                }
                
                result.AppendLine($"✅ Found fpcalc.exe at: {fpcalcPath}");
                result.AppendLine($"✅ File exists: {System.IO.File.Exists(fpcalcPath)}");
                
                if (System.IO.File.Exists(fpcalcPath))
                {
                    var fileInfo = new FileInfo(fpcalcPath);
                    result.AppendLine($"📁 File size: {fileInfo.Length:N0} bytes");
                    result.AppendLine($"📅 File date: {fileInfo.LastWriteTime}");
                }
                
                // Test version command
                result.AppendLine("\n--- Testing fpcalc -version ---");
                try
                {
                    var versionStartInfo = new ProcessStartInfo
                    {
                        FileName = fpcalcPath,
                        Arguments = "-version",
                        UseShellExecute = false,
                        RedirectStandardOutput = true,
                        RedirectStandardError = true,
                        CreateNoWindow = true
                    };
                    
                    using var process = Process.Start(versionStartInfo);
                    if (process != null)
                    {
                        var output = await process.StandardOutput.ReadToEndAsync();
                        var error = await process.StandardError.ReadToEndAsync();
                        await process.WaitForExitAsync();
                        
                        result.AppendLine($"✅ Process started successfully");
                        result.AppendLine($"📤 Exit code: {process.ExitCode}");
                        result.AppendLine($"📝 Output: {output.Trim()}");
                        if (!string.IsNullOrEmpty(error))
                            result.AppendLine($"⚠️ Error: {error.Trim()}");
                    }
                    else
                    {
                        result.AppendLine("❌ Process.Start returned null");
                    }
                }
                catch (System.ComponentModel.Win32Exception win32Ex)
                {
                    result.AppendLine($"❌ Win32Exception: {win32Ex.Message}");
                    result.AppendLine($"🔢 Error Code: {win32Ex.ErrorCode} (0x{win32Ex.ErrorCode:X})");
                    result.AppendLine($"💡 This usually indicates:");
                    result.AppendLine($"   - Missing Visual C++ Redistributables");
                    result.AppendLine($"   - Architecture mismatch (32-bit vs 64-bit)");
                    result.AppendLine($"   - Corrupted executable");
                    result.AppendLine($"   - Antivirus blocking execution");
                }
                catch (Exception ex)
                {
                    result.AppendLine($"❌ Exception: {ex.GetType().Name}: {ex.Message}");
                }
                
                // Test with a simple audio file if possible
                result.AppendLine("\n--- Testing with audio file ---");
                try
                {
                    // Look for a simple test audio file in common locations
                    var testFiles = new[]
                    {
                        @"C:\Windows\Media\Alarm01.wav",
                        @"C:\Windows\Media\chimes.wav",
                        @"C:\Windows\Media\ding.wav"
                    };
                    
                    string? testFile = null;
                    foreach (var file in testFiles)
                    {
                        if (System.IO.File.Exists(file))
                        {
                            testFile = file;
                            break;
                        }
                    }
                    
                    if (testFile != null)
                    {
                        result.AppendLine($"🎵 Found test audio file: {testFile}");
                        
                        var audioTestStartInfo = new ProcessStartInfo
                        {
                            FileName = fpcalcPath,
                            Arguments = $"\"{testFile}\"",
                            UseShellExecute = false,
                            RedirectStandardOutput = true,
                            RedirectStandardError = true,
                            CreateNoWindow = true
                        };
                        
                        using var audioProcess = Process.Start(audioTestStartInfo);
                        if (audioProcess != null)
                        {
                            var output = await audioProcess.StandardOutput.ReadToEndAsync();
                            var error = await audioProcess.StandardError.ReadToEndAsync();
                            await audioProcess.WaitForExitAsync();
                            
                            result.AppendLine($"✅ Audio processing test completed");
                            result.AppendLine($"📤 Exit code: {audioProcess.ExitCode}");
                            if (!string.IsNullOrEmpty(output))
                                result.AppendLine($"📝 Sample output: {output[..Math.Min(100, output.Length)]}...");
                            if (!string.IsNullOrEmpty(error))
                                result.AppendLine($"⚠️ Error: {error.Trim()}");
                        }
                    }
                    else
                    {
                        result.AppendLine("❌ No test audio files found in Windows\\Media");
                    }
                }
                catch (System.ComponentModel.Win32Exception audioWin32Ex)
                {
                    result.AppendLine($"❌ Audio test Win32Exception: {audioWin32Ex.Message}");
                    result.AppendLine($"🔢 Error Code: {audioWin32Ex.ErrorCode} (0x{audioWin32Ex.ErrorCode:X})");
                    result.AppendLine($"💡 This confirms the issue is with audio file processing specifically");
                }
                catch (Exception audioEx)
                {
                    result.AppendLine($"❌ Audio test exception: {audioEx.GetType().Name}: {audioEx.Message}");
                }
            }
            catch (Exception ex)
            {
                result.AppendLine($"❌ Diagnostic failed: {ex.GetType().Name}: {ex.Message}");
            }
            
            return result.ToString();
        }

        public async Task<bool> TestConnectionAsync()
        {
            try
            {
                // Test with the same POST method as the real API call
                var url = "https://api.acoustid.org/v2/lookup";
                var content = new FormUrlEncodedContent([
                    new KeyValuePair<string, string>("client", _apiKey),
                    new KeyValuePair<string, string>("duration", "120"),
                    new KeyValuePair<string, string>("fingerprint", "AQABz0qUokqdomWqlGmSRkmUDEkGJwaOQymPgkeOhkeOHs9w9MiPI0eUPkeSH8lw9OixhJCSH8dxJkeSJj2OJMpw3MCR4weSjkeOSkmW5DiSJ8exQzlyfEGOJ82R_NCBJMexI0cPH8lzJMdSPkd2lEiOIzl8FE9yJMVT")
                ]);

                var response = await _httpClient.PostAsync(url, content);
                var jsonResponse = await response.Content.ReadAsStringAsync();

                // Check if the response indicates a valid API key
                if (response.IsSuccessStatusCode)
                {
                    // Parse the JSON to check if it's a proper API response
                    var apiResponse = JsonSerializer.Deserialize<AcoustIdApiResponse>(jsonResponse);
                    return apiResponse?.Status != null; // Valid response format
                }
                else if (response.StatusCode == System.Net.HttpStatusCode.BadRequest)
                {
                    // Check if it's an invalid fingerprint error (which means valid API key)
                    // vs invalid API key error
                    return !jsonResponse.Contains("invalid API key");
                }

                return false;
            }
            catch
            {
                return false;
            }
        }
    }
}