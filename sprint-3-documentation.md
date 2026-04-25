# Sprint 3 Documentation (Final)
## Secure Distributed Messenger

**Team Name:** Group 19

**Team Members:**
- Aveinn Swar - Resilient Connections
- Matthew Luan - Peer Discovery, Peer-to-Peer Architecture
- Aman Shah - Message History
- Jordany Roman - Decnetralized Chat rooms, Documentation

**Date:** 4/24/2026

---

## Build & Run Instructions

### Prerequisites
- Ensure .NET 9.0 SDK is installed

### Building
```
In a terminal, `dotnet build`
```

### Running
```
In the same terminal, `dotnet run`
On the same terminal, type `/listen <port>` to start a server (e.g., `/listen 5001`)
On another terminal, type `dotnet run` & `/connect <ip> <port>` to connect as a client (e.g., `/connect 127.0.0.1 5001`)
When prompted, enter your name
Type messages to chat, or use `/help` to see available commands
Chat room commands: `/create #room`, `/join #room`, `/leave #room`, `/rooms`, `/msg #room message`
```

### Command Line Arguments
| Argument | Description | Default |
|----------|-------------|---------|
|  None    | Application doesn't require. User prompted for name at runtime and interacts entirely through console comands. | N/A |

---

## Application Commands

| Command | Description | Example |
|---------|-------------|---------|
| `/connect <ip> <port>` | Connect to a peer | `/connect 192.168.1.100 5000` |
| `/listen <port>` | Start listening | `/listen 5000` | 
| `/peers` | List known peers | `/peers` |
| `/history` | View message history | `/history` |
| `/quit` or `/exit/` | Exit application | `/quit` or `/exit/` |
| `/create #room` | Creating a chat room | `/create room1` |
| `/join #room` | Joining an existing chat room | `/join room1` |
| `/leave #room` | Leaving a chat room | `/leave room1` |
| `/rooms` | List known chat rooms | `/rooms` |
| `/msg #room message` | Send a message to an existing chat room | `/msg #room1 Hello` |
| `/msg @peer message` | Send a message to a peer | `/msg @Bob Hi` |

---

## Architecture Diagram

```
+---------------------+        +----------------------+
|     Console UI      |<------>|    Message Queue     |
+---------------------+        +----------------------+
           |                              |
           v                              v
+---------------------+        +----------------------+
|    TCP Client       |<------>|    TCP Server        |
|   (Outgoing)        |        |   (Incoming)         |
+---------------------+        +----------------------+
           |                              |
           v                              v
     +------------------------------------------+
     |           Connected Peers (P2P)          |
     +------------------------------------------+

Additional Background Components:
- Peer Discovery (UDP Broadcast)
- Heartbeat Monitor
- Reconnection Policy
- Message History (File Storage)
- Security Layer (RSA + AES + Signatures)

```

### Component Descriptions

| Component | Responsibility |
|-----------|----------------|
| MessageQueue | Thread-safe producer-consumer queues for incoming and outgoing messages using BlockingCollection |
| TcpServer | Listens for incoming TCP connections and handles receiving messages |
| Tcp ClientHandler | Establishes outgoing connections and sends messages to peers |
| Peer | Represents a connected node including network info, encryption state, and room membership |
| PeerDiscovery | Uses UDP broadcast to automatically discover peers on the network |
| HeartbeatMonitor | Tracks peer liveness using periodic heartbeat messages |
| Reconnection Policy | Attempts reconnection with exponential backoff when a peer disconnects |
| ConsoleUI | Handles user input, command parsing, and message display |
| MessageHistory | Stores messages locally in a JSON file and retrieves them on request |
| Security (AES/RSA) | Provides encryption, key exchange, and message signing/verification |
---

## Protocol Specification

### Connection Establishment


```
Peer A                              Peer B
  |                                    |
  |------ TCP Connect Request -------->|
  |<----- Connection Accepted ---------|
  |------ Public Key ----------------->|
  |<----- Public Key ------------------|
  |------ Encrypted AES Key ---------->|
  |<----- Acknowledgment --------------|
  |------ Name Exchange -------------->|
  |                                    |
```
The connection begins with a TCP handshake. Once connected, both peers exchange RSA public keys. One peer generates an AES session key, encrypts it using the other peer’s public key, and sends it. After decryption, both peers share the same symmetric key. A name exchange message follows to identify peers.

### Message Flow
Messages originate from either user input or the network. Outgoing messages are placed into the outgoing queue and sent via the TCP client handler. Incoming messages are received by TCP server/client threads, deserialized, optionally decrypted, verified using digital signatures, and then placed into the incoming queue. A processing thread consumes these messages, displays them, and stores them in history.

### Peer Discovery Protocol


#### Broadcast Message Format
```
PEER:{peerId}:{name}:{tcpPort}:{room1,room2,...}
```

#### Discovery Process
1. Each peer broadcasts its presence every 5 seconds using UDP.
2. Messages include peer ID, name, port, and joined rooms.
3. Listening peers receive and parse the broadcast.
4. New peers are added to the known peers list.
5. Existing peers update their last seen timestamp.
6. If no broadcast is received for 30 seconds, the peer is removed.

### Heartbeat Protocol
[Describe heartbeat mechanism]

- **Interval:** 5 seconds
- **Timeout:** 15 seconds
- **Action on timeout:** Peer is marked as disconnected, removed from monitoring, and reconnection is triggered

---

