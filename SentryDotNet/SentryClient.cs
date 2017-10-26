﻿using System;
using System.IO;
using System.IO.Compression;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading.Tasks;

using Newtonsoft.Json;
using Newtonsoft.Json.Converters;

namespace SentryDotNet
{
    public class SentryClient : ISentryClient
    {
        private static readonly HttpClient SingletonHttpClient = new HttpClient();
        
        private readonly Func<HttpRequestMessage, Task<HttpResponseMessage>> _sendHttpRequestFunc;

        private readonly decimal _sampleRate;

        /// <summary>
        /// Creates a client capable of sending events to Sentry.
        /// </summary>
        /// <param name="dsn">The DSN to use when sending events to Sentry.</param>
        /// <param name="sampleRate">The percentage of events that are actually sent to Sentry e.g. 0.26.</param>
        /// <param name="sendHttpRequestFunc">Function that invokes a HttpClient with the given request. This may be used to install 
        /// retry policies or share the HttpClient. If not provided, <c>r => SingletonHttpClient.SendAsync(r)</c> will be used.</param>
        public SentryClient(Dsn dsn,
                            decimal sampleRate = 1m,
                            Func<HttpRequestMessage, Task<HttpResponseMessage>> sendHttpRequestFunc = null)
        {
            Dsn = dsn ?? throw new ArgumentNullException(nameof(dsn));
            
            if (sampleRate < 0 || sampleRate > 1)
            {
                throw new ArgumentException("sample rate must be in the [0,1] interval", nameof(sampleRate));
            }

            _sampleRate = sampleRate;
            _sendHttpRequestFunc = sendHttpRequestFunc ?? SendHttpRequestAsync;
        }
        
        public Dsn Dsn { get; }
        
        public async Task<string> SendAsync(SentryEvent sentryEvent)
        {
            if (Dsn.IsEmpty)
            {
                return "";
            }
            
            if (sentryEvent == null)
            {
                throw new ArgumentNullException(nameof(sentryEvent));
            } 

            if (_sampleRate < 1 && Convert.ToDecimal(new Random().NextDouble()) > _sampleRate)
            {
                return "";
            }

            return await SerializeAndSendAsync(sentryEvent);
        }
        
        public async Task<string> CaptureAsync(Exception exception)
        {
            if (Dsn.IsEmpty)
            {
                return "";
            }
            
            var builder = CreateEventBuilder();
            
            builder.SetException(exception);

            return await builder.CaptureAsync();
        }

        public async Task<string> CaptureAsync(string message)
        {
            if (Dsn.IsEmpty)
            {
                return "";
            }
            
            var builder = CreateEventBuilder();

            builder.SetMessage(message);

            return await builder.CaptureAsync();
        }

        public SentryEventBuilder CreateEventBuilder()
        {
            return new SentryEventBuilder(this);
        }
        
        private static async Task<HttpResponseMessage> SendHttpRequestAsync(HttpRequestMessage request)
        {
            return await SingletonHttpClient.SendAsync(request);
        }

        private async Task<string> SerializeAndSendAsync(SentryEvent sentryEvent)
        {
            var request = PrepareRequest(sentryEvent);

            var response = await _sendHttpRequestFunc(request);

            switch ((int)response.StatusCode)
            {
                case 200:
                    var responseMessage = JsonConvert.DeserializeObject<SentrySuccessResponse>(await response.Content.ReadAsStringAsync());
                    return responseMessage.Id;
                case 400:
                    throw new SentryClientException(string.Join(".", response.Headers.GetValues("X-Sentry-Error")));
                case 429:
                    return "";
                default:
                    throw new SentryClientException(JsonConvert.SerializeObject(response));
            }
        }

        private HttpRequestMessage PrepareRequest(SentryEvent sentryEvent)
        {
            var content = Serialize(sentryEvent);

            var request = new HttpRequestMessage(HttpMethod.Post, Dsn.ReportApiUri) { Content = content };
            request.Headers.UserAgent.Add(new ProductInfoHeaderValue(sentryEvent.Sdk.Name, sentryEvent.Sdk.Version));

            var unixTimeSeconds = ((DateTimeOffset) sentryEvent.Timestamp).ToUnixTimeSeconds();

            request.Headers.Add(
                "X-Sentry-Auth",
                $"Sentry sentry_version=7,sentry_timestamp={unixTimeSeconds},sentry_key={Dsn.PublicKey},sentry_secret={Dsn.PrivateKey},sentry_client=sentryDotNet/1.0");
            
            return request;
        }

        private StreamContent Serialize(SentryEvent sentryEvent)
        {
            var payload = JsonConvert.SerializeObject(sentryEvent,
                CreateSerializerSettings());
            
            var payloadBytes = Encoding.UTF8.GetBytes(payload);

            var ms = new MemoryStream();

            using (var gzip = new GZipStream(ms, CompressionMode.Compress, true))
            {
                gzip.Write(payloadBytes, 0, payloadBytes.Length);
            }

            ms.Position = 0;

            return new StreamContent(ms)
            {
                Headers = {ContentType = new MediaTypeHeaderValue("application/json"), ContentEncoding = {"gzip"}}
            };
        }

        private static JsonSerializerSettings CreateSerializerSettings()
        {
            var serializer =
                new JsonSerializerSettings { ContractResolver = new UnderscorePropertyNamesContractResolver() };
            
            serializer.Converters.Add(new StringEnumConverter(true));

            return serializer;
        }

        // ReSharper disable once ClassNeverInstantiated.Local
        private class SentrySuccessResponse
        {
            // ReSharper disable once UnusedAutoPropertyAccessor.Local
            public string Id { get; set; }
        }
    }
}