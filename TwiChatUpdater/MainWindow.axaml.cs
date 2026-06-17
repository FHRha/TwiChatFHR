using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Threading;

namespace TwiChatUpdater;

public partial class MainWindow : Window
{
    private List<GitHubRelease> _releases = new();
    private GitHubRelease? _selectedRelease;
    private readonly HttpClient _httpClient;

    public MainWindow()
    {
        InitializeComponent();
        
        _httpClient = new HttpClient();
        _httpClient.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("TwiChatUpdater", "1.0"));

        this.Loaded += (s, e) => {
            this.Topmost = true;
            this.Topmost = false;
            this.Activate();
        };

        LoadReleasesAsync();
    }

    private async void LoadReleasesAsync()
    {
        try
        {
            StatusTextBlock.Text = "Загрузка релизов...";
            var response = await _httpClient.GetStringAsync("https://api.github.com/repos/FHRha/TwiChatFHR/releases");
            _releases = JsonSerializer.Deserialize<List<GitHubRelease>>(response) ?? new List<GitHubRelease>();

            if (_releases.Count == 0)
            {
                StatusTextBlock.Text = "Релизы не найдены.";
                return;
            }

            foreach (var release in _releases)
            {
                VersionComboBox.Items.Add(release.Name ?? release.TagName);
            }

            VersionComboBox.SelectedIndex = 0;
            StatusTextBlock.Text = "";
        }
        catch (Exception ex)
        {
            StatusTextBlock.Text = $"Ошибка загрузки: {ex.Message}";
        }
    }

    private void VersionComboBox_SelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (VersionComboBox.SelectedIndex >= 0 && VersionComboBox.SelectedIndex < _releases.Count)
        {
            _selectedRelease = _releases[VersionComboBox.SelectedIndex];
            ChangelogMarkdownViewer.Markdown = _selectedRelease.Body;
        }
    }

    private async void UpdateButton_Click(object? sender, RoutedEventArgs e)
    {
        if (_selectedRelease == null) return;

        var asset = _selectedRelease.Assets.Find(a => a.Name.EndsWith(".zip"));
        if (asset == null)
        {
            StatusTextBlock.Text = "ZIP архив не найден в этом релизе.";
            return;
        }

        UpdateButton.IsEnabled = false;
        VersionComboBox.IsEnabled = false;
        UpdateProgressBar.IsVisible = true;
        UpdateProgressBar.Value = 0;

        try
        {
            // Close main app if running
            foreach (var proc in System.Diagnostics.Process.GetProcessesByName("TwitchChatCore"))
            {
                try { proc.Kill(); proc.WaitForExit(3000); } catch { }
            }

            StatusTextBlock.Text = "Скачивание...";
            string tempZip = Path.Combine(Path.GetTempPath(), "TwiChatUpdate.zip");
            
            using (var response = await _httpClient.GetAsync(asset.BrowserDownloadUrl, HttpCompletionOption.ResponseHeadersRead))
            using (var stream = await response.Content.ReadAsStreamAsync())
            using (var fileStream = new FileStream(tempZip, FileMode.Create, FileAccess.Write, FileShare.None))
            {
                var totalBytes = response.Content.Headers.ContentLength ?? -1L;
                var buffer = new byte[8192];
                long totalRead = 0;
                int bytesRead;

                while ((bytesRead = await stream.ReadAsync(buffer, 0, buffer.Length)) > 0)
                {
                    await fileStream.WriteAsync(buffer, 0, bytesRead);
                    totalRead += bytesRead;
                    if (totalBytes != -1)
                    {
                        var progress = (double)totalRead / totalBytes * 100;
                        Dispatcher.UIThread.Post(() => UpdateProgressBar.Value = progress);
                    }
                }
            }

            StatusTextBlock.Text = "Распаковка...";
            UpdateProgressBar.Value = 100;
            UpdateProgressBar.IsIndeterminate = true;

            string extractPath = Path.Combine(Path.GetTempPath(), "TwiChatExtract");
            if (Directory.Exists(extractPath)) Directory.Delete(extractPath, true);
            ZipFile.ExtractToDirectory(tempZip, extractPath);

            StatusTextBlock.Text = "Установка...";
            
            string appDir = Path.GetDirectoryName(Environment.ProcessPath) ?? AppDomain.CurrentDomain.BaseDirectory;
            
            CopyDirectory(extractPath, appDir);

            // Clean up
            File.Delete(tempZip);
            Directory.Delete(extractPath, true);

            StatusTextBlock.Text = "Обновление завершено! Запуск...";
            
            var exePath = Path.Combine(appDir, "TwitchChatCore.exe");
            if (File.Exists(exePath))
            {
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                {
                    FileName = exePath,
                    UseShellExecute = true
                });
            }
            
            Environment.Exit(0);
        }
        catch (Exception ex)
        {
            StatusTextBlock.Text = $"Ошибка обновления: {ex.Message}";
            UpdateProgressBar.IsIndeterminate = false;
            UpdateButton.IsEnabled = true;
            VersionComboBox.IsEnabled = true;
        }
    }

    private void CopyDirectory(string sourceDir, string destinationDir)
    {
        var dir = new DirectoryInfo(sourceDir);
        DirectoryInfo[] dirs = dir.GetDirectories();
        Directory.CreateDirectory(destinationDir);

        foreach (FileInfo file in dir.GetFiles())
        {
            string targetFilePath = Path.Combine(destinationDir, file.Name);
            // Skip updater itself if it's currently running, though we can't overwrite ourselves
            if (file.Name.Equals("TwiChatUpdater.exe", StringComparison.OrdinalIgnoreCase) ||
                file.Name.Equals("TwiChatUpdater.dll", StringComparison.OrdinalIgnoreCase))
                continue;
                
            file.CopyTo(targetFilePath, true);
        }

        foreach (DirectoryInfo subDir in dirs)
        {
            // IMPORTANT: Skip cache directory so settings aren't overwritten (though releases probably don't have one)
            if (subDir.Name.Equals("cache", StringComparison.OrdinalIgnoreCase))
                continue;

            string newDestinationDir = Path.Combine(destinationDir, subDir.Name);
            CopyDirectory(subDir.FullName, newDestinationDir);
        }
    }
}

public class GitHubRelease
{
    [JsonPropertyName("name")]
    public string? Name { get; set; }
    
    [JsonPropertyName("tag_name")]
    public string? TagName { get; set; }
    
    [JsonPropertyName("body")]
    public string? Body { get; set; }
    
    [JsonPropertyName("assets")]
    public List<GitHubAsset> Assets { get; set; } = new();
}

public class GitHubAsset
{
    [JsonPropertyName("name")]
    public string? Name { get; set; }
    
    [JsonPropertyName("browser_download_url")]
    public string? BrowserDownloadUrl { get; set; }
}