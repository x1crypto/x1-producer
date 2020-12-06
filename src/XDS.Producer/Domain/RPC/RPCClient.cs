using System;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Utf8Json;
using XDS.Producer.Domain.RPC.DumpWallet;
using XDS.Producer.Domain.RPC.GetBlockTemplate;
using XDS.Producer.Domain.RPC.GetUnspentOutputs;
using XDS.Producer.Domain.RPC.SubmitBlock;
using XDS.Producer.Services;

namespace XDS.Producer.Domain.RPC
{
    public class RPCClient
    {
        readonly ILogger logger;
        readonly IAppConfiguration appConfiguration;
        readonly Stopwatch stopwatch;
        HttpWebRequest httpWebRequest;

        public RPCClient(ILoggerFactory loggerFactory, IAppConfiguration appConfiguration)
        {
            this.logger = loggerFactory.CreateLogger<RPCClient>();
            this.appConfiguration = appConfiguration;
            this.stopwatch = new Stopwatch();

            appConfiguration.Cts.Token.Register(() =>
            {
                if (this.httpWebRequest != null)
                {
                    try
                    {
                        this.httpWebRequest.Abort();
                    }
                    catch (Exception e)
                    {
                        this.logger.LogWarning($"HttpWebRequest: {e.Message}");
                    }

                }
            });
        }

        string InitialGbtJson => "{ \"jsonrpc\": \"2.0\", \"id\":\"" + this.appConfiguration.ClientId + "\", \"method\": \"getblocktemplate\",\"params\":[{\"rules\": [\"segwit\"]}]}";

        string GbtWithLongPollIdJson(string lpid)
        {
            return "{ \"jsonrpc\": \"2.0\", \"id\":\"" + this.appConfiguration.ClientId + "\", \"method\": \"getblocktemplate\",\"params\":[{\"rules\": [\"segwit\"],\"longpollid\":\"" +
                   lpid +
                   "\"}]}";
        }

        public async Task<RPCResponse<RPCDumpWalletResult>> DumpWallet(string absoluteFilePath)
        {
            if (File.Exists(absoluteFilePath))
                File.Delete(absoluteFilePath);

            var dir = Path.GetDirectoryName(absoluteFilePath);
            if (!Directory.Exists(dir))
                Directory.CreateDirectory(dir);

            var request = new RPCRequest
            {
                ClientId = this.appConfiguration.ClientId,
                RequestJson = DumpWalletJson(absoluteFilePath)
            };

            return await Post<RPCDumpWalletResult>(request);
        }

       

        string GetUnspentOutputsJson => "{ \"jsonrpc\": \"2.0\", \"id\":\"" + this.appConfiguration.ClientId + "\", \"method\": \"listunspent\"}";

        string GetPrivateKeysJson => "{ \"jsonrpc\": \"2.0\", \"id\":\"" + this.appConfiguration.ClientId + "\", \"method\": \"dumpwallet\",\"params\":[\"c:\\\\users\\\\ce\\\\mydump2.txt\"]}";

        string DumpWalletJson(string absoluteFilePath)
        {
            var escaped = absoluteFilePath.Replace("\\", "\\\\");
            var json = $"{{ \"jsonrpc\": \"2.0\", \"id\":\"{this.appConfiguration.ClientId}\", \"method\": \"dumpwallet\",\"params\":[\"{escaped}\"]}}";
            return json;
        }

        public async Task<RPCResponse<RPCListUnspentResult>> GetUnspentOutputs()
        {
            var request = new RPCRequest
            {
                ClientId = this.appConfiguration.ClientId,
                RequestJson = GetUnspentOutputsJson
            };

            return await Post<RPCListUnspentResult>(request);
        }

        string SubmitBlockJson(string hexBlock)
        {
            return "{ \"jsonrpc\": \"2.0\", \"id\":\"" + this.appConfiguration.ClientId + "\", \"method\": \"submitblock\",\"params\":[\"" +
                   hexBlock +
                   "\"]}";
        }

