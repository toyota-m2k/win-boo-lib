using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace io.github.toyota32k.BooLib.Server.client;

public interface ICustonHttpClient : IDisposable {
    TimeSpan Timeout { get; set; }
    Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, HttpCompletionOption completionOption, CancellationToken? cancellationToken = null);
    Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken? cancellationToken = null);

    Task<HttpResponseMessage> GetAsync([StringSyntax(StringSyntaxAttribute.Uri)] string requestUri, HttpCompletionOption completionOption, CancellationToken? cancellationToken = null);
    Task<HttpResponseMessage> GetAsync([StringSyntax(StringSyntaxAttribute.Uri)] string requestUri, CancellationToken? cancellationToken = null);

    Task<HttpResponseMessage> PutAsync([StringSyntax(StringSyntaxAttribute.Uri)] string requestUri, HttpContent? content, HttpCompletionOption completionOption, CancellationToken? cancellationToken = null);
    Task<HttpResponseMessage> PutAsync([StringSyntax(StringSyntaxAttribute.Uri)] string requestUri, HttpContent? content, CancellationToken? cancellationToken = null);

    Task<HttpResponseMessage> PostAsync([StringSyntax(StringSyntaxAttribute.Uri)] string requestUri, HttpContent? content, HttpCompletionOption completionOption, CancellationToken? cancellationToken = null);
    Task<HttpResponseMessage> PostAsync([StringSyntax(StringSyntaxAttribute.Uri)] string requestUri, HttpContent? content, CancellationToken? cancellationToken = null);
}
