﻿//
// This file manually written from cef/include/internal/cef_types.h.
// C API name: cef_ssl_version_t.
//
#pragma warning disable 1591
// ReSharper disable once CheckNamespace
namespace Xilium.CefGlue
{
    /// <summary>
    /// Supported SSL version values. See net/ssl/ssl_connection_status_flags.h
    /// for more information.
    /// </summary>
    public enum CefSslVersion
    {
        /// <summary>
        /// Unknown SSL version.
        /// </summary>
        Unknown = 0,
        Ssl2 = 1,
        Ssl3 = 2,
        Tls1 = 3,
        Tls1_1 = 4,
        Tls1_2 = 5,
        // Reserve 6 for TLS 1.3.
        Quic = 7,
    }
}