        string lastLongPollId;

        public async Task<RPCResponse<RPCGetBlockTemplateResult>> GetBlockTemplate()
        {
            RPCRequest request;
            if (lastLongPollId == null)
            {
                request = new RPCRequest
                {
                    ClientId = this.appConfiguration.ClientId,
                    RequestJson = InitialGbtJson
                };
            }
            else
            {
                request = new RPCRequest
                {
                    ClientId = this.appConfiguration.ClientId,
                    RequestJson = GbtWithLongPollIdJson(lastLongPollId)
                };
            }

            var response = await Post<RPCGetBlockTemplateResult>(request);

            lastLongPollId = response.Status == 200 ? response.Result.result.longpollid : null;

            return response;
        }

        public async Task<RPCResponse<RPCSubmitBlockResult>> SubmitBlock(string hexBlock)
        {
            var request = new RPCRequest { ClientId = this.appConfiguration.ClientId, RequestJson = SubmitBlockJson(hexBlock) };

            return await Post<RPCSubmitBlockResult>(request);
        }


        public async Task<RPCResponse<T>> Post<T>(RPCRequest request, [CallerMemberName] string method = null) where T : class
        {
            this.stopwatch.Restart();

            var signalResponse = new RPCResponse<T> { Status = -1, StatusText = $"Error in method {nameof(Post)}." };

            try
            {
                var credentialCache = new CredentialCache();
                credentialCache.Add(new Uri($"{this.appConfiguration.RPCHost}:{this.appConfiguration.RPCPort}"), "Basic", new NetworkCredential(this.appConfiguration.RPCUser, this.appConfiguration.RPCPassword));
                this.httpWebRequest = WebRequest.CreateHttp($"{this.appConfiguration.RPCHost}:{this.appConfiguration.RPCPort}");
                this.httpWebRequest.Timeout = (int)TimeSpan.FromMinutes(20).TotalMilliseconds;
                this.httpWebRequest.ReadWriteTimeout = (int)TimeSpan.FromMinutes(20).TotalMilliseconds;
                this.httpWebRequest.Method = "Post";
                this.httpWebRequest.ContentType = "application/json";
                this.httpWebRequest.Credentials = credentialCache;

                var requestUtf8Bytes = Encoding.ASCII.GetBytes(request.RequestJson);
                //var requestUtf8Bytes = Serializer.Instance.Serialize(request);
                using (var requestStream = await httpWebRequest.GetRequestStreamAsync())
                {
                    requestStream.Write(requestUtf8Bytes, 0, requestUtf8Bytes.Length);
                }

                using (var webResponse = await this.httpWebRequest.GetResponseAsync())
                {
                    var response = (HttpWebResponse)webResponse;
                    signalResponse.Status = (int)response.StatusCode;
                    signalResponse.StatusText = response.StatusDescription;

                    if (response.StatusCode == HttpStatusCode.OK)
                    {
                        // assume the request body has valid utf8
                        using (Stream body = response.GetResponseStream())
                        {
                            using (var ms = new MemoryStream((int)response.ContentLength))
                            {
                                body.CopyTo(ms);
                                var bytes = ms.ToArray();

                                //var jsonText = Encoding.ASCII.GetString(bytes);
                                //this.logger.LogInformation(jsonText);

                                signalResponse.Result = JsonSerializer.Deserialize<T>(bytes);

                            }
                        }
                    }
                }
            }
            catch (Exception e)
            {
                if (e is WebException webException)
                {
                    if (webException.InnerException is System.Net.Http.HttpRequestException httpRequestException)
                    {
                        signalResponse.Status = httpRequestException.HResult;
                        signalResponse.StatusText = httpRequestException.Message;
                    }
                    else
                    {
                        signalResponse.Status = (int)webException.Status;
                        signalResponse.StatusText = webException.Message;
                    }
                }
                else
                {
                    signalResponse.StatusText = e.Message;
                    signalResponse.Status = 0;
                }
            }

            if (signalResponse.Status != 200)
            {
                this.logger.LogWarning($"{method}: {signalResponse.Status} - {signalResponse.StatusText} - {stopwatch.ElapsedMilliseconds} ms");
            }
            else
            {
                this.logger.LogInformation($"{method}: {signalResponse.Status} - {signalResponse.StatusText} - {stopwatch.ElapsedMilliseconds} ms");
            }

            return signalResponse;
        }

