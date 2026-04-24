// Matthew Luan
// CSCI 251 - Secure Distributed Messenger
// Group Project
//
// SPRINT 1: Threading & Basic Networking
// Due: Week 5 | Work on: Weeks 3-4
// (Continue enhancing in Sprints 2 & 3)
//

using System.Collections.Concurrent;
using System.Runtime.InteropServices;
using SecureMessenger.Core;
using SecureMessenger.Network;
using SecureMessenger.Security;
using SecureMessenger.UI;
using static SecureMessenger.Core.Message;

namespace SecureMessenger;

/// <summary>
/// Main entry point for the Secure Distributed Messenger.
///
/// Architecture Overview:
/// This application uses multiple threads to handle concurrent operations:
///
/// 1. Main Thread (UI Thread)
///    - Reads user input from console
///    - Parses commands using ConsoleUI
///    - Dispatches commands to appropriate handlers
///
/// 2. Listen Thread (Server) 
///    - Runs TcpServer to accept incoming connections
///    - Each accepted connection spawns a receive thread
///
/// 3. Receive Thread(s)
///    - One per connected peer
///    - Reads messages from network
///    - Enqueues to incoming message queue
///
/// 4. Send Thread
///    - Dequeues from outgoing message queue
///    - Sends messages to connected peers
///
/// 5. Process Thread (Optional)
///    - Dequeues from incoming message queue
///    - Displays messages to user
///    - Handles decryption and verification
///
/// Thread Communication:
/// - Use MessageQueue for thread-safe message passing
/// - Use CancellationToken for graceful shutdown
/// - Use events for peer connection/disconnection notifications
///
/// Sprint Progression:
/// - Sprint 1: Basic threading and networking (connect, send, receive)
/// - Sprint 2: Add encryption (key exchange, AES encryption, signing)
/// - Sprint 3: Add resilience (peer discovery, heartbeat, reconnection)
/// </summary>
class Program
{
    // TODO: Declare your components as fields if needed for access across methods
    // Examples:
    private static MessageQueue? _messageQueue;
    private static TcpServer? _tcpServer;
    private static TcpClientHandler? _tcpClientHandler;
    private static ConsoleUI? _consoleUI;
    private static CancellationTokenSource? _cancellationTokenSource;
    private static ConcurrentDictionary<string, Peer> _peers = new();
    private static PeerDiscovery? _peerDiscovery;
    private static string? _localName;
    private static HashSet<string> _localRooms = new();
    private static readonly HashSet<string> _networkRooms = new();
    private static readonly HashSet<string> _pendingConnections = new();
    private static MessageHistory? _messageHistory;

    
    static async Task Main(string[] args)
    {
        Console.WriteLine("Secure Distributed Messenger");
        Console.WriteLine("============================");

        Console.Write("Enter your name: ");
        _localName = Console.ReadLine();
        while (string.IsNullOrWhiteSpace(_localName))
        {
            Console.Write("Name cannot be empty. Enter your name: ");
            _localName = Console.ReadLine();
        }

        _cancellationTokenSource = new CancellationTokenSource();
        _messageQueue = new MessageQueue();
        _consoleUI = new ConsoleUI(_messageQueue);
        _tcpServer = new TcpServer();
        _tcpClientHandler = new TcpClientHandler();
        _messageHistory = new MessageHistory();

        _ = Task.Run(() => ProcessIncomingMessages(_cancellationTokenSource.Token));
        _ = Task.Run(() => ProcessOutgoingMessages(_cancellationTokenSource.Token));

        _tcpServer.OnPeerConnected += async (peer) =>
        {

            var publicKey = peer.KeyExchange.GetPublicKey();

            _consoleUI.DisplaySystem($"Connected (awaiting secure channel...)");

            await _tcpServer.SendToPeerAsync(new Message
            {
                Type = MessageType.KeyExchange,
                PublicKey = publicKey,
                TargetPeerId = peer.Id
            });
        };
        _tcpServer.OnMessageReceived += async (peer, message) =>
        {
            if (message.Type == MessageType.NameExchange)
            {
                peer.Name = message.Content;

                var existing = _peers.Values.FirstOrDefault(p => p.Name == peer.Name && p.Id != peer.Id);

                if (existing != null)
                {
                    return;
                }

                _peers[peer.Id] = peer;
                _consoleUI.DisplaySystem($"Peer identified as: {peer.Name}");

                return;
            }

            if (message.Type == MessageType.KeyExchange)
            {
                await HandleKeyExchange(peer, message);
                return;
            }


            if (message.Type == MessageType.Command)
            {
                HandleCommandMessage(peer, message);
                return;
            }

            message.Sender = peer.Name;

            if (message.Type == MessageType.PrivateMessage)
            {
                if (peer.Aes == null)
                {
                    _consoleUI.DisplaySystem("No AES key for this connection");
                    return;
                }

                if (message.EncryptedContent == null)
                {
                    _consoleUI.DisplaySystem("Missing encrypted payload");
                    return;
                }

                try
                {
                    message.Content = peer.Aes.Decrypt(message.EncryptedContent);
                }
                catch
                {
                    _consoleUI.DisplaySystem("Failed to decrypt message");
                    return;
                }
            }

            if (message.Type == MessageType.RoomMessage && !_localRooms.Contains(message.Room))
            {
                return;
            }

            _messageQueue.EnqueueIncoming(message);

        };

        _tcpServer.OnPeerDisconnected += (peer) =>
        {
            _peers.TryRemove(peer.Id, out _);
            var name = string.IsNullOrWhiteSpace(peer.Name) ? "Unknown" : peer.Name;
            _consoleUI.DisplaySystem($"Peer disconnected: {name}");
        };

        _tcpClientHandler.OnConnected += async (peer) =>
        {
            var publicKey = peer.KeyExchange.GetPublicKey();

            _consoleUI.DisplaySystem("Connected (awaiting identity...)");

            await _tcpClientHandler.SendAsync(peer.Id, new Message
            {
                Type = MessageType.KeyExchange,
                PublicKey = publicKey
            });

            await _tcpClientHandler.SendAsync(peer.Id, new Message
            {
                Type = MessageType.NameExchange,
                Content = _localName
            });
        };
        _tcpClientHandler.OnMessageReceived += async (peer, message) =>
        {
            if (message.Type == MessageType.NameExchange)
            {
                peer.Name = message.Content;

                var existing = _peers.Values.FirstOrDefault(p => p.Name == peer.Name && p.Id != peer.Id);
                
                if (existing != null)
                {
                    return;
                }

                _peers[peer.Id] = peer;

                _consoleUI.DisplaySystem($"Peer identified as: {peer.Name}");

                return;
            }

            if(message.Type == MessageType.KeyExchange)
            {
                await HandleKeyExchange(peer, message);
                return;
            }

            if(message.Type == MessageType.Command)
            {
                message.Sender ??= peer.Name;
                HandleCommandMessage(peer, message);
                return;
            }

            if (message.Content.StartsWith("Created Rooms:"))
            {
                return;
            }
            if (message.Type == MessageType.PrivateMessage)
            {

                if (peer.Aes == null)
                {
                    _consoleUI.DisplaySystem("No AES key for this connection");
                    return;
                }

                if (message.EncryptedContent == null)
                {
                    _consoleUI.DisplaySystem("Missing encrypted payload");
                    return;
                }

                try
                {
                    message.Content = peer.Aes.Decrypt(message.EncryptedContent);
                }
                catch
                {
                    _consoleUI.DisplaySystem("Failed to decrypt message");
                    return;
                }
            }
            if (message.Type == MessageType.RoomMessage && !_localRooms.Contains(message.Room))
            {
                return;
            }
            _consoleUI.DisplayMessage(message);
            _messageHistory.SaveMessage(message);
        };
        _tcpClientHandler.OnDisconnected += (peer) =>
        {
            _peers.TryRemove(peer.Id, out _);
            _consoleUI.DisplaySystem($"Disconnected from server.");
        };
        // _tcpClientHandler.OnJoinRoom += (roomName, peer) =>
        // {
        //     _tcpServer.JoinRoom(roomName, peer.Id);
        //     _consoleUI.DisplaySystem($"Joined room: {roomName}");
        // };

        _tcpServer.Start();
        _tcpServer.LocalName = _localName!;
        _tcpClientHandler.LocalPort = _tcpServer.Port;
        // Console.WriteLine($"Your port: {_tcpServer.Port} - share this with peers to connect");

        _peerDiscovery = new PeerDiscovery(
            () => _localName!,
            () => _localRooms
        );
        _peerDiscovery.OnPeerDiscovered += async (peer) =>
        {
            if (peer.Id == _peerDiscovery.LocalPeerId) return;
            if (_peers.ContainsKey(peer.Id)) return;
            if (_pendingConnections.Contains(peer.Id)) return;

            lock (_pendingConnections)
            {
                if (_pendingConnections.Contains(peer.Id)) return;
                _pendingConnections.Add(peer.Id);
            }

            await _tcpClientHandler.ConnectAsync(peer.Address!.ToString(), peer.Port, _localName!);

            lock (_pendingConnections)
                _pendingConnections.Remove(peer.Id);
        };
        _peerDiscovery.OnPeerLost += (peer) =>
        {
            _consoleUI.DisplaySystem($"Peer {peer.Name} lost (no broadcast in 30s).");
        };
        _peerDiscovery.Start(_tcpServer.Port);



        Console.WriteLine("Type /help for available commands");
        Console.WriteLine();

        // Main loop - handle user input
        bool running = true;
        while (running)
        {

            // command parsing
            string? input = Console.ReadLine();
            // add it so the empty str msg not printed when someone disconnects
            if (input == null)
            {
                continue;
            }
            if (string.IsNullOrWhiteSpace(input))
            {
                Console.WriteLine("Empty message not allowed.");
                continue;
            };
            CommandResult commandResult = _consoleUI.ParseCommand(input);
            if (!commandResult.IsCommand)
            {
                if (!_peers.Any())
                {
                    Console.WriteLine("No peers connected.");
                    continue;
                }
                _messageQueue.EnqueueOutgoing(new Message
                {
                    Content = commandResult.Message!,
                    Type = MessageType.RoomMessage
                });
                continue;
            }

            switch (commandResult.CommandType)
            {   
                case CommandType.Connect:
                    string connectHost = commandResult.Args[0];
                    int connectPort = int.Parse(commandResult.Args[1]);
                    if (_peers.Values.Any(p => p.Port == connectPort))
                    {
                        Console.WriteLine("Already connected to that peer.");
                        break;
                    }
                    if (_peers.Values.Any(p => p.Address?.ToString() == connectHost && p.Port == connectPort))
                    {
                        Console.WriteLine("Already connected to that peer.");
                        break;
                    }
                    await _tcpClientHandler.ConnectAsync(connectHost, connectPort, _localName!);
                    break;
                case CommandType.ListPeers:
                    Console.WriteLine("Connected Peers:");
                    foreach (var peer in _peers.Values)
                    {
                        var name = string.IsNullOrWhiteSpace(peer.Name) ? "Unknown" : peer.Name;
                        Console.WriteLine($"- {name} ({peer.Address}:{peer.Port})");
                    }
                    break;
                case CommandType.History:
                    _messageHistory.ShowHistory();
                    break;
                case CommandType.Quit:
                    running = false;
                    break;
                case CommandType.Help:
                    ShowHelp();
                    break;
                case CommandType.CreateRoom:
                    _localRooms.Add(commandResult.Args[0]);
                    _networkRooms.Add(commandResult.Args[0]);
                    _consoleUI.DisplaySystem($"You created room: {commandResult.Args[0]}"); 
                    var createMsg = new Message
                    {
                        Content = $"/create {commandResult.Args[0]}",
                        Type = MessageType.Command,
                        Sender = _localName
                    };
                    foreach(var peer in _peers.Values)
                    {
                        if (_tcpServer.GetConnectedPeers().Any(sp => sp.Id == peer.Id))
                        {
                            createMsg.TargetPeerId = peer.Id;
                            await _tcpServer.SendToPeerAsync(createMsg);
                        }
                        else
                            await _tcpClientHandler.SendAsync(peer.Id, createMsg);
                    }
                    break;
                case CommandType.JoinRoom:
                    _localRooms.Add(commandResult.Args[0]);
                    _networkRooms.Add(commandResult.Args[0]);
                    _consoleUI.DisplaySystem($"You joined room: {commandResult.Args[0]}");
                    var joinMsg = new Message
                    {
                        Content = $"/join {commandResult.Args[0]}",
                        Type = MessageType.Command,
                        Sender = _localName
                    };
                    foreach(var peer in _peers.Values)
                    {
                        if (_tcpServer.GetConnectedPeers().Any(sp => sp.Id == peer.Id))
                        {
                            joinMsg.TargetPeerId = peer.Id;
                            await _tcpServer.SendToPeerAsync(joinMsg);
                        }
                        else
                            await _tcpClientHandler.SendAsync(peer.Id, joinMsg);
                    }
                    break;
                case CommandType.LeaveRoom:
                    _localRooms.Remove(commandResult.Args[0]);
                    _consoleUI.DisplaySystem($"You left room: {commandResult.Args[0]}");
                    var leaveMsg = new Message
                    {
                        Content = $"/leave {commandResult.Args[0]}",
                        Type = MessageType.Command,
                        Sender = _localName
                    };
                    foreach(var peer in _peers.Values)
                    {
                        if (_tcpServer.GetConnectedPeers().Any(sp => sp.Id == peer.Id))
                        {
                            leaveMsg.TargetPeerId = peer.Id;
                            await _tcpServer.SendToPeerAsync(leaveMsg);
                        }
                        else
                            await _tcpClientHandler.SendAsync(peer.Id, leaveMsg);
                    }
                    break;
                case CommandType.MessageRoom:
                    string room = commandResult.Args[0].TrimStart('#');
                    string content = string.Join(" ", commandResult.Args[1..]);
                    _messageQueue.EnqueueOutgoing(new Message 
                    { 
                        Content = content,
                        Room = room,
                        Type = MessageType.RoomMessage
                    });
                    break;
                case CommandType.ListRooms:
                    var roomList = _networkRooms.Count > 0 ? string.Join(", ", _networkRooms) : "No rooms";
                    _consoleUI.DisplaySystem($"Network rooms: {roomList}");
                    break;
                    // _tcpServer.ListRooms();
                    //break;
                case CommandType.MessagePeer:
                    string targetPeerName = commandResult.Args[0][1..]; // remove @
                    string privateContent = string.Join(" ", commandResult.Args[1..]);
                    var targetPeer = _peers.Values.FirstOrDefault(p => p.Name == targetPeerName);
                    if (targetPeer == null)
                    {
                        Console.WriteLine($"No peer found with name: {targetPeerName}");
                        break;
                    }
                    _messageQueue.EnqueueOutgoing(new Message 
                    { 
                        Content = privateContent,
                        TargetPeerId = targetPeer.Id,
                        Type = MessageType.PrivateMessage,
                        Sender = _localName
                    });
                    break;
                default:
                    Console.WriteLine("not a command");
                    break;
            }
        }

        _cancellationTokenSource.Cancel();
        var peersToDisconnect = _tcpServer.GetConnectedPeers();
        foreach(var peer in peersToDisconnect)
        {
            _tcpClientHandler.Disconnect(peer.Id);
        }
        _messageQueue.CompleteAdding();
        _peerDiscovery.Stop();
        _tcpServer.Stop();
        Console.WriteLine("Goodbye!");
    }

