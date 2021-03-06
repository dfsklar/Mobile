﻿using System;
using System.Collections.Generic;
using System.Text;
using System.Security.Cryptography;
using System.Net;
using System.Diagnostics;
using System.IO;
using System.Net.Sockets;
using System.Net.Security;
using System.Security.Authentication;
//using Newtonsoft.Json;
using SimpleJSON;

namespace Uptred
{
    [Serializable]
    public class VerifyFeedback
    {
        public long FirstByte = 0;
        public long LastByte = 0;
        public long ContentSize = 0;
    }

    public static partial class Core
    {
        public const int DEFAULT_CHUNK_SIZE = 1024 * 1024;
        public const int DEFAULT_MAX_ATTEMPTS = 5;
        public static Action<string> VerboseCallback = null;
		public static Action<int, int> UpstreamCallback = null;
        public static Action<string> EndpointCallback = null;

        public static void Trace(string s)
        {
            if (VerboseCallback == null) return;
            VerboseCallback(s);
        }

		static uint _upstreamCallbackRate = 10240;

        static bool _active = false;
        public static void Halt()
        {
            _active = false;
        }

        public static bool IsActive()
        {
            return _active;
        }

		/// <summary>
		/// The upstream callback rate in bytes.
		/// </summary>
		public static uint UpstreamCallbackRate {
			get {
				return _upstreamCallbackRate;
			}
			set {
				_upstreamCallbackRate = value;
			}
		}

        /// <summary>
        /// Encodes a set of key-value pairs into JSON string.
        /// </summary>
		public static string JsonEncode(JSONNode parameters)
        {
			return parameters.ToString ();
        }

		public static string JsonEncode(Dictionary<string, string> parameters)
		{
			var j = new JSONClass ();
            foreach (var item in parameters)
                j[item.Key] = item.Value;
            return j.ToString();
		}

        /// <summary>
        /// Decodes a JSON string into an object.
        /// </summary>
		public static JSONNode JsonDecode(string parameters)
        {
			return JSON.Parse (parameters);
        }

        /// <summary>
        /// Calculates the number of chunks for a file. Default chunk size is 1MB.
        /// </summary>
        /// <param name="path"></param>
        /// <param name="chunk_size">Size of each chunk in bytes</param>
        public static int GetChunksCount(string path, int chunk_size = 1048576)
        {
            var fi = new FileInfo(path);
            return (int)Math.Ceiling((double)fi.Length / (double)chunk_size);
        }

        /// <summary>
        /// Calculates the number of chunks for a file based on the size of the file in bytes. Default chunk size is 1MB.
        /// </summary>
        /// <param name="fileSize">Size of the file in bytes.</param>
        /// <param name="chunk_size">Size of each chunk in bytes</param>
        public static int GetChunksCount(long fileSize, int chunk_size = 1048576)
        {
            if (chunk_size < 0) return 1;
            if (chunk_size == 0) return 0;
            return (int)Math.Ceiling((double)fileSize / (double)chunk_size);
        }

        /// <summary>
        /// Calculates the number of chunks for a file based on the chunk ID. Default chunk size is 1MB.
        /// </summary>
        /// <param name="path"></param>
        /// <param name="index">Index of chunk or chunk_id starting with 0.</param>
        /// <param name="size">Maximum size of chunk in bytes</param>
        /// <returns></returns>
        public static long GetChunkSize(string path, long index, int size = 1048576)
        {
            return GetChunkSize(new FileInfo(path).Length, index, size);
        }

        /// <summary>
        /// Calculates the number of chunks for a file based on the size of the file in bytes and chunk ID. Default chunk size is 1MB.
        /// </summary>
        /// <param name="fileSize">Size of video file in bytes.</param>
        /// <param name="index">Index of chunk or chunk_id starting with 0.</param>
        /// <param name="size">Maximum size of chunk in bytes</param>
        /// <returns></returns>
        public static long GetChunkSize(long fileSize, long index, long size = 1048576)
        {
            var startbyte = index * size;
            if (size > 0)
            {
                if (size + startbyte > fileSize)
                {
                    size = fileSize - startbyte;
                }
                return size;
            }
            else
            {
                return fileSize;
            }
        }

