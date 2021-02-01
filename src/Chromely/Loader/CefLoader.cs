using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Threading.Tasks;
using Chromely.Constants;
using Chromely.Core.Configuration;
using Chromely.Core.Infrastructure;
using Chromely.Interfaces;
using Chromely.Models;
using Chromely.ViewModels;
using ICSharpCode.SharpZipLib.BZip2;
using ICSharpCode.SharpZipLib.Tar;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Chromely.Loader
{
    public class CefLoader : IDynamicCefLoader
    {
        #region Services

        // ReSharper disable once InconsistentNaming
        protected readonly HttpClient _httpClient;

        #endregion

        #region Constructor

        public CefLoader(IServiceProvider serviceProvider)
        {
            _logger = serviceProvider.GetService<ILogger>();
            _platform = serviceProvider.GetService<IChromelyConfiguration>().Platform;
            _httpClient = serviceProvider.GetService<HttpClient>();

            _build = ChromelyRuntime.GetExpectedCefBuild();

            _lastPercent = 0;
            _numberOfParallelDownloads = Environment.ProcessorCount;

            _tempTarStream = Path.GetTempFileName();
            _tempBz2File = Path.GetTempFileName();
            _tempTarFile = Path.GetTempFileName();
            _tempDirectory = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        }

        #endregion

        #region Properties

        private readonly ILogger _logger;

        private readonly ChromelyPlatform _platform;

        private readonly CefBuildNumbers _build;

        private readonly string _tempTarStream;

        private readonly string _tempBz2File;

        private readonly string _tempTarFile;

        private readonly string _tempDirectory;

        private string _archiveName;

        private string _folderName;

        private string _downloadUrl;

        private long _downloadLength;

        private readonly int _numberOfParallelDownloads;

        private int _lastPercent;

        private readonly string _macOSConfigFile = "Info.plist";

        private readonly string _macOSDefaultAppName = "Chromium Embedded Framework";

        /// <summary>
        ///     Gets or sets the timeout for the CEF download in minutes.
        /// </summary>
        // ReSharper disable once MemberCanBePrivate.Global
        // ReSharper disable once AutoPropertyCanBeMadeGetOnly.Global
        public int DownloadTimeoutMinutes { get; set; } = 10;

        #endregion

        #region Methods

        /// <summary>
        ///     <inheritdoc />
        /// </summary>
        /// <param name="platform"></param>
        /// <param name="architecture"></param>
        /// <param name="build"></param>
        /// <returns></returns>
        public virtual string GetCefArchiveName(ChromelyPlatform platform, Architecture architecture,
            CefBuildNumbers build)
        {
            var requestUrl = $"{GetCdnBaseUrl()}/index.json";

            var httpResponseMessage = _httpClient.GetAsync(requestUrl, HttpCompletionOption.ResponseContentRead).Result;
            if (!httpResponseMessage.IsSuccessStatusCode)
                return null;

            var httpContent = httpResponseMessage.Content;
            if (httpContent == null)
                return null;

            var szContent = httpContent.ReadAsStringAsync().Result;
            var osToBuildsRaw = JsonSerializer.Deserialize(szContent, typeof(Dictionary<string, OsBuildViewModel>));
            if (osToBuildsRaw == null)
                return null;

            var osToBuilds = osToBuildsRaw as Dictionary<string, OsBuildViewModel>;
            if (osToBuilds == null)
                return null;

            // Build platform identifier
            var platformId = GetPlatformIdentifier(platform, architecture);
            if (!osToBuilds.ContainsKey(platformId))
                return null;

            var osBuilds = osToBuilds[platformId];
            var designatedBuild = osBuilds.Versions?.Where(x =>
                    x.Version.Equals(build.CefVersion) && x.ChromiumVersion.Equals(build.ChromiumVersion) &&
                    CefBuildChannels.Stable.Equals(x.Channel))
                .FirstOrDefault();

            if (designatedBuild == null)
                return null;

            var attachment = designatedBuild.Binaries?.FirstOrDefault(x => CefBuildTypes.Minimal.Equals(x.Type));
            return attachment?.Name;
        }

        public virtual void SetMacOSAppName(IChromelyConfiguration config)
        {
            if (config.Platform != ChromelyPlatform.MacOSX)
                return;

            try
            {
                var appName = config.AppName;
                if (string.IsNullOrWhiteSpace(appName)) appName = Assembly.GetEntryAssembly()?.GetName().Name;

                var appDirectory = AppDomain.CurrentDomain.BaseDirectory;
                var pInfoFile = Path.Combine(appDirectory, _macOSConfigFile);
                if (File.Exists(pInfoFile))
                {
                    var pInfoFileText = File.ReadAllText(pInfoFile);
                    pInfoFileText = pInfoFileText.Replace(_macOSDefaultAppName, appName);
                    File.WriteAllText(_macOSConfigFile, pInfoFileText);
                }
            }
            catch (Exception exception)
            {
                // Suppress error.
                _logger?.LogError(exception, exception.Message);
            }
        }

        /// <summary>
        ///     Download CEF runtime files.
        /// </summary>
        /// <exception cref="Exception"></exception>
        public void Download(ChromelyPlatform platform)
        {
            try
            {
                var watch = new Stopwatch();
                watch.Start();
                GetDownloadUrl();
                if (!ParallelDownload()) Download();
                _logger?.LogInformation($"CefLoader: Download took {watch.ElapsedMilliseconds}ms");
                watch.Restart();
                DecompressArchive();
                _logger?.LogInformation($"CefLoader: Decompressing archive took {watch.ElapsedMilliseconds}ms");
                watch.Restart();
                CopyFilesToAppDirectory();
                _logger?.LogInformation($"CefLoader: Copying files took {watch.ElapsedMilliseconds}ms");
            }
            catch (Exception ex)
            {
                _logger?.LogError("CefLoader: " + ex.Message);
                throw;
            }
            finally
            {
                if (!string.IsNullOrEmpty(_tempBz2File)) File.Delete(_tempBz2File);

                if (!string.IsNullOrEmpty(_tempTarStream)) File.Delete(_tempTarStream);

                if (!string.IsNullOrEmpty(_tempTarFile)) File.Delete(_tempTarFile);

                if (!string.IsNullOrEmpty(_tempDirectory) && Directory.Exists(_tempDirectory))
                    Directory.Delete(_tempDirectory, true);
            }
        }

        public virtual Task<string> LoadChromiumDownloadUrlAsync(ChromelyPlatform platform, Architecture architecture,
            CefBuildNumbers build)
        {
            var archiveName = GetCefArchiveName(platform, architecture, build);
            return Task.FromResult($"{GetCdnBaseUrl()}/{archiveName}");
        }

        #endregion

        #region Internal methods

        /// <summary>
        ///     Build download url from archive name.
        /// </summary>
        protected void GetDownloadUrl()
        {
            _archiveName = LoadChromiumDownloadUrlAsync(_platform, GetSystemArchitecture(), _build)
                .Result;
            _folderName = _archiveName
                .Replace("%2B", "+")
                .Replace(".tar.bz2", "");
            _downloadUrl = LoadChromiumDownloadUrlAsync(_platform, GetSystemArchitecture(), _build).Result;
            _logger?.LogInformation($"CefLoader: Found download URL {_downloadUrl}");
        }

        private bool ParallelDownload()
        {
            try
            {
                var webRequest = WebRequest.Create(_downloadUrl);
                webRequest.Method = "HEAD";
                using (var webResponse = webRequest.GetResponse())
                {
                    _downloadLength = long.Parse(webResponse.Headers.Get("Content-Length"));
                }

                _logger?.LogInformation(
                    $"CefLoader: Parallel download {_archiveName}, {_downloadLength / (1024 * 1024)}MB");

                // Calculate ranges  
                var readRanges = new List<DownloadRange>();
                for (var chunk = 0; chunk < _numberOfParallelDownloads - 1; chunk++)
                {
                    var range = new DownloadRange(chunk * (_downloadLength / _numberOfParallelDownloads),
                        (chunk + 1) * (_downloadLength / _numberOfParallelDownloads) - 1);
                    readRanges.Add(range);
                }

                readRanges.Add(new DownloadRange(readRanges.Any() ? readRanges.Last().End + 1 : 0, _downloadLength - 1));

                // Parallel download
                var tempFilesDictionary = new ConcurrentDictionary<long, string>();

                Parallel.ForEach(readRanges, new ParallelOptions { MaxDegreeOfParallelism = _numberOfParallelDownloads },
                    readRange =>
                    {
                        var httpWebRequest = WebRequest.Create(_downloadUrl) as HttpWebRequest;
                        // ReSharper disable once PossibleNullReferenceException
                        httpWebRequest.Method = "GET";
                        httpWebRequest.Timeout = (int)TimeSpan.FromMinutes(DownloadTimeoutMinutes).TotalMilliseconds;
                        httpWebRequest.AddRange(readRange.Start, readRange.End);
                        using (var httpWebResponse = httpWebRequest.GetResponse() as HttpWebResponse)
                        {
                            var tempFilePath = Path.GetTempFileName();
                            _logger?.LogInformation(
                                $"CefLoader: Load {tempFilePath} ({readRange.Start}..{readRange.End})");
                            using (var fileStream = new FileStream(tempFilePath, FileMode.Create, FileAccess.Write,
                                FileShare.Write))
                            {
                                httpWebResponse?.GetResponseStream()?.CopyTo(fileStream);
                                tempFilesDictionary.TryAdd(readRange.Start, tempFilePath);
                            }
                        }
                    });

                // Merge to single file
                if (File.Exists(_tempBz2File))
                    File.Delete(_tempBz2File);

                using (var destinationStream = new FileStream(_tempBz2File, FileMode.Append))
                {
                    foreach (var tempFile in tempFilesDictionary.OrderBy(b => b.Key))
                    {
                        var tempFileBytes = File.ReadAllBytes(tempFile.Value);
                        destinationStream.Write(tempFileBytes, 0, tempFileBytes.Length);
                        File.Delete(tempFile.Value);
                    }
                }

                return true;
            }
            catch (Exception ex)
            {
                _logger?.LogError("CefLoader.ParallelDownload: " + ex.Message);
            }

            return false;
        }

        private void Download()
        {
            using var client = new WebClient();

            if (File.Exists(_tempBz2File))
                File.Delete(_tempBz2File);

            _logger?.LogInformation($"CefLoader: Loading {_tempBz2File}");
            client.DownloadProgressChanged += Client_DownloadProgressChanged;

            client.DownloadFile(_downloadUrl, _tempBz2File);
        }

        private void DecompressArchive()
        {
            _logger?.LogInformation("CefLoader: Decompressing BZ2 archive");
            using (var tarStream = new FileStream(_tempTarStream, FileMode.Create, FileAccess.ReadWrite))
            {
                using (var inStream = new FileStream(_tempBz2File, FileMode.Open, FileAccess.Read, FileShare.None))
                {
                    BZip2.Decompress(inStream, tarStream, false, DecompressProgressChanged);
                }

                _logger?.LogInformation("CefLoader: Decompressing TAR archive");
                tarStream.Seek(0, SeekOrigin.Begin);
                var tar = TarArchive.CreateInputTarArchive(tarStream);
                tar.ProgressMessageEvent += (archive, entry, message) =>
                    _logger?.LogInformation("CefLoader: Extracting " + entry.Name);

                Directory.CreateDirectory(_tempDirectory);
                tar.ExtractContents(_tempDirectory);
            }
        }

        private void CopyFilesToAppDirectory()
        {
            _logger?.LogInformation("CefLoader: Copy files to application directory");
            // now we have all files in the temporary directory
            // we have to copy the 'Release' folder to the application directory
            var srcPathRelease = Path.Combine(_tempDirectory, _folderName, "Release");
            var appDirectory = AppDomain.CurrentDomain.BaseDirectory;
            if (_platform != ChromelyPlatform.MacOSX)
            {
                CopyDirectory(srcPathRelease, appDirectory);

                var srcPathResources = Path.Combine(_tempDirectory, _folderName, "Resources");
                CopyDirectory(srcPathResources, appDirectory);
            }

            if (_platform != ChromelyPlatform.MacOSX) return;

            var cefFrameworkFolder = Path.Combine(srcPathRelease, "Chromium Embedded Framework.framework");

            // rename Chromium Embedded Framework to libcef.dylib and copy to destination folder
            var frameworkFile = Path.Combine(cefFrameworkFolder, "Chromium Embedded Framework");
            var libcefFile = Path.Combine(appDirectory, "libcef.dylib");
            var libcefdylibInfo = new FileInfo(frameworkFile);
            libcefdylibInfo.CopyTo(libcefFile, true);

            // Copy Libraries files
            var librariesFolder = Path.Combine(cefFrameworkFolder, "Libraries");
            CopyDirectory(librariesFolder, appDirectory);

            // Copy Resource files
            var resourcesFolder = Path.Combine(cefFrameworkFolder, "Resources");
            CopyDirectory(resourcesFolder, appDirectory);
        }

        private void DecompressProgressChanged(int percent)
        {
            if (percent < 10) _lastPercent = 0;
            if (percent % 10 != 0 || percent == _lastPercent) return;
            _lastPercent = percent;
            _logger?.LogInformation($"CefLoader: Decompress progress = {percent}%");
        }

        private void Client_DownloadProgressChanged(object sender, DownloadProgressChangedEventArgs e)
        {
            var percent = (int)(e.BytesReceived * 100.0 / e.TotalBytesToReceive);
            if (percent < 10) _lastPercent = 0;
            if (percent % 10 != 0 || percent == _lastPercent) return;

            _lastPercent = percent;
            _logger?.LogInformation($"CefLoader: Download progress = {percent}%");
        }

        private static void CopyDirectory(string sourceDirName, string destDirName)
        {
            // Get the subdirectories for the specified directory.
            var dirInfo = new DirectoryInfo(sourceDirName);
            var dirs = dirInfo.GetDirectories();

            // If the destination directory doesn't exist, create it.
            if (!Directory.Exists(destDirName)) Directory.CreateDirectory(destDirName);

            // Get the files in the directory and copy them to the new location.
            var files = dirInfo.GetFiles();
            foreach (var file in files)
            {
                var tempPath = Path.Combine(destDirName, file.Name);
                file.CopyTo(tempPath, true);
            }

            foreach (var subDir in dirs)
            {
                var tempPath = Path.Combine(destDirName, subDir.Name);
                CopyDirectory(subDir.FullName, tempPath);
            }
        }

        #endregion

        #region Internal methods

        /// <summary>
        ///     Get system architecture
        /// </summary>
        /// <returns></returns>
        protected virtual Architecture GetSystemArchitecture()
        {
            return RuntimeInformation.ProcessArchitecture;
        }

        /// <summary>
        ///     Get platform identifier from platform information & architecture.
        /// </summary>
        /// <param name="platform"></param>
        /// <param name="architecture"></param>
        /// <returns></returns>
        protected virtual string GetPlatformIdentifier(ChromelyPlatform platform, Architecture architecture)
        {
            var arch = architecture.ToString()
                .Replace("X64", "64")
                .Replace("X86", "32");

            return (platform + arch).ToLower();
        }

        /// <summary>
        ///     Base url about cef build cdn
        /// </summary>
        /// <returns></returns>
        protected virtual string GetCdnBaseUrl()
        {
            return "https://cef-builds.spotifycdn.com";
        }

        #endregion
    }
}