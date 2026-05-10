using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace OpenFrp.Service.Net
{
    /// <summary>
    /// Note: Post content will be seen as "application/json"
    /// </summary>
    public class HttpClient
    {
        /// <summary>
        /// 默认实例，为以后可能多开独立需求做准备。
        /// </summary>
        public static HttpClient DefualtInstance { get; private set; } = new HttpClient();

        private readonly Hashtable _userAuthorizationMapping;

        internal string? GetAuthorization(string host)
        {
            return _userAuthorizationMapping[host]?.ToString();
        }

        internal HttpClient()
        {
            _userAuthorizationMapping = new Hashtable();
            _httpClientHandler = new HttpClientHandler()
            {
                AllowAutoRedirect = true,
                ServerCertificateCustomValidationCallback = (_,_,_,error) =>
                {
                    if (error is System.Net.Security.SslPolicyErrors.None)
                    {
                        return true;
                    }
                    return ServerCertificateValidationFailedCallback.Invoke(error);
                },
                SslProtocols = System.Security.Authentication.SslProtocols.Tls13 | System.Security.Authentication.SslProtocols.Tls12
            };
            _httpClient = new System.Net.Http.HttpClient(_httpClientHandler, true)
            {
                Timeout = TimeSpan.FromSeconds(10),
                DefaultRequestHeaders =
                {
                    { "Accept","application/json" },
                },
            };
            _httpClient.DefaultRequestHeaders.UserAgent.ParseAdd("App/OpenFrpLauncher");
        }


        private void RenewHttpClient()
        {
            _httpClient = new System.Net.Http.HttpClient(_httpClientHandler, true)
            {
                Timeout = TimeSpan.FromSeconds(10),
                DefaultRequestHeaders =
                {
                    { "Accept","application/json" },
                    { "User-Agent","App/OpenFrpLauncher" }
                },
            };
            _httpClient.DefaultRequestHeaders.UserAgent.ParseAdd("App/OpenFrpLauncher");
        }

        public void SetUseProxy(bool value)
        {
            if (_httpClientHandler.UseProxy == value)
            {
                return;
            }
            _httpClient.Dispose();

            _httpClientHandler = new HttpClientHandler
            {
                AllowAutoRedirect = true,
                ServerCertificateCustomValidationCallback = (_, _, _, error) =>
                {
                    if (error is System.Net.Security.SslPolicyErrors.None)
                    {
                        return true;
                    }
                    return ServerCertificateValidationFailedCallback.Invoke(error);
                },
                UseProxy = value,
                SslProtocols = System.Security.Authentication.SslProtocols.Tls13 | System.Security.Authentication.SslProtocols.Tls12
            };
            RenewHttpClient();
        }

        public delegate bool SslHandshakeFailedHandler(System.Net.Security.SslPolicyErrors error);

        public void SetAuthorityAndAuthorization(string authority,string? code)
        {
            if (string.IsNullOrEmpty(authority))
            {
                throw new ArgumentNullException(nameof(authority));
            }
            if (string.IsNullOrEmpty(code))
            {
                _userAuthorizationMapping.Remove(authority);
            }
            else _userAuthorizationMapping[authority] = code;
        }

        public void RemoveAuthroization(string authority)
        {
            if (_userAuthorizationMapping.ContainsKey(authority) )
            {
                if (_httpClient.DefaultRequestHeaders.Authorization is not null && _userAuthorizationMapping.ContainsValue(_httpClient.DefaultRequestHeaders.Authorization.Parameter))
                {
                    _httpClient.DefaultRequestHeaders.Remove("Authorization");
                }
                _userAuthorizationMapping.Remove(authority);
            }
        }

        public static Dictionary<string, string> ParseQueryString(string url)
        {
            if (string.IsNullOrWhiteSpace(url))
            {
                throw new ArgumentNullException("url");
            }
            var uri = new Uri(url);
            if (string.IsNullOrWhiteSpace(uri.Query))
            {
                return new Dictionary<string, string>();
            }
            //1.去除第一个前导?字符
            var dic = uri.Query.Substring(1)
                    //2.通过&划分各个参数
                    .Split(new char[] { '&' }, StringSplitOptions.RemoveEmptyEntries)
                    //3.通过=划分参数key和value,且保证只分割第一个=字符
                    .Select(param => param.Split(new char[] { '=' }, 2, StringSplitOptions.RemoveEmptyEntries))
                    //4.通过相同的参数key进行分组
                    .GroupBy(part => part[0], part => part.Length > 1 ? part[1] : string.Empty)
                    //5.将相同key的value以,拼接
                    .ToDictionary(group => group.Key, group => string.Join(",", group));

            return dic;
        }


        /// <summary>
        /// Failed to ssl handshake
        /// SSL 握手失败时走这里！
        /// </summary>
        public event SslHandshakeFailedHandler ServerCertificateValidationFailedCallback = delegate { return false; };

        public async Task<Yue3.Model.Result.HttpResponse<HttpContent>> SendStreamAsync<T>(string method,string url,T? jsonBody = default, CancellationToken cancellationToken = default) 
        {
            if (!Uri.TryCreate(url, UriKind.RelativeOrAbsolute, out var uri) || uri is null)
            {
                throw new NotSupportedException("Please request with the string began with http(s) scheme.");
            }
            HttpMethod httpMethod;
#if NET
            httpMethod = HttpMethod.Parse(method);
#else
            httpMethod = method.Trim().ToUpper() switch
            {
                "POST" => HttpMethod.Post,
                "GET" => HttpMethod.Get,
                "OPTION" => HttpMethod.Options,
                "DELETE" => HttpMethod.Delete,
                "HEAD" => HttpMethod.Head,
                "PUT" => HttpMethod.Put,
                "TRACE" => HttpMethod.Trace,
                _ => HttpMethod.Get
            };
#endif



            HttpRequestMessage message = new HttpRequestMessage(httpMethod, uri);

            if (httpMethod.Equals(HttpMethod.Post) && jsonBody != null)
            {
                using var input = new MemoryStream();

                await JsonSerializer.SerializeAsync(input, jsonBody, cancellationToken: cancellationToken);

                if (input.Length > 0) 
                {
                    input.Seek(0, SeekOrigin.Begin);

                    message.Content = new System.Net.Http.StreamContent(input)
                    {
                        Headers =
                        {
                            { "Content-Type","application/json" }
                        }
                    };
                }
            }
            return await SendStreamAsync(message);
        }

        public async Task<Yue3.Model.Result.HttpResponse<HttpContent>> SendStreamAsync(HttpRequestMessage request, CancellationToken cancellationToken = default)
        {
            try
            {
                if (request.RequestUri is null)
                {
                    throw new NotSupportedException("Please request with the string began with http(s) scheme.");
                }
                if (_userAuthorizationMapping[request.RequestUri.Authority] is string authorizationCode)
                {
                    if (_httpClient.DefaultRequestHeaders.Authorization is not { Parameter: string parameter } || !parameter.Equals(authorizationCode))
                    {
                        _httpClient.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", authorizationCode);
                    }
                }

                var resp = await _httpClient.SendAsync(request);

                if (resp.Headers.TryGetValues("Authorization", out var newValues))
                {
                    if (newValues.FirstOrDefault() is string newAuthorization)
                    {
                        cancellationToken.ThrowIfCancellationRequested();

                        SetAuthorityAndAuthorization(request.RequestUri.Authority, newAuthorization);
                    }
                }
                return new()
                {
                    Data = resp.Content,
                    StatusCode = resp.StatusCode,
                    Headers = resp.Headers
                };
            }
            catch (HttpRequestException ex)
            {
                return new()
                {
                    Exception = ex,
#if NET
                    StatusCode = ex.StatusCode ?? System.Net.HttpStatusCode.Unused
#endif
                };
            }
            catch (ObjectDisposedException ex)
            {
                return new()
                {
                    Exception = new TaskCanceledException("HttpClientHandler may be changing.", ex)
                };
            }
            catch (Exception ex)
            {
                return new()
                {
                    Exception = ex
                };
            }
        }

        public async Task<Yue3.Model.Result.HttpResponse<Stream>> GetStreamAsync(string url, CancellationToken cancellationToken = default)
        {
            try
            {
                if (!Uri.TryCreate(url, UriKind.RelativeOrAbsolute, out var uri) || uri is null)
                {
                    throw new NotSupportedException("Please request with the string began with http(s) scheme.");
                }
                if (_userAuthorizationMapping[uri.Authority] is string authorizationCode)
                {
                    if (_httpClient.DefaultRequestHeaders.Authorization is not { Parameter: string parameter } || !parameter.Equals(authorizationCode))
                    {
                        _httpClient.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", authorizationCode);
                    }
                }
                var resp = await _httpClient.GetAsync(uri, HttpCompletionOption.ResponseContentRead, cancellationToken);

                if (resp.Headers.TryGetValues("Authorization", out var newValues))
                {
                    if (newValues.FirstOrDefault() is string newAuthorization)
                    {
                        cancellationToken.ThrowIfCancellationRequested();

                        SetAuthorityAndAuthorization(uri.Authority, newAuthorization);
                    }
                }
                return new()
                {
                    Data = resp.StatusCode is System.Net.HttpStatusCode.NoContent ? Stream.Null : await resp.Content.ReadAsStreamAsync(),
                    StatusCode = resp.StatusCode,
                    Headers = resp.Headers
                };
            }
            catch (HttpRequestException ex)
            {
                return new()
                {
                    Exception = ex,
#if NET
                    StatusCode = ex.StatusCode ?? System.Net.HttpStatusCode.Unused
#endif
                };
            }
            catch (ObjectDisposedException ex)
            {
                return new()
                {
                    Exception = new TaskCanceledException("HttpClientHandler may be changing.", ex)
                };
            }
            catch (Exception ex)
            {
                return new()
                {
                    Exception = ex
                };
            }
        }

        public async Task<Yue3.Model.Result.HttpResponse<T>> GetAsync<T>(string url,IDictionary<string,string> kv, CancellationToken cancellationToken = default)
        {
            var su = new StringBuilder(url);

            for (int i = 0; i < kv.Count; i++)
            {
                su.Append(i is 0 ? '?' : '&');

                su.Append($"{kv.Keys.ElementAt(i)}={kv.Values.ElementAt(i)}");
            }

            return await GetAsync<T>(su.ToString(), cancellationToken);
        }

        public async Task<Yue3.Model.Result.HttpResponse<T>> GetAsync<T>(string url, CancellationToken cancellationToken = default)
        {
            //example: "https://of-dev-api.bfsea.xyz/oauth2/callback"
            try
            {
                if (!Uri.TryCreate(url, UriKind.RelativeOrAbsolute, out var uri) || uri is null)
                {
                    throw new NotSupportedException("Please request with the string began with http(s) scheme.");
                }
                if (_userAuthorizationMapping[uri.Authority] is string authorizationCode)
                {
                    if (_httpClient.DefaultRequestHeaders.Authorization is not { Parameter: string parameter } || !parameter.Equals(authorizationCode))
                    {
                        _httpClient.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", authorizationCode);
                    }
                }
                var resp = await _httpClient.GetAsync(uri, HttpCompletionOption.ResponseContentRead, cancellationToken);

                if (resp.Headers.TryGetValues("Authorization", out var newValues))
                {
                    if (newValues.FirstOrDefault() is string newAuthorization)
                    {
                        cancellationToken.ThrowIfCancellationRequested();

                        SetAuthorityAndAuthorization(uri.Authority, newAuthorization);
                    }
                }
                if (resp.Content.Headers.ContentType is { MediaType: "application/json" })
                {

                    return new Yue3.Model.Result.HttpResponse<T>
                    {
                        Data = resp.StatusCode is System.Net.HttpStatusCode.NoContent ? default(T) : await resp.Content.ReadFromJsonAsync<T>(cancellationToken: cancellationToken),
                        StatusCode = resp.StatusCode,
                        Headers = resp.Headers
                    };
                }
                return new Yue3.Model.Result.HttpResponse<T>
                {
                    StatusCode = System.Net.HttpStatusCode.UnsupportedMediaType,
                    Exception = new HttpRequestException($"UnsupportedMediaType: {resp}")
                };
            }
            catch (HttpRequestException ex)
            {
                return new Yue3.Model.Result.HttpResponse<T>
                {
                    Exception = ex,
#if NET
                    StatusCode = ex.StatusCode ?? System.Net.HttpStatusCode.Unused
#endif
                };
            }
            catch (ObjectDisposedException ex)
            {
                return new Yue3.Model.Result.HttpResponse<T>()
                {
                    Exception = new TaskCanceledException("HttpClientHandler may be changing.", ex)
                };
            }
            catch (Exception ex)
            {
                return new Yue3.Model.Result.HttpResponse<T>
                {
                    Exception = ex
                };
            }
        }

        public async Task<Yue3.Model.Result.HttpResponse> GetAsync(string url,Stream outputStream, IProgress<HttpDownloadProgress> progress,CancellationToken cancellationToken = default)
        {
            //example: "https://of-dev-api.bfsea.xyz/oauth2/callback"
            try
            {
                if (!Uri.TryCreate(url,UriKind.RelativeOrAbsolute,out var uri) || uri is null)
                {
                    throw new NotSupportedException("Please request with the string began with http(s) scheme.");
                }
                if (_userAuthorizationMapping[uri.Authority] is string authorizationCode)
                {
                    if (_httpClient.DefaultRequestHeaders.Authorization is not { Parameter: string parameter } || !parameter.Equals(authorizationCode))
                    {
                        _httpClient.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", authorizationCode);
                    }
                }
                var resp = await _httpClient.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead, cancellationToken);

                if(resp.Headers.TryGetValues("Authorization", out var newValues))
                {
                    if (newValues.FirstOrDefault() is string newAuthorization)
                    {
                        cancellationToken.ThrowIfCancellationRequested();

                        SetAuthorityAndAuthorization(uri.Authority, newAuthorization);
                    }
                }
                if (cancellationToken.IsCancellationRequested)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                }
                
                long fileLength = resp.Content.Headers.ContentLength ?? -1;


                if (fileLength <= 0)
                {
                    if (resp.Headers.TransferEncodingChunked is true)
                    {
                        progress.Report(new HttpDownloadProgress { ReadLength = -1, TotalLength = -1 });

#if NET
                        using (var t = await resp.Content.ReadAsStreamAsync(cancellationToken))
                        {
                            await t.CopyToAsync(outputStream, cancellationToken);
                        }
#else
                        using (var t = await resp.Content.ReadAsStreamAsync())
                        {
                            await t.CopyToAsync(outputStream);
                        }
#endif

                        return new Yue3.Model.Result.HttpResponse
                        {
                            StatusCode = resp.StatusCode,
                            Headers = resp.Headers,
                            Message = $"User file type: [{resp.Content.Headers.ContentType}]"
                        };
                    }
                    return new Yue3.Model.Result.HttpResponse
                    {
                        StatusCode = resp.IsSuccessStatusCode ? System.Net.HttpStatusCode.BadRequest : resp.StatusCode,
                        Message = $"ContentLength propertY: {fileLength}"
                    };
                }
#if NET
                using (Stream stream = await resp.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false))
                {
#else
                using (Stream stream = await resp.Content.ReadAsStreamAsync().ConfigureAwait(false))
                {
#endif
                    var buffer = new byte[4096];

                    var dndProgess = new HttpDownloadProgress()
                    {
                        TotalLength = fileLength
                    };

                    progress?.Report(dndProgess);
                    int readLength = -1;
#if NET
                    while ((readLength = await stream.ReadAsync(buffer,cancellationToken)) > 0)
                    {
                        await outputStream.WriteAsync(buffer, cancellationToken);
#else
                    while ((readLength = await stream.ReadAsync(buffer,0,buffer.Length,cancellationToken)) > 0)
                    {
                        await outputStream.WriteAsync(buffer,0,buffer.Length, cancellationToken);
#endif
                        dndProgess.ReadLength += readLength;

                        progress?.Report(dndProgess);
                    }
                    return new Yue3.Model.Result.HttpResponse<IEnumerable<byte>>
                    {
                        StatusCode = resp.StatusCode,
                        Headers = resp.Headers,
                        Message = $"User file type: [{resp.Content.Headers.ContentType}]"
                    };
                }
            }
            catch(HttpRequestException ex)
            {
                return new Yue3.Model.Result.HttpResponse
                {
                    Exception = ex,
#if NET
                    StatusCode = ex.StatusCode ?? System.Net.HttpStatusCode.Unused
#endif
                };
            }
            catch (ObjectDisposedException ex)
            {
                return new Yue3.Model.Result.HttpResponse()
                {
                    Exception = new TaskCanceledException("HttpClientHandler may be changing.", ex)
                };
            }
            catch(Exception ex)
            {
                return new Yue3.Model.Result.HttpResponse
                {
                    Exception = ex
                };
            }
        }

        public async Task<Yue3.Model.Result.HttpResponse<T>> PostAsync<T>(string url, object? data, CancellationToken cancellationToken = default)
        {
            try
            {
                if (!Uri.TryCreate(url, UriKind.RelativeOrAbsolute, out var uri) || uri is null)
                {
                    throw new NotSupportedException("Please request with the string began with http(s) scheme.");
                }
                if (_userAuthorizationMapping[uri.Authority] is string authorizationCode)
                {
                    if (_httpClient.DefaultRequestHeaders.Authorization is not { Parameter: string parameter } || !parameter.Equals(authorizationCode))
                    {
                        _httpClient.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", authorizationCode);
                    }
                }
                var resp = await _httpClient.PostAsJsonAsync(url, data, cancellationToken);
                if (resp.Headers.TryGetValues("Authorization", out var newValues))
                {
                    if (newValues.FirstOrDefault() is string newAuthorization)
                    {
                        cancellationToken.ThrowIfCancellationRequested();

                        SetAuthorityAndAuthorization(uri.Authority, newAuthorization);
                    }
                }
                if (resp.Content.Headers.ContentType is { MediaType: "application/json" })
                {
                    return new Yue3.Model.Result.HttpResponse<T>
                    {
                        Data = resp.StatusCode is System.Net.HttpStatusCode.NoContent ? default(T) : await resp.Content.ReadFromJsonAsync<T>(cancellationToken: cancellationToken),
                        StatusCode = resp.StatusCode,
                        Headers = resp.Headers
                    };
                }
                return new Yue3.Model.Result.HttpResponse<T>
                {
                    StatusCode = System.Net.HttpStatusCode.UnsupportedMediaType,
                    Exception = new HttpRequestException($"UnsupportedMediaType: {resp}")
                };
            }
            catch (ObjectDisposedException ex)
            {
                return new Yue3.Model.Result.HttpResponse<T>()
                {
                    Exception = new TaskCanceledException("HttpClientHandler may be changing.", ex)
                };
            }
            catch (Exception ex)
            {
                return new Yue3.Model.Result.HttpResponse<T>
                {
                    Exception = ex
                };
            }
        }

        private System.Net.Http.HttpClientHandler _httpClientHandler;
        private System.Net.Http.HttpClient _httpClient;

        public class HttpDownloadProgress
        {
            public long TotalLength { get; set; }

            public long ReadLength { get; set; }


            public bool IsIndeterminate { get => TotalLength is -1 || ReadLength is -1; }
        }
    }
}
