// Matthew Luan
// CSCI 251 - Secure Distributed Messenger
//
// SPRINT 1: Threading & Basic Networking
// Due: Week 5 | Work on: Weeks 3-4
//

using System.Net;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using SecureMessenger.Core;
using SecureMessenger.Security;
using static SecureMessenger.Core.Message;

namespace SecureMessenger.Network;

/// <summary>
/// TCP server that listens for incoming peer connections.
/// Each peer runs both a server (to accept connections) and client (to initiate connections).
/// </summary>
public class TcpServer
{
    private TcpListener? _listener;
    // btw the public keys are stored here since peers track their public keys
    private readonly List<Peer> _connectedPeers = new();
    private CancellationTokenSource? _cancellationTokenSource;
    private Thread? _listenThread;

    private readonly object _lock = new();

    public event Action<Peer>? OnPeerConnected;
    public event Action<Peer>? OnPeerDisconnected;
    public event Action<Peer, Message>? OnMessageReceived;

    public int Port { get; private set; }
    public bool IsListening { get; private set; }
    public Dictionary<string, List<string>> _rooms = new();
    private readonly RsaEncryption _rsa = new RsaEncryption();    /// <summary>
    /// Start listening for incoming connections on the specified port.
    ///
    /// TODO: Implement the following:
    /// 1. Store the port number
    /// 2. Create a new CancellationTokenSource
    /// 3. Create and start a TcpListener on IPAddress.Any and the specified port
    /// 4. Set IsListening to true
    /// 5. Create and start a new Thread running ListenLoop
    /// 6. Print a message indicating the server is listening
    /// </summary>
    public void Start(int port)
    {
        Port = port;
        _cancellationTokenSource = new CancellationTokenSource();
        _listener = new TcpListener(IPAddress.Any, port);
        _listener.Start();
        IsListening = true;
        _listenThread = new Thread(ListenLoop);
        _listenThread.Start();
        Console.WriteLine($"Server istening on port {port}.");
    }

    /// <summary>
    /// Main loop that accepts incoming connections.
    ///
    /// TODO: Implement the following:
    /// 1. Loop while cancellation is not requested
    /// 2. Check if a connection is pending using _listener.Pending()
    /// 3. If pending, accept the connection with AcceptTcpClient()
    /// 4. Call HandleNewConnection with the new client
    /// 5. If not pending, sleep briefly (e.g., 100ms) to avoid busy-waiting
    /// 6. Handle SocketException and IOException appropriately
    /// </summary>
    private void ListenLoop()
    {
        while(!_cancellationTokenSource!.IsCancellationRequested)
        {
            try
            {
                if (_listener!.Pending())
                {
                    TcpClient client = _listener.AcceptTcpClient();
                    HandleNewConnection(client);
                }
                else
                {
                    Thread.Sleep(100);
                }
            }
            catch (SocketException ex)
            {
                Console.WriteLine($"SocketException in ListenLoop: {ex.Message}");
            }
            catch (IOException ex)
            {
                Console.WriteLine($"IOException in ListenLoop: {ex.Message}");
            }
        }

    }

    /// <summary>
    /// Handle a new incoming connection by creating a Peer and starting its receive thread.
    ///
    /// TODO: Implement the following:
    /// 1. Create a new Peer object with:
    ///    - Client = the TcpClient
    ///    - Stream = client.GetStream()
    ///    - Address = extracted from client.Client.RemoteEndPoint
    ///    - Port = extracted from client.Client.RemoteEndPoint
    ///    - IsConnected = true
    /// 2. Add the peer to _connectedPeers (with proper locking)
    /// 3. Invoke OnPeerConnected event
    /// 4. Create and start a new Thread running ReceiveLoop for this peer
    /// </summary>
    private void HandleNewConnection(TcpClient client)
    {   
        Peer peer = new Peer
        {
            Client = client,
            Stream = client.GetStream(),
            Address = ((IPEndPoint)client.Client.RemoteEndPoint!).Address,
            Port = ((IPEndPoint)client.Client.RemoteEndPoint!).Port,
            IsConnected = true
        };
        lock (_lock)
        {
            _connectedPeers.Add(peer);
        }
        Thread receiveThread = new Thread(() => ReceiveLoop(peer));
        receiveThread.Start();
    }