        public static byte[] GetPayload(string path, long startbyte=0, int? psize=null)
        {
            int size = psize.HasValue ? psize.Value : -1;
            var fi = new FileInfo(path);
            FileStream sr = fi.OpenRead();
            sr.Seek(startbyte, SeekOrigin.Begin);
            byte[] data;
            if (size > 0)
            {
                if (size + startbyte > fi.Length)
                {
                    size = (int)(fi.Length - startbyte);
                }
                data = new byte[size];
                sr.Read(data, 0, size);
            }
            else
            {
                data = new byte[fi.Length - startbyte];
                sr.Read(data, 0, (int)(fi.Length - startbyte));
            }
            sr.Close();
            return data;
        }

        public static byte[] ToByteArray(string s)
        {
            return Encoding.UTF8.GetBytes(s);
        }

        public static string ToBase64(string s)
        {
            return Convert.ToBase64String(ToByteArray(s));
        }

        public static string ComputeHash(HashAlgorithm hashAlgorithm, string data)
        {
            if (hashAlgorithm == null)
                throw new ArgumentNullException("hashAlgorithm");

            if (string.IsNullOrEmpty(data))
                throw new ArgumentNullException("data");

            byte[] dataBuffer = Encoding.ASCII.GetBytes(data);
            byte[] hashBytes = hashAlgorithm.ComputeHash(dataBuffer);
            return Convert.ToBase64String(hashBytes);
        }

        public static string PercentEncode(string value)
        {
            const string unreservedChars = "abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789-_.~";
            var result = new StringBuilder();

            foreach (char symbol in value)
            {
                if (unreservedChars.IndexOf(symbol) != -1)
                    result.Append(symbol);
                else
                    result.Append('%' + String.Format("{0:X2}", (int)symbol));
            }

            return result.ToString();
        }

        public static bool IsNullOrWhiteSpace(string value)
        {
            if (value != null)
            {
                for (int i = 0; i < value.Length; i++)
                {
                    if (!char.IsWhiteSpace(value[i]))
                    {
                        return false;
                    }
                }
            }
            return true;
        }

        public static string KeyValueToString(Dictionary<string, string> payload)
        {
            string body = "";
            foreach (var item in payload)
                body += String.Format("{0}={1}&",
                    PercentEncode(item.Key),
                    PercentEncode(item.Value));
            if (body[body.Length - 1] == '&') body = body.Substring(0, body.Length - 1);
            return body;
        }

        public static Dictionary<string, string> QueryParametersFromUrl(string url)
        {
            var parametersStr = url.Split('?')[1].Split('#')[0];
            var keyvalueStr = parametersStr.Split('&');
            var results = new Dictionary<string, string>();
            foreach (string kv in keyvalueStr)
            {
                var pair = kv.Split('=');
                results[pair[0]] = pair[1];
            }
            return results;
        }

        public static string HTTPFetch(string url, string method,
            WebHeaderCollection headers, Dictionary<string, string> payload,
            string contentType = "application/x-www-form-urlencoded",
            string requestAccept = null, string host = null)
        {
            string[] responseFields;
            return HTTPFetch(url, method, headers, payload, out responseFields, contentType, requestAccept, host);
        }

        public static string HTTPFetch(string url, string method,
            WebHeaderCollection headers, Dictionary<string, string> payload,
            out string[] responseFields, 
            string contentType = "application/x-www-form-urlencoded",
            string requestAccept = null, 
            string host = null)
        {
            return HTTPFetch(url, method, headers,
                KeyValueToString(payload), out responseFields, contentType, requestAccept, host);
        }

