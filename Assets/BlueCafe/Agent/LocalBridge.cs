using System;
using System.Collections.Concurrent;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;

namespace BlueCafe.Agent
{
    [Serializable] public class Request
    {
        public int version = 1;
        public string requestId, token, command;
        public long expectedRevision = -1;
        public Arguments arguments = new Arguments();
    }
    [Serializable] public class Arguments
    {
        public string furnitureId, query, category, theme, path, json, cafeId, name, action, target, workspace;
        public bool shift;
        public string[] instanceIds;
        public float x, z, dx, dz, elevation = -1;
        public int quarterTurns, steps = 1, width = 1600, height = 1200;
    }
    [Serializable] public class Response
    {
        public int version = 1;
        public string requestId, code, message, json, path;
        public bool success;
        public long revision;
        public string[] instanceIds, affectedIds;
    }
    [Serializable] sealed class Discovery { public int version = 1, port, processId; public string token; }
    public sealed class LocalBridge : IDisposable
    {
        public const int MaxBytes = 4 * 1024 * 1024;
                readonly TcpListener listener;
        readonly CancellationTokenSource cancel = new CancellationTokenSource();
        readonly string token = Guid.NewGuid().ToString("N") + Guid.NewGuid().ToString("N");
        readonly string discoveryPath;
        int pendingCount;
        public LocalBridge(string discoveryPath)
        {
            this.discoveryPath = discoveryPath;
            listener = new TcpListener(IPAddress.Loopback, 0); listener.Start(8);
            Directory.CreateDirectory(Path.GetDirectoryName(discoveryPath));
            File.WriteAllText(discoveryPath, JsonUtility.ToJson(new Discovery { port = ((IPEndPoint)listener.LocalEndpoint).Port, token = token, processId = System.Diagnostics.Process.GetCurrentProcess().Id }));
            if (Application.platform == RuntimePlatform.LinuxPlayer || Application.platform == RuntimePlatform.LinuxEditor)
                Chmod(discoveryPath, 384); // owner read/write only
            _ = Accept();
        }
        [System.Runtime.InteropServices.DllImport("libc", EntryPoint="chmod", SetLastError=true)] static extern int Chmod(string path, uint mode);
        async Task Accept()
        {
            while (!cancel.IsCancellationRequested)
            {
                try { var client = await listener.AcceptTcpClientAsync(); _ = Serve(client); }
                catch (ObjectDisposedException) { break; }
                catch (SocketException) { if (cancel.IsCancellationRequested) break; }
            }
        }
        static async Task ReadExact(NetworkStream stream, byte[] bytes, CancellationToken token)
        {
            int offset = 0;
            while (offset < bytes.Length) { var count = await stream.ReadAsync(bytes, offset, bytes.Length - offset, token); if (count == 0) throw new EndOfStreamException(); offset += count; }
        }
        async Task Serve(TcpClient client)
        {
            using (client)
            using (var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancel.Token))
            {
                timeout.CancelAfter(TimeSpan.FromSeconds(30));
                try
                {
                    var stream = client.GetStream(); var header = new byte[4]; await ReadExact(stream, header, timeout.Token);
                    int length = IPAddress.NetworkToHostOrder(BitConverter.ToInt32(header, 0));
                    if (length < 2 || length > MaxBytes) return;
                    var body = new byte[length]; await ReadExact(stream, body, timeout.Token);
                    // Parsing and command dispatch happen on the Unity main thread.
                    var completion = new TaskCompletionSource<string>();
                    var text = new UTF8Encoding(false, true).GetString(body);
                    var job = new PendingText { json = text, reply = completion, deadline = DateTime.UtcNow.AddSeconds(30), cancellation = timeout.Token };
                    if (Interlocked.Increment(ref pendingCount) > 32) { Interlocked.Decrement(ref pendingCount); return; }
                    texts.Enqueue(job);
                    var finished = await Task.WhenAny(completion.Task, Task.Delay(30000, timeout.Token));
                    if (finished != completion.Task) { job.cancelled = true; return; }
                    var bytes = Encoding.UTF8.GetBytes(await completion.Task);
                    if (bytes.Length > MaxBytes) bytes = Encoding.UTF8.GetBytes("{\"success\":false,\"code\":\"RESPONSE_TOO_LARGE\"}");
                    header = BitConverter.GetBytes(IPAddress.HostToNetworkOrder(bytes.Length));
                    await stream.WriteAsync(header, 0, 4, timeout.Token); await stream.WriteAsync(bytes, 0, bytes.Length, timeout.Token);
                }
                catch (Exception e) when (e is IOException || e is SocketException || e is OperationCanceledException || e is DecoderFallbackException) { }
            }
        }
        sealed class PendingText { public string json; public DateTime deadline; public CancellationToken cancellation; public volatile bool cancelled; public TaskCompletionSource<string> reply; }
        readonly ConcurrentQueue<PendingText> texts = new ConcurrentQueue<PendingText>();
        public void Pump(Func<Request, Response> dispatch)
        {
            for (int i = 0; i < 4 && texts.TryDequeue(out var job); i++)
            {
                Interlocked.Decrement(ref pendingCount);
                if (job.cancelled || job.cancellation.IsCancellationRequested || DateTime.UtcNow >= job.deadline) { job.reply.TrySetCanceled(); continue; }
                Response reply;
                try
                {
                    var request = JsonUtility.FromJson<Request>(job.json);
                    if (request == null || request.version != 1) reply = new Response { code = "PROTOCOL_VERSION", message = "Expected request version 1." };
                    else if (request.token != token) reply = new Response { requestId = request.requestId, code = "UNAUTHORIZED" };
                    else { reply = dispatch(request); reply.requestId = request.requestId; }
                }
                catch (Exception e) { reply = new Response { code = "INVALID_REQUEST", message = e.Message }; }
                job.reply.TrySetResult(JsonUtility.ToJson(reply));
            }
        }
        public void Dispose()
        {
            cancel.Cancel(); listener.Stop();
            while (texts.TryDequeue(out var job)) job.reply.TrySetCanceled();
            try { var stored = JsonUtility.FromJson<Discovery>(File.ReadAllText(discoveryPath)); if (stored.token == token) File.Delete(discoveryPath); } catch (IOException) { }
            cancel.Dispose();
        }
    }
}