## P2P Architecture

### Peer Management
Peers are stored in thread-safe collections and tracked by unique IDs. Each peer object contains connection state, encryption keys, and room membership. The system updates peer status dynamically based on connection events, discovery broadcasts, and heartbeat signals.

### Connection Strategy
Connections are established manually via /connect or automatically through peer discovery. Each peer maintains both incoming and outgoing connections, forming a decentralized mesh network. Duplicate connections are prevented by checking existing peers.

### Message Routing
Messages are routed based on type. Broadcast messages are sent to all connected peers. Room messages are delivered to peers within the same room. Private messages are directed to a specific peer using their ID. Encryption and signing are applied before transmission.

---

## Resilience Features

### Failure Detection
Failures are detected through missing heartbeat signals or broken TCP connections. When a peer fails to respond within the timeout window, it is marked as disconnected.

### Automatic Reconnection
The system attempts to reconnect automatically after detecting a failure. If all attempts fail, the peer is marked as unreachable.

- **Initial delay:** 1 second
- **Backoff strategy:** (1s → 2s → 4s → 8s → 16s, capped at 30s)
- **Max attempts:** 5

### Graceful Degradation
If peers become unavailable, the system continues operating with remaining peers. Messages to unavailable peers fail silently or are retried depending on reconnection status.

---

## Message History

### Storage Format
Messages are stored as JSON objects containing sender, content, timestamp, and metadata such as room or target peer.

### File Location
Messages are stored in a local file named message_history.json.

### History Commands
Users can retrieve stored messages using /history, which displays recent messages in chronological order.

---

## User Guide

### Getting Started
1. Start the application from the console. When prompted, enter your username. This name will be used to identify you to other peers in the network.
2. Wait for the secure connection setup to complete. During this process, the system performs a key exchange, establishes encryption, and identifies the connected peer.
3. Create a chat room using `/create room1` if you want to communicate in a group setting.
4. Join the chat room using `/join room1` to begin participating in that room.
5. Send a message to the room using `/msg #room1 Hello everyone`.
6. Send a private message to a specific peer using `/msg @PeerName Hello`.
7. View all connected peers at any time using the `/peers` command.
8. View previously sent and received messages using the `/history` command.
9. Leave a chat room using `/leave room1` when you no longer want to participate.
10. Exit the application gracefully by entering  `/quit` or `/exit`, which will close connections and stop all background processes.

### Connecting to Peers
Rely on automatic peer discovery upon starting the application.

### Sending Messages
Typing a message without a command sends a broadcast message. Use /msg #room for group messages or /msg @peer for private communication.

### Viewing Peer Status
Use /peers to view all known and connected peers, including their connection status.

### Troubleshooting
| Problem | Solution |
|---------|----------|
| Cannot connect to peer | [Check firewall, verify IP/port] |
| Messages not sending | [Check connection status] |
| Peer not discovered | Ensure UDP broadcast is allowed on the network |

---

## Features Implemented

### Core Features
- [X] P2P architecture (no central server)
- [X] Peer discovery (UDP broadcast)
- [X] Automatic peer connection
- [X] Heartbeat monitoring
- [X] Failure detection
- [X] Automatic reconnection
- [X] Message history (file-based)
- [X] Parallel message processing

### Security Features (from Sprint 2)
- [X] AES encryption
- [X] RSA key exchange
- [X] Message signing

### Bonus Features (if implemented)
- [ ] Message relay through intermediate peers
- [ ] Encrypted history storage
- [ ] Peer persistence (save/load known peers)

---

## Testing Performed

### P2P Tests
| Test | Expected Result | Actual Result | Pass/Fail |
|------|-----------------|---------------|-----------|
| 3+ peers can form mesh | All peers connected | Successful in testing | Pass |
| Peer discovery works | New peer found automatically | Works via UDP broadcast | Pass |
| Peer leaving detected | Removed from peer list | Timeout removal works | Pass |
| Reconnection after failure | Connection restored | Works with retries | Pass |

### Resilience Tests
| Test | Expected Result | Actual Result | Pass/Fail |
|------|-----------------|---------------|-----------|
| Kill peer process | Detected as failed | Detected via Heartbeat | Pass|
| Network interruption | Reconnection attempted | Retries triggered | Pass |
| Peer rejoins | Connection restored | Reconnected Successfully | Pass |

---

## Known Issues

| Issue | Description | Severity | Workaround |
|-------|-------------|----------|------------|
| Duplicate peer Identification | Same peer may appear twice breifly | Low | Filter Duplicates by Id |
| Room sync inconsistency | Rooms not fully synchronized across peers | Medium | Rejoin or rebroadcast |
| Encryption edge cases | Messages may fail if key exchange incomplete | Medium | Ensure handshake completes|

---

## Future Improvements

The system could be enhanced by implementing message relaying through intermediate peers to support multi-hop communication across disconnected networks. Encrypting stored message history would improve security at rest. Additionally, persisting known peers between sessions would eliminate the need for rediscovery on every startup. Improvements to room synchronization and better UI feedback for connection states would further refine the user experience.

---

## Video Demo Checklist

Your demo video (8-10 minutes) should show:
- [ ] Starting 3+ peer instances
- [ ] Peer discovery in action
- [ ] Messages between multiple peers
- [ ] Killing a peer and showing failure detection
- [ ] Automatic reconnection when peer returns
- [ ] Message history feature
- [ ] `/peers` command showing connected peers