        public async Task<RPCResponse<T>> PostBlock<T>(RPCRequest request, [CallerMemberName] string method = null) where T : class
        {
            this.stopwatch.Restart();

            var signalResponse = new RPCResponse<T> { Status = -1, StatusText = $"Error in method {nameof(Post)}." };

            int submitblockport = 48333;

            try
            {
                var credentialCache = new CredentialCache();
                credentialCache.Add(new Uri($"{this.appConfiguration.RPCHost}:{submitblockport}"), "Basic", new NetworkCredential(this.appConfiguration.RPCUser, this.appConfiguration.RPCPassword));
                this.httpWebRequest = WebRequest.CreateHttp($"{this.appConfiguration.RPCHost}:{submitblockport}");
                this.httpWebRequest.Timeout = (int)TimeSpan.FromMinutes(20).TotalMilliseconds;
                this.httpWebRequest.ReadWriteTimeout = (int)TimeSpan.FromMinutes(20).TotalMilliseconds;
                this.httpWebRequest.Method = "Post";
                this.httpWebRequest.ContentType = "application/json";
                this.httpWebRequest.Credentials = credentialCache;

                var requestUtf8Bytes = Encoding.ASCII.GetBytes(request.RequestJson);
                //var requestUtf8Bytes = Serializer.Instance.Serialize(request);
                using (var requestStream = await httpWebRequest.GetRequestStreamAsync())
                {
                    requestStream.Write(requestUtf8Bytes, 0, requestUtf8Bytes.Length);
                }

                using (var webResponse = await this.httpWebRequest.GetResponseAsync())
                {
                    var response = (HttpWebResponse)webResponse;
                    signalResponse.Status = (int)response.StatusCode;
                    signalResponse.StatusText = response.StatusDescription;

                    if (response.StatusCode == HttpStatusCode.OK)
                    {
                        // assume the request body has valid utf8
                        using (Stream receiveStream = response.GetResponseStream())
                        {
                            // Pipes the stream to a higher level stream reader with the required encoding format.
                            StreamReader readStream = new StreamReader(receiveStream, Encoding.UTF8);
                            var text = readStream.ReadToEnd();
                            this.logger.LogWarning($"Submit block returned: {text}");
                            signalResponse.Result = JsonSerializer.Deserialize<T>(text);
                        }
                    }
                }
            }
            catch (Exception e)
            {
                if (e is WebException webException)
                {
                    if (webException.InnerException is System.Net.Http.HttpRequestException httpRequestException)
                    {
                        signalResponse.Status = httpRequestException.HResult;
                        signalResponse.StatusText = httpRequestException.Message;
                    }
                    else
                    {
                        signalResponse.Status = (int)webException.Status;
                        signalResponse.StatusText = webException.Message;
                    }
                }
                else
                {
                    signalResponse.StatusText = e.Message;
                    signalResponse.Status = 0;
                }
            }

            if (signalResponse.Status != 200)
            {
                this.logger.LogWarning($"{method}: {signalResponse.Status} - {signalResponse.StatusText} - {stopwatch.ElapsedMilliseconds} ms");
            }
            else
            {
                this.logger.LogInformation($"{method}: {signalResponse.Status} - {signalResponse.StatusText} - {stopwatch.ElapsedMilliseconds} ms");
            }

            return signalResponse;
        }


    }
}
