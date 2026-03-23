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
using static SecureMessenger.Core.Message;
using System.Security.Cryptography;


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
    private KeyExchange? _keyExchange;

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
            
            _keyExchange = new KeyExchange();
            byte[] publicKey = _keyExchange.GetPublicKey();
            await writer.WriteLineAsync(Convert.ToBase64String(publicKey));
            await writer.FlushAsync();

            string? serverPublicKeyBase64 = await reader.ReadLineAsync();
            _keyExchange.ReceivePublicKey(Convert.FromBase64String(serverPublicKeyBase64!));

            byte[] encryptedSessionKey = _keyExchange.CreateEncryptedSessionKey();
            await writer.WriteLineAsync(Convert.ToBase64String(encryptedSessionKey));
            await writer.FlushAsync();

            _keyExchange.Complete();
            peer.AesKey = _keyExchange.SessionKey;
            // Console.WriteLine($"Client AES key: {Convert.ToBase64String(peer.AesKey!)}");

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
                // Console.WriteLine($"Signature null: {message.Signature == null}, PublicKey null: {message.PublicKey == null}");
                if (message.EncryptedContent != null && peer.AesKey != null)
                {
                    var aes = new AesEncryption(peer.AesKey);
                    message.Content = aes.Decrypt(message.EncryptedContent);
                }
                if (message.Signature != null && message.PublicKey != null)
                {
                    var signer = new MessageSigner(RSA.Create());
                    bool valid = signer.VerifyData(
                        System.Text.Encoding.UTF8.GetBytes(message.Content),
                        message.Signature,
                        message.PublicKey);
                    if (!valid)
                    {
                        Console.WriteLine("Rejecting tampered message.");
                        continue;
                    }
                }
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
    /// </summary>
    public async Task SendAsync(string peerId, Message message)
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
                using var writer = new StreamWriter(peer.Stream, leaveOpen: true);
                var messageToSend = new Message
                {
                    Id = message.Id,
                    Sender = message.Sender,
                    Content = message.Content,
                    Timestamp = message.Timestamp,
                    Type = message.Type,
                    TargetPeerId = message.TargetPeerId,
                    Room = message.Room,
                    Signature = message.Signature,
                    PublicKey = message.PublicKey
                };
                if (_keyExchange != null && (message.Type == MessageType.Text || (message.Type == MessageType.RoomMessage)))
                {
                    messageToSend.Signature = _keyExchange.Sign(
                        System.Text.Encoding.UTF8.GetBytes(message.Content));
                }
                if (peer.AesKey != null && message.Type == MessageType.Text)
                {
                    var aes = new AesEncryption(peer.AesKey);
                    messageToSend.EncryptedContent = aes.Encrypt(message.Content);
                    messageToSend.Content = string.Empty;
                }
                string json = System.Text.Json.JsonSerializer.Serialize(messageToSend);
                await writer.WriteLineAsync(json);
                await writer.FlushAsync();
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
    /// </summary>
    public async Task BroadcastAsync(Message message)
    {
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
