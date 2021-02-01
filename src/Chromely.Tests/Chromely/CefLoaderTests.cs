using System.IO;
using System.Net;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using Chromely.Core.Configuration;
using Chromely.Core.Infrastructure;
using Chromely.Interfaces;
using Chromely.Loader;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using Moq.Protected;
using Serilog;
using Xunit;

namespace Chromely.Tests.Chromely
{
    public class CefLoaderTests
    {
        #region Methods

        [Fact]
        public async Task LoadChromiumLinuxArmDownloadUrlAsync_Returns_LinuxArmDownloadUrl()
        {
            var apiContentPath = Path.Combine(Directory.GetCurrentDirectory(), "Assets", "cef-builds.json");
            var apiContent = await File.ReadAllTextAsync(apiContentPath);

            var services = new ServiceCollection();
            var handlerMock = new Mock<HttpMessageHandler>(MockBehavior.Strict);
            handlerMock
                .Protected()
                // Setup the PROTECTED method to mock
                .Setup<Task<HttpResponseMessage>>(
                    "SendAsync",
                    ItExpr.IsAny<HttpRequestMessage>(),
                    ItExpr.IsAny<CancellationToken>()
                )
                // prepare the expected response of the mocked http call
                .ReturnsAsync(new HttpResponseMessage()
                {
                    StatusCode = HttpStatusCode.OK,
                    Content = new StringContent(apiContent),
                });

            var httpClient = new HttpClient(handlerMock.Object);
            services.AddSingleton<HttpClient>(httpClient);

            var logger = new LoggerConfiguration()
                .WriteTo.Console()
                .CreateLogger();
            services.AddSingleton<ILogger>(logger);

            var configuration = DefaultConfiguration.CreateForRuntimePlatform();
            services.AddSingleton(configuration);

            var baseUrl = "https://test-host.spotifycdn.com";

            var serviceProvider = services.BuildServiceProvider();
            var moqCefLoader = new Mock<CefLoader>(() => new CefLoader(serviceProvider));
            moqCefLoader.CallBase = true;
            moqCefLoader.Protected().Setup<string>("GetCdnBaseUrl")
                .Returns(baseUrl);

            var linuxPlatform = ChromelyPlatform.Linux;
            var cefBuildNumber = new CefBuildNumbers("87.1.14+ga29e9a3+chromium-87.0.4280.141", "87.0.4280.141");
            var url = await moqCefLoader.Object.LoadChromiumDownloadUrlAsync(linuxPlatform, Architecture.Arm, cefBuildNumber);
            Assert.Equal($"{baseUrl}/cef_binary_{cefBuildNumber.CefVersion}_linuxarm_minimal.tar.bz2", url);
        }

        #endregion
    }
}