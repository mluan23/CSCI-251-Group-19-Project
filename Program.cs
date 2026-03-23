// Matthew Luan
// CSCI 251 - Secure Distributed Messenger
// Group Project
//
// SPRINT 1: Threading & Basic Networking
// Due: Week 5 | Work on: Weeks 3-4
// (Continue enhancing in Sprints 2 & 3)
//

using SecureMessenger.Core;
using SecureMessenger.Network;
using SecureMessenger.Security;
using SecureMessenger.UI;

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
    
    static async Task Main(string[] args)
    {
        Console.WriteLine("Secure Distributed Messenger");
        Console.WriteLine("============================");

        _cancellationTokenSource = new CancellationTokenSource();
        _messageQueue = new MessageQueue();
        _consoleUI = new ConsoleUI(_messageQueue);
        _tcpServer = new TcpServer();
        _tcpClientHandler = new TcpClientHandler();

        _tcpServer.OnPeerConnected += (peer) =>
        {
            _consoleUI.DisplaySystem($"Peer connected: {peer.Name}");
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
                _tcpServer.BroadcastAsync(message);
            
        };
        _tcpServer.OnPeerDisconnected += (peer) =>
        {
            _consoleUI.DisplaySystem($"Peer disconnected: {peer.Name}");
        };
        _tcpClientHandler.OnConnected += (peer) =>
        {
            _consoleUI.DisplaySystem("Connected to server.");
        };
        _tcpClientHandler.OnMessageReceived += (peer, message) =>
        {   
            _consoleUI.DisplayMessage(message);
        };
        _tcpClientHandler.OnDisconnected += (peer) =>
        {
            _consoleUI.DisplaySystem($"Disconnected from server.");
        };
        // _tcpClientHandler.OnJoinRoom += (roomName, peer) =>
        // {
        //     _tcpServer.JoinRoom(roomName, peer.Id);
        //     _consoleUI.DisplaySystem($"Joined room: {roomName}");
        // };

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
                if (!_tcpClientHandler.GetConnectedPeers().Any())
                {
                    Console.WriteLine("Join a chat to send messages.");
                    continue;
                }
                await _tcpClientHandler.BroadcastAsync(commandResult.Message!);
                //await _tcpServer.BroadcastAsync(commandResult.Message!);
                continue;
            }

            switch (commandResult.CommandType)
            {   
                case CommandType.Connect:
                    await _tcpClientHandler.ConnectAsync(commandResult.Args[0], int.Parse(commandResult.Args[1]));
                    break;
                case CommandType.Listen:
                    _tcpServer.Start(int.Parse(commandResult.Args[0]));
                    break;
                case CommandType.ListPeers:
                    var peers = _tcpServer.GetConnectedPeers();
                    Console.WriteLine("Connected Peers:");
                    foreach (var peer in peers)
                    {
                        Console.WriteLine($"- {peer.Name}");
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
                    await _tcpClientHandler.BroadcastAsync($"/create {commandResult.Args[0]}");
                    break;
                case CommandType.JoinRoom:
                    await _tcpClientHandler.BroadcastAsync($"/join {commandResult.Args[0]}");
                    break;
                case CommandType.LeaveRoom:
                    await _tcpClientHandler.BroadcastAsync($"/leave {commandResult.Args[0]}");
                    break;
                case CommandType.MessageRoom:
                    string room = commandResult.Args[0];
                    string content = string.Join(" ", commandResult.Args[1..]);
                    await _tcpClientHandler.BroadcastAsync($"/msg {room} {content}");
                    break;
                case CommandType.ListRooms:
                    _tcpServer.ListRooms();
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
}