        public static string HTTPFetch(string url, string method,
            WebHeaderCollection headers, 
            string payload,
            string contentType = "application/x-www-form-urlencoded",
            string requestAccept = null, 
            string host = null)
        {
            string[] responseFields;
            return HTTPFetch(url, method, headers, payload, out responseFields, contentType, requestAccept, host);
        }

        public static string HTTPFetch(string url, string method,
            WebHeaderCollection headers, 
            string payload, 
            out string[] responseFields,
            string contentType = "application/x-www-form-urlencoded",
            string requestAccept = null, 
            string host = null)
        {
            byte[] streamBytes = null;
            int contentLength = 0;
            if (!IsNullOrWhiteSpace(payload))
            {
                streamBytes = ToByteArray(payload);
                contentLength = streamBytes.Length;
            }
            return HTTPFetch(url, method, headers, streamBytes, contentLength, out responseFields,
                contentType, requestAccept, host);
        }
        
        public static string HTTPFetch(string url, string method,
            WebHeaderCollection headers, 
            byte[] streamBytes, 
            int contentLength, 
            out string[] responseFields,
            string contentType = "application/x-www-form-urlencoded",
            string requestAccept = null, 
            string host = null)
        {
            _active = true;
            try
            {
                var uri = new Uri(url);
                if (EndpointCallback != null) EndpointCallback(uri.AbsolutePath);
                if (string.IsNullOrEmpty(host)) host = uri.Host;

                string reqStr =
                    String.Format("{0} {1} HTTP/1.1\r\n", method, url) +
                    String.Format("Host: {0}\r\n", host) +
                    String.Format("Connection: Close\r\n");

                if (!IsNullOrWhiteSpace(requestAccept))
                    reqStr += String.Format("Accept: {0}\r\n", requestAccept);
                if (contentType != null)
                    reqStr += String.Format("Content-Type: {0}\r\n", contentType);

                if (headers != null)
                {
                    for (int i = 0; i < headers.Count; ++i)
                    {
                        string header = headers.GetKey(i);
                        foreach (string value in headers.GetValues(i))
                        {
                            reqStr += String.Format("{0}: {1}\r\n", header, value);
                        }
                    }
                }

                reqStr += String.Format("Content-Length: {0}\r\n", contentLength);
                reqStr += "\r\n";
                byte[] headerBytes = ToByteArray(reqStr);

                byte[] finalBytes = headerBytes;
                if (contentLength > 0)
                {
                    var requestBytes = new byte[headerBytes.Length + contentLength];
                    Buffer.BlockCopy(headerBytes, 0, requestBytes, 0, headerBytes.Length);
                    Buffer.BlockCopy(streamBytes, 0, requestBytes, headerBytes.Length, contentLength);
                    finalBytes = requestBytes;
                }

                string responseFromServer = "";
                responseFields = new string[] { };
                var tcpClient = new TcpClient();
                string responseStr = "";
                int responseLength = 0;

                if (url.ToLower().StartsWith("https"))
                {
                    //HTTPS
                    tcpClient.Connect(uri.Host, 443);
                    ServicePointManager.ServerCertificateValidationCallback += new RemoteCertificateValidationCallback((sender, certificate, chain, policyErrors) => true);

                    //This has a bug on mono: Message	"The authentication or decryption has failed."
                    //Therefore unfortunately we have to ignore certificates.
                    using (var s = new SslStream(tcpClient.GetStream(), false,
                        new RemoteCertificateValidationCallback((sender, certificate, chain, policyErrors) => true)))
                    {
                        s.AuthenticateAsClient(uri.Host, null, SslProtocols.Tls, false);
                        if (UpstreamCallback != null && UpstreamCallbackRate > 0)
                        {
                            int i = 0;
                            while (i < finalBytes.Length)
                            {
                                if (!_active) throw new Exception("Halt");
                                if (i + _upstreamCallbackRate > finalBytes.Length)
                                {
                                    s.Write(finalBytes, i, finalBytes.Length - i);
                                    UpstreamCallback(contentLength, contentLength);
                                    break;
                                }
                                s.Write(finalBytes, i, (int)_upstreamCallbackRate);
                                i += (int)_upstreamCallbackRate;
                                UpstreamCallback(Math.Min(i, contentLength), contentLength);
                            }
                        }
                        else s.Write(finalBytes, 0, finalBytes.Length);
                        s.Flush();

                        while (true)
                        {
                            var responseBytes = new byte[8192];
                            int i = s.Read(responseBytes, 0, responseBytes.Length);
                            if (i == 0) break;
                            responseStr += Encoding.UTF8.GetString(responseBytes, 0, i);
                            responseLength += i;
                        }
                    }
                }
                else
                {
                    //HTTP
                    tcpClient.Connect(uri.Host, 80);
                    if (UpstreamCallback != null && UpstreamCallbackRate > 0)
                    {
                        var s = tcpClient.GetStream();
                        int i = 0;
                        while (i < finalBytes.Length)
                        {
							if (!_active) throw new Exception("Halt");
                            if (i + _upstreamCallbackRate > finalBytes.Length)
                            {
                                s.Write(finalBytes, i, finalBytes.Length - i);
                                UpstreamCallback(contentLength, contentLength);
                                break;
                            }
                            s.Write(finalBytes, i, (int)_upstreamCallbackRate);
                            i += (int)_upstreamCallbackRate;
                            UpstreamCallback(Math.Min(i, contentLength), contentLength);
                        }
                    }
                    else tcpClient.Client.Send(finalBytes);

                    while (true)
                    {
                        var responseBytes = new byte[8192];
                        int i = tcpClient.Client.Receive(responseBytes);
                        if (i == 0) break;
                        responseStr += Encoding.UTF8.GetString(responseBytes, 0, i);
                        responseLength += i;
                    }
                }

                tcpClient.Close();

                var bodyPos = responseStr.IndexOf("\r\n\r\n");
                if (bodyPos >= 0)
                {
                    responseFields = responseStr.Substring(0, bodyPos).Split(new[] { "\r\n" }, StringSplitOptions.RemoveEmptyEntries);
                    bodyPos += 4;
                    responseFromServer = responseStr.Substring(bodyPos, responseStr.Length - bodyPos);
                }

                Debug.WriteLine(String.Format("Response from URL {0}:", url), "HTTPFetch");
                Debug.WriteLine(responseFromServer, "HTTPFetch");

                if (VerboseCallback != null)
                {
                    VerboseCallback(String.Format("Response from URL {0}:", url));
                    VerboseCallback(responseFromServer);
                }

                return responseFromServer;
            }
            catch (Exception e)
            {
                _active = false;
                throw e;
            }
        }