    /// <summary>
    /// Display help information.
    /// </summary>
    private static void ShowHelp()
    {
        _consoleUI.ShowHelp();
    }

    // TODO: Add helper methods as needed
    // Examples:
    // - ProcessIncomingMessages() - background task to process received messages
    // - SendOutgoingMessages() - background task to send queued messages
    // - HandlePeerConnected(Peer peer) - event handler for new connections
    // - HandleMessageReceived(Peer peer, Message message) - event handler for messages

    private static void ProcessIncomingMessages(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                var message = _messageQueue.DequeueIncoming(ct);
                _consoleUI.DisplayMessage(message);
                _messageHistory?.SaveMessage(message);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }
    private static async Task ProcessOutgoingMessages(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                var message = _messageQueue.DequeueOutgoing(ct);
                message.Sender = _localName;

                // Add this check before displaying:
                if (message.Type == MessageType.RoomMessage && !_localRooms.Contains(message.Room))
                {
                    _consoleUI.DisplaySystem($"You are not in room: {message.Room}");
                    continue;
                }

                _consoleUI.DisplayMessage(message);
                _messageHistory?.SaveMessage(message);
                await BroadcastToPeers(message);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }
    private static async Task BroadcastToPeers(Message message)
{
    if (message.Type == MessageType.Command)
        return;

    var targets = _peers.Values.Where(p => p.IsConnected);

    var tasks = targets.Select(async peer =>
    {
        try
        {
            Message msgToSend;

            if (message.Type == MessageType.PrivateMessage)
            {
                if (peer.Id != message.TargetPeerId)
                    return;

                if (peer.Aes == null)
                    return;

                var encrypted = peer.Aes.Encrypt(message.Content);

                msgToSend = new Message
                {
                    Id = message.Id,
                    Sender = message.Sender,
                    EncryptedContent = encrypted,
                    Timestamp = message.Timestamp,
                    Type = MessageType.PrivateMessage,
                    TargetPeerId = peer.Id
                };
            }
            else
            {
                msgToSend = new Message
                {
                    Id = message.Id,
                    Sender = message.Sender,
                    Content = message.Content,
                    Timestamp = message.Timestamp,
                    Type = message.Type,
                    Room = message.Room,
                    TargetPeerId = peer.Id
                };
            }

            if (_tcpServer.GetConnectedPeers().Any(p => p.Id == peer.Id))
                await _tcpServer.SendToPeerAsync(msgToSend);
            else
                await _tcpClientHandler.SendAsync(peer.Id, msgToSend);
        }
        catch (Exception ex)
        {
            _consoleUI.DisplaySystem($"Error sending to {peer.Id}: {ex.Message}");
        }
    });

    foreach (var task in tasks)
    {
        await task;
    }
    }

