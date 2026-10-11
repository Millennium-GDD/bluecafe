using System;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using UnityEngine;

namespace BlueCafe.Agent.Tests
{
    public sealed class LocalBridgeTests
    {
        [Serializable] sealed class Discovery { public int port; public string token; }
        sealed class NonPumpingContext : SynchronizationContext
        {
            public int Posts;
            public override void Post(SendOrPostCallback callback, object state) { Interlocked.Increment(ref Posts); }
        }
        string folder;
        LocalBridge bridge;
        [SetUp] public void SetUp() { folder = Path.Combine(Path.GetTempPath(), "bluecafe-bridge-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(folder); }
        [TearDown] public void TearDown() { bridge?.Dispose(); bridge = null; if (Directory.Exists(folder)) Directory.Delete(folder, true); }
        static void ReadExact(NetworkStream stream, byte[] buffer)
        {
            int offset = 0;
            while (offset < buffer.Length) { int count = stream.Read(buffer, offset, buffer.Length - offset); if (count == 0) throw new EndOfStreamException(); offset += count; }
        }
        static Task<string> Exchange(int port, string json)
        {
            return Task.Run(() => {
                using (var client = new TcpClient())
                {
                    client.ReceiveTimeout = 4000; client.SendTimeout = 4000; client.Connect(IPAddress.Loopback, port);
                    var stream = client.GetStream(); byte[] payload = Encoding.UTF8.GetBytes(json); byte[] header = BitConverter.GetBytes(IPAddress.HostToNetworkOrder(payload.Length));
                    // Fragment both protocol sections to exercise incomplete ReadExact
                    // awaits rather than relying on an already-buffered request.
                    stream.Write(header, 0, 2); Thread.Sleep(10); stream.Write(header, 2, 2);
                    int split = payload.Length / 2; stream.Write(payload, 0, split); Thread.Sleep(10); stream.Write(payload, split, payload.Length - split);
                    ReadExact(stream, header); int length = IPAddress.NetworkToHostOrder(BitConverter.ToInt32(header, 0));
                    if (length < 2 || length > LocalBridge.MaxBytes) throw new InvalidDataException("Invalid response length.");
                    var response = new byte[length]; ReadExact(stream, response); return Encoding.UTF8.GetString(response);
                }
            });
        }
        [TestCase(false)] [TestCase(true)]
        public void SocketExchangeNeedsOnlyMainThreadPumpNotSynchronizationContextCallbacks(bool unauthorized)
        {
            int owner = Thread.CurrentThread.ManagedThreadId; var previous = SynchronizationContext.Current; var context = new NonPumpingContext();
            try
            {
                SynchronizationContext.SetSynchronizationContext(context);
                string discoveryPath = Path.Combine(folder, "agent.json"); bridge = new LocalBridge(discoveryPath);
                var discovery = JsonUtility.FromJson<Discovery>(File.ReadAllText(discoveryPath));
                var request = JsonUtility.ToJson(new Request { requestId = "fragmented", token = unauthorized ? "incorrect" : discovery.token, command = "status" });
                var exchange = Exchange(discovery.port, request); int calls = 0, dispatchThread = -1;var limit = DateTime.UtcNow.AddSeconds(5);
                while (!exchange.IsCompleted && DateTime.UtcNow < limit)
                {
                    bridge.Pump(r => { calls++; dispatchThread = Thread.CurrentThread.ManagedThreadId; return new Response { success = true, json = "{\"ok\":true}" }; });
                    Thread.Sleep(2);
                }
                Assert.IsTrue(exchange.IsCompleted, "Socket I/O was blocked waiting for a main-thread synchronization-context callback.");
                var response = JsonUtility.FromJson<Response>(exchange.GetAwaiter().GetResult());
                Assert.AreEqual(0, context.Posts, "Transport continuations must never capture the Unity context.");
                Assert.AreEqual("fragmented", response.requestId);
                if (unauthorized) { Assert.AreEqual(0, calls); Assert.IsFalse(response.success); Assert.AreEqual("UNAUTHORIZED", response.code); }
                else { Assert.AreEqual(1, calls); Assert.AreEqual(owner, dispatchThread); Assert.IsTrue(response.success); }
            }
            finally { bridge?.Dispose(); SynchronizationContext.SetSynchronizationContext(previous); }
        }
        [Test] public void DisposeClosesAdmissionAndIsIdempotentWithAnIncompleteRequest()
        {
            string discoveryPath = Path.Combine(folder, "agent.json"); bridge = new LocalBridge(discoveryPath);
            var discovery = JsonUtility.FromJson<Discovery>(File.ReadAllText(discoveryPath));
            using (var client = new TcpClient())
            {
                client.Connect(IPAddress.Loopback, discovery.port); client.GetStream().Write(new byte[] { 0, 0 }, 0, 2);
                Assert.DoesNotThrow(() => bridge.Dispose()); Assert.DoesNotThrow(() => bridge.Dispose());
                int calls = 0; bridge.Pump(r => { calls++; return new Response { success = true }; }); Assert.AreEqual(0, calls);
            }
            Assert.IsFalse(File.Exists(discoveryPath));
        }
    }
}
