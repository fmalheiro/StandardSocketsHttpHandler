// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System.Collections.Generic;
using System.IO;
using System.Net.Security;
using System.Net.Test.Common;
using System.Runtime.InteropServices;
using System.Security.Authentication;
using System.Threading.Tasks;
using Xunit;

namespace System.Net.Http.Functional.Tests
{
    using Configuration = System.Net.Test.Common.Configuration;

    [SkipOnTargetFramework(TargetFrameworkMonikers.Uap, "SslProtocols not supported on UAP")]
    public abstract partial class HttpClientHandler_SslProtocols_Test : HttpClientTestBase
    {
        [Fact]
        public void DefaultProtocols_MatchesExpected()
        {
            using (StandardSocketsHttpHandler handler = CreateSocketsHttpHandler())
            {
                Assert.Equal(SslProtocols.None, handler.SslOptions.EnabledSslProtocols);
            }
        }

        [Theory]
        [InlineData(SslProtocols.None)]
        [InlineData(SslProtocols.Tls)]
        [InlineData(SslProtocols.Tls11)]
        [InlineData(SslProtocols.Tls12)]
        [InlineData(SslProtocols.Tls | SslProtocols.Tls11)]
        [InlineData(SslProtocols.Tls11 | SslProtocols.Tls12)]
        [InlineData(SslProtocols.Tls | SslProtocols.Tls12)]
        [InlineData(SslProtocols.Tls | SslProtocols.Tls11 | SslProtocols.Tls12)]
#if !netstandard
        [InlineData(Tls13Protocol)]
        [InlineData(SslProtocols.Tls11 | Tls13Protocol)]
        [InlineData(SslProtocols.Tls12 | Tls13Protocol)]
        [InlineData(SslProtocols.Tls | Tls13Protocol)]
        [InlineData(SslProtocols.Tls | SslProtocols.Tls11 | SslProtocols.Tls12 | Tls13Protocol)]
#endif
        public void SetGetProtocols_Roundtrips(SslProtocols protocols)
        {
            using (StandardSocketsHttpHandler handler = CreateSocketsHttpHandler())
            {
                handler.SslOptions.EnabledSslProtocols = protocols;
                Assert.Equal(protocols, handler.SslOptions.EnabledSslProtocols);
            }
        }

        // It is HttpClientHandler that wraps SocketsHttpHandler that throws this exception 
        //[OuterLoop] // TODO: Issue #11345
        //[Fact]
        //public async Task SetProtocols_AfterRequest_ThrowsException()
        //{
        //    if (!BackendSupportsSslConfiguration)
        //    {
        //        return;
        //    }
        //
        //    using (StandardSocketsHttpHandler handler = CreateSocketsHttpHandler())
        //    using (var client = new HttpClient(handler))
        //    {
        //        handler.SslOptions.RemoteCertificateValidationCallback = SecurityHelper.AllowAllCertificates;
        //        await LoopbackServer.CreateServerAsync(async (server, url) =>
        //        {
        //            await TestHelper.WhenAllCompletedOrAnyFailed(
        //                server.AcceptConnectionSendResponseAndCloseAsync(),
        //                client.GetAsync(url));
        //        });
        //        Assert.Throws<InvalidOperationException>(() => handler.SslOptions.EnabledSslProtocols = SslProtocols.Tls12);
        //    }
        //}


        public static IEnumerable<object[]> GetAsync_AllowedSSLVersion_Succeeds_MemberData()
        {
            // These protocols are all enabled by default, so we can connect with them both when
            // explicitly specifying it in the client and when not.
            foreach (SslProtocols protocol in new[] { SslProtocols.Tls, SslProtocols.Tls11, SslProtocols.Tls12 })
            {
                yield return new object[] { protocol, false };
                yield return new object[] { protocol, true };
            }

            // These protocols are disabled by default, so we can only connect with them explicitly.
            // On certain platforms these are completely disabled and cannot be used at all.
#pragma warning disable 0618
            if (PlatformDetection.SupportsSsl3)
            {
                // TODO #28790: SSLv3 is supported on RHEL 6, but this test case still fails.
                yield return new object[] { SslProtocols.Ssl3, true };
            }
            if (PlatformDetection.IsWindows && !PlatformDetection.IsWindows10Version1607OrGreater)
            {
                yield return new object[] { SslProtocols.Ssl2, true };
            }
#pragma warning restore 0618
#if !netstandard
            // These protocols are new, and might not be enabled everywhere yet
            if (PlatformDetection.IsUbuntu1810OrHigher)
            {
                yield return new object[] { Tls13Protocol, false };
                yield return new object[] { Tls13Protocol, true };
            }
#endif
        }