    private static void HandleCommandMessage(Peer peer, Message message)
    {
        var parts = message.Content.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0) return;

        string senderName = message.Sender ?? peer.Name ?? "Unknown";
        var registeredPeer = _peers.Values.FirstOrDefault(p => p.Id == peer.Id) ?? peer;
        
        string command = parts[0];

        if (command == "/join" && parts.Length >= 2)
        {
            string room = parts[1];
            if (!registeredPeer.Rooms.Contains(room))
                registeredPeer.Rooms.Add(room);

            _consoleUI.DisplaySystem($"{senderName} joined {room}");
        }
        else if (command == "/leave" && parts.Length >= 2)
        {
            string room = parts[1];
            registeredPeer.Rooms.Remove(room);
            _consoleUI.DisplaySystem($"{senderName} left {room}");
        } 
        else if (command == "/create" && parts.Length >= 2)
        {
            string room = parts[1];
            _networkRooms.Add(room);
            if(!registeredPeer.Rooms.Contains(room))
            {
                registeredPeer.Rooms.Add(room);
                _consoleUI.DisplaySystem($"{senderName} created {room}");
            }
            
        }
        else if (command == "/rooms")
        {
            string roomList = registeredPeer.Rooms.Count > 0 ? string.Join(", ", registeredPeer.Rooms) : "No rooms";
            _consoleUI.DisplaySystem($"{senderName} is in rooms: {roomList}");
        }
    }
    private static async Task HandleKeyExchange(Peer peer, Message message)
    {
        // Step 1: Receive public key
        if (message.PublicKey != null && peer.PublicKey == null)
        {
            peer.PublicKey = message.PublicKey;
            peer.KeyExchange.ReceivePublicKey(message.PublicKey);

            // Send session key if we are initiator
            var encryptedSessionKey = peer.KeyExchange.CreateEncryptedSessionKey();

            await SendToPeer(peer, new Message
            {
                Type = MessageType.KeyExchange,
                EncryptedSessionKey = encryptedSessionKey
            });

            peer.KeyExchange.Complete();
            peer.Aes = new AesEncryption(peer.KeyExchange.SessionKey!);


            await SendToPeer(peer, new Message
            {
                Type = MessageType.NameExchange,
                Content = _localName
            });

            return;
        }

        // Step 2: Receive encrypted session key (client side)
        if (message.EncryptedSessionKey != null)
        {
            peer.KeyExchange.ReceiveEncryptedSessionKey(message.EncryptedSessionKey);

            peer.Aes = new AesEncryption(peer.KeyExchange.SessionKey!);

            _consoleUI.DisplaySystem("Secure channel established");

            await SendToPeer(peer, new Message
            {
                Type = MessageType.NameExchange,
                Content = _localName
            });
        }
    }

    private static async Task SendToPeer(Peer peer, Message message)
    {
        if (_tcpServer.GetConnectedPeers().Any(p => p.Id == peer.Id))
        {
            await _tcpServer.SendToPeerAsync(message);
        }
        else
        {
            await _tcpClientHandler.SendAsync(peer.Id, message);
        }
    } 
}
