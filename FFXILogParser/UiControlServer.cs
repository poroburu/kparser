using System;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Threading;

namespace WaywardGamers.KParser
{
    internal delegate string UiResetHandler(
        string resetId,
        string sessionUuid,
        string afterMessageId,
        string boundaryMode,
        string boundaryQuality,
        string boundaryReason);

    internal sealed class UiControlServer : IDisposable
    {
        private readonly string serviceName;
        private readonly string descriptorPath;
        private readonly string token;
        private readonly Func<string> status;
        private readonly UiResetHandler reset;
        private readonly TcpListener listener;
        private readonly Thread acceptThread;
        private volatile bool stopping;

        internal UiControlServer(
            string serviceName,
            string descriptorPath,
            Func<string> status,
            UiResetHandler reset)
        {
            this.serviceName = serviceName;
            this.descriptorPath = descriptorPath;
            this.status = status;
            this.reset = reset;
            this.token = CreateToken();
            this.listener = new TcpListener(IPAddress.Loopback, 0);
            this.acceptThread = new Thread(AcceptLoop);
            this.acceptThread.IsBackground = true;
            this.acceptThread.Name = serviceName + " UI control";
        }

        internal int Port { get; private set; }

        internal void Start()
        {
            listener.Start();
            Port = ((IPEndPoint)listener.LocalEndpoint).Port;
            WriteDescriptor();
            acceptThread.Start();
        }

        private void AcceptLoop()
        {
            while (!stopping)
            {
                try
                {
                    TcpClient client = listener.AcceptTcpClient();
                    ThreadPool.QueueUserWorkItem(HandleClient, client);
                }
                catch (SocketException)
                {
                    if (!stopping)
                        Thread.Sleep(50);
                }
                catch (ObjectDisposedException)
                {
                    break;
                }
            }
        }

        private void HandleClient(object state)
        {
            using (TcpClient client = (TcpClient)state)
            using (NetworkStream stream = client.GetStream())
            using (StreamReader reader = new StreamReader(stream, Encoding.ASCII))
            {
                try
                {
                    string requestLine = reader.ReadLine();
                    if (String.IsNullOrEmpty(requestLine))
                        return;

                    string authorization = String.Empty;
                    string line;
                    while (!String.IsNullOrEmpty(line = reader.ReadLine()))
                    {
                        int separator = line.IndexOf(':');
                        if (separator > 0 &&
                            String.Equals(
                                line.Substring(0, separator),
                                "Authorization",
                                StringComparison.OrdinalIgnoreCase))
                        {
                            authorization = line.Substring(separator + 1).Trim();
                        }
                    }

                    string[] parts = requestLine.Split(
                        new[] { ' ' },
                        StringSplitOptions.RemoveEmptyEntries);
                    if (parts.Length < 2)
                    {
                        WriteResponse(stream, 400, "{\"ok\":false,\"error\":\"bad request\"}");
                        return;
                    }

                    if (!String.Equals(
                        authorization,
                        "Bearer " + token,
                        StringComparison.Ordinal))
                    {
                        WriteResponse(stream, 401, "{\"ok\":false,\"error\":\"unauthorized\"}");
                        return;
                    }

                    string method = parts[0].ToUpperInvariant();
                    string target = parts[1];
                    string path = target.Split(new[] { '?' }, 2)[0];
                    string resetId = QueryValue(target, "reset_id") ?? String.Empty;
                    string sessionUuid = QueryValue(target, "session_uuid") ?? String.Empty;
                    string afterMessageId = QueryValue(target, "after_message_id") ?? "0";
                    string boundaryMode = QueryValue(target, "boundary_mode") ?? "degraded";
                    string boundaryQuality = QueryValue(target, "boundary_quality") ?? "unavailable";
                    string boundaryReason = QueryValue(target, "boundary_reason") ?? String.Empty;

                    if (method == "GET" && path == "/status")
                        WriteResponse(stream, 200, status());
                    else if (method == "POST" && path == "/reset")
                        WriteResponse(
                            stream,
                            200,
                            reset(
                                resetId,
                                sessionUuid,
                                afterMessageId,
                                boundaryMode,
                                boundaryQuality,
                                boundaryReason));
                    else
                        WriteResponse(stream, 404, "{\"ok\":false,\"error\":\"not found\"}");
                }
                catch (Exception ex)
                {
                    WriteResponse(stream, 500, "{\"ok\":false,\"error\":\"" + JsonEscape(ex.Message) + "\"}");
                }
            }
        }

        private static void WriteResponse(NetworkStream stream, int status, string body)
        {
            byte[] payload = Encoding.UTF8.GetBytes(body);
            string reason = status == 200 ? "OK" :
                status == 401 ? "Unauthorized" :
                status == 404 ? "Not Found" :
                status == 500 ? "Internal Server Error" : "Bad Request";
            string header = String.Format(
                "HTTP/1.1 {0} {1}\r\nContent-Type: application/json\r\nContent-Length: {2}\r\nConnection: close\r\n\r\n",
                status,
                reason,
                payload.Length);
            byte[] headerBytes = Encoding.ASCII.GetBytes(header);
            stream.Write(headerBytes, 0, headerBytes.Length);
            stream.Write(payload, 0, payload.Length);
        }

        private static string QueryValue(string target, string name)
        {
            int queryStart = target.IndexOf('?');
            if (queryStart < 0)
                return null;

            string[] pairs = target.Substring(queryStart + 1).Split(
                new[] { '&' },
                StringSplitOptions.RemoveEmptyEntries);
            foreach (string pair in pairs)
            {
                string[] parts = pair.Split(new[] { '=' }, 2);
                if (parts.Length == 2 &&
                    String.Equals(parts[0], name, StringComparison.OrdinalIgnoreCase))
                {
                    return Uri.UnescapeDataString(parts[1]);
                }
            }

            return null;
        }

        private static string CreateToken()
        {
            byte[] bytes = new byte[32];
            RNGCryptoServiceProvider rng = new RNGCryptoServiceProvider();
            rng.GetBytes(bytes);
            return BitConverter.ToString(bytes).Replace("-", String.Empty);
        }

        private static string JsonEscape(string value)
        {
            return (value ?? String.Empty)
                .Replace("\\", "\\\\")
                .Replace("\"", "\\\"")
                .Replace("\r", "\\r")
                .Replace("\n", "\\n");
        }

        private void WriteDescriptor()
        {
            string directory = Path.GetDirectoryName(descriptorPath);
            if (!String.IsNullOrEmpty(directory))
                Directory.CreateDirectory(directory);

            string descriptor = String.Format(
                "{{\"schema_version\":1,\"service\":\"{0}\",\"pid\":{1},\"port\":{2},\"base_url\":\"http://127.0.0.1:{2}\",\"token\":\"{3}\",\"started_at_utc\":\"{4:O}\"}}",
                JsonEscape(serviceName),
                Process.GetCurrentProcess().Id,
                Port,
                token,
                DateTime.UtcNow);
            File.WriteAllText(descriptorPath, descriptor, new UTF8Encoding(false));
        }

        public void Dispose()
        {
            if (stopping)
                return;

            stopping = true;
            listener.Stop();
            if (acceptThread.IsAlive)
                acceptThread.Join(2000);

            try { File.Delete(descriptorPath); }
            catch { }
        }
    }
}
