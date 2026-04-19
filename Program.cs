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
    private static ConcurrentDictionary<string, Peer>? _peers = new();
    private static PeerDiscovery? _peerDiscovery;
    private static string? _localName;
    private static readonly HashSet<string> _pendingConnections = new();

    
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

        _ = Task.Run(() => ProcessIncomingMessages(_cancellationTokenSource.Token));
        _ = Task.Run(() => ProcessOutgoingMessages(_cancellationTokenSource.Token));

        _tcpServer.OnPeerConnected += (peer) =>
        {
            _peers[peer.Id] = peer;
            _consoleUI.DisplaySystem($"Connected to peer: {peer.Name}");
            // Console.WriteLine(_peers);
        };
        _tcpServer.OnMessageReceived += async (peer, message) =>
        {
            // idk if awaiting will be needed for the future but leaving this note incase we get some broken stuff in the future and that fixes anything
            message.Sender = peer.Name;

            if (message.TargetPeerId != null)
            {
                _tcpServer.SendToPeerAsync(message);
                // Message senderMsg = new Message
                // {
                //     Content = message.Content,
                //     Sender = message.Sender,
                //     TargetPeerId = peer.Id
                // };
                // // send back the msg to the sender
                // _tcpServer.SendToPeerAsync(senderMsg);
            }
            else
            {
                _messageQueue.EnqueueIncoming(message);
            }
            
        };
        _tcpServer.OnPeerDisconnected += (peer) =>
        {
            _peers.TryRemove(peer.Id, out _);
            _consoleUI.DisplaySystem($"Peer disconnected: {peer.Name}");
        };
        _tcpClientHandler.OnConnected += (peer) =>
        {
            _peers[peer.Id] = peer;
            _consoleUI.DisplaySystem("Connected to peer: " + peer.Name);
        };
        _tcpClientHandler.OnMessageReceived += (peer, message) =>
        {   
            if (message.Content.StartsWith("Created Rooms:"))
            {
                return;
            }
            _consoleUI.DisplayMessage(message);
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

        _peerDiscovery = new PeerDiscovery();
        _peerDiscovery.OnPeerDiscovered += async (peer) =>
        {
            if (peer.Id == _peerDiscovery.LocalPeerId) return;
            if (string.Compare(_peerDiscovery.LocalPeerId, peer.Id) >= 0) return;
            if (_peers.ContainsKey(peer.Id)) return;

            lock (_pendingConnections)
            {
                if (_pendingConnections.Contains(peer.Id)) return;
                _pendingConnections.Add(peer.Id);
            }

            await _tcpClientHandler.ConnectAsync(peer.Address.ToString(), peer.Port, _localName!);

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
                _messageQueue.EnqueueOutgoing(new Message { Content = commandResult.Message! });
                //await _tcpServer.BroadcastAsync(commandResult.Message!);
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
                        Console.WriteLine($"- {peer.Name} ({peer.Address}:{peer.Port})");
                    }
                    break;
                case CommandType.History:
                    // DisplayHistory() needs to be implemented during Sprint 3
                    //_consoleUI.DisplayHistory();
                    break; 
                case CommandType.Quit:
                    running = false;
                    break;
                case CommandType.Help:
                    ShowHelp();
                    break;
                case CommandType.CreateRoom:
                    await _tcpClientHandler.BroadcastAsync(new Message { Content = $"/create {commandResult.Args[0]}", Type = MessageType.Command });
                    break;
                case CommandType.JoinRoom:
                    await _tcpClientHandler.BroadcastAsync(new Message { Content = $"/join {commandResult.Args[0]}", Type = MessageType.Command });
                    break;
                case CommandType.LeaveRoom:
                    await _tcpClientHandler.BroadcastAsync(new Message { Content = $"/leave {commandResult.Args[0]}", Sender = "Server", Type = MessageType.Command });
                    break;
                case CommandType.MessageRoom:
                    string room = commandResult.Args[0];
                    string content = string.Join(" ", commandResult.Args[1..]);
                    _messageQueue.EnqueueOutgoing(new Message 
                    { 
                        Content = content,
                        Room = room,
                        Type = MessageType.RoomMessage
                    });
                    break;
                case CommandType.ListRooms:
                    await _tcpClientHandler.BroadcastAsync(new Message { Content = "/rooms", Type = MessageType.Command });
                    break;
                    // _tcpServer.ListRooms();
                    // break;
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
                message.Sender = _localName; // or whatever local name
                _consoleUI.DisplayMessage(message);
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
        var tasks = _peers.Values
            .Where(p => p.IsConnected && p.Stream != null)
            .Select(async peer =>
            {
                try
                {
                    // stupid shit trying to use client and server classes instead of a dedicated peer
                    // who is the one who initiaited this connection
                    if (_tcpServer.GetConnectedPeers().Any(p => p.Id == peer.Id))
                    {
                        var msgCopy = new Message
                        {
                            Id = message.Id,
                            Sender = message.Sender,
                            Content = message.Content,
                            Timestamp = message.Timestamp,
                            Type = message.Type,
                            Room = message.Room,
                            TargetPeerId = peer.Id
                        };
                        await _tcpServer.SendToPeerAsync(msgCopy);
                    }
                    else
                    {
                        await _tcpClientHandler.SendAsync(peer.Id, message);
                    }
                }
                catch (Exception ex)
                {
                    _consoleUI.DisplaySystem($"Error sending to {peer.Id}: {ex.Message}");
                }
            });

        await Task.WhenAll(tasks);
    }
}