    /// <summary>
    /// Receive loop for a specific peer - reads messages until disconnection.
    ///
    /// TODO: Implement the following:
    /// 1. Create a StreamReader from the peer's stream
    /// 2. Loop while peer is connected and cancellation not requested
    /// 3. Read a line from the stream (ReadLine blocks until data available)
    /// 4. If line is null, the connection was closed - break the loop
    /// 5. Create a Message object with the received content
    /// 6. Invoke OnMessageReceived event with the peer and message
    /// 7. Handle IOException (connection lost)
    /// 8. In finally block, call DisconnectPeer
    /// </summary>
    private void ReceiveLoop(Peer peer)
    {
        // make the user enter their name here so the server thread not blocked
        StreamReader streamReader = new StreamReader(peer.Stream);
        try
        {
            // receive client's public key
            string? clientPublicKeyBase64 = streamReader.ReadLine();
            peer.PublicKey = Convert.FromBase64String(clientPublicKeyBase64!);

            // send server's public key
            using var writer = new StreamWriter(peer.Stream, leaveOpen: true);
            writer.WriteLine(Convert.ToBase64String(_rsa.ExportPublicKey()));
            writer.Flush();

            // receive encrypted session key, decrypt with server's private key
            string? encryptedSessionKeyBase64 = streamReader.ReadLine();
            peer.AesKey = _rsa.DecryptSessionKey(Convert.FromBase64String(encryptedSessionKeyBase64!));
            // Console.WriteLine($"Server AES key for {peer.Name}: {Convert.ToBase64String(peer.AesKey!)}");


            // now read name
            string? name = streamReader.ReadLine();
            peer.Name = name;
            OnPeerConnected?.Invoke(peer);
            while (peer.IsConnected && !_cancellationTokenSource!.IsCancellationRequested)
            {
                string? line = streamReader.ReadLine();
                if (line == null)
                {
                    break;
                }
                Message incoming = System.Text.Json.JsonSerializer.Deserialize<Message>(line)!;
                string content = incoming.Content;
                if (content.StartsWith("/create "))
                    {
                        // strip "/create", just get room
                        string roomName = content.Substring(8);
                        if (!_rooms.ContainsKey(roomName))
                        {
                            _rooms.Add(roomName, new List<string>());
                            Console.WriteLine($"{peer.Name} created room {roomName}");
                        }

                        continue;
                    }
                if (content.StartsWith("/join "))
                    {
                        string roomName = content.Substring(6);
                        if (_rooms.ContainsKey(roomName))
                        {
                            _rooms[roomName].Add(peer.Id);
                            peer.Rooms.Add(roomName);
                            Console.WriteLine($"{peer.Name} joined room {roomName}");

                        }
                        else
                        {
                            Console.WriteLine($"Room {roomName} does not exist. Create it first.");
                        }
                        continue;
                    }
                if (content.StartsWith("/leave "))
                    {
                        string roomName = content.Substring(7);
                        if (_rooms.ContainsKey(roomName))
                        {
                            if (_rooms[roomName].Remove(peer.Id))
                            {   
                                peer.Rooms.Remove(roomName);
                                Console.WriteLine($"{peer.Name} left room {roomName}");
                            }
                        }
                        else
                        {
                            Console.WriteLine($"Room {roomName} does not exist.");
                        }
                        continue;
                    }
                if (incoming.Type == MessageType.RoomMessage)
                {
                    string roomName = incoming.Room!;
                    string messageContent = incoming.Content;
                    if (_rooms.ContainsKey(roomName) && _rooms[roomName].Contains(peer.Id))
                    {
                        foreach (string peerId in _rooms[roomName])
                        {
                            Message msg = new Message
                            {
                                Content = messageContent,
                                Sender = peer.Name,
                                TargetPeerId = peerId,
                                Room = roomName,
                                Signature = incoming.Signature,
                                PublicKey = peer.PublicKey,
                                Type = MessageType.RoomMessage
                            };
                            OnMessageReceived?.Invoke(peer, msg);
                        }
                    }
                    continue;
                }
                incoming.Sender = peer.Name;
                if (incoming.EncryptedContent != null && peer.AesKey != null)
                {
                    var aes = new AesEncryption(peer.AesKey);
                    incoming.Content = aes.Decrypt(incoming.EncryptedContent);
                    incoming.EncryptedContent = null; // clear so server re-encrypts for each recipient
                }
                // idk name or id check later if stuff breaks
                incoming.Sender = peer.Name;
                incoming.PublicKey = peer.PublicKey;
                OnMessageReceived?.Invoke(peer, incoming);
            }
        }
        catch (IOException)
        {
            
        }
        finally
        {
            DisconnectPeer(peer);
        }
    }

