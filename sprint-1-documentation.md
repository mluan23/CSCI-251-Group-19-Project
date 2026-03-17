# Sprint 1 Documentation
## Secure Distributed Messenger

**Team Name:** Group 19

**Team Members:**
- Aveinn Swar - Implemented the TcpClientHandler and did some documentation
- Matthew Luan - Implemented the TcpServer, Program, various bug fixes, documentation, and demo
- Aman Shah - Implemented the console UI and various bug fixes
- Jordany Roman - Implemented the MessageQueue (not needed for this sprint), and did various bug fixes

**Date:** 2/27/26

---

## Build Instructions

### Prerequisites
- .NET 9.0 SDK or later

### Building the Project
```
dotnet build
```

---

## Run Instructions

### Starting the Application
```
dotnet run
```

### Command Line Arguments (if any)
| Argument | Description | Example |
|----------|-------------|---------|
None.

---

## Application Commands

| Command | Description | Example |
|---------|-------------|---------|
| `/connect <ip> <port>` | Connect to a peer | `/connect 127.0.0.1 5001` |
| `/listen <port>` | Start listening for connections | `/listen 5001` |
| `/peers` | List known peers | `/peers` |
| `/quit` | Exit/disconnect the application | `/quit` |
| `/help` | Prints a message listing the commands | `/help` |

---

## Architecture Overview

### Client-Server Architecture
An instance is set as the server, and each other instance acts as a client and connects to that server. The clients send messages, which get sent to the server, then the server broadcasts those messages to all connected clients.

### Threading Model

- **Main Thread:** The thread handles the UI, command inputs, and the overall application. It interacts with the MessageQueue to put outgoing messages in the queue and dequeues incoming messages to display them.
- **Receive Thread:** The thread is created for every incoming connection. The thread executes the ReceiveLoop, which blocks and waits for data from peers. The network stream is monitored for incoming messages and continues without blocking other application operations.
- **Listen Thread:** The thread monitors for new incoming connection requests.

### Thread-Safe Message Queue
The MessageQueue is implemented as a thread-safe producer-consumer buffer using BlockingCollection<T>. This provides a layer of abstraction for thread safety without requiring manual locks for the primary queue operations. The queue uses BlockingCollection<Message> to handle internal synchronization, ensuring that multiple threads can add to the queue and dequeue messages at the same time without errors.

---

## Features Implemented

- [X] Multi-threaded architecture
- [X] Thread-safe message queue
- [X] TCP server (listen for connections)
- [X] TCP client (connect to peers)
- [X] Send/receive text messages
- [X] Graceful disconnection handling
- [X] Console UI with commands

---

## Testing Performed

### Test Cases
| Test | Expected Result | Actual Result | Pass/Fail |
|------|-----------------|---------------|-----------|
| Client can connect to server | Connection established | Connection established | Pass |
| Messages sent and received | Message appears on other instance | Message appears on other instance | Pass |
| Disconnection handled | No crash, appropriate message | Disconnection Message | Pass |
| Thread safety under load | No race conditions | No race conditions | Pass |

---

## Known Issues

| Issue | Description | Workaround |
|-------|-------------|------------|
None for now.

---

## Video Demo Checklist

Your demo video (3-5 minutes) should show:
- [X] Starting three instances of the application
- [X] Connecting two client instances to the server
- [X] Sending messages in both directions
- [X] Disconnecting gracefully
- [ ] (Optional) Showing thread-safe behavior under load
