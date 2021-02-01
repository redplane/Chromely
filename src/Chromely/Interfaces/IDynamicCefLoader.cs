using System;
using System.Runtime.InteropServices;
using Chromely.Core.Configuration;
using Chromely.Core.Infrastructure;

namespace Chromely.Interfaces
{
    public interface IDynamicCefLoader
    {
        string GetCefArchiveName(ChromelyPlatform platform,
            Architecture architecture, CefBuildNumbers build);

        void SetMacOSAppName(IChromelyConfiguration config);

        /// <summary>
        /// Download CEF runtime files.
        /// </summary>
        /// <exception cref="Exception"></exception>
        void Download(ChromelyPlatform platform);

    }
}