        [OuterLoop] // TODO: Issue #11345
        [Theory]
        [MemberData(nameof(GetAsync_AllowedSSLVersion_Succeeds_MemberData))]
        public async Task GetAsync_AllowedSSLVersion_Succeeds(SslProtocols acceptedProtocol, bool requestOnlyThisProtocol)
        {
            if (!BackendSupportsSslConfiguration)
            {
                return;
            }

#pragma warning disable 0618
            if (UseSocketsHttpHandler  || (IsCurlHandler && PlatformDetection.IsRedHatFamily6 && acceptedProtocol == SslProtocols.Ssl3))
            {
                // TODO #26186: SocketsHttpHandler is failing on some OSes.
                return;
            }
#pragma warning restore 0618

            using (StandardSocketsHttpHandler handler = CreateSocketsHttpHandler())
            using (var client = new HttpClient(handler))
            {
                handler.SslOptions.RemoteCertificateValidationCallback = SecurityHelper.AllowAllCertificates;

                if (requestOnlyThisProtocol)
                {
                    handler.SslOptions.EnabledSslProtocols = acceptedProtocol;
                }
                var options = new LoopbackServer.Options { UseSsl = true, SslProtocols = acceptedProtocol };
                await LoopbackServer.CreateServerAsync(async (server, url) =>
                {
                    await TestHelper.WhenAllCompletedOrAnyFailed(
                        server.AcceptConnectionSendResponseAndCloseAsync(),
                        client.GetAsync(url));
                }, options);
            }
        }

        public static IEnumerable<object[]> SupportedSSLVersionServers()
        {
#pragma warning disable 0618 // SSL2/3 are deprecated
            if (PlatformDetection.IsWindows ||
                PlatformDetection.IsOSX ||
                (RuntimeInformation.IsOSPlatform(OSPlatform.Linux) && PlatformDetection.OpenSslVersion < new Version(1, 0, 2) && !PlatformDetection.IsDebian))
            {
                yield return new object[] { SslProtocols.Ssl3, Configuration.Http.SSLv3RemoteServer };
            }
#pragma warning restore 0618
            yield return new object[] { SslProtocols.Tls, Configuration.Http.TLSv10RemoteServer };
            yield return new object[] { SslProtocols.Tls11, Configuration.Http.TLSv11RemoteServer };
            yield return new object[] { SslProtocols.Tls12, Configuration.Http.TLSv12RemoteServer };
        }

        // We have tests that validate with SslStream, but that's limited by what the current OS supports.
        // This tests provides additional validation against an external server.
        [ActiveIssue(26186)]
        [OuterLoop("Avoid www.ssllabs.com dependency in innerloop.")]
        [Theory]
        [MemberData(nameof(SupportedSSLVersionServers))]
        public async Task GetAsync_SupportedSSLVersion_Succeeds(SslProtocols sslProtocols, string url)
        {
            if (UseSocketsHttpHandler)
            {
                // TODO #26186: SocketsHttpHandler is failing on some OSes.
                return;
            }

            using (StandardSocketsHttpHandler handler = CreateSocketsHttpHandler())
            {
                handler.SslOptions.EnabledSslProtocols = sslProtocols;
                using (var client = new HttpClient(handler))
                {
                    (await RemoteServerQuery.Run(() => client.GetAsync(url), remoteServerExceptionWrapper, url)).Dispose();
                }
            }
        }

        public Func<Exception, bool> remoteServerExceptionWrapper = (exception) =>
        {
            Type exceptionType = exception.GetType();
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
            {
                // On linux, taskcanceledexception is thrown.
                return exceptionType.Equals(typeof(TaskCanceledException));
            }
            else
            {
                // The internal exceptions return operation timed out.
                return exceptionType.Equals(typeof(HttpRequestException)) && exception.InnerException.Message.Contains("timed out");
            }
        };

        public static IEnumerable<object[]> NotSupportedSSLVersionServers()
        {
#pragma warning disable 0618
            if (PlatformDetection.IsWindows10Version1607OrGreater)
            {
                yield return new object[] { SslProtocols.Ssl2, Configuration.Http.SSLv2RemoteServer };
            }
#pragma warning restore 0618
        }

        // We have tests that validate with SslStream, but that's limited by what the current OS supports.
        // This tests provides additional validation against an external server.
        [OuterLoop("Avoid www.ssllabs.com dependency in innerloop.")]
        [Theory]
        [MemberData(nameof(NotSupportedSSLVersionServers))]
        public async Task GetAsync_UnsupportedSSLVersion_Throws(SslProtocols sslProtocols, string url)
        {
            using (StandardSocketsHttpHandler handler = CreateSocketsHttpHandler())
            using (HttpClient client = new HttpClient(handler))
            {
                handler.SslOptions.EnabledSslProtocols = sslProtocols;
                await Assert.ThrowsAsync<HttpRequestException>(() => RemoteServerQuery.Run(() => client.GetAsync(url), remoteServerExceptionWrapper, url));
            }
        }

