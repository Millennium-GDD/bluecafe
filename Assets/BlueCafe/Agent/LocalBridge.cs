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
        public string areaId, wallId;
        public int vertexIndex;
        public float centerX, centerZ, extentWidth, extentDepth, along;
        public BlueCafe.Core.Area[] areas;
        public BlueCafe.Core.WallOpening[] openings;
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
        readonly CancellationToken shutdown;
        readonly object lifecycleGate = new object();
        bool disposed;
        readonly string token = Guid.NewGuid().ToString("N") + Guid.NewGuid().ToString("N");
        readonly string discoveryPath;
        int pendingCount;
        public LocalBridge(string discoveryPath)
        {
            this.discoveryPath = discoveryPath;
            shutdown = cancel.Token;
            listener = new TcpListener(IPAddress.Loopback, 0); listener.Start(8);
            Directory.CreateDirectory(Path.GetDirectoryName(discoveryPath));
            File.WriteAllText(discoveryPath, JsonUtility.ToJson(new Discovery { port = ((IPEndPoint)listener.LocalEndpoint).Port, token = token, processId = System.Diagnostics.Process.GetCurrentProcess().Id }));
            if (Application.platform == RuntimePlatform.LinuxPlayer || Application.platform == RuntimePlatform.LinuxEditor)
                Chmod(discoveryPath, 384); // owner read/write only
            _ = Accept();
        }
        [System.Runtime.InteropServices.DllImport("libc", EntryPoint="chmod", SetLastError=true)] static extern int Chmod(string path, uint mode);
        // Socket continuations stay on worker threads. Only Pump parses Unity JSON
        // and dispatches commands; each response requires at most one Pump turn.
        async Task Accept()
        {
            while (!shutdown.IsCancellationRequested)
            {
                try { var client = await listener.AcceptTcpClientAsync().ConfigureAwait(false); _ = Serve(client); }
                catch (ObjectDisposedException) { break; }
                catch (SocketException) { if (shutdown.IsCancellationRequested) break; }
            }
        }
        static async Task ReadExact(NetworkStream stream, byte[] bytes, CancellationToken token)
        {
            int offset = 0;
            while (offset < bytes.Length) { var count = await stream.ReadAsync(bytes, offset, bytes.Length - offset, token).ConfigureAwait(false); if (count == 0) throw new EndOfStreamException(); offset += count; }
        }
        async Task Serve(TcpClient client)
        {
            using (client)
            using (var timeout = CreateTimeout())
            {
                if (timeout == null) return;
                try
                {
                    var stream = client.GetStream(); var header = new byte[4]; await ReadExact(stream, header, timeout.Token).ConfigureAwait(false);
                    int length = IPAddress.NetworkToHostOrder(BitConverter.ToInt32(header, 0));
                    if (length < 2 || length > MaxBytes) return;
                    var body = new byte[length]; await ReadExact(stream, body, timeout.Token).ConfigureAwait(false);
                    // Parsing and command dispatch happen on the Unity main thread.
                    var completion = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
                    var text = new UTF8Encoding(false, true).GetString(body);
                    var job = new PendingText { json = text, reply = completion, deadline = DateTime.UtcNow.AddSeconds(30), cancellation = timeout.Token };
                    if (!TryQueue(job)) return;
                    var finished = await Task.WhenAny(completion.Task, Task.Delay(30000, timeout.Token)).ConfigureAwait(false);
                    if (finished != completion.Task) { job.cancelled = true; return; }
                    var bytes = Encoding.UTF8.GetBytes(await completion.Task.ConfigureAwait(false));
                    if (bytes.Length > MaxBytes) bytes = Encoding.UTF8.GetBytes("{\"success\":false,\"code\":\"RESPONSE_TOO_LARGE\"}");
                    header = BitConverter.GetBytes(IPAddress.HostToNetworkOrder(bytes.Length));
                    await stream.WriteAsync(header, 0, 4, timeout.Token).ConfigureAwait(false);
                    await stream.WriteAsync(bytes, 0, bytes.Length, timeout.Token).ConfigureAwait(false);
                }
                catch (Exception e) when (e is IOException || e is SocketException || e is OperationCanceledException || e is DecoderFallbackException || e is ObjectDisposedException) { }
            }
        }
        CancellationTokenSource CreateTimeout()
        {
            lock (lifecycleGate)
            {
                if (disposed) return null;
                var timeout = CancellationTokenSource.CreateLinkedTokenSource(shutdown);
                timeout.CancelAfter(TimeSpan.FromSeconds(30));
                return timeout;
            }
        }
        bool TryQueue(PendingText job)
        {
            // Dispose closes admission before draining, so a network continuation
            // cannot enqueue an orphan after the main thread's final Pump.
            lock (lifecycleGate)
            {
                if (disposed || job.cancellation.IsCancellationRequested) return false;
                if (Interlocked.Increment(ref pendingCount) > 32) { Interlocked.Decrement(ref pendingCount); return false; }
                texts.Enqueue(job);
                return true;
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
            lock (lifecycleGate) { if (disposed) return; disposed = true; }
            cancel.Cancel(); listener.Stop();
            while (texts.TryDequeue(out var job)) { Interlocked.Decrement(ref pendingCount); job.reply.TrySetCanceled(); }
            try { var stored = JsonUtility.FromJson<Discovery>(File.ReadAllText(discoveryPath)); if (stored.token == token) File.Delete(discoveryPath); } catch (IOException) { }
            cancel.Dispose();
        }
    }
}