        public static string GetHeaderValue(string[] responseHeaders, string header)
        {
            string result = null;
            foreach (var item in responseHeaders)
            {
                if (item.Contains(header))
                {
                    var i = item.IndexOf(':');
                    return item.Substring(i + 1, item.Length - i - 1).Trim();
                }
            }
            return result;
        }

        public static string RequestRaw(
            string url,
            string method,
            bool jsonBody,
            string body,
            WebHeaderCollection headers,
            out string[] responseHeaders)
        {
            string contentType = "application/x-www-form-urlencoded";
            if (jsonBody) contentType = "application/json; charset=utf-8";
            if (string.IsNullOrEmpty(body)) contentType = null;
            method = method.ToUpper();
            return Core.HTTPFetch(url, method, headers, body,
                out responseHeaders, contentType, requestAccept: null, host: null);
        }

        public static string RequestRaw(
            string url,
            string method,
            bool jsonBody,
            Dictionary<string, string> body,
            WebHeaderCollection headers,
            out string[] responseHeaders)
        {
            string contentType = "application/x-www-form-urlencoded";
            if (jsonBody) contentType = "application/json; charset=utf-8";
            if (body == null) contentType = null;
            method = method.ToUpper();
            return Core.HTTPFetch(url, method, headers, body,
                out responseHeaders, contentType, requestAccept: null, host: null);
        }
    }
}