    /// <summary>
    /// Clean up a disconnected peer.
    ///
    /// TODO: Implement the following:
    /// 1. Set peer.IsConnected to false
    /// 2. Dispose the peer's Client and Stream
    /// 3. Remove the peer from _connectedPeers (with proper locking)
    /// 4. Invoke OnPeerDisconnected event
    /// </summary>
    private void DisconnectPeer(Peer peer)
    {
        peer.IsConnected = false;
        peer.Stream?.Dispose();
        peer.Client?.Dispose();
        lock (_lock)
        {
            _connectedPeers.Remove(peer);
        }
        OnPeerDisconnected?.Invoke(peer);
    }

    /// <summary>
    /// Stop the server and close all connections.
    ///
    /// TODO: Implement the following:
    /// 1. Cancel the cancellation token
    /// 2. Stop the listener
    /// 3. Set IsListening to false
    /// 4. Disconnect all connected peers (with proper locking)
    /// 5. Wait for the listen thread to finish (with timeout)
    /// </summary>
    public void Stop()
    {
        _cancellationTokenSource?.Cancel();
        _listener?.Stop();
        IsListening = false;
        lock (_lock)
        {
            foreach (var peer in _connectedPeers.ToList())
            {
                DisconnectPeer(peer);
            }
        }
        _listenThread?.Join(1000);
    }

    /// <summary>
    /// Broadcast a message to all peers connected to this server.
    /// </summary>
    public async Task BroadcastAsync(Message message)
    {
        List<Peer> peers;
        lock (_lock)
        {
            peers = _connectedPeers.ToList();
        }
        foreach (var peer in peers)
        {
            if (peer.IsConnected && peer.Stream != null)
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
                    if (peer.AesKey != null && message.Type == MessageType.Text)
                    {
                        // Console.WriteLine($"Encrypting content: '{message.Content}'");
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
                    Console.WriteLine($"Failed to send to {peer.Address}: {ex.Message}");
                }
            }
        }
    }

    public async Task SendToPeerAsync(Message message)
    {
        Peer? targetPeer;
        lock (_lock)
        {
            targetPeer = _connectedPeers.FirstOrDefault(p => p.Id == message.TargetPeerId);
        }
        if (targetPeer != null && targetPeer.IsConnected && targetPeer.Stream != null)
        {
            try
            {
                using var writer = new StreamWriter(targetPeer.Stream, leaveOpen: true);
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
                if (targetPeer.AesKey != null && message.Type == MessageType.Text)
                {
                    var aes = new AesEncryption(targetPeer.AesKey);
                    messageToSend.EncryptedContent = aes.Encrypt(message.Content);
                    messageToSend.Content = string.Empty;
                }
                string json = System.Text.Json.JsonSerializer.Serialize(messageToSend);
                await writer.WriteLineAsync(json);
                await writer.FlushAsync();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Failed to send to {targetPeer.Address}: {ex.Message}");
            }
        }
    }

    /// <summary>
    /// Get a list of currently connected peers.
    /// Remember to use proper locking when accessing _connectedPeers.
    /// </summary>
    public IEnumerable<Peer> GetConnectedPeers()
    {
        lock (_lock)
        {
            return _connectedPeers.ToList();
        }
    }

    public List<string> GetAvailableRooms()
    {
        lock (_lock)
        {
            return _rooms.Keys.ToList();
        }
    }

    public void addRoom(string roomName)
    {
        lock (_lock)
        {
            _rooms.Add(roomName, []);
        }
    }

    public void JoinRoom(string roomName, string peerName)
    {
        lock (_lock)
        {
            if (_rooms.ContainsKey(roomName))
            {
                _rooms[roomName].Add(peerName);
            }
        }
    }

    public void ListRooms()
    {
        List<string> createdRooms = GetAvailableRooms();
        Console.WriteLine("Created Rooms");
        foreach(var room in createdRooms)
        {
            Console.WriteLine($"- {room}");
        }
    }
}