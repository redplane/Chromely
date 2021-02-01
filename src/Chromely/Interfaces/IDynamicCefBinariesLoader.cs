using System.Collections.Generic;
using System.Runtime.InteropServices;
using Chromely.Core.Configuration;
using Chromely.Core.Infrastructure;

namespace Chromely.Interfaces
{
    public interface IDynamicCefBinariesLoader
    {
        #region Methods

        /// <summary>Load Cef binaries</summary>
        /// <param name="config">The chromely configuration.</param>
        /// <returns>The list of temporary files generated</returns>
        List<string> Load(IChromelyConfiguration config);

        /// <summary>
        /// The delete temp files.
        /// </summary>
        /// <param name="tempFiles">
        /// The temp files.
        /// </param>
        void DeleteTempFiles(List<string> tempFiles);

        #endregion
    }
}