        [OuterLoop] // TODO: Issue #11345
        [Fact]
        public async Task GetAsync_NoSpecifiedProtocol_DefaultsToTls12()
        {
            if (!BackendSupportsSslConfiguration)
            {
                return;
            }

            using (StandardSocketsHttpHandler handler = CreateSocketsHttpHandler())
            using (var client = new HttpClient(handler))
            {
                handler.SslOptions.RemoteCertificateValidationCallback = SecurityHelper.AllowAllCertificates;

                var options = new LoopbackServer.Options { UseSsl = true, SslProtocols = SslProtocols.Tls12 };
                await LoopbackServer.CreateServerAsync(async (server, url) =>
                {
                    await TestHelper.WhenAllCompletedOrAnyFailed(
                        client.GetAsync(url),
                        server.AcceptConnectionAsync(async connection =>
                        {
                            Assert.Equal(SslProtocols.Tls12, Assert.IsType<SslStream>(connection.Stream).SslProtocol);
                            await connection.ReadRequestHeaderAndSendResponseAsync();
                        }));
                }, options);
            }
        }

        [OuterLoop] // TODO: Issue #11345
        [Theory]
#pragma warning disable 0618 // SSL2/3 are deprecated
        [InlineData(SslProtocols.Ssl2, SslProtocols.Tls12)]
        [InlineData(SslProtocols.Ssl3, SslProtocols.Tls12)]
#pragma warning restore 0618
        [InlineData(SslProtocols.Tls11, SslProtocols.Tls)]
        [InlineData(SslProtocols.Tls12, SslProtocols.Tls11)]
        [InlineData(SslProtocols.Tls, SslProtocols.Tls12)]
        public async Task GetAsync_AllowedSSLVersionDiffersFromServer_ThrowsException(
            SslProtocols allowedProtocol, SslProtocols acceptedProtocol)
        {
            if (!BackendSupportsSslConfiguration)
                return;
            using (StandardSocketsHttpHandler handler = CreateSocketsHttpHandler())
            using (var client = new HttpClient(handler))
            {
                handler.SslOptions.EnabledSslProtocols = allowedProtocol;
                handler.SslOptions.RemoteCertificateValidationCallback = SecurityHelper.AllowAllCertificates;

                var options = new LoopbackServer.Options { UseSsl = true, SslProtocols = acceptedProtocol };
                await LoopbackServer.CreateServerAsync(async (server, url) =>
                {
                    Task serverTask = server.AcceptConnectionSendResponseAndCloseAsync();
                    await Assert.ThrowsAsync<HttpRequestException>(() => client.GetAsync(url));
                    try
                    {
                        await serverTask;
                    }
                    catch (Exception e) when (e is IOException || e is AuthenticationException)
                    {
                        // Some SSL implementations simply close or reset connection after protocol mismatch.
                        // Newer OpenSSL sends Fatal Alert message before closing.
                        return;
                    }
                    // We expect negotiation to fail so one or the other expected exception should be thrown.
                    Assert.True(false, "Expected exception did not happen.");
                }, options);
            }
        }

        [OuterLoop] // TODO: Issue #11345
        [ActiveIssue(8538, TestPlatforms.Windows)]
        [Fact]
        public async Task GetAsync_DisallowTls10_AllowTls11_AllowTls12()
        {
            using (StandardSocketsHttpHandler handler = CreateSocketsHttpHandler())
            using (var client = new HttpClient(handler))
            {
                handler.SslOptions.EnabledSslProtocols = SslProtocols.Tls11 | SslProtocols.Tls12;
                handler.SslOptions.RemoteCertificateValidationCallback = SecurityHelper.AllowAllCertificates;

                if (BackendSupportsSslConfiguration)
                {
                    LoopbackServer.Options options = new LoopbackServer.Options { UseSsl = true };

                    options.SslProtocols = SslProtocols.Tls;
                    await LoopbackServer.CreateServerAsync(async (server, url) =>
                    {
                        Task serverTask =  server.AcceptConnectionSendResponseAndCloseAsync();
                        await Assert.ThrowsAsync<HttpRequestException>(() => client.GetAsync(url));
                        try
                        {
                            await serverTask;
                        }
                        catch (Exception e) when (e is IOException || e is AuthenticationException)
                        {
                            // Some SSL implementations simply close or reset connection after protocol mismatch.
                            // Newer OpenSSL sends Fatal Alert message before closing.
                            return;
                        }
                        // We expect negotiation to fail so one or the other expected exception should be thrown.
                        Assert.True(false, "Expected exception did not happen.");
                    }, options);

                    foreach (var prot in new[] { SslProtocols.Tls11, SslProtocols.Tls12 })
                    {
                        options.SslProtocols = prot;
                        await LoopbackServer.CreateServerAsync(async (server, url) =>
                        {
                            await TestHelper.WhenAllCompletedOrAnyFailed(
                                server.AcceptConnectionSendResponseAndCloseAsync(),
                                client.GetAsync(url));
                        }, options);
                    }
                }
                else
                {
                    await Assert.ThrowsAnyAsync<NotSupportedException>(() => client.GetAsync($"http://{Guid.NewGuid().ToString()}/"));
                }
            }
        }
    }
}
