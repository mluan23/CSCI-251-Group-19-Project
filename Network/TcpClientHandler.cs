// Aveinn Swar
// CSCI 251 - Secure Distributed Messenger
//
// SPRINT 1: Threading & Basic Networking
// Due: Week 5 | Work on: Weeks 3-4
//

using System.Net;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using SecureMessenger.Core;
using System.Collections.Concurrent;
using SecureMessenger.Security;


namespace SecureMessenger.Network;

/// <summary>
/// Handles outgoing TCP connections to other peers.
/// </summary>
public class TcpClientHandler
{
    private readonly ConcurrentDictionary<string, Peer> _connections = new();
    private readonly object _lock = new();

    public event Action<Peer>? OnConnected;
    public event Action<Peer>? OnDisconnected;
    public event Action<Peer, Message>? OnMessageReceived;

    // public event Action<string, Peer>? OnJoinRoom;
    public Peer? _CurrentPeer { get; private set; }

    /// <summary>
    /// Connect to a peer at the specified address and port.
    ///
    /// TODO: Implement the following:
    /// 1. Create a new TcpClient
    /// 2. Connect asynchronously to the host and port
    /// 3. Create a Peer object with:
    ///    - Client = the TcpClient
    ///    - Stream = client.GetStream()
    ///    - Address = parsed from host string
    ///    - Port = the port parameter
    ///    - IsConnected = true
    /// 4. Add to _connections dictionary (with proper locking)
    /// 5. Invoke OnConnected event
    /// 6. Start a background task running ReceiveLoop for this peer
    /// 7. Return true on success
    /// 8. Handle SocketException - print error and return false
    /// </summary>
    public async Task<bool> ConnectAsync(string host, int port)
    {
        try
        {
            TcpClient client = new TcpClient();
            Console.WriteLine("Establishing Connection...");
            await client.ConnectAsync(host, port);
            var peer = new Peer

            {
                Client = client,
                Stream = client.GetStream(),
                Address = IPAddress.Parse(host),
                Port = port,
                IsConnected = true,
            };
            _CurrentPeer = peer;
            var reader = new StreamReader(peer.Stream, leaveOpen: true);
            using var writer = new StreamWriter(peer.Stream, leaveOpen: true);
            
            var keyExchange = new KeyExchange();
            byte[] publicKey = keyExchange.GetPublicKey();
            await writer.WriteLineAsync(Convert.ToBase64String(publicKey));
            await writer.FlushAsync();

            string? serverPublicKeyBase64 = await reader.ReadLineAsync();
            keyExchange.ReceivePublicKey(Convert.FromBase64String(serverPublicKeyBase64!));

            byte[] encryptedSessionKey = keyExchange.CreateEncryptedSessionKey();
            await writer.WriteLineAsync(Convert.ToBase64String(encryptedSessionKey));
            await writer.FlushAsync();

            keyExchange.Complete();
            peer.AesKey = keyExchange.SessionKey;

            // prompt for name
            OnConnected?.Invoke(peer);
            Console.Write("What is your name? ");
            string? name = Console.ReadLine();
            await writer.WriteLineAsync(name);
            await writer.FlushAsync();

            lock (_lock)
            {
                _connections[host] = peer;
            }
            OnConnected?.Invoke(peer);
            _ = Task.Run(() => ReceiveLoop(peer, reader)); // run for lifetime of connection
            return true;
        }
        catch (SocketException ex)
        {
            Console.WriteLine($"Connection failed: {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// Receive loop for a connected peer - reads messages until disconnection.
    ///
    /// TODO: Implement the following:
    /// 1. Create a StreamReader from the peer's stream
    /// 2. Loop while peer is connected
    /// 3. Read a line asynchronously (ReadLineAsync)
    /// 4. If line is null, connection was closed - break
    /// 5. Create a Message object with the received content
    /// 6. Invoke OnMessageReceived event
    /// 7. Handle IOException (connection lost)
    /// 8. In finally block, call Disconnect
    /// </summary>
    private async Task ReceiveLoop(Peer peer, StreamReader reader)
    {
        try
        {
            while (peer.IsConnected)
            {
                string line = await reader.ReadLineAsync();
                if (line == null)
                {
                    break;
                }
                Message message = System.Text.Json.JsonSerializer.Deserialize<Message>(line);
                OnMessageReceived.Invoke(peer, message);
            }
        }
        catch (IOException ex)
        {
            Console.WriteLine($"Connection lost with {peer.Address}: {ex.Message}");
        }
        finally
        {
            reader.Dispose();
            Disconnect(peer.Address.ToString());
        }
    }

    /// <summary>
    /// Send a message to a specific peer.
    ///
    /// TODO: Implement the following:
    /// 1. Look up the peer in _connections by peerId (with proper locking)
    /// 2. If peer exists and is connected with a valid stream:
    ///    - Create a StreamWriter (with leaveOpen: true)
    ///    - Write the message line asynchronously
    ///    - Flush the writer
    /// </summary>
    public async Task SendAsync(string peerId, string message)
    {
        Peer peer;
        lock (_lock)
        {
            _connections.TryGetValue(peerId, out peer);
        }
        if (peer != null && peer.IsConnected && peer.Stream != null)
        {
            try
            {
                using (var writer = new StreamWriter(peer.Stream, leaveOpen: true))
                {
                    await writer.WriteLineAsync(message);
                    await writer.FlushAsync();
                }
            }       
            catch (Exception ex)
            {
                Console.WriteLine($"Failed to send message to {peerId}: {ex.Message}");
            }
        }
    }

    /// <summary>
    /// Broadcast a message to all connected peers.
    ///
    /// TODO: Implement the following:
    /// 1. Get a copy of all peers (with proper locking)
    /// 2. Loop through each peer and call SendAsync
    /// </summary>
    public async Task BroadcastAsync(string message)
    {
        // this broadcasts to every "peer", although the only peer is going to be the server for any given client; 
        // just to clear up any confusion in the future because of this stupid naming convention; at least i think this is right
        // we could also just call SendAsync but who the hell cares
        List<Peer> peersToMessage;
        lock (_lock)
        {
            peersToMessage = _connections.Values.ToList();
        }
        foreach (var peer in peersToMessage)
        {
            string peerId = peer.Address.ToString();
            await SendAsync(peerId, message);
        }
    }

    /// <summary>
    /// Disconnect from a peer.
    ///
    /// TODO: Implement the following:
    /// 1. Remove the peer from _connections (with proper locking)
    /// 2. If peer was found:
    ///    - Set IsConnected to false
    ///    - Dispose the Client and Stream
    ///    - Invoke OnDisconnected event
    /// </summary>
    public void Disconnect(string peerId)
    {
        Peer? peer;
        lock (_lock)
        {
            if (!_connections.Remove(peerId, out peer))
            {
                return;
            }
        }
        if (peer != null)
        {
            peer.IsConnected = false;
            peer.Stream.Dispose();
            peer.Client.Dispose();
            OnDisconnected.Invoke(peer);
        }
    }

    /// <summary>
    /// Get all currently connected peers.
    /// Remember to use proper locking when accessing _connections.
    /// </summary>
    public IEnumerable<Peer> GetConnectedPeers()
    {
        lock (_lock)
        {
            return _connections.Values.ToList();
        }
    }

    // public void joinRoom(string roomName)
    // {
    //     if (_CurrentPeer != null)
    //     {
    //         _CurrentPeer.rooms = _CurrentPeer.rooms.Append(roomName).ToArray();
    //     }
    // }
}
