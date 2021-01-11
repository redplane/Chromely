using System;
using System.Runtime.InteropServices;
using Chromely.Core.Configuration;
using Chromely.Core.Infrastructure;

namespace Chromely.Interfaces
{
    public interface IDynamicCefLoader
    {
        string FindCefArchiveName(ChromelyPlatform platform,
            Architecture processArchitecture, CefBuildNumbers build);

        void SetMacOSAppName(IChromelyConfiguration config);

        /// <summary>
        /// Download CEF runtime files.
        /// </summary>
        /// <exception cref="Exception"></exception>
        void Download(ChromelyPlatform platform);

    }